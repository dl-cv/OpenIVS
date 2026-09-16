#include "dlcv_infer.h"
#include "SharedIndexResolver.h"
#include "flow/SharedFlowRegistry.h"
#include "dlcv_sntl_admin.h"
#include "ImageInputUtils.h"
#include "MaskUtils.h"
#include "flow/FlowGraphModel.h"
#include "flow/FlowPayloadTypes.h"
#include "flow/modules/ModelModules.h"
#include "flow/utils/MaskRleUtils.h"
#ifdef _WIN32
#include <Windows.h>
#include <TlHelp32.h>
#else
#include <dlfcn.h>
#include <filesystem>
#include <iconv.h>
#include <link.h>
#include <unistd.h>
#endif
#include <algorithm>
#include <atomic>
#include <limits>
#include <cerrno>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <codecvt>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwctype>
#include <fstream>
#include <exception>
#include <iterator>
#include <locale>
#include <set>
#include <mutex>
#include <stdexcept>
#include <system_error>
#include <typeinfo>
#include <unordered_map>
#include <unordered_set>
#include <utility>

#if defined(_MSC_VER) && defined(_DEBUG)
#pragma optimize("gt", on)
#endif

namespace {

using Json = dlcv_infer::json;

thread_local double g_lastDlcvInferMs = 0.0;
thread_local double g_lastTotalInferMs = 0.0;
thread_local std::vector<dlcv_infer::FlowNodeTiming> g_lastFlowNodeTimings;

struct InspectionStatusSnapshot final {
    bool HasOk = false;
    bool Ok = false;
    std::vector<std::string> Reasons;
};

thread_local std::vector<InspectionStatusSnapshot> g_lastInspectionStatuses;

void SetLastInferTiming(double dlcvInferMs, double totalInferMs, std::vector<dlcv_infer::FlowNodeTiming> nodeTimings = {}) {
    g_lastDlcvInferMs = std::max(0.0, dlcvInferMs);
    g_lastTotalInferMs = std::max(0.0, totalInferMs);
    g_lastFlowNodeTimings = std::move(nodeTimings);
}

InspectionStatusSnapshot ParseInspectionStatus(const Json& token) {
    InspectionStatusSnapshot status;
    try {
        if (!token.is_object() || !token.contains("ok") || !token.at("ok").is_boolean()) {
            return status;
        }
        status.HasOk = true;
        status.Ok = token.at("ok").get<bool>();
        if (!token.contains("reason") || token.at("reason").is_null()) return status;

        const Json& reason = token.at("reason");
        if (reason.is_array()) {
            for (const auto& item : reason) {
                if (item.is_string()) status.Reasons.push_back(item.get<std::string>());
            }
        } else if (reason.is_string()) {
            status.Reasons.push_back(reason.get<std::string>());
        }
    } catch (...) {
        return InspectionStatusSnapshot();
    }
    return status;
}

void ClearLastInspectionStatuses() {
    g_lastInspectionStatuses.clear();
}

void SetLastInspectionStatusesFromFlowRoot(const Json& flowRoot, size_t expectedImageCount) {
    g_lastInspectionStatuses.clear();
    if (expectedImageCount > 0) g_lastInspectionStatuses.resize(expectedImageCount);

    try {
        const InspectionStatusSnapshot rootStatus = ParseInspectionStatus(flowRoot);
        if (rootStatus.HasOk) {
            if (g_lastInspectionStatuses.empty()) g_lastInspectionStatuses.resize(1);
            g_lastInspectionStatuses[0] = rootStatus;
            return;
        }

        if (!flowRoot.is_object() || !flowRoot.contains("result_list") ||
            !flowRoot.at("result_list").is_array()) return;

        const Json& resultList = flowRoot.at("result_list");
        for (size_t i = 0; i < resultList.size(); ++i) {
            const InspectionStatusSnapshot status = ParseInspectionStatus(resultList.at(i));
            if (!status.HasOk) continue;
            if (i >= g_lastInspectionStatuses.size()) g_lastInspectionStatuses.resize(i + 1);
            g_lastInspectionStatuses[i] = status;
        }
    } catch (...) {
        g_lastInspectionStatuses.clear();
    }
}

Json WrapNormalizedFlowJsonWithInspectionStatus(
    Json normalized,
    const InspectionStatusSnapshot& status) {
    if (!status.HasOk) return normalized;

    Json reason = Json();
    if (!status.Reasons.empty()) reason = status.Reasons;
    return Json::object({
        {"result_list", std::move(normalized)},
        {"ok", status.Ok},
        {"reason", std::move(reason)}
    });
}

#ifndef _WIN32
namespace fs = std::filesystem;

void* LoadSharedLibrary(const std::string& name, const std::string& fallbackPath) {
    void* handle = dlopen(name.c_str(), RTLD_LAZY | RTLD_LOCAL);
    if (handle == nullptr && !fallbackPath.empty() && fallbackPath != name) {
        handle = dlopen(fallbackPath.c_str(), RTLD_LAZY | RTLD_LOCAL);
    }
    return handle;
}

void* ResolveSharedSymbol(void* handle, const char* symbolName) {
    return handle == nullptr ? nullptr : dlsym(handle, symbolName);
}

std::string WideToUtf8Portable(const std::wstring& input) {
    if (input.empty()) return {};
    std::wstring_convert<std::codecvt_utf8<wchar_t>> converter;
    return converter.to_bytes(input);
}

std::wstring Utf8ToWidePortable(const std::string& input) {
    if (input.empty()) return {};
    std::wstring_convert<std::codecvt_utf8<wchar_t>> converter;
    return converter.from_bytes(input);
}

std::string ConvertEncodingPortable(const std::string& input, const char* fromCode, const char* toCode) {
    if (input.empty()) return {};

    iconv_t cd = iconv_open(toCode, fromCode);
    if (cd == (iconv_t)-1) {
        throw std::runtime_error("failed to open iconv converter");
    }

    size_t inBytesLeft = input.size();
    char* inBuf = const_cast<char*>(input.data());
    std::string output(std::max<size_t>(input.size() * 4, 32), '\0');
    char* outBuf = output.data();
    size_t outBytesLeft = output.size();

    for (;;) {
        const size_t ret = iconv(cd, &inBuf, &inBytesLeft, &outBuf, &outBytesLeft);
        if (ret != static_cast<size_t>(-1)) break;
        if (errno != E2BIG) {
            iconv_close(cd);
            throw std::runtime_error("failed to convert encoding");
        }

        const size_t used = static_cast<size_t>(outBuf - output.data());
        output.resize(output.size() * 2);
        outBuf = output.data() + used;
        outBytesLeft = output.size() - used;
    }

    output.resize(output.size() - outBytesLeft);
    iconv_close(cd);
    return output;
}
#endif

std::string ParentDirectoryOf(const std::string& path) {
    const size_t pos = path.find_last_of("/\\");
    if (pos == std::string::npos) return "";
    return path.substr(0, pos);
}

std::string GetSelfModuleDirectory() {
#ifdef _WIN32
    char path[MAX_PATH];
    HMODULE hModule = nullptr;
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCSTR>(&GetSelfModuleDirectory), &hModule)) {
        if (GetModuleFileNameA(hModule, path, MAX_PATH) > 0) {
            return ParentDirectoryOf(path);
        }
    }
#else
    Dl_info info;
    if (dladdr(reinterpret_cast<void*>(&GetSelfModuleDirectory), &info) && info.dli_fname) {
        std::error_code ec;
        std::filesystem::path p = info.dli_fname;
        auto canonicalPath = std::filesystem::canonical(p, ec);
        if (!ec) {
            return canonicalPath.parent_path().string();
        }
        return p.parent_path().string();
    }
#endif
    return "";
}

#ifdef _WIN32
bool DllExists(const std::string& dllDevPath, const std::string& dllCurrentPath, const std::string& dllName, const std::string& dllPath) {
    if (!dllDevPath.empty() && GetFileAttributesA(dllDevPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
        return true;
    }
    if (!dllCurrentPath.empty() && GetFileAttributesA(dllCurrentPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
        return true;
    }
    if (SearchPathA(nullptr, dllName.c_str(), nullptr, 0, nullptr, nullptr) != 0) {
        return true;
    }
    return GetFileAttributesA(dllPath.c_str()) != INVALID_FILE_ATTRIBUTES;
}
#endif

#ifdef _WIN32
inline void* ResolveSymbol(void* module, const char* name) {
    return GetProcAddress((HMODULE)module, name);
}
#else
inline void* ResolveSymbol(void* module, const char* name) {
    return dlsym(module, name);
}
#endif

struct DvsArchiveData {
    Json pipelineRoot = Json::object();
    Json originalPipelineRoot = Json::object();
    std::shared_ptr<dlcv_infer::flow::ModelBinaryStore> modelBinaryStore;
};

std::atomic<uint64_t> g_nextModelBinaryStoreId{1};

std::string ToLowerAscii(std::string s) {
    for (size_t i = 0; i < s.size(); i++) {
        const unsigned char ch = static_cast<unsigned char>(s[i]);
        if (ch >= 'A' && ch <= 'Z') s[i] = static_cast<char>(ch - 'A' + 'a');
    }
    return s;
}

std::string NormalizeArchiveName(std::string name) {
    const auto first = name.find_first_not_of(" \t\r\n\f\v");
    if (first == std::string::npos) return std::string();
    name = name.substr(first, name.find_last_not_of(" \t\r\n\f\v") - first + 1);
    std::replace(name.begin(), name.end(), '\\', '/');
    while (name.rfind("./", 0) == 0) name.erase(0, 2);
    return name;
}

bool EndsWithIgnoreCase(const std::string& text, const std::string& suffix) {
    if (text.size() < suffix.size()) return false;
    const size_t off = text.size() - suffix.size();
    for (size_t i = 0; i < suffix.size(); i++) {
        char a = text[off + i];
        char b = suffix[i];
        if (a >= 'A' && a <= 'Z') a = static_cast<char>(a - 'A' + 'a');
        if (b >= 'A' && b <= 'Z') b = static_cast<char>(b - 'A' + 'a');
        if (a != b) return false;
    }
    return true;
}

bool IsFlowArchivePath(const std::string& pathUtf8) {
    return EndsWithIgnoreCase(pathUtf8, ".dvst") ||
           EndsWithIgnoreCase(pathUtf8, ".dvso");
}

bool IsUnsupportedDvspPath(const std::string& pathUtf8) {
    return EndsWithIgnoreCase(pathUtf8, ".dvsp");
}

std::string JoinPath(const std::string& a, const std::string& b) {
    if (a.empty()) return b;
    if (b.empty()) return a;
    const char tail = a.back();
    if (tail == '\\' || tail == '/') return a + b;
#ifdef _WIN32
    return a + "\\" + b;
#else
    return a + "/" + b;
#endif
}

std::string GetFileNameOnly(const std::string& path) {
    const size_t pos = path.find_last_of("\\/");
    return (pos == std::string::npos) ? path : path.substr(pos + 1);
}

std::string GetExtensionWithDot(const std::string& path) {
    const std::string name = GetFileNameOnly(path);
    const size_t pos = name.find_last_of('.');
    if (pos == std::string::npos) return std::string();
    return name.substr(pos);
}

void ReadExactOrThrow(FILE* fp, char* dst, size_t len, const std::string& errMsg) {
    if (len == 0) return;
    if (fp == nullptr || dst == nullptr) throw std::runtime_error(errMsg);
    const size_t n = std::fread(dst, 1, len, fp);
    if (n != len) throw std::runtime_error(errMsg);
}

std::string ReadLineOrThrow(FILE* fp) {
    if (fp == nullptr) throw std::runtime_error("file handle is null");
    std::string line;
    for (;;) {
        const int c = std::fgetc(fp);
        if (c == EOF) break;
        if (c == '\n') break;
        line.push_back(static_cast<char>(c));
    }
    if (line.empty()) throw std::runtime_error("failed to read dvst header line");
    return line;
}

long long ReadFileSizeFromJson(const Json& v) {
    try {
        if (v.is_number_integer()) return v.get<long long>();
        if (v.is_number()) return static_cast<long long>(v.get<double>());
        if (v.is_string()) return std::stoll(v.get<std::string>());
    } catch (...) {}
    return -1;
}

void BindPipelineModelBuffers(
    Json& pipelineRoot,
    const dlcv_infer::flow::ModelBinaryStore& modelBinaryStore) {
    if (!pipelineRoot.is_object() || !pipelineRoot.contains("nodes") || !pipelineRoot.at("nodes").is_array()) {
        throw std::runtime_error("pipeline.json 缺少 nodes");
    }

    for (auto& node : pipelineRoot.at("nodes")) {
        if (!node.is_object()) continue;
        const std::string nodeType = ToLowerAscii(node.value("type", std::string()));
        if (nodeType.rfind("model/", 0) != 0) continue;
        if (!node.contains("properties") || !node.at("properties").is_object()) continue;

        auto& props = node.at("properties");
        if (!props.contains("model_path") || !props.at("model_path").is_string()) continue;

        const std::string originalPath = props.at("model_path").get<std::string>();
        props["model_path_original"] = originalPath;
        const std::string originalName = GetFileNameOnly(originalPath);
        props["model_name"] = originalName.empty() ? originalPath : originalName;

        std::string aliasKey = ToLowerAscii(NormalizeArchiveName(originalPath));
        if (modelBinaryStore.Buffers.find(aliasKey) != modelBinaryStore.Buffers.end()) {
            props["model_buffer_key"] = aliasKey;
            continue;
        }
        auto aliasIt = modelBinaryStore.Aliases.find(ToLowerAscii(GetFileNameOnly(aliasKey)));
        if (aliasIt != modelBinaryStore.Aliases.end() && aliasIt->second.empty()) {
            throw std::runtime_error("流程归档中存在多个同名子模型，请使用完整路径: " + originalPath);
        }
        if (aliasIt == modelBinaryStore.Aliases.end() ||
            modelBinaryStore.Buffers.find(aliasIt->second) == modelBinaryStore.Buffers.end()) {
            throw std::runtime_error("流程归档中未找到子模型: " + originalPath);
        }
        props["model_buffer_key"] = aliasIt->second;
    }
}

DvsArchiveData ReadDvsArchive(const std::wstring& archivePathW) {
#ifdef _WIN32
    FILE* fp = nullptr;
    if (_wfopen_s(&fp, archivePathW.c_str(), L"rb") != 0 || fp == nullptr) {
#else
    FILE* fp = std::fopen(WideToUtf8Portable(archivePathW).c_str(), "rb");
    if (fp == nullptr) {
#endif
        throw std::runtime_error("无法打开流程模型文件");
    }

    DvsArchiveData out;
    out.modelBinaryStore = std::make_shared<dlcv_infer::flow::ModelBinaryStore>();
    out.modelBinaryStore->StoreId = g_nextModelBinaryStoreId.fetch_add(1, std::memory_order_relaxed);
    try {
        char magic[3] = { 0 };
        ReadExactOrThrow(fp, magic, 3, "无法读取流程模型文件头");
        if (!(magic[0] == 'D' && magic[1] == 'V' && magic[2] == '\n')) {
            throw std::runtime_error("流程模型格式无效: 缺少 DV 文件头");
        }

        const std::string headerLine = ReadLineOrThrow(fp);
        const Json header = Json::parse(headerLine);
        if (!header.is_object() ||
            !header.contains("file_list") || !header.at("file_list").is_array() ||
            !header.contains("file_size") || !header.at("file_size").is_array() ||
            header.at("file_list").size() != header.at("file_size").size()) {
            throw std::runtime_error("流程模型文件头中的 file_list 与 file_size 不匹配");
        }

        bool gotPipeline = false;
        std::string pipelineData;

        const auto& fileList = header.at("file_list");
        const auto& fileSize = header.at("file_size");
        for (size_t i = 0; i < fileList.size(); i++) {
            if (!fileList.at(i).is_string()) {
                throw std::runtime_error("流程模型文件头中的文件名不是字符串");
            }

            const std::string fileName = NormalizeArchiveName(fileList.at(i).get<std::string>());
            const long long size = ReadFileSizeFromJson(fileSize.at(i));
            if (size < 0 || static_cast<unsigned long long>(size) >
                static_cast<unsigned long long>(std::numeric_limits<size_t>::max())) {
                throw std::runtime_error("流程模型中的文件大小无效");
            }
            const size_t byteCount = static_cast<size_t>(size);

            if (ToLowerAscii(fileName) == "pipeline.json") {
                std::string text(byteCount, '\0');
                if (byteCount > 0) {
                    ReadExactOrThrow(fp, &text[0], byteCount, "无法读取 pipeline.json");
                }
                if (gotPipeline) {
                    if (pipelineData != text) {
                        throw std::runtime_error("归档中存在同名但内容不同的文件：pipeline.json");
                    }
                    continue;
                }
                out.pipelineRoot = Json::parse(text);
                pipelineData = std::move(text);
                gotPipeline = true;
            } else {
                auto bytes = std::make_shared<std::vector<unsigned char>>(byteCount);
                if (byteCount > 0) {
                    ReadExactOrThrow(
                        fp,
                        reinterpret_cast<char*>(bytes->data()),
                        byteCount,
                        "无法读取流程模型中的文件数据");
                }
                const std::shared_ptr<const std::vector<unsigned char>> readonlyBytes = bytes;
                const std::string canonicalKey = ToLowerAscii(fileName);
                const auto existing = out.modelBinaryStore->Buffers.find(canonicalKey);
                if (existing != out.modelBinaryStore->Buffers.end()) {
                    if (*existing->second != *readonlyBytes) {
                        throw std::runtime_error("归档中存在同名但内容不同的文件：" + fileName);
                    }
                    continue;
                }
                out.modelBinaryStore->Buffers.emplace(canonicalKey, readonlyBytes);
                const std::string shortName = ToLowerAscii(GetFileNameOnly(fileName));
                const auto alias = out.modelBinaryStore->Aliases.emplace(shortName, canonicalKey);
                if (!alias.second && alias.first->second != canonicalKey) {
                    alias.first->second.clear();
                }
            }

        }

        if (!gotPipeline) throw std::runtime_error("流程模型中未找到 pipeline.json");
        dlcv_infer::flow::detail::RemoveArchiveModelIndexes(out.pipelineRoot);
        out.originalPipelineRoot = out.pipelineRoot;
        BindPipelineModelBuffers(out.pipelineRoot, *out.modelBinaryStore);
    } catch (...) {
        std::fclose(fp);
        throw;
    }

    std::fclose(fp);
    return out;
}

std::wstring DecodeModelPathString(const std::string& modelPath) {
    try {
        const std::wstring utf8Path = dlcv_infer::convertUtf8ToWstring(modelPath);
        if (dlcv_infer::convertWstringToUtf8(utf8Path) == modelPath) {
            return utf8Path;
        }
    } catch (...) {
    }
    return dlcv_infer::convertGbkToWstring(modelPath);
}

double ReadJsonNumber(const Json& v, double dv = 0.0) {
    try {
        if (v.is_number()) return v.get<double>();
        if (v.is_string()) return std::stod(v.get<std::string>());
    } catch (...) {}
    return dv;
}

bool ReadJsonBool(const Json& v, bool dv = false) {
    try {
        if (v.is_boolean()) return v.get<bool>();
        if (v.is_number_integer()) return v.get<int>() != 0;
        if (v.is_string()) {
            const std::string s = ToLowerAscii(v.get<std::string>());
            if (s == "1" || s == "true") return true;
            if (s == "0" || s == "false") return false;
        }
    } catch (...) {}
    return dv;
}

bool ResolveWithMaskOutputFlag(const Json& paramsJson, bool defaultValue = true) {
    try {
        if (paramsJson.is_object() && paramsJson.contains("with_mask")) {
            return ReadJsonBool(paramsJson.at("with_mask"), defaultValue);
        }
    } catch (...) {}
    return defaultValue;
}

std::vector<double> ParseFlowBboxToModel(
    const Json& entry,
    bool& withBbox,
    bool& withAngle,
    float& angle,
    bool& isRotated) {

    withBbox = false;
    withAngle = false;
    angle = -100.0f;
    isRotated = false;

    if (!entry.is_object() || !entry.contains("bbox") || !entry.at("bbox").is_array()) {
        return std::vector<double>();
    }

    const Json& bbox = entry.at("bbox");
    bool metadataRotated = false;
    try {
        if (entry.contains("metadata") && entry.at("metadata").is_object() &&
            entry.at("metadata").contains("is_rotated")) {
            metadataRotated = ReadJsonBool(entry.at("metadata").at("is_rotated"), false);
        }
    } catch (...) {}

    if (bbox.size() >= 5 || metadataRotated) {
        if (bbox.size() >= 5) {
            const double cx = ReadJsonNumber(bbox.at(0), 0.0);
            const double cy = ReadJsonNumber(bbox.at(1), 0.0);
            const double w = std::max(0.0, ReadJsonNumber(bbox.at(2), 0.0));
            const double h = std::max(0.0, ReadJsonNumber(bbox.at(3), 0.0));
            angle = static_cast<float>(ReadJsonNumber(bbox.at(4), -100.0));
            withBbox = true;
            withAngle = true;
            isRotated = true;
            return { cx, cy, w, h };
        }
    }

    if (bbox.size() >= 4) {
        const double x1 = ReadJsonNumber(bbox.at(0), 0.0);
        const double y1 = ReadJsonNumber(bbox.at(1), 0.0);
        const double x2 = ReadJsonNumber(bbox.at(2), x1);
        const double y2 = ReadJsonNumber(bbox.at(3), y1);
        withBbox = true;
        return { x1, y1, std::max(0.0, x2 - x1), std::max(0.0, y2 - y1) };
    }

    return std::vector<double>();
}

cv::Mat BuildMaskFromFlowPoly(const Json& entry, const std::vector<double>& bbox, bool isRotated) {
    if (isRotated) return cv::Mat();
    if (!entry.is_object() || !entry.contains("poly") || !entry.at("poly").is_array()) return cv::Mat();
    if (bbox.size() < 4) return cv::Mat();

    const int width = std::max(1, static_cast<int>(std::llround(std::max(0.0, bbox[2]))));
    const int height = std::max(1, static_cast<int>(std::llround(std::max(0.0, bbox[3]))));
    const double x0 = bbox[0];
    const double y0 = bbox[1];

    cv::Mat mask = cv::Mat::zeros(height, width, CV_8UC1);
    bool anyDrawn = false;
    const Json& poly = entry.at("poly");

    for (const auto& contourToken : poly) {
        if (!contourToken.is_array() || contourToken.size() < 3) continue;
        std::vector<cv::Point> contour;
        contour.reserve(contourToken.size());
        for (const auto& pt : contourToken) {
            if (!pt.is_array() || pt.size() < 2) continue;
            const int px = static_cast<int>(std::llround(ReadJsonNumber(pt.at(0), 0.0) - x0));
            const int py = static_cast<int>(std::llround(ReadJsonNumber(pt.at(1), 0.0) - y0));
            contour.emplace_back(
                std::max(0, std::min(width - 1, px)),
                std::max(0, std::min(height - 1, py))
            );
        }
        if (contour.size() > 2) {
            const std::vector<std::vector<cv::Point>> contours{ contour };
            cv::fillPoly(mask, contours, cv::Scalar(255));
            anyDrawn = true;
        }
    }
    return anyDrawn ? mask : cv::Mat();
}

cv::Mat BuildMaskFromFlowEntry(const Json& entry, const std::vector<double>& bbox, bool isRotated) {
    if (entry.is_object() && entry.contains("mask_rle") && entry.at("mask_rle").is_object()) {
        try {
            cv::Mat m = dlcv_infer::flow::MaskInfoToMat(entry.at("mask_rle"));
            if (!m.empty()) return m;
        } catch (...) {}
    }
    return BuildMaskFromFlowPoly(entry, bbox, isRotated);
}

double ComputeFlowArea(const Json& entry, const cv::Mat& mask, const std::vector<double>& bbox, bool allowMaskDerived) {
    if (entry.is_object() && entry.contains("area")) {
        return ReadJsonNumber(entry.at("area"), 0.0);
    }

    if (allowMaskDerived && entry.is_object() && entry.contains("mask_rle") && entry.at("mask_rle").is_object()) {
        try { return dlcv_infer::flow::CalculateMaskArea(entry.at("mask_rle")); } catch (...) {}
    }

    if (allowMaskDerived && !mask.empty()) {
        return static_cast<double>(cv::countNonZero(mask));
    }

    if (bbox.size() >= 4) {
        return std::max(0.0, bbox[2]) * std::max(0.0, bbox[3]);
    }
    return 0.0;
}

std::vector<dlcv_infer::ObjectResult> ConvertFlowResultListToObjects(const Json& flowResultList, bool emitMaskOutput) {
    std::vector<dlcv_infer::ObjectResult> out;
    if (!flowResultList.is_array()) return out;
    std::unordered_map<std::string, std::string> categoryNameCache;
    categoryNameCache.reserve(16);

    for (const auto& entry : flowResultList) {
        if (!entry.is_object()) continue;

        const int categoryId = entry.value("category_id", 0);
        const std::string categoryNameUtf8 = entry.value("category_name", std::string());
        auto itCachedName = categoryNameCache.find(categoryNameUtf8);
        if (itCachedName == categoryNameCache.end()) {
            const std::string categoryNameGbk = dlcv_infer::convertUtf8ToGbk(categoryNameUtf8);
            itCachedName = categoryNameCache.emplace(categoryNameUtf8, categoryNameGbk).first;
        }
        const float score = static_cast<float>(ReadJsonNumber(entry.contains("score") ? entry.at("score") : Json(), 0.0));

        bool withBbox = false;
        bool withAngle = false;
        float angle = -100.0f;
        bool isRotated = false;
        std::vector<double> bbox = ParseFlowBboxToModel(entry, withBbox, withAngle, angle, isRotated);

        cv::Mat mask;
        if (emitMaskOutput) {
            mask = BuildMaskFromFlowEntry(entry, bbox, isRotated);
        }
        const bool withMask = emitMaskOutput && !mask.empty();
        const float area = static_cast<float>(ComputeFlowArea(entry, mask, bbox, emitMaskOutput));
        const bool withMean = ReadJsonBool(
            entry.contains("with_mean") ? entry.at("with_mean") : Json(), false);
        const double foregroundMean = ReadJsonNumber(
            entry.contains("foreground_mean") ? entry.at("foreground_mean") : Json(), 0.0);
        const double backgroundMean = ReadJsonNumber(
            entry.contains("background_mean") ? entry.at("background_mean") : Json(), 0.0);

        out.emplace_back(
            categoryId,
            itCachedName->second,
            score,
            area,
            bbox,
            withMask,
            mask,
            withBbox,
            withAngle,
            angle,
            withMean,
            foregroundMean,
            backgroundMean
        );
    }
    return out;
}

Json MaskToPointsJson(const cv::Mat& mask, double xOffset, double yOffset) {
    Json points = Json::array();
    if (mask.empty()) return points;

    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(mask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);
    if (contours.empty()) return points;

    for (const auto& pt : contours[0]) {
        Json p = Json::object();
        p["x"] = static_cast<int>(std::llround(static_cast<double>(pt.x) + xOffset));
        p["y"] = static_cast<int>(std::llround(static_cast<double>(pt.y) + yOffset));
        points.push_back(p);
    }
    return points;
}

Json NormalizeFlowOneOutJson(const Json& flowResultList, bool emitMaskOutput) {
    Json normalized = Json::array();
    if (!flowResultList.is_array()) return normalized;

    for (const auto& entry : flowResultList) {
        if (!entry.is_object()) continue;

        Json out = Json::object();
        out["category_id"] = entry.value("category_id", 0);
        out["category_name"] = entry.value("category_name", std::string());
        out["score"] = ReadJsonNumber(entry.contains("score") ? entry.at("score") : Json(), 0.0);

        bool withBbox = false;
        bool withAngle = false;
        float angle = -100.0f;
        bool isRotated = false;
        std::vector<double> bbox = ParseFlowBboxToModel(entry, withBbox, withAngle, angle, isRotated);

        out["bbox"] = bbox;
        out["with_bbox"] = withBbox;
        out["with_angle"] = withAngle;
        out["angle"] = withAngle ? static_cast<double>(angle) : -100.0;

        cv::Mat mask;
        Json maskPoints = Json::array();
        if (emitMaskOutput) {
            mask = BuildMaskFromFlowEntry(entry, bbox, isRotated);
            if (!mask.empty() && bbox.size() >= 4 && !isRotated) {
                maskPoints = MaskToPointsJson(mask, bbox[0], bbox[1]);
            }
        }

        if (!maskPoints.empty()) {
            out["mask"] = maskPoints;
            out["with_mask"] = true;
        } else {
            out["mask"] = Json::object({ {"height", -1}, {"mask_ptr", 0}, {"width", -1} });
            out["with_mask"] = false;
        }

        out["area"] = ComputeFlowArea(entry, mask, bbox, emitMaskOutput);
        out["with_mean"] = ReadJsonBool(
            entry.contains("with_mean") ? entry.at("with_mean") : Json(), false);
        out["foreground_mean"] = ReadJsonNumber(
            entry.contains("foreground_mean") ? entry.at("foreground_mean") : Json(), 0.0);
        out["background_mean"] = ReadJsonNumber(
            entry.contains("background_mean") ? entry.at("background_mean") : Json(), 0.0);
        normalized.push_back(out);
    }
    return normalized;
}

dlcv_infer::flow::FlowBatchResult ParseFlowBatchResultFromToken(const Json& resultListToken) {
    dlcv_infer::flow::FlowBatchResult batch;
    if (!resultListToken.is_array()) return batch;

    bool isBatchContainer = false;
    try {
        if (!resultListToken.empty()) {
            const auto& first = resultListToken.at(0);
            isBatchContainer = first.is_object() &&
                               first.contains("result_list") &&
                               first.at("result_list").is_array();
        }
    } catch (...) {
        isBatchContainer = false;
    }

    if (isBatchContainer) {
        for (const auto& token : resultListToken) {
            std::vector<dlcv_infer::flow::FlowResultItem> oneSample;
            try {
                if (token.is_object() && token.contains("result_list") && token.at("result_list").is_array()) {
                    for (const auto& entry : token.at("result_list")) {
                        if (entry.is_object()) {
                            oneSample.push_back(dlcv_infer::flow::FlowResultItem::FromJson(entry));
                        }
                    }
                }
            } catch (...) {}
            batch.PerImageResults.push_back(std::move(oneSample));
        }
        return batch;
    }

    std::vector<dlcv_infer::flow::FlowResultItem> oneSample;
    for (const auto& entry : resultListToken) {
        if (entry.is_object()) {
            oneSample.push_back(dlcv_infer::flow::FlowResultItem::FromJson(entry));
        }
    }
    batch.PerImageResults.push_back(std::move(oneSample));
    return batch;
}

dlcv_infer::flow::FlowBatchResult ParseFlowBatchResultFromRoot(const Json& flowRoot, size_t expectedImageCount) {
    Json resultListToken = Json::array();
    try {
        if (flowRoot.is_object() && flowRoot.contains("result_list")) {
            resultListToken = flowRoot.at("result_list");
        } else if (flowRoot.is_array()) {
            resultListToken = flowRoot;
        }
    } catch (...) {
        resultListToken = Json::array();
    }

    dlcv_infer::flow::FlowBatchResult batch = ParseFlowBatchResultFromToken(resultListToken);
    if (expectedImageCount > 0 && batch.PerImageResults.size() < expectedImageCount) {
        batch.PerImageResults.resize(expectedImageCount);
    }
    return batch;
}

Json ExtractFlowResultListToken(const Json& flowRoot) {
    Json resultListToken = Json::array();
    try {
        if (flowRoot.is_object() && flowRoot.contains("result_list")) {
            resultListToken = flowRoot.at("result_list");
        } else if (flowRoot.is_array()) {
            resultListToken = flowRoot;
        }
    } catch (...) {
        resultListToken = Json::array();
    }
    return resultListToken;
}

std::vector<dlcv_infer::SampleResult> ConvertFlowResultListTokenToSampleResults(
    const Json& resultListToken,
    size_t expectedImageCount,
    bool emitMaskOutput) {

    std::vector<dlcv_infer::SampleResult> sampleResults;
    if (!resultListToken.is_array()) {
        if (expectedImageCount > 0) {
            const dlcv_infer::SampleResult emptySample(std::vector<dlcv_infer::ObjectResult>{});
            sampleResults.resize(expectedImageCount, emptySample);
        }
        return sampleResults;
    }

    bool isBatchContainer = false;
    try {
        if (!resultListToken.empty()) {
            const auto& first = resultListToken.at(0);
            isBatchContainer = first.is_object() &&
                               first.contains("result_list") &&
                               first.at("result_list").is_array();
        }
    } catch (...) {
        isBatchContainer = false;
    }

    if (isBatchContainer) {
        sampleResults.reserve(std::max(expectedImageCount, resultListToken.size()));
        for (const auto& token : resultListToken) {
            if (token.is_object() && token.contains("result_list") && token.at("result_list").is_array()) {
                sampleResults.emplace_back(ConvertFlowResultListToObjects(token.at("result_list"), emitMaskOutput));
            } else {
                sampleResults.emplace_back(std::vector<dlcv_infer::ObjectResult>{});
            }
        }
    } else {
        sampleResults.emplace_back(ConvertFlowResultListToObjects(resultListToken, emitMaskOutput));
    }

    if (expectedImageCount > 0 && sampleResults.size() < expectedImageCount) {
        const dlcv_infer::SampleResult emptySample(std::vector<dlcv_infer::ObjectResult>{});
        sampleResults.resize(expectedImageCount, emptySample);
    }
    return sampleResults;
}

void ParseFlowTimingFromRoot(
    const Json& flowRoot,
    double& dlcvInferMs,
    double& totalInferMs,
    std::vector<dlcv_infer::FlowNodeTiming>& nodeTimings) {

    dlcvInferMs = 0.0;
    totalInferMs = 0.0;
    nodeTimings.clear();

    if (!flowRoot.is_object() || !flowRoot.contains("timing") || !flowRoot.at("timing").is_object()) {
        return;
    }

    const Json& timing = flowRoot.at("timing");
    try { dlcvInferMs = std::max(0.0, timing.value("dlcv_infer_ms", 0.0)); } catch (...) { dlcvInferMs = 0.0; }
    try { totalInferMs = std::max(0.0, timing.value("flow_infer_ms", 0.0)); } catch (...) { totalInferMs = 0.0; }

    try {
        if (timing.contains("node_timings") && timing.at("node_timings").is_array()) {
            for (const auto& one : timing.at("node_timings")) {
                if (!one.is_object()) continue;
                dlcv_infer::FlowNodeTiming item;
                try { item.nodeId = one.value("node_id", -1); } catch (...) { item.nodeId = -1; }
                try { item.nodeType = one.value("node_type", std::string()); } catch (...) { item.nodeType.clear(); }
                try { item.nodeTitle = one.value("node_title", std::string()); } catch (...) { item.nodeTitle.clear(); }
                try { item.elapsedMs = std::max(0.0, one.value("elapsed_ms", 0.0)); } catch (...) { item.elapsedMs = 0.0; }
                nodeTimings.push_back(std::move(item));
            }
        }
    } catch (...) {
        nodeTimings.clear();
    }
}

} // namespace

namespace {

using MIJson = dlcv_infer::json;

int ReadJsonIntLike(const MIJson& v, int dv) {
    try {
        if (v.is_number_integer()) {
            return v.get<int>();
        }
        if (v.is_number()) {
            return static_cast<int>(std::llround(v.get<double>()));
        }
        if (v.is_string()) {
            return std::stoi(v.get<std::string>());
        }
    } catch (...) {
    }
    return dv;
}

bool IsLikelySpatialDim(int v) {
    return v >= 8 && v <= 65536;
}

int InferChannelCountFromMaxShape(const MIJson& shapeArr) {
    if (!shapeArr.is_array()) {
        return 0;
    }
    const size_t n = shapeArr.size();
    if (n >= 4) {
        const int d1 = ReadJsonIntLike(shapeArr.at(1), -1);
        const int d2 = ReadJsonIntLike(shapeArr.at(2), -1);
        const int d3 = ReadJsonIntLike(shapeArr.at(3), -1);
        if (IsLikelySpatialDim(d2) && IsLikelySpatialDim(d3)) {
            if (d1 == 1 || d1 == 3) {
                return d1;
            }
        }
        const int d0 = ReadJsonIntLike(shapeArr.at(0), -1);
        if (IsLikelySpatialDim(d0) && IsLikelySpatialDim(d1)) {
            if (d3 == 1 || d3 == 3) {
                return d3;
            }
        }
    }
    if (n == 3) {
        const int d0 = ReadJsonIntLike(shapeArr.at(0), -1);
        const int d1 = ReadJsonIntLike(shapeArr.at(1), -1);
        const int d2 = ReadJsonIntLike(shapeArr.at(2), -1);
        if ((d0 == 1 || d0 == 3) && IsLikelySpatialDim(d1) && IsLikelySpatialDim(d2)) {
            return d0;
        }
        if (IsLikelySpatialDim(d0) && IsLikelySpatialDim(d1) && (d2 == 1 || d2 == 3)) {
            return d2;
        }
    }
    return 0;
}

const MIJson* FindInputShapesObject(const MIJson& root) {
    if (root.contains("model_info") && root.at("model_info").is_object()) {
        const MIJson& mi = root.at("model_info");
        if (mi.contains("input_shapes") && mi.at("input_shapes").is_object()) {
            return &mi.at("input_shapes");
        }
        if (mi.contains("model_info") && mi.at("model_info").is_object()) {
            const MIJson& inner = mi.at("model_info");
            if (inner.contains("input_shapes") && inner.at("input_shapes").is_object()) {
                return &inner.at("input_shapes");
            }
        }
    }
    if (root.contains("input_shapes") && root.at("input_shapes").is_object()) {
        return &root.at("input_shapes");
    }
    return nullptr;
}

int ParseInputChFromModelInfo(const MIJson& modelInfoRoot) {
    const MIJson* shapesPtr = FindInputShapesObject(modelInfoRoot);
    if (shapesPtr == nullptr || !shapesPtr->is_object()) {
        return 0;
    }
    const MIJson& shapes = *shapesPtr;

    auto tryInputDesc = [](const MIJson& inputDesc) -> int {
        if (!inputDesc.is_object() || !inputDesc.contains("max_shape")) {
            return 0;
        }
        return InferChannelCountFromMaxShape(inputDesc.at("max_shape"));
    };

    if (shapes.contains("input")) {
        const int ch = tryInputDesc(shapes.at("input"));
        if (ch == 1 || ch == 3) {
            return ch;
        }
    }
    for (const auto& kv : shapes.items()) {
        const int ch = tryInputDesc(kv.value());
        if (ch == 1 || ch == 3) {
            return ch;
        }
    }
    return 0;
}

cv::Mat NormalizeInferInputImage(const cv::Mat& src, int expectedChannels) {
    if (src.empty()) {
        return {};
    }
    // 应用层负责把 OpenCV 读盘得到的 BGR/BGRA 颜色图整理为 RGB；
    // 接口层再按模型期望通道数补齐/压缩通道，并统一位深到 8U。
    return dlcv_infer::image_input::NormalizeInferInputImage(src, expectedChannels);
}

    class NvmlLibrary {
    public:
        static NvmlLibrary& Get() {
            static NvmlLibrary instance;
            return instance;
        }

        bool IsLoaded() const { return hModule != nullptr; }

        int Init() { return init ? init() : -1; }
        int Shutdown() { return shutdown ? shutdown() : -1; }
        int DeviceGetCount(unsigned int* count) { return deviceGetCount ? deviceGetCount(count) : -1; }
        int DeviceGetName(dlcv_infer::nvmlDevice_t device, char* name, unsigned int length) {
            return deviceGetName ? deviceGetName(device, name, length) : -1;
        }
        int DeviceGetHandleByIndex(unsigned int index, dlcv_infer::nvmlDevice_t* device) {
            return deviceGetHandleByIndex ? deviceGetHandleByIndex(index, device) : -1;
        }

    private:
        NvmlLibrary() {
#ifdef _WIN32
            hModule = LoadLibraryA("nvml.dll");
#else
            hModule = dlopen("libnvidia-ml.so.1", RTLD_LAZY | RTLD_LOCAL);
            if (!hModule) {
                hModule = dlopen("libnvidia-ml.so", RTLD_LAZY | RTLD_LOCAL);
            }
#endif
            if (!hModule) return;

            init = (NvmlInitFunc)ResolveSymbol(hModule, "nvmlInit");
            shutdown = (NvmlShutdownFunc)ResolveSymbol(hModule, "nvmlShutdown");
            deviceGetCount = (NvmlDeviceGetCountFunc)ResolveSymbol(hModule, "nvmlDeviceGetCount");
            deviceGetName = (NvmlDeviceGetNameFunc)ResolveSymbol(hModule, "nvmlDeviceGetName");
            deviceGetHandleByIndex = (NvmlDeviceGetHandleByIndexFunc)ResolveSymbol(hModule, "nvmlDeviceGetHandleByIndex");
        }

        ~NvmlLibrary() {
#ifdef _WIN32
            if (hModule) FreeLibrary((HMODULE)hModule);
#else
            if (hModule) dlclose(hModule);
#endif
        }

        NvmlLibrary(const NvmlLibrary&) = delete;
        NvmlLibrary& operator=(const NvmlLibrary&) = delete;

        void* hModule = nullptr;
        typedef int (*NvmlInitFunc)();
        typedef int (*NvmlShutdownFunc)();
        typedef int (*NvmlDeviceGetCountFunc)(unsigned int*);
        typedef int (*NvmlDeviceGetNameFunc)(dlcv_infer::nvmlDevice_t, char*, unsigned int);
        typedef int (*NvmlDeviceGetHandleByIndexFunc)(unsigned int, dlcv_infer::nvmlDevice_t*);
        NvmlInitFunc init = nullptr;
        NvmlShutdownFunc shutdown = nullptr;
        NvmlDeviceGetCountFunc deviceGetCount = nullptr;
        NvmlDeviceGetNameFunc deviceGetName = nullptr;
        NvmlDeviceGetHandleByIndexFunc deviceGetHandleByIndex = nullptr;
    };

} // namespace

namespace dlcv_infer {

    // C 与 C++ 入口共用全局释放流程；模型表的销毁在底层释放前完成。
    void ClearCApiModelsForFreeAllModels();

    static std::mutex g_modelLoadMu;

    namespace {
        std::mutex& DllLoaderRegistryMutex() {
            static std::mutex mutex;
            return mutex;
        }

        std::unordered_map<void*, std::unique_ptr<DllLoader>>& DllLoaderRegistry() {
            static std::unordered_map<void*, std::unique_ptr<DllLoader>> loaders;
            return loaders;
        }

        std::mutex& DefaultLoaderMutex() {
            static std::mutex mutex;
            return mutex;
        }

        DllLoader*& DefaultLoaderSlot() {
            static DllLoader* loader = nullptr;
            return loader;
        }

        struct LoadedInferModule final {
            sntl_admin::DogProvider Provider = sntl_admin::DogProvider::Unknown;
            void* Module = nullptr;
            std::string Path;
        };

        static std::string LowerAscii(std::string value) {
            for (char& ch : value) {
                ch = static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
            }
            return value;
        }

        static bool IsTargetInferModuleName(const std::string& name, sntl_admin::DogProvider& provider) {
            const std::string lowerName = LowerAscii(name);
            const size_t slash = lowerName.find_last_of("/\\");
            const std::string baseName = slash == std::string::npos
                ? lowerName
                : lowerName.substr(slash + 1);
            if (baseName == "dlcv_infer.dll" || baseName == "libdlcv_infer_s.so") {
                provider = sntl_admin::DogProvider::Sentinel;
                return true;
            }
            if (baseName == "dlcv_infer_v.dll" || baseName == "libdlcv_infer_v.so") {
                provider = sntl_admin::DogProvider::Virbox;
                return true;
            }
            return false;
        }

        std::mutex& HeldInferModuleMutex() {
            static std::mutex mutex;
            return mutex;
        }

        std::unordered_map<void*, void*>& HeldInferModules() {
            static std::unordered_map<void*, void*> modules;
            return modules;
        }

#ifndef _WIN32
        struct DlIterateInferContext final {
            std::vector<LoadedInferModule>* Modules = nullptr;
            std::string Error;
            std::exception_ptr Exception;
        };

        static int CollectLoadedInferModule(
            struct dl_phdr_info* info,
            size_t,
            void* userData) {
            if (info == nullptr || info->dlpi_name == nullptr || info->dlpi_name[0] == '\0') {
                return 0;
            }
            auto* context = static_cast<DlIterateInferContext*>(userData);
            try {
                sntl_admin::DogProvider provider = sntl_admin::DogProvider::Unknown;
                if (!IsTargetInferModuleName(info->dlpi_name, provider)) return 0;

                void* module = dlopen(info->dlpi_name, RTLD_LAZY | RTLD_LOCAL | RTLD_NOLOAD);
                if (module == nullptr) {
                    context->Error = "无法持有已加载的推理 DLL";
                    return 1;
                }
                {
                    std::lock_guard<std::mutex> lock(HeldInferModuleMutex());
                    auto& heldModules = HeldInferModules();
                    const auto it = heldModules.find(module);
                    if (it == heldModules.end()) {
                        heldModules.emplace(module, module);
                    } else {
                        dlclose(module);
                        module = it->second;
                    }
                }
                context->Modules->push_back({provider, module, info->dlpi_name});
                return 0;
            } catch (...) {
                context->Exception = std::current_exception();
                return 1;
            }
        }
#endif

        static std::vector<LoadedInferModule> EnumerateLoadedInferModules() {
            std::vector<LoadedInferModule> modules;
#ifdef _WIN32
            const HANDLE snapshot = CreateToolhelp32Snapshot(
                TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,
                GetCurrentProcessId());
            if (snapshot == INVALID_HANDLE_VALUE) {
                throw std::runtime_error("无法枚举当前进程模块");
            }

            MODULEENTRY32W entry{};
            entry.dwSize = sizeof(entry);
            BOOL hasEntry = Module32FirstW(snapshot, &entry);
            while (hasEntry) {
                const std::wstring moduleName(entry.szModule);
                const std::string moduleNameUtf8 = convertWstringToUtf8(moduleName);
                sntl_admin::DogProvider provider = sntl_admin::DogProvider::Unknown;
                if (IsTargetInferModuleName(moduleNameUtf8, provider)) {
                    if (entry.hModule == nullptr) {
                        CloseHandle(snapshot);
                        throw std::runtime_error("已加载推理 DLL 句柄为空");
                    }
                    HMODULE heldModule = nullptr;
                    {
                        std::lock_guard<std::mutex> lock(HeldInferModuleMutex());
                        auto& heldModules = HeldInferModules();
                        const auto it = heldModules.find(entry.hModule);
                        if (it != heldModules.end()) {
                            heldModule = static_cast<HMODULE>(it->second);
                        } else {
                            if (!GetModuleHandleExW(
                                    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                                    reinterpret_cast<LPCWSTR>(entry.hModule),
                                    &heldModule)) {
                                CloseHandle(snapshot);
                                throw std::runtime_error("无法持有已加载的推理 DLL");
                            }
                            if (heldModule != entry.hModule) {
                                FreeLibrary(heldModule);
                                CloseHandle(snapshot);
                                throw std::runtime_error("已加载推理 DLL 句柄发生变化");
                            }
                            heldModules.emplace(entry.hModule, heldModule);
                        }
                    }
                    modules.push_back({
                        provider,
                        static_cast<void*>(heldModule),
                        convertWstringToUtf8(std::wstring(entry.szExePath))
                    });
                }
                hasEntry = Module32NextW(snapshot, &entry);
            }
            if (!hasEntry) {
                const DWORD error = GetLastError();
                if (error == ERROR_NO_MORE_FILES) {
                    CloseHandle(snapshot);
                    return modules;
                }
                CloseHandle(snapshot);
                throw std::runtime_error(
                    "枚举当前进程模块失败: " + std::to_string(error));
            }
            CloseHandle(snapshot);
#else
            DlIterateInferContext context;
            context.Modules = &modules;
            dl_iterate_phdr(CollectLoadedInferModule, &context);
            if (context.Exception) std::rethrow_exception(context.Exception);
            if (!context.Error.empty()) {
                throw std::runtime_error(context.Error);
            }
#endif
            return modules;
        }
    }

    static int LoadModelIndexByPath(
        DllLoader* loader,
        const std::string& modelPathUtf8,
        int deviceId) {
        if (loader == nullptr || loader->GetLoadModelFunc() == nullptr ||
            loader->GetFreeResultFunc() == nullptr) {
            throw std::runtime_error("模型加载接口不可用");
        }

        json config;
        config["model_path"] = modelPathUtf8;
        config["device_id"] = deviceId;
        const std::string jsonStr = config.dump();
        const char* resultPtr = loader->GetLoadModelFunc()(jsonStr.c_str());
        if (resultPtr == nullptr) throw std::runtime_error("模型加载未返回结果");

        int modelIndex = -1;
        try {
            const json resultObject = json::parse(static_cast<const char*>(resultPtr));
            if (!resultObject.contains("model_index")) {
                throw std::runtime_error("load model failed: " + resultObject.dump());
            }
            modelIndex = resultObject.at("model_index").get<int>();
        } catch (...) {
            loader->GetFreeResultFunc()(resultPtr);
            throw;
        }
        loader->GetFreeResultFunc()(resultPtr);
        if (modelIndex < 0) throw std::runtime_error("模型加载返回的 index 无效");
        return modelIndex;
    }

    static std::mutex g_flowModelIndexMu;
    static int g_nextSentinelFlowModelIndex = 10000;
    static int g_nextVirboxFlowModelIndex = 30000;

    static int AllocateFlowModelIndex(sntl_admin::DogProvider provider) {
        std::lock_guard<std::mutex> lock(g_flowModelIndexMu);
        int* nextIndex = nullptr;
        int lastIndex = -1;
        if (provider == sntl_admin::DogProvider::Sentinel) {
            nextIndex = &g_nextSentinelFlowModelIndex;
            lastIndex = 19999;
        } else if (provider == sntl_admin::DogProvider::Virbox) {
            nextIndex = &g_nextVirboxFlowModelIndex;
            lastIndex = 39999;
        } else {
            throw std::runtime_error("无法确定本地流程索引的 provider");
        }
        if (*nextIndex > lastIndex) return -1;
        return (*nextIndex)++;
    }

#ifdef _WIN32
    std::wstring Win32MultiByteToWide(const std::string& input, uint32_t codePage) {
        int len = MultiByteToWideChar(codePage, 0, input.c_str(), -1, nullptr, 0);
        if (len <= 0) return {};
        std::vector<wchar_t> str(len);
        MultiByteToWideChar(codePage, 0, input.c_str(), -1, &str[0], len);
        return std::wstring(str.begin(), str.end() - 1);
    }

    std::string Win32WideToMultiByte(const std::wstring& input, uint32_t codePage) {
        int len = WideCharToMultiByte(codePage, 0, input.c_str(), -1, nullptr, 0, nullptr, nullptr);
        if (len <= 0) return {};
        std::vector<char> str(len);
        WideCharToMultiByte(codePage, 0, input.c_str(), -1, &str[0], len, nullptr, nullptr);
        return std::string(str.begin(), str.end() - 1);
    }
#endif

    std::wstring convertStringToWstring(const std::string& inputString) {
#ifdef _WIN32
        return Win32MultiByteToWide(inputString, CP_ACP);
#else
        return Utf8ToWidePortable(inputString);
#endif
    }

    std::string convertWstringToString(const std::wstring& inputWstring) {
#ifdef _WIN32
        return Win32WideToMultiByte(inputWstring, CP_ACP);
#else
        return WideToUtf8Portable(inputWstring);
#endif
    }

    std::string convertWstringToUtf8(const std::wstring& inputWstring) {
#ifdef _WIN32
        return Win32WideToMultiByte(inputWstring, CP_UTF8);
#else
        return WideToUtf8Portable(inputWstring);
#endif
    }

    std::wstring convertUtf8ToWstring(const std::string& inputUtf8) {
#ifdef _WIN32
        return Win32MultiByteToWide(inputUtf8, CP_UTF8);
#else
        return Utf8ToWidePortable(inputUtf8);
#endif
    }

    std::string convertWstringToGbk(const std::wstring& inputWstring) {
#ifdef _WIN32
        return Win32WideToMultiByte(inputWstring, 936);
#else
        return WideToUtf8Portable(inputWstring);
#endif
    }

    std::wstring convertGbkToWstring(const std::string& inputGbk) {
#ifdef _WIN32
        return Win32MultiByteToWide(inputGbk, 936);
#else
        return Utf8ToWidePortable(inputGbk);
#endif
    }

    std::string convertUtf8ToGbk(const std::string& inputUtf8) {
        std::wstring wstr = convertUtf8ToWstring(inputUtf8);
        return convertWstringToGbk(wstr);
    }

    std::string convertGbkToUtf8(const std::string& inputGbk) {
        std::wstring wstr = convertGbkToWstring(inputGbk);
        return convertWstringToUtf8(wstr);
    }

    json GetAllDogInfo() {
        return sntl_admin::DogUtils::GetAllDogInfo();
    }

    // DllLoader类实现
    DllLoader::DllLoader(sntl_admin::DogProvider provider) : dogProvider(provider) {
        switch (provider) {
        case sntl_admin::DogProvider::Unknown:
            // 只执行加密狗检测，不加载推理 DLL
            dllName.clear();
            dllPath.clear();
            dllDevPath.clear();
            return;
        case sntl_admin::DogProvider::Sentinel:
#ifdef _WIN32
            dllName = "dlcv_infer.dll";
            dllPath = "C:\\dlcv\\Lib\\site-packages\\dlcvpro_infer\\dlcv_infer.dll";
#else
            dllName = "libdlcv_infer_s.so";
            dllPath = "/root/dlcv/lib/python3.11/site-packages/dlcvpro_infer/libdlcv_infer_s.so";
#endif
            break;
        case sntl_admin::DogProvider::Virbox:
#ifdef _WIN32
            dllName = "dlcv_infer_v.dll";
            dllPath = "C:\\dlcv\\Lib\\site-packages\\dlcvpro_infer\\dlcv_infer_v.dll";
#else
            dllName = "libdlcv_infer_v.so";
            dllPath = "/root/dlcv/lib/python3.11/site-packages/dlcvpro_infer/libdlcv_infer_v.so";
#endif
            break;
        default:
            throw std::runtime_error("unsupported dog provider");
        }

        std::string selfDir = GetSelfModuleDirectory();
        if (!selfDir.empty()) {
            dllDevPath = JoinPath(selfDir, dllName);
        }

        LoadDll();
    }

    DllLoader::DllLoader(
        sntl_admin::DogProvider provider,
        void* existingModule,
        const std::string& loadedPath)
        : dogProvider(provider), hModule(existingModule) {
        if (hModule == nullptr) {
            throw std::runtime_error("已加载推理 DLL 句柄为空");
        }
#ifdef _WIN32
        dllName = provider == sntl_admin::DogProvider::Sentinel
            ? "dlcv_infer.dll"
            : "dlcv_infer_v.dll";
#else
        dllName = provider == sntl_admin::DogProvider::Sentinel
            ? "libdlcv_infer_s.so"
            : "libdlcv_infer_v.so";
#endif
        dllPath = loadedPath;
        ResolveSymbols();
    }

    void DllLoader::LoadDll() {
#ifdef _WIN32
        const std::string dllCurrentPath = JoinPath(".", dllName);
        if (!DllExists(dllDevPath, dllCurrentPath, dllName, dllPath))
        {
            MessageBoxA(nullptr, "需要先安装 dlcv_infer", "提示", MB_OK | MB_ICONWARNING);
            throw std::runtime_error("need install dlcv_infer first");
        }

        // 1. 开发环境路径（与当前模块同目录）
        if (!dllDevPath.empty()) {
            hModule = LoadLibraryA(dllDevPath.c_str());
        }
        // 2. 当前工作目录
        if (!hModule) {
            hModule = LoadLibraryA(dllCurrentPath.c_str());
        }
        // 3. 可执行文件目录 / 系统搜索路径
        if (!hModule) {
            hModule = LoadLibraryA(dllName.c_str());
        }
        // 4. site-packages 固定路径
        if (!hModule) {
            hModule = LoadLibraryA(dllPath.c_str());
        }
        if (!hModule) {
            throw std::runtime_error("failed to load dll");
        }

        char pathBuffer[MAX_PATH];
        if (GetModuleFileNameA((HMODULE)hModule, pathBuffer, MAX_PATH) > 0) {
            std::cout << "[dlcv_infer] loaded: " << pathBuffer << std::endl;
        } else {
            std::cout << "[dlcv_infer] loaded: (unknown path)" << std::endl;
        }

#else
        const std::string dllCurrentPath = JoinPath(".", dllName);
        std::vector<std::string> candidates;
        // 1. 开发环境路径（与当前模块同目录）
        if (!dllDevPath.empty()) {
            candidates.push_back(dllDevPath);
        }
        // 2. 当前工作目录
        candidates.push_back(dllCurrentPath);
        // 3. site-packages 候选路径（新环境）
        candidates.push_back(dllPath);
        // 4. site-packages 候选路径（旧环境兼容）
        candidates.push_back("/root/miniconda3/lib/python3.11/site-packages/dlcvpro_infer/" + dllName);

        for (const auto& path : candidates) {
            if (path.empty()) continue;
            hModule = dlopen(path.c_str(), RTLD_LAZY | RTLD_LOCAL);
            if (hModule) break;
        }
        // 5. 系统搜索路径（LD_LIBRARY_PATH / rpath 等）
        if (!hModule) {
            hModule = dlopen(dllName.c_str(), RTLD_LAZY | RTLD_LOCAL);
        }
        if (hModule == nullptr)
        {
            const char* err = dlerror();
            throw std::runtime_error(std::string("failed to load dll")
                + (err != nullptr ? (std::string(": ") + err) : std::string()));
        }

        struct link_map* linkMap = nullptr;
        std::string loadedPath;
        if (dlinfo(hModule, RTLD_DI_LINKMAP, &linkMap) == 0 && linkMap && linkMap->l_name) {
            loadedPath = linkMap->l_name;
            if (!loadedPath.empty() && loadedPath[0] != '/') {
                char origin[4096];
                if (dlinfo(hModule, RTLD_DI_ORIGIN, origin) == 0) {
                    loadedPath = std::string(origin) + "/" + loadedPath;
                }
            }
            if (!loadedPath.empty() && loadedPath[0] == '/') {
                std::error_code ec;
                auto canonicalPath = fs::canonical(loadedPath, ec);
                if (!ec) {
                    loadedPath = canonicalPath.string();
                }
            }
        }
        if (!loadedPath.empty()) {
            std::cout << "[dlcv_infer] loaded: " << loadedPath << std::endl;
        } else {
            std::cout << "[dlcv_infer] loaded: (unknown path)" << std::endl;
        }
#endif

        ResolveSymbols();
    }

    void DllLoader::ResolveSymbols() {
        dlcv_load_model = (LoadModelFuncType)ResolveSymbol(hModule, "dlcv_load_model");
        dlcv_load_model_binary = (LoadModelBinaryFuncType)ResolveSymbol(hModule, "dlcv_load_model_binary");
        dlcv_free_model = (FreeModelFuncType)ResolveSymbol(hModule, "dlcv_free_model");
        dlcv_get_model_info = (GetModelInfoFuncType)ResolveSymbol(hModule, "dlcv_get_model_info");
        dlcv_infer = (InferFuncType)ResolveSymbol(hModule, "dlcv_infer");
        dlcv_free_model_result = (FreeModelResultFuncType)ResolveSymbol(hModule, "dlcv_free_model_result");
        dlcv_free_result = (FreeResultFuncType)ResolveSymbol(hModule, "dlcv_free_result");
        dlcv_free_all_models = (FreeAllModelsFuncType)ResolveSymbol(hModule, "dlcv_free_all_models");
        dlcv_get_device_info = (GetDeviceInfoFuncType)ResolveSymbol(hModule, "dlcv_get_device_info");
        dlcv_keep_max_clock = (KeepMaxClockFuncType)ResolveSymbol(hModule, "dlcv_keep_max_clock");
        allocateIndex = (AllocateIndexFunc)ResolveSymbol(hModule, "dlcv_allocate_index_c");
        dlcv_get_index_type_c = (GetIndexTypeFuncType)ResolveSymbol(hModule, "dlcv_get_index_type_c");
        dlcv_get_model_info_c = (GetModelInfoByIndexFuncType)ResolveSymbol(hModule, "dlcv_get_model_info_c");
        dlcv_bind_index_c = (BindIndexFuncType)ResolveSymbol(hModule, "dlcv_bind_index_c");
        dlcv_unbind_index_c = (UnbindIndexFuncType)ResolveSymbol(hModule, "dlcv_unbind_index_c");
        dlcv_free_string = (FreeStringFuncType)ResolveSymbol(hModule, "dlcv_free_result");
        dlcv_get_gpu_info = (GetGpuInfoFuncType)ResolveSymbol(hModule, "dlcv_get_gpu_info");
        dlcv_reset_max_clock = (ResetMaxClockFuncType)ResolveSymbol(hModule, "dlcv_reset_max_clock");
        dlcv_set_gpu_max_clock = (SetGpuMaxClockFuncType)ResolveSymbol(hModule, "dlcv_set_gpu_max_clock");
        dlcv_reset_gpu_max_clock = (ResetGpuMaxClockFuncType)ResolveSymbol(hModule, "dlcv_reset_gpu_max_clock");
        dlcv_get_power_scheme_guid = (GetPowerSchemeGuidFuncType)ResolveSymbol(hModule, "dlcv_get_power_scheme_guid");
        dlcv_set_power_scheme_guid = (SetPowerSchemeGuidFuncType)ResolveSymbol(hModule, "dlcv_set_power_scheme_guid");
        dlcv_get_power_scheme = (GetPowerSchemeFuncType)ResolveSymbol(hModule, "dlcv_get_power_scheme");
        dlcv_set_power_scheme = (SetPowerSchemeFuncType)ResolveSymbol(hModule, "dlcv_set_power_scheme");
        dlcv_set_current_process_affinity_to_big_cores =
            (SetCurrentProcessAffinityToBigCoresFuncType)ResolveSymbol(
                hModule,
                "dlcv_set_current_process_affinity_to_big_cores");
        dlcv_set_current_process_priority_highest =
            (SetCurrentProcessPriorityHighestFuncType)ResolveSymbol(
                hModule,
                "dlcv_set_current_process_priority_highest");
        dlcv_load_model_c = (LoadModelCFuncType)ResolveSymbol(hModule, "dlcv_load_model_c");
        dlcv_free_model_c = (FreeModelCFuncType)ResolveSymbol(hModule, "dlcv_free_model_c");
        dlcv_infer_c = (InferCFuncType)ResolveSymbol(hModule, "dlcv_infer_c");
        dlcv_free_model_result_c =
            (FreeModelResultCFuncType)ResolveSymbol(hModule, "dlcv_free_model_result_c");
    }

    sntl_admin::DogProvider DllLoader::AutoDetectProvider() {
        // 只做一次加密狗检测：先 Sentinel 再 Virbox；都没有则不加载任何推理 DLL，也不抛异常
        auto available = sntl_admin::DogUtils::GetAvailableProviders();
        for (const auto& p : available) {
            if (p == sntl_admin::DogProvider::Sentinel) {
                return sntl_admin::DogProvider::Sentinel;
            }
        }
        for (const auto& p : available) {
            if (p == sntl_admin::DogProvider::Virbox) {
                return sntl_admin::DogProvider::Virbox;
            }
        }
        return sntl_admin::DogProvider::Unknown;
    }

    DllLoader& DllLoader::Instance() {
        DllLoader* defaultLoader = nullptr;
        {
            std::lock_guard<std::mutex> lock(DefaultLoaderMutex());
            defaultLoader = DefaultLoaderSlot();
        }
        if (defaultLoader != nullptr) return *defaultLoader;

        DllLoader& detectedLoader = GetOrCreateForProvider(AutoDetectProvider());
        {
            std::lock_guard<std::mutex> lock(DefaultLoaderMutex());
            DllLoader*& currentDefaultLoader = DefaultLoaderSlot();
            if (currentDefaultLoader == nullptr) {
                currentDefaultLoader = &detectedLoader;
            }
            return *currentDefaultLoader;
        }
    }

    DllLoader& DllLoader::GetOrCreateForProvider(sntl_admin::DogProvider provider) {
        std::lock_guard<std::mutex> lock(DllLoaderRegistryMutex());
        auto& loaders = DllLoaderRegistry();
        std::unique_ptr<DllLoader> loader(new DllLoader(provider));
        void* module = loader->NativeModuleIdentity();
        const auto existing = loaders.find(module);
        if (existing != loaders.end()) {
            // 同一模块可能先由另一语言加载；撤销本次加载增加的引用并复用已有 loader。
            if (module != nullptr) {
#ifdef _WIN32
                FreeLibrary(static_cast<HMODULE>(module));
#else
                dlclose(module);
#endif
            }
            return *existing->second;
        }
        const auto inserted = loaders.emplace(module, std::move(loader));
        return *inserted.first->second;
    }

    DllLoader& DllLoader::GetOrCreateForExistingModule(
        sntl_admin::DogProvider provider,
        void* module,
        const std::string& loadedPath) {
        if (module == nullptr) {
            throw std::invalid_argument("已加载推理 DLL 句柄为空");
        }

        std::lock_guard<std::mutex> lock(DllLoaderRegistryMutex());
        auto& loaders = DllLoaderRegistry();
        auto it = loaders.find(module);
        if (it == loaders.end()) {
            it = loaders.emplace(
                module,
                std::unique_ptr<DllLoader>(new DllLoader(provider, module, loadedPath))).first;
        }
        return *it->second;
    }

    DllLoader& DllLoader::GetExistingOrDefaultSentinel() {
        DllLoader* defaultLoader = nullptr;
        {
            std::lock_guard<std::mutex> lock(DefaultLoaderMutex());
            defaultLoader = DefaultLoaderSlot();
        }
        if (defaultLoader != nullptr &&
            defaultLoader->GetDogProvider() != sntl_admin::DogProvider::Unknown) {
            return *defaultLoader;
        }

        DllLoader& sentinelLoader = GetOrCreateForProvider(sntl_admin::DogProvider::Sentinel);
        {
            std::lock_guard<std::mutex> lock(DefaultLoaderMutex());
            DllLoader*& currentDefaultLoader = DefaultLoaderSlot();
            if (currentDefaultLoader == nullptr ||
                currentDefaultLoader->GetDogProvider() == sntl_admin::DogProvider::Unknown) {
                currentDefaultLoader = &sentinelLoader;
            }
            return *currentDefaultLoader;
        }
    }

    int DllLoader::QueryIndexType(int index) const {
        if (!dlcv_get_index_type_c) throw std::runtime_error("缺少模型索引查询接口");
        const int nativeType = dlcv_get_index_type_c(index);
        if (nativeType != 0) return nativeType;
        const int flow = openivs_flow_contains(hModule, index);
        if (flow < 0) throw std::runtime_error("流程索引查询失败");
        return flow ? 2 : 0;
    }

    int DllLoader::BindIndex(int index) const {
        if (QueryIndexType(index) == 2) return openivs_flow_retain(hModule, index);
        return dlcv_bind_index_c ? dlcv_bind_index_c(index) : -1;
    }

    int DllLoader::UnbindIndex(int index) const {
        // 释放不依赖子模型是否仍有效，全量释放后也能清理流程记录。
        const int result = openivs_flow_release(hModule, index, 0);
        return result == 0 ? 0 : (dlcv_unbind_index_c ? dlcv_unbind_index_c(index) : -1);
    }

    int DllLoader::RegisterFlow(const char* text) const {
        return openivs_flow_register(hModule, text);
    }

    json DllLoader::GetFlowInfo(int index) const {
        std::unique_ptr<const char, decltype(&openivs_flow_free_result)> result(
            openivs_flow_get_info(hModule, index), openivs_flow_free_result);
        if (!result) throw std::runtime_error("读取流程信息失败");
        return json::parse(result.get());
    }

    int DllLoader::FreeFlow(int index) const {
        return openivs_flow_release(hModule, index, 1);
    }

    DllLoader& DllLoader::ResolveForIndex(int index, int& indexType) {
        if (index < 0) {
            throw std::invalid_argument("共享 index 无效");
        }

        std::vector<detail::SharedIndexCandidate> candidates;
        std::unordered_set<void*> seenModules;
        for (const auto& module : EnumerateLoadedInferModules()) {
            if (module.Module == nullptr || !seenModules.insert(module.Module).second) {
                continue;
            }
            DllLoader& loader = GetOrCreateForExistingModule(
                module.Provider,
                module.Module,
                module.Path);
            candidates.push_back({
                module.Module,
                &loader,
                loader.GetIndexTypeFunc() ? std::function<int(int)>([p = &loader](int value) { return p->QueryIndexType(value); }) : nullptr
            });
        }

        const detail::SharedIndexCandidate& selected =
            detail::SelectSharedIndexCandidate(index, candidates, indexType);
        if (selected.Loader == nullptr) {
            throw std::runtime_error("共享 index 没有关联推理 DLL");
        }
        return *selected.Loader;
    }

    namespace {
        bool TryResolveExplicitProviderFromHeaderJson(
            const std::string& headerJsonStr,
            sntl_admin::DogProvider& outProvider) {
            auto headerJson = nlohmann::json::parse(headerJsonStr);
            if (!headerJson.contains("dog_provider")) {
                return false;
            }
            std::string providerName = headerJson["dog_provider"].get<std::string>();
            for (auto& c : providerName) {
                c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
            }
            if (providerName == "sentinel") {
                outProvider = sntl_admin::DogProvider::Sentinel;
                return true;
            }
            if (providerName == "virbox") {
                outProvider = sntl_admin::DogProvider::Virbox;
                return true;
            }
            throw std::runtime_error("模型文件中的 dog_provider 无效: " + providerName);
        }

        bool TryResolveExplicitProviderFromStream(std::istream& stream, sntl_admin::DogProvider& outProvider) {
            std::string header;
            std::string headerJsonStr;
            std::getline(stream, header);
            std::getline(stream, headerJsonStr);
            if (header != "DV") {
                throw std::runtime_error("invalid model format: missing DV header");
            }
            return TryResolveExplicitProviderFromHeaderJson(headerJsonStr, outProvider);
        }

        bool TryResolveExplicitProviderFromBuffer(
            const unsigned char* modelData,
            size_t modelSize,
            sntl_admin::DogProvider& outProvider) {
            if (modelData == nullptr || modelSize < 4) {
                throw std::runtime_error("子模型数据为空或不完整");
            }
            const unsigned char* dataEnd = modelData + modelSize;
            const unsigned char* firstLineEnd = std::find(
                modelData, dataEnd, static_cast<unsigned char>('\n'));
            const std::string magicLine(
                reinterpret_cast<const char*>(modelData),
                firstLineEnd == dataEnd ? modelSize : static_cast<size_t>(firstLineEnd - modelData));
            if (firstLineEnd == dataEnd || magicLine != "DV") {
                throw std::runtime_error("子模型格式无效: 缺少 DV 文件头");
            }
            const unsigned char* headerStart = firstLineEnd + 1;
            const unsigned char* headerEnd = std::find(
                headerStart, dataEnd, static_cast<unsigned char>('\n'));
            if (headerEnd == dataEnd) {
                throw std::runtime_error("子模型格式无效: 缺少头信息");
            }
            const std::string headerJsonStr(
                reinterpret_cast<const char*>(headerStart),
                static_cast<size_t>(headerEnd - headerStart));
            return TryResolveExplicitProviderFromHeaderJson(headerJsonStr, outProvider);
        }

    }

    static void EnsureProviderAvailable(sntl_admin::DogProvider needed) {
#ifdef _WIN32
        const auto dogInfo = needed == sntl_admin::DogProvider::Sentinel
            ? sntl_admin::DogUtils::GetSentinelInfo()
            : sntl_admin::DogUtils::GetVirboxInfo();
        if (dogInfo.provider == sntl_admin::DogProvider::Unknown) {
            throw std::runtime_error(std::string("模型要求 provider ")
                + (needed == sntl_admin::DogProvider::Sentinel ? "Sentinel" : "Virbox")
                + "，但未检测到对应的加密狗设备或特性");
        }
#else
        (void)needed;
#endif
    }

    DllLoader& DllLoader::GetOrSelectDefaultForModelProvider(
        sntl_admin::DogProvider provider,
        bool hasExplicitProvider) {
        {
            std::lock_guard<std::mutex> lock(DefaultLoaderMutex());
            if (DefaultLoaderSlot() != nullptr) return *DefaultLoaderSlot();
        }
        if (!hasExplicitProvider) return Instance();

        DllLoader& modelLoader = GetOrCreateForProvider(provider);
        std::lock_guard<std::mutex> lock(DefaultLoaderMutex());
        DllLoader*& defaultLoader = DefaultLoaderSlot();
        if (defaultLoader == nullptr) defaultLoader = &modelLoader;
        return *defaultLoader;
    }

    DllLoader& DllLoader::ForModelBuffer(const unsigned char* modelData, size_t modelSize) {
        sntl_admin::DogProvider needed = sntl_admin::DogProvider::Unknown;
        const bool hasExplicitProvider =
            TryResolveExplicitProviderFromBuffer(modelData, modelSize, needed);
        if (hasExplicitProvider) EnsureProviderAvailable(needed);
        return GetOrSelectDefaultForModelProvider(needed, hasExplicitProvider);
    }

    DllLoader& DllLoader::EnsureForModel(const std::string& modelPath) {
        return EnsureForModel(convertUtf8ToWstring(modelPath));
    }

    DllLoader& DllLoader::EnsureForModel(const std::wstring& modelPath) {
#ifdef _WIN32
        std::ifstream file(modelPath);
#else
        std::ifstream file(WideToUtf8Portable(modelPath));
#endif
        if (!file) {
            throw std::runtime_error("failed to open model file");
        }
        sntl_admin::DogProvider needed = sntl_admin::DogProvider::Unknown;
        const bool hasExplicitProvider = TryResolveExplicitProviderFromStream(file, needed);
        if (hasExplicitProvider) EnsureProviderAvailable(needed);
        return GetOrSelectDefaultForModelProvider(needed, hasExplicitProvider);
    }

    static json ReadSharedIndexResult(DllLoader* loader, const char* resultPtr) {
        if (loader == nullptr || loader->GetFreeStringFunc() == nullptr || resultPtr == nullptr) {
            throw std::runtime_error("共享索引接口未返回结果");
        }
        json result;
        try {
            result = json::parse(resultPtr);
        } catch (...) {
            loader->GetFreeStringFunc()(resultPtr);
            throw;
        }
        loader->GetFreeStringFunc()(resultPtr);
        return result;
    }

    static bool HasSharedIndexFunctions(const DllLoader* loader) {
        return loader != nullptr &&
            loader->GetIndexTypeFunc() != nullptr &&
            loader->GetModelInfoByIndexFunc() != nullptr &&
            loader->GetBindIndexFunc() != nullptr &&
            loader->GetUnbindIndexFunc() != nullptr &&
            loader->GetFreeStringFunc() != nullptr;
    }

    static void EnsureSharedIndexFunctions(DllLoader* loader) {
        if (!HasSharedIndexFunctions(loader)) {
            throw std::runtime_error("dlcv_infer 不支持共享模型索引接口");
        }
    }

    static std::string ProviderToRegistryName(sntl_admin::DogProvider provider) {
        if (provider == sntl_admin::DogProvider::Sentinel) return "sentinel";
        if (provider == sntl_admin::DogProvider::Virbox) return "virbox";
        throw std::runtime_error("无法确定流程 provider");
    }

    static DllLoader* ResolveFlowRegistrationLoader(
        const std::vector<std::shared_ptr<dlcv_infer::Model>>& childModels) {
        DllLoader* selectedLoader = nullptr;
        for (const auto& childModel : childModels) {
            if (!childModel || childModel->modelIndex < 0) {
                throw std::runtime_error("流程模型绑定格式无效");
            }
            DllLoader* childLoader = childModel->LoadedDllLoader();
            if (childLoader == nullptr) {
                throw std::runtime_error("流程子模型没有关联推理 DLL");
            }
            if (selectedLoader == nullptr) {
                selectedLoader = childLoader;
            } else if (selectedLoader->NativeModuleIdentity() != childLoader->NativeModuleIdentity()) {
                throw std::runtime_error("同一流程不能混用不同推理 DLL");
            }
        }
        return selectedLoader != nullptr
            ? selectedLoader
            : &DllLoader::GetExistingOrDefaultSentinel();
    }

    static void EnsureArchiveCanLoadInMemory(const DvsArchiveData& archive) {
        if (!archive.pipelineRoot.is_object() ||
            !archive.pipelineRoot.contains("nodes") ||
            !archive.pipelineRoot.at("nodes").is_array() ||
            !archive.modelBinaryStore) {
            throw std::runtime_error("流程归档信息不完整");
        }

        sntl_admin::DogProvider archiveProvider = sntl_admin::DogProvider::Unknown;
        for (const auto& node : archive.pipelineRoot.at("nodes")) {
            if (!node.is_object() || node.value("type", std::string()).rfind("model/", 0) != 0) continue;
            if (!node.contains("properties") || !node.at("properties").is_object()) {
                throw std::runtime_error("流程模型节点缺少 properties");
            }
            const auto& properties = node.at("properties");
            const std::string bufferKey = properties.value("model_buffer_key", std::string());
            const auto bufferIt = archive.modelBinaryStore->Buffers.find(bufferKey);
            if (bufferKey.empty() || bufferIt == archive.modelBinaryStore->Buffers.end() ||
                !bufferIt->second || bufferIt->second->empty()) {
                throw std::runtime_error("流程模型节点缺少有效的子模型数据");
            }

            sntl_admin::DogProvider modelProvider;
            if (TryResolveExplicitProviderFromBuffer(
                    bufferIt->second->data(), bufferIt->second->size(), modelProvider)) {
                if (archiveProvider == sntl_admin::DogProvider::Unknown) {
                    archiveProvider = modelProvider;
                } else if (archiveProvider != modelProvider) {
                    throw std::runtime_error("同一流程不能混用不同授权 provider 的子模型");
                }
            }
            DllLoader& loader = DllLoader::ForModelBuffer(
                bufferIt->second->data(), bufferIt->second->size());
            if (loader.GetLoadModelBinaryFunc() == nullptr) {
                throw std::runtime_error("当前推理 DLL 缺少 dlcv_load_model_binary，无法从内存加载流程子模型");
            }
        }
    }

    static bool CanRegisterSharedFlow(
        DllLoader* registrationLoader,
        const std::vector<std::shared_ptr<dlcv_infer::Model>>& childModels) {
        if (!HasSharedIndexFunctions(registrationLoader) || !registrationLoader->SupportsFlowRegistry()) return false;
        for (const auto& childModel : childModels) {
            if (!childModel) return false;
            DllLoader* childLoader = childModel->LoadedDllLoader();
            if (childLoader == nullptr ||
                childLoader->NativeModuleIdentity() != registrationLoader->NativeModuleIdentity() ||
                !HasSharedIndexFunctions(childLoader)) {
                return false;
            }
        }
        return true;
    }

    static std::string FlowTypeFromPath(const std::wstring& modelPath) {
        std::string extension = GetExtensionWithDot(convertWstringToUtf8(modelPath));
        if (!extension.empty() && extension.front() == '.') extension.erase(extension.begin());
        for (char& ch : extension) ch = static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
        if (extension != "dvst" && extension != "dvso") {
            throw std::runtime_error("不支持的流程模型类型");
        }
        return extension;
    }

    static std::string AbsolutePathUtf8(const std::wstring& path) {
#ifdef _WIN32
        const DWORD requiredSize = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
        if (requiredSize == 0) throw std::runtime_error("无法获取流程模型绝对路径");
        std::vector<wchar_t> buffer(requiredSize, L'\0');
        const DWORD length = GetFullPathNameW(
            path.c_str(),
            static_cast<DWORD>(buffer.size()),
            buffer.data(),
            nullptr);
        if (length == 0 || length >= buffer.size()) {
            throw std::runtime_error("无法获取流程模型绝对路径");
        }
        return convertWstringToUtf8(std::wstring(buffer.data(), length));
#else
        std::error_code ec;
        const fs::path absolutePath = fs::absolute(fs::path(path), ec);
        if (ec) throw std::runtime_error("无法获取流程模型绝对路径");
        return convertWstringToUtf8(absolutePath.wstring());
#endif
    }

    static std::set<int> CollectFlowModelNodeIds(const json& pipelineRoot) {
        if (!pipelineRoot.is_object() || !pipelineRoot.contains("nodes") || !pipelineRoot.at("nodes").is_array()) {
            throw std::runtime_error("流程文件缺少 nodes");
        }
        std::set<int> nodeIds;
        for (const auto& node : pipelineRoot.at("nodes")) {
            if (!node.is_object()) continue;
            const std::string type = node.value("type", std::string());
            if (type.rfind("model/", 0) != 0) continue;
            const int nodeId = node.value("id", -1);
            if (nodeId < 0 || !nodeIds.insert(nodeId).second) {
                throw std::runtime_error("流程模型节点 id 无效");
            }
        }
        return nodeIds;
    }

    bool Model::UnbindCurrentIndexNoexcept() {
        if (!_indexBound) return true;
        if (modelIndex < 0 || _dllLoader == nullptr || _dllLoader->GetUnbindIndexFunc() == nullptr) {
            return false;
        }
        try {
            if (_dllLoader->UnbindIndex(modelIndex) != 0) return false;
            _indexBound = false;
            _indexReady = false;
            return true;
        } catch (...) {
            return false;
        }
    }

    void Model::LoadFlowArchiveAndRegister(const std::wstring& modelPath, int deviceId) {
        _isFlowGraphMode = true;
        _flowModel = new flow::FlowGraphModel();
        DvsArchiveData archive = ReadDvsArchive(modelPath);
        const json& originalPipelineRoot = archive.originalPipelineRoot;
        EnsureArchiveCanLoadInMemory(archive);
        const json report = _flowModel->LoadFromArchive(
            archive.pipelineRoot,
            archive.modelBinaryStore,
            deviceId);
        if (report.value("code", 1) != 0) {
            throw std::runtime_error(report.dump());
        }

        const std::set<int> expectedNodeIds = CollectFlowModelNodeIds(originalPipelineRoot);
        if (!report.contains("models") || !report.at("models").is_array()) {
            throw std::runtime_error("流程模型加载结果缺少模型节点信息");
        }

        json modelBindings = json::array();
        std::vector<std::shared_ptr<dlcv_infer::Model>> childModels;
        std::set<int> boundNodeIds;
        for (const auto& item : report.at("models")) {
            if (!item.is_object() || item.value("status_code", 1) != 0) {
                throw std::runtime_error("流程模型节点加载失败");
            }
            const int nodeId = item.value("node_id", -1);
            const int childModelIndex = item.value("model_index", -1);
            if (nodeId < 0 || childModelIndex < 0 ||
                expectedNodeIds.find(nodeId) == expectedNodeIds.end() ||
                !boundNodeIds.insert(nodeId).second) {
                throw std::runtime_error("流程模型绑定无效");
            }

            const std::shared_ptr<dlcv_infer::Model> childModel =
                _flowModel->GetLoadedModelByIndex(childModelIndex);
            if (!childModel) {
                throw std::runtime_error("流程模型没有保留加载实例");
            }
            childModels.push_back(childModel);
            modelBindings.push_back({
                {"node_id", nodeId},
                {"model_index", childModelIndex}
            });
        }
        if (boundNodeIds != expectedNodeIds) {
            throw std::runtime_error("流程模型绑定不完整");
        }

        _dllLoader = ResolveFlowRegistrationLoader(childModels);
        if (_dllLoader != nullptr) {
            _loadedDogProvider = _dllLoader->GetDogProvider();
            _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();
        }
        if (!CanRegisterSharedFlow(_dllLoader, childModels)) {
            modelIndex = AllocateFlowModelIndex(_loadedDogProvider);
            if (modelIndex < 0) {
                throw std::runtime_error("本地流程索引范围已用尽");
            }
            _indexReady = true;
            return;
        }

        json flowRegistration = json::object();
        flowRegistration["schema_version"] = 1;
        flowRegistration["flow_type"] = FlowTypeFromPath(modelPath);
        flowRegistration["provider"] = ProviderToRegistryName(_loadedDogProvider);
        flowRegistration["source_path"] = AbsolutePathUtf8(modelPath);
        flowRegistration["device_id"] = deviceId;
        flowRegistration["pipeline"] = originalPipelineRoot;
        flowRegistration["model_bindings"] = std::move(modelBindings);

        const std::string registrationText = flowRegistration.dump();
        const int flowIndex = _dllLoader->RegisterFlow(registrationText.c_str());
        if (flowIndex < 0) {
            throw std::runtime_error("注册流程失败");
        }
        modelIndex = flowIndex;
        _ownsRegisteredFlowIndex = true;
        _indexReady = true;
    }

    void Model::RestoreFlowFromSharedInfo(const json& flowInfo) {
        if (!flowInfo.is_object() || !flowInfo.contains("provider") || !flowInfo.at("provider").is_string() ||
            !flowInfo.contains("pipeline") || !flowInfo.at("pipeline").is_object() ||
            !flowInfo.contains("model_bindings") || !flowInfo.at("model_bindings").is_array()) {
            throw std::runtime_error("共享流程信息不完整");
        }
        if (flowInfo.at("provider").get<std::string>() != ProviderToRegistryName(_loadedDogProvider)) {
            throw std::runtime_error("流程 provider 与索引所属推理 DLL 不一致");
        }

        json pipelineRoot = flowInfo.at("pipeline");
        const std::set<int> expectedNodeIds = CollectFlowModelNodeIds(pipelineRoot);
        std::unordered_map<int, int> modelBindings;
        for (const auto& item : flowInfo.at("model_bindings")) {
            if (!item.is_object() || !item.contains("node_id") || !item.contains("model_index")) {
                throw std::runtime_error("共享流程模型绑定无效");
            }
            const int nodeId = item.at("node_id").get<int>();
            const int modelIndexValue = item.at("model_index").get<int>();
            if (modelIndexValue < 0 || expectedNodeIds.find(nodeId) == expectedNodeIds.end() ||
                !modelBindings.emplace(nodeId, modelIndexValue).second) {
                throw std::runtime_error("共享流程模型绑定无效");
            }
        }
        if (modelBindings.size() != expectedNodeIds.size()) {
            throw std::runtime_error("共享流程模型绑定不完整");
        }

        for (auto& node : pipelineRoot["nodes"]) {
            if (!node.is_object()) continue;
            const auto binding = modelBindings.find(node.value("id", -1));
            if (binding == modelBindings.end()) continue;
            if (!node.contains("properties") || !node.at("properties").is_object()) {
                node["properties"] = json::object();
            }
            node["properties"]["model_index"] = binding->second;
        }

        _deviceId = flowInfo.value("device_id", _deviceId);
        _isFlowGraphMode = true;
        _flowModel = new flow::FlowGraphModel();
        _flowModel->SetPreferredDllLoader(_dllLoader);
        const json report = _flowModel->LoadFromRoot(pipelineRoot, _deviceId, nullptr);
        if (report.value("code", 1) != 0) {
            throw std::runtime_error("恢复流程失败: " + report.dump());
        }
        _cachedModelInfo = _flowModel->GetModelInfo();
        _hasCachedModelInfo = true;
    }

    void Model::SetPreferredDllLoader(DllLoader* loader) noexcept {
        _dllLoader = loader;
    }

    void Model::EnsureBoundIndexReady() {
        std::lock_guard<std::mutex> lock(_indexStateMu);
        if (modelIndex < 0) return;
        if (_indexReady || _ownsNativeModelIndex || _ownsRegisteredFlowIndex) {
            // 全局释放可能由另一语言发起，缓存信息不能证明底层资源仍然存在。
            // 只检查已保存的所属模块，不按默认 DLL 或编号重新选择模块。
            const bool isNativeIndex = _ownsNativeModelIndex || _ownsRegisteredFlowIndex || _indexBound;
            if (isNativeIndex && _dllLoader != nullptr && _dllLoader->GetIndexTypeFunc() != nullptr) {
                const int indexType = _dllLoader->QueryIndexType(modelIndex);
                const int expectedType = _isFlowGraphMode ? 2 : 1;
                if (indexType != expectedType) {
                    throw std::runtime_error("已保存的 index 在所属推理 DLL 中不可用");
                }
            }
            return;
        }

        try {
            int indexType = 0;
            if (!_indexBound && _dllLoader == nullptr) {
                DllLoader* resolvedLoader = &DllLoader::ResolveForIndex(modelIndex, indexType);
                _dllLoader = resolvedLoader;
                // 先保存所属加载器，再检查共享接口；失败后仍固定使用该加载器。
                EnsureSharedIndexFunctions(_dllLoader);
            } else {
                if (_dllLoader == nullptr) throw std::runtime_error("共享索引没有关联推理 DLL");
                EnsureSharedIndexFunctions(_dllLoader);
                indexType = _dllLoader->QueryIndexType(modelIndex);
                if (indexType != 1 && indexType != 2) {
                    throw std::runtime_error("共享 index 不可用");
                }
            }

            if (!_indexBound) {
                if (_dllLoader->BindIndex(modelIndex) != 0) {
                    throw std::runtime_error("绑定索引失败");
                }
                _indexBound = true;
            }
            _loadedDogProvider = _dllLoader->GetDogProvider();
            _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();

            if (indexType == 1) {
                _cachedModelInfo = ReadSharedIndexResult(
                    _dllLoader, _dllLoader->GetModelInfoByIndexFunc()(modelIndex));
                if (_cachedModelInfo.value("code", 1) != 0) {
                    throw std::runtime_error("读取模型信息失败: " + _cachedModelInfo.dump());
                }
                _hasCachedModelInfo = true;
            } else if (indexType == 2) {
                const json flowInfo = _dllLoader->GetFlowInfo(modelIndex);
                if (flowInfo.value("code", 1) != 0) {
                    throw std::runtime_error("读取流程信息失败: " + flowInfo.dump());
                }
                RestoreFlowFromSharedInfo(flowInfo);
            } else {
                throw std::runtime_error("索引不可用");
            }
            _indexReady = true;
        } catch (...) {
            delete _flowModel;
            _flowModel = nullptr;
            _isFlowGraphMode = false;
            _hasCachedModelInfo = false;
            _cachedModelInfo = json();
            _indexReady = false;
            if (_indexBound) {
                (void)UnbindCurrentIndexNoexcept();
            }
            throw;
        }
    }

    // Model类实现
    Model::Model() {}

    Model::Model(const std::string& modelPath, int device_id)
        : _deviceId(device_id) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        const std::wstring modelPathW = DecodeModelPathString(modelPath);
        const std::string modelPathUtf8 = convertWstringToUtf8(modelPathW);
        if (IsUnsupportedDvspPath(modelPathUtf8)) {
            throw std::invalid_argument("不支持 .dvsp 模型推理");
        }
        if (IsFlowArchivePath(modelPathUtf8)) {
            try {
                LoadFlowArchiveAndRegister(modelPathW, device_id);
                return;
            } catch (const std::exception& ex) {
                delete _flowModel;
                _flowModel = nullptr;
                throw std::runtime_error(std::string("failed to load dvs model: ") + ex.what());
            }
        }

        std::lock_guard<std::mutex> modelLoadLock(g_modelLoadMu);
        _dllLoader = &DllLoader::EnsureForModel(modelPathUtf8);
        _loadedDogProvider = _dllLoader->GetDogProvider();
        _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();
        if (!_dllLoader->GetLoadModelFunc()) {
            throw std::runtime_error("未检测到授权");
        }

        modelIndex = LoadModelIndexByPath(_dllLoader, modelPathUtf8, device_id);
        _ownsNativeModelIndex = modelIndex >= 0;
        _indexReady = _ownsNativeModelIndex;
    }

    Model::Model(const std::wstring& modelPath, int device_id)
        : _deviceId(device_id) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        const std::string modelPathUtf8 = convertWstringToUtf8(modelPath);
        if (IsUnsupportedDvspPath(modelPathUtf8)) {
            throw std::invalid_argument("不支持 .dvsp 模型推理");
        }
        if (IsFlowArchivePath(modelPathUtf8)) {
            try {
                LoadFlowArchiveAndRegister(modelPath, device_id);
                return;
            } catch (const std::exception& ex) {
                delete _flowModel;
                _flowModel = nullptr;
                throw std::runtime_error(std::string("failed to load dvs model: ") + ex.what());
            }
        }

        std::lock_guard<std::mutex> modelLoadLock(g_modelLoadMu);
        _dllLoader = &DllLoader::EnsureForModel(modelPath);
        _loadedDogProvider = _dllLoader->GetDogProvider();
        _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();
        if (!_dllLoader->GetLoadModelFunc()) {
            throw std::runtime_error("未检测到授权");
        }

        modelIndex = LoadModelIndexByPath(_dllLoader, modelPathUtf8, device_id);
        _ownsNativeModelIndex = modelIndex >= 0;
        _indexReady = _ownsNativeModelIndex;
    }

    Model::Model(
        std::shared_ptr<const std::vector<unsigned char>> modelData,
        const std::string& modelName,
        int device_id)
        : _deviceId(device_id) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::lock_guard<std::mutex> modelLoadLock(g_modelLoadMu);
        if (!modelData || modelData->empty()) {
            throw std::invalid_argument("子模型数据为空");
        }
        const std::string displayName = modelName.empty() ? "未命名子模型" : modelName;

        _dllLoader = &DllLoader::ForModelBuffer(modelData->data(), modelData->size());
        _loadedDogProvider = _dllLoader->GetDogProvider();
        _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();
        const auto loadModelBinary = _dllLoader->GetLoadModelBinaryFunc();
        if (loadModelBinary == nullptr || _dllLoader->GetFreeResultFunc() == nullptr) {
            throw std::runtime_error("当前 dlcv_infer 缺少二进制模型加载或结果释放接口: " + displayName);
        }

        json config;
        config["device_id"] = device_id;
        const std::string jsonStr = config.dump();

        const char* resultPtr = loadModelBinary(
            modelData->data(),
            modelData->size(),
            jsonStr.c_str());
        if (resultPtr == nullptr) {
            throw std::runtime_error("二进制模型加载未返回结果");
        }

        try {
            const std::string resultJson(static_cast<const char*>(resultPtr));
            const json resultObject = json::parse(resultJson);
            if (!resultObject.contains("model_index")) {
                throw std::runtime_error(
                    "二进制模型加载失败: " + displayName + ": " + resultObject.dump());
            }
            modelIndex = resultObject.at("model_index").get<int>();
        } catch (...) {
            _dllLoader->GetFreeResultFunc()(resultPtr);
            throw;
        }
        _dllLoader->GetFreeResultFunc()(resultPtr);
        if (modelIndex < 0) {
            throw std::runtime_error("二进制模型加载返回的 index 无效: " + displayName);
        }
        _ownsNativeModelIndex = true;
        _indexReady = true;
    }

    Model::Model(Model&& other) noexcept {
        std::unique_lock<std::shared_mutex> otherStateLock(other._stateMutex);
        std::lock_guard<std::mutex> otherModelInfoLock(other._modelInfoMutex);
        modelIndex = other.modelIndex;
        OwnModelIndex = other.OwnModelIndex;
        _isFlowGraphMode = other._isFlowGraphMode;
        _deviceId = other._deviceId;
        _flowModel = other._flowModel;
        _expectedChCache = other._expectedChCache;
        _hasCachedModelInfo = other._hasCachedModelInfo;
        _cachedModelInfo = std::move(other._cachedModelInfo);
        _dllLoader = other._dllLoader;
        _loadedDogProvider = other._loadedDogProvider;
        _loadedNativeDllName = std::move(other._loadedNativeDllName);

        _indexBound = other._indexBound;
        _indexReady = other._indexReady;
        _ownsNativeModelIndex = other._ownsNativeModelIndex;
        _ownsRegisteredFlowIndex = other._ownsRegisteredFlowIndex;
        other.modelIndex = -1;
        other.OwnModelIndex = true;
        other._isFlowGraphMode = false;
        other._deviceId = 0;
        other._flowModel = nullptr;
        other._expectedChCache = -2;
        other._hasCachedModelInfo = false;
        other._cachedModelInfo = json();
        other._indexBound = false;
        other._indexReady = false;
        other._ownsNativeModelIndex = false;
        other._ownsRegisteredFlowIndex = false;
        other._dllLoader = nullptr;
        other._loadedDogProvider = sntl_admin::DogProvider::Unknown;
        other._loadedNativeDllName.clear();
    }

    Model& Model::operator=(Model&& other) noexcept {
        if (this == &other) {
            return *this;
        }

        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::unique_lock<std::shared_mutex> stateLock(_stateMutex, std::defer_lock);
        std::unique_lock<std::shared_mutex> otherStateLock(other._stateMutex, std::defer_lock);
        std::lock(stateLock, otherStateLock);
        try { freeModelLocked(); } catch (...) {}
        std::unique_lock<std::mutex> modelInfoLock(_modelInfoMutex, std::defer_lock);
        std::unique_lock<std::mutex> otherModelInfoLock(other._modelInfoMutex, std::defer_lock);
        std::lock(modelInfoLock, otherModelInfoLock);
        modelIndex = other.modelIndex;
        OwnModelIndex = other.OwnModelIndex;
        _isFlowGraphMode = other._isFlowGraphMode;
        _deviceId = other._deviceId;
        _flowModel = other._flowModel;
        _expectedChCache = other._expectedChCache;
        _hasCachedModelInfo = other._hasCachedModelInfo;
        _cachedModelInfo = std::move(other._cachedModelInfo);
        _indexBound = other._indexBound;
        _indexReady = other._indexReady;
        _ownsNativeModelIndex = other._ownsNativeModelIndex;
        _ownsRegisteredFlowIndex = other._ownsRegisteredFlowIndex;
        _dllLoader = other._dllLoader;
        _loadedDogProvider = other._loadedDogProvider;
        _loadedNativeDllName = std::move(other._loadedNativeDllName);

        other.modelIndex = -1;
        other.OwnModelIndex = true;
        other._isFlowGraphMode = false;
        other._deviceId = 0;
        other._flowModel = nullptr;
        other._expectedChCache = -2;
        other._hasCachedModelInfo = false;
        other._cachedModelInfo = json();
        other._indexBound = false;
        other._indexReady = false;
        other._ownsNativeModelIndex = false;
        other._ownsRegisteredFlowIndex = false;
        other._dllLoader = nullptr;
        other._loadedDogProvider = sntl_admin::DogProvider::Unknown;
        other._loadedNativeDllName.clear();
        return *this;
    }

    Model::~Model() {
        try { FreeModel(); } catch (...) {}
    }

    void Model::FreeModel() {
        try {
            flow::ModelLifecycleReadGuard lifecycleGuard;
            std::unique_lock<std::shared_mutex> stateLock(_stateMutex);
            freeModelLocked();
        } catch (const std::exception& ex) {
            try { std::cerr << "模型本地释放发生异常：" << ex.what() << std::endl; } catch (...) {}
        } catch (...) {
            try { std::cerr << "模型本地释放发生未知异常" << std::endl; } catch (...) {}
        }
    }

    void Model::freeModelLocked() {
        {
            std::lock_guard<std::mutex> modelInfoLock(_modelInfoMutex);
            _expectedChCache = -2;
            _hasCachedModelInfo = false;
            _cachedModelInfo = json();
        }
        std::lock_guard<std::mutex> indexLock(_indexStateMu);

        const auto clearLocalState = [this]() noexcept {
            try { delete _flowModel; } catch (...) {}
            _flowModel = nullptr;
            _isFlowGraphMode = false;
            modelIndex = -1;
            OwnModelIndex = true;
            _indexBound = false;
            _indexReady = false;
            _ownsNativeModelIndex = false;
            _ownsRegisteredFlowIndex = false;
            _dllLoader = nullptr;
            _loadedDogProvider = sntl_admin::DogProvider::Unknown;
            _loadedNativeDllName.clear();
        };
        const auto logFailure = [](const std::string& message) noexcept {
            try { std::cerr << message << std::endl; } catch (...) {}
        };

        if (_ownsRegisteredFlowIndex) {
            try {
                if (modelIndex >= 0 && _dllLoader != nullptr &&
                    _dllLoader->SupportsFlowRegistry()) {
                    if (_dllLoader->FreeFlow(modelIndex) != 0) {
                        logFailure("释放流程索引失败");
                    }
                } else {
                    logFailure("释放流程索引失败：缺少有效索引或释放接口");
                }
            } catch (const std::exception& ex) {
                logFailure(std::string("释放流程索引失败：") + ex.what());
            } catch (...) {
                logFailure("释放流程索引发生异常");
            }
            clearLocalState();
            return;
        }

        if (_indexBound) {
            if (!UnbindCurrentIndexNoexcept()) {
                logFailure("解绑共享索引失败");
            }
            clearLocalState();
            return;
        }

        if (_isFlowGraphMode || modelIndex < 0 || !_ownsNativeModelIndex || !OwnModelIndex) {
            clearLocalState();
            return;
        }

        DllLoader* loader = _dllLoader;
        const int releasedIndex = modelIndex;
        if (loader == nullptr || loader->GetFreeModelFunc() == nullptr ||
            loader->GetFreeResultFunc() == nullptr) {
            logFailure("DVT模型释放失败：推理DLL缺少释放接口");
            clearLocalState();
            return;
        }

        const auto freeModel = loader->GetFreeModelFunc();
        const auto freeResult = loader->GetFreeResultFunc();
        const char* resultPtr = nullptr;
        try {
            json config;
            config["model_index"] = releasedIndex;
            const std::string jsonStr = config.dump();
            resultPtr = freeModel(jsonStr.c_str());
            if (resultPtr == nullptr) {
                logFailure("DVT模型释放未返回结果");
            } else {
                try {
                    const json resultObject = json::parse(resultPtr);
                    int code = -1;
                    if (resultObject.is_object() && resultObject.contains("code") &&
                        resultObject.at("code").is_number_integer()) {
                        code = resultObject.at("code").get<int>();
                    }
                    if (code != 0) {
                        std::string detail;
                        if (resultObject.is_object() && resultObject.contains("message")) {
                            const json& value = resultObject.at("message");
                            detail = value.is_string() ? value.get<std::string>() : value.dump();
                        }
                        if (resultObject.is_object() && resultObject.contains("traceback")) {
                            const json& value = resultObject.at("traceback");
                            const std::string traceback = value.is_string()
                                ? value.get<std::string>()
                                : value.dump();
                            if (!traceback.empty()) {
                                if (!detail.empty()) detail += "；";
                                detail += traceback;
                            }
                        }
                        logFailure(detail.empty()
                            ? "DVT模型释放失败"
                            : "DVT模型释放失败：" + detail);
                    }
                } catch (const std::exception& ex) {
                    logFailure(std::string("DVT模型释放结果无效：") + ex.what());
                } catch (...) {
                    logFailure("DVT模型释放结果解析发生异常");
                }
            }
        } catch (const std::exception& ex) {
            logFailure(std::string("DVT模型释放失败：") + ex.what());
        } catch (...) {
            logFailure("DVT模型释放发生异常");
        }
        if (resultPtr != nullptr) {
            try { freeResult(resultPtr); } catch (...) {}
        }
        clearLocalState();
    }

    json Model::getModelInfoLocked() {
        EnsureBoundIndexReady();
        if (_hasCachedModelInfo) {
            return _cachedModelInfo;
        }

        if (!_isFlowGraphMode && modelIndex < 0) {
            throw std::runtime_error("模型尚未加载");
        }

        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");
            _cachedModelInfo = _flowModel->GetModelInfo();
            _hasCachedModelInfo = true;
            return _cachedModelInfo;
        }

        json config;
        config["model_index"] = modelIndex;

        std::string jsonStr = config.dump();
        const char* resultPtr = _dllLoader->GetModelInfoFunc()(jsonStr.c_str());
        std::string resultJson = std::string(resultPtr);
        json resultObject = json::parse(resultJson);
        _dllLoader->GetFreeResultFunc()(resultPtr);
        _cachedModelInfo = resultObject;
        _hasCachedModelInfo = true;
        return resultObject;
    }

    json Model::GetModelInfo() {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::shared_lock<std::shared_mutex> stateLock(_stateMutex);
        std::lock_guard<std::mutex> lock(_modelInfoMutex);
        return getModelInfoLocked();
    }

    json Model::GetDvsModelInfo() {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::shared_lock<std::shared_mutex> stateLock(_stateMutex);
        EnsureBoundIndexReady();
        if (!_isFlowGraphMode) {
            throw std::runtime_error("GetDvsModelInfo 仅支持流程模型");
        }
        if (!_flowModel) {
            throw std::runtime_error("DVS 模型尚未加载");
        }
        return _flowModel->GetDvsModelInfo();
    }

    int Model::resolveEffectiveInputCh() {
        std::lock_guard<std::mutex> lock(_modelInfoMutex);
        if (_expectedChCache != -2) {
            return (_expectedChCache == -1) ? 3 : _expectedChCache;
        }
        try {
            if (modelIndex < 0 && !_isFlowGraphMode) {
                _expectedChCache = -1;
                return 3;
            }
            const json info = getModelInfoLocked();
            const int p = ParseInputChFromModelInfo(info);
            if (p == 1 || p == 3) {
                _expectedChCache = p;
                return p;
            }
        } catch (...) {
        }
        _expectedChCache = -1;
        return 3;
    }

    std::vector<cv::Mat> Model::prepareInferInputBatch(const std::vector<cv::Mat>& images) {
        const int expCh = resolveEffectiveInputCh();
        const int ec = (expCh == 1 || expCh == 3) ? expCh : 3;
        std::vector<cv::Mat> out;
        out.reserve(images.size());
        const bool allowFlowFastPassThrough = _isFlowGraphMode;
        for (const auto& im : images) {
            if (allowFlowFastPassThrough && !im.empty() && im.depth() == CV_8U && im.channels() == ec) {
                out.push_back(im);
                continue;
            }
            out.push_back(NormalizeInferInputImage(im, ec));
        }
        return out;
    }

    std::pair<json, const char*> Model::InferInternal(const std::vector<cv::Mat>& images, const json& params_json) {
        EnsureBoundIndexReady();
        json imageInfoList = json::array();
        std::vector<std::pair<cv::Mat, bool>> processImages;

        try
        {
            // 处理输入图像
            for (const auto& image : images)
            {
                // 检查图像是否连续，如不连续则创建连续副本
                cv::Mat processImage = image;
                bool needDispose = false;
                if (!image.isContinuous())
                {
                    processImage = image.clone();
                    needDispose = true;
                }

                processImages.emplace_back(processImage, needDispose);

                json imageInfo;
                imageInfo["width"] = processImage.cols;
                imageInfo["height"] = processImage.rows;
                imageInfo["channels"] = processImage.channels();
                imageInfo["image_ptr"] = reinterpret_cast<uint64_t>(processImage.data);

                imageInfoList.push_back(imageInfo);
            }

            // 构建请求参数
            json inferRequest;
            inferRequest["model_index"] = modelIndex;
            inferRequest["image_list"] = imageInfoList;

            // 如果提供了参数JSON，合并到inferRequest
            if (!params_json.is_null())
            {
                for (auto it = params_json.begin(); it != params_json.end(); ++it)
                {
                    inferRequest[it.key()] = it.value();
                }
            }

            // 执行推理
            std::string jsonStr = inferRequest.dump();
            const char* resultPtr = _dllLoader->GetInferFunc()(jsonStr.c_str());
            std::string resultJson = std::string(resultPtr);
            json resultObject = json::parse(resultJson);

            // 检查是否返回错误
            if (resultObject.contains("code") && resultObject["code"].get<int>() != 0)
            {
                _dllLoader->GetFreeModelResultFunc()(resultPtr);
                throw std::runtime_error("Inference failed: " + resultObject["message"].get<std::string>());
            }

            // 推理完成，返回结果对象和结果指针
            return std::make_pair(resultObject, resultPtr);
        }
        catch (...)
        {
            // 释放处理时创建的图像资源
            for (auto& pair : processImages)
            {
                if (pair.second)
                { // 如果需要释放
  // 在C++中Mat析构函数会自动释放资源
                }
            }
            throw;
        }
    }

    Result Model::ParseToStructResult(const json& resultObject) {
        return ParseToStructResultInternal(resultObject, false);
    }

    Result Model::ParseToStructResultPreservingOriginalMask(const json& resultObject) {
        return ParseToStructResultInternal(resultObject, true);
    }

    Result Model::ParseToStructResultInternal(
        const json& resultObject,
        bool preserveOriginalMask) {
        std::vector<SampleResult> sampleResults;
        auto sampleResultsArray = resultObject["sample_results"];

        for (const auto& sampleResult : sampleResultsArray)
        {
            std::vector<ObjectResult> results;
            auto resultsArray = sampleResult["results"];

            for (const auto& result : resultsArray)
            {
                int categoryId = result["category_id"].get<int>();
                std::string categoryName = result["category_name"].get<std::string>();
                categoryName = convertUtf8ToGbk(categoryName);
                float score = static_cast<float>(result["score"].get<double>());
                float area = static_cast<float>(result["area"].get<double>());
                std::vector<double> bbox = result["bbox"].get<std::vector<double>>();
                bool withMask = result["with_mask"].get<bool>();
                bool withBbox = false;
                bool withAngle = false;
                float angle = -100.0f;
                bool withMean = false;
                double foregroundMean = 0.0;
                double backgroundMean = 0.0;

                try
                {
                    if (result.contains("with_bbox"))
                    {
                        withBbox = result["with_bbox"].get<bool>();
                    }
                    else
                    {
                        withBbox = bbox.size() >= 4;
                    }
                }
                catch (...)
                {
                    withBbox = bbox.size() >= 4;
                }

                try
                {
                    if (result.contains("with_angle"))
                    {
                        withAngle = result["with_angle"].get<bool>();
                    }
                }
                catch (...)
                {
                    withAngle = false;
                }
                try
                {
                    if (result.contains("angle"))
                    {
                        angle = static_cast<float>(result["angle"].get<double>());
                    }
                }
                catch (...)
                {
                    angle = -100.0f;
                }

                try
                {
                    if (result.contains("with_mean"))
                    {
                        withMean = result["with_mean"].get<bool>();
                    }
                }
                catch (...)
                {
                    withMean = false;
                }
                try
                {
                    if (result.contains("foreground_mean"))
                    {
                        foregroundMean = result["foreground_mean"].get<double>();
                    }
                }
                catch (...)
                {
                    foregroundMean = 0.0;
                }
                try
                {
                    if (result.contains("background_mean"))
                    {
                        backgroundMean = result["background_mean"].get<double>();
                    }
                }
                catch (...)
                {
                    backgroundMean = 0.0;
                }

                // 兼容某些输出直接将 angle 放入 bbox[4]
                if (!withAngle && bbox.size() >= 5)
                {
                    withAngle = true;
                    try { angle = static_cast<float>(bbox[4]); } catch (...) { angle = -100.0f; }
                }
                if (!withAngle)
                {
                    // 若 angle 字段存在且有效，也认为有角度信息
                    if (angle > -99.0f) withAngle = true;
                    else angle = -100.0f;
                }

                auto mask = result["mask"];
                int mask_width = mask["width"].get<int>();
                int mask_height = mask["height"].get<int>();
                cv::Mat mask_img;

                if (withMask)
                {
                    void* mask_ptr = reinterpret_cast<void*>(static_cast<uintptr_t>(mask["mask_ptr"].get<uint64_t>()));
                    mask_img = cv::Mat(mask_height, mask_width, CV_8UC1, mask_ptr).clone();
                }

                // 普通 C++ 模型路径使用 bbox 尺寸和最近邻插值生成有效 mask。
                if (!preserveOriginalMask)
                {
                    mask_img = mask_utils::ResizeMaskToBboxGrid(mask_img, bbox);
                }

                // area 表示当前 mask 的面积，不能沿用缩放前的 SDK 面积。
                if (!mask_img.empty()) {
                    area = static_cast<float>(cv::countNonZero(mask_img));
                }

                if ((bbox.size() < 4) && !mask_img.empty())
                {
                    std::vector<cv::Point> nz;
                    cv::findNonZero(mask_img, nz);
                    if (!nz.empty())
                    {
                        const cv::Rect rect = cv::boundingRect(nz);
                        bbox = {
                            static_cast<double>(rect.x),
                            static_cast<double>(rect.y),
                            static_cast<double>(rect.width),
                            static_cast<double>(rect.height)
                        };
                        withBbox = true;
                    }
                }

                results.emplace_back(categoryId, categoryName, score, area, bbox, withMask, mask_img,
                    withBbox, withAngle, angle, withMean, foregroundMean, backgroundMean);
            }

            sampleResults.emplace_back(results);
        }

        return Result(sampleResults);
    }

    Result Model::Infer(const cv::Mat& image, const json& params_json) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::shared_lock<std::shared_mutex> stateLock(_stateMutex);
        ClearLastInspectionStatuses();
        EnsureBoundIndexReady();
        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");
            if (image.empty()) throw std::invalid_argument("image is empty");

            const std::vector<cv::Mat> prepared = prepareInferInputBatch({ image });
            if (prepared.empty() || prepared.front().empty()) {
                throw std::invalid_argument("image is empty after preparation");
            }

            const auto begin = std::chrono::steady_clock::now();
            json flowRoot = _flowModel->InferInternal(prepared, params_json);
            SetLastInspectionStatusesFromFlowRoot(flowRoot, prepared.size());
            const auto end = std::chrono::steady_clock::now();

            double dlcvInferMs = 0.0;
            double totalInferMs = 0.0;
            std::vector<FlowNodeTiming> nodeTimings;
            ParseFlowTimingFromRoot(flowRoot, dlcvInferMs, totalInferMs, nodeTimings);
            if (totalInferMs <= 0.0) {
                totalInferMs = std::chrono::duration<double, std::milli>(end - begin).count();
            }
            if (dlcvInferMs <= 0.0) {
                dlcvInferMs = totalInferMs;
            }
            SetLastInferTiming(dlcvInferMs, totalInferMs, std::move(nodeTimings));

            const bool emitMaskOutput = ResolveWithMaskOutputFlag(params_json, true);
            const Json resultListToken = ExtractFlowResultListToken(flowRoot);
            std::vector<SampleResult> sampleResults =
                ConvertFlowResultListTokenToSampleResults(resultListToken, 1, emitMaskOutput);
            return Result(std::move(sampleResults));
        }

        const std::vector<cv::Mat> prepared = prepareInferInputBatch({ image });
        if (prepared.empty() || prepared.front().empty()) {
            throw std::invalid_argument("image is empty after preparation");
        }

        const auto begin = std::chrono::steady_clock::now();
        auto resultTuple = InferInternal(prepared, params_json);
        const auto end = std::chrono::steady_clock::now();
        const double inferMs = std::chrono::duration<double, std::milli>(end - begin).count();
        SetLastInferTiming(inferMs, inferMs);

        try
        {
            Result result = ParseToStructResult(resultTuple.first);
            // 完成后释放结果
            _dllLoader->GetFreeModelResultFunc()(resultTuple.second);
            return result;
        }
        catch (...)
        {
            // 发生异常时也需要释放结果
            _dllLoader->GetFreeModelResultFunc()(resultTuple.second);
            throw;
        }
    }

    Result Model::InferBatch(const std::vector<cv::Mat>& image_list, const json& params_json) {
        return InferBatchInternal(image_list, params_json, false);
    }

    Result Model::InferBatchPreservingOriginalMask(
        const std::vector<cv::Mat>& image_list,
        const json& params_json) {
        return InferBatchInternal(image_list, params_json, true);
    }

    Result Model::InferBatchInternal(
        const std::vector<cv::Mat>& image_list,
        const json& params_json,
        bool preserveOriginalMask) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::shared_lock<std::shared_mutex> stateLock(_stateMutex);
        ClearLastInspectionStatuses();
        EnsureBoundIndexReady();
        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");
            if (image_list.empty()) {
                SetLastInferTiming(0.0, 0.0);
                return Result(std::vector<SampleResult>{});
            }

            const std::vector<cv::Mat> prepared = prepareInferInputBatch(image_list);
            if (prepared.size() != image_list.size()) {
                throw std::runtime_error("prepareInferInputBatch size mismatch");
            }
            for (const auto& m : prepared) {
                if (m.empty()) {
                    throw std::invalid_argument("image is empty after preparation");
                }
            }

            const auto begin = std::chrono::steady_clock::now();
            json flowRoot = _flowModel->InferInternal(prepared, params_json);
            SetLastInspectionStatusesFromFlowRoot(flowRoot, prepared.size());
            const auto end = std::chrono::steady_clock::now();

            double dlcvInferMs = 0.0;
            double totalInferMs = 0.0;
            std::vector<FlowNodeTiming> nodeTimings;
            ParseFlowTimingFromRoot(flowRoot, dlcvInferMs, totalInferMs, nodeTimings);
            if (totalInferMs <= 0.0) {
                totalInferMs = std::chrono::duration<double, std::milli>(end - begin).count();
            }
            if (dlcvInferMs <= 0.0) {
                dlcvInferMs = totalInferMs;
            }
            SetLastInferTiming(dlcvInferMs, totalInferMs, std::move(nodeTimings));

            const bool emitMaskOutput = ResolveWithMaskOutputFlag(params_json, true);
            const Json resultListToken = ExtractFlowResultListToken(flowRoot);
            std::vector<SampleResult> sampleResults =
                ConvertFlowResultListTokenToSampleResults(resultListToken, image_list.size(), emitMaskOutput);
            return Result(std::move(sampleResults));
        }

        const std::vector<cv::Mat> prepared = prepareInferInputBatch(image_list);
        if (prepared.size() != image_list.size()) {
            throw std::runtime_error("prepareInferInputBatch size mismatch");
        }
        for (const auto& m : prepared) {
            if (m.empty()) {
                throw std::invalid_argument("image is empty after preparation");
            }
        }

        const auto begin = std::chrono::steady_clock::now();
        auto resultTuple = InferInternal(prepared, params_json);
        const auto end = std::chrono::steady_clock::now();
        const double inferMs = std::chrono::duration<double, std::milli>(end - begin).count();
        SetLastInferTiming(inferMs, inferMs);

        try
        {
            Result result = preserveOriginalMask
                ? ParseToStructResultPreservingOriginalMask(resultTuple.first)
                : ParseToStructResult(resultTuple.first);
            // 完成后释放结果
            _dllLoader->GetFreeModelResultFunc()(resultTuple.second);
            return result;
        }
        catch (...)
        {
            // 发生异常时也需要释放结果
            _dllLoader->GetFreeModelResultFunc()(resultTuple.second);
            throw;
        }
    }

    json Model::InferOneOutJson(const cv::Mat& image, const json& params_json) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::shared_lock<std::shared_mutex> stateLock(_stateMutex);
        ClearLastInspectionStatuses();
        EnsureBoundIndexReady();
        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");
            if (image.empty()) throw std::invalid_argument("image is empty");

            const std::vector<cv::Mat> prepared = prepareInferInputBatch({ image });
            if (prepared.empty() || prepared.front().empty()) {
                throw std::invalid_argument("image is empty after preparation");
            }

            const auto begin = std::chrono::steady_clock::now();
            json flowRoot = _flowModel->InferInternal(prepared, params_json);
            SetLastInspectionStatusesFromFlowRoot(flowRoot, prepared.size());
            const auto end = std::chrono::steady_clock::now();

            double dlcvInferMs = 0.0;
            double totalInferMs = 0.0;
            std::vector<FlowNodeTiming> nodeTimings;
            ParseFlowTimingFromRoot(flowRoot, dlcvInferMs, totalInferMs, nodeTimings);
            if (totalInferMs <= 0.0) {
                totalInferMs = std::chrono::duration<double, std::milli>(end - begin).count();
            }
            if (dlcvInferMs <= 0.0) {
                dlcvInferMs = totalInferMs;
            }
            SetLastInferTiming(dlcvInferMs, totalInferMs, std::move(nodeTimings));

            const bool emitMaskOutput = ResolveWithMaskOutputFlag(params_json, true);
            const Json resultListToken = ExtractFlowResultListToken(flowRoot);
            json flowResults = json::array();
            try {
                if (resultListToken.is_array() && !resultListToken.empty()) {
                    const Json& first = resultListToken.at(0);
                    if (first.is_object() && first.contains("result_list") && first.at("result_list").is_array()) {
                        flowResults = first.at("result_list");
                    } else {
                        flowResults = resultListToken;
                    }
                } else if (resultListToken.is_array()) {
                    flowResults = resultListToken;
                }
            } catch (...) {
                flowResults = json::array();
            }
            Json normalized = NormalizeFlowOneOutJson(flowResults, emitMaskOutput);
            const InspectionStatusSnapshot status = g_lastInspectionStatuses.empty()
                ? InspectionStatusSnapshot() : g_lastInspectionStatuses.front();
            return WrapNormalizedFlowJsonWithInspectionStatus(std::move(normalized), status);
        }

        const std::vector<cv::Mat> prepared = prepareInferInputBatch({ image });
        if (prepared.empty() || prepared.front().empty()) {
            throw std::invalid_argument("image is empty after preparation");
        }

        const auto begin = std::chrono::steady_clock::now();
        auto resultTuple = InferInternal(prepared, params_json);
        const auto end = std::chrono::steady_clock::now();
        const double inferMs = std::chrono::duration<double, std::milli>(end - begin).count();
        SetLastInferTiming(inferMs, inferMs);

        try
        {
            json results = resultTuple.first["sample_results"][0]["results"];
            for (auto& result : results) {
                mask_utils::ConvertPointerMaskToContour(result);
            }

            // 完成后释放结果
            _dllLoader->GetFreeModelResultFunc()(resultTuple.second);
            return results;
        }
        catch (...)
        {
            // 发生异常时也需要释放结果
            _dllLoader->GetFreeModelResultFunc()(resultTuple.second);
            throw;
        }
    }

    void Model::GetLastInferTiming(double& dlcvInferMs, double& totalInferMs) {
        dlcvInferMs = g_lastDlcvInferMs;
        totalInferMs = g_lastTotalInferMs;
    }

    std::vector<FlowNodeTiming> Model::GetLastFlowNodeTimings() {
        return g_lastFlowNodeTimings;
    }

    bool Model::GetLastInspectionStatus(
        bool& ok,
        std::vector<std::string>& reasons,
        size_t sampleIndex) {
        ok = false;
        reasons.clear();
        if (sampleIndex >= g_lastInspectionStatuses.size()) return false;

        const InspectionStatusSnapshot& status = g_lastInspectionStatuses[sampleIndex];
        if (!status.HasOk) return false;
        ok = status.Ok;
        reasons = status.Reasons;
        return true;
    }

    // Utils类实现
    std::string Utils::JsonToString(const json& j) {
        return j.dump(4); // 缩进为4
    }

    void Utils::FreeAllModels() {
        const auto providerName = [](sntl_admin::DogProvider provider) {
            if (provider == sntl_admin::DogProvider::Sentinel) return std::string("sentinel");
            if (provider == sntl_admin::DogProvider::Virbox) return std::string("virbox");
            return std::string("unknown");
        };
        const auto logFailure = [](const std::string& stage,
                                   const std::string& module,
                                   const std::string& detail) noexcept {
            try {
                std::cerr << "全量释放模型失败：阶段=" << stage
                          << "，模块=" << (module.empty() ? "unknown" : module)
                          << "，详情=" << detail << std::endl;
            } catch (...) {}
        };

        try {
            flow::ModelLifecycleWriteGuard lifecycleGuard;
            try {
                ClearCApiModelsForFreeAllModels();
            } catch (const std::exception& ex) {
                logFailure("清理 C API 模型", "dlcv_infer_cpp", ex.what());
            } catch (...) {
                logFailure("清理 C API 模型", "dlcv_infer_cpp", "未知异常");
            }

            // 底层模型表清空后，模型池中的对象将持有失效 index。
            // 先清空模型池，确保后续流程加载会重新创建子模型。
            try {
                flow::ModelPool::Instance().Clear();
            } catch (const std::exception& ex) {
                logFailure("清理流程模型池", "dlcv_infer_cpp", ex.what());
            } catch (...) {
                logFailure("清理流程模型池", "dlcv_infer_cpp", "未知异常");
            }

            for (const auto& module : EnumerateLoadedInferModules()) {
                try {
                    (void)DllLoader::GetOrCreateForExistingModule(
                        module.Provider,
                        module.Module,
                        module.Path);
                } catch (const std::exception& ex) {
                    logFailure("读取已加载模块", GetFileNameOnly(module.Path), ex.what());
                } catch (...) {
                    logFailure("读取已加载模块", GetFileNameOnly(module.Path), "未知异常");
                }
            }

            struct FreeAllModelsTarget final {
                FreeAllModelsFuncType Function = nullptr;
                void* Identity = nullptr;
                std::string Module;
            };
            std::vector<FreeAllModelsTarget> targets;
            {
                std::lock_guard<std::mutex> lock(DllLoaderRegistryMutex());
                // 注册表以真实模块句柄为键，同一个 DLL 只处理一次。
                for (const auto& item : DllLoaderRegistry()) {
                    if (item.first == nullptr || !item.second) continue;
                    const std::string module = item.second->GetLoadedNativeDllName()
                        + "[" + providerName(item.second->GetDogProvider()) + "]";
                    targets.push_back({item.second->GetFreeAllModelsFunc(), item.first, module});
                }
            }

            for (const auto& target : targets) {
                if (target.Function == nullptr) {
                    logFailure("调用底层释放", target.Module, "缺少 dlcv_free_all_models 接口");
                    continue;
                }
                try {
                    openivs_flow_free_all_models(target.Identity);
                } catch (const std::exception& ex) {
                    logFailure("调用底层释放", target.Module, ex.what());
                } catch (...) {
                    logFailure("调用底层释放", target.Module, "未知异常");
                }
            }
        } catch (const std::exception& ex) {
            logFailure("执行全量释放", "dlcv_infer_cpp", ex.what());
        } catch (...) {
            logFailure("执行全量释放", "dlcv_infer_cpp", "未知异常");
        }
    }

    json Utils::GetDeviceInfo() {
        auto& loader = DllLoader::Instance();
        const char* resultPtr = nullptr;
        if (loader.GetDeviceInfoFunc())
        {
            resultPtr = loader.GetDeviceInfoFunc()();
        }
        else
        {
            json ret;
            ret["code"] = -1;
            ret["message"] = "dlcv_get_device_info 不可用";
            return ret;
        }
        std::string resultJson = std::string(resultPtr);
        json resultObject = json::parse(resultJson);
        loader.GetFreeResultFunc()(resultPtr);
        return resultObject;
    }

    void Utils::KeepMaxClock() {
        auto& loader = DllLoader::Instance();
        if (loader.GetKeepMaxClockFunc())
        {
            loader.GetKeepMaxClockFunc()();
        }
    }

    // OCR推理方法
    Result Utils::OcrInfer(Model& detectModel, Model& recognizeModel, const cv::Mat& image) {
        try
        {
            // 使用检测模型进行推理
            std::vector<cv::Mat> imageList = { image };
            Result result = detectModel.InferBatch(imageList);

            // 处理第一个模型的检测结果
            for (auto& sampleResult : result.sampleResults)
            {
                for (size_t i = 0; i < sampleResult.results.size(); i++)
                {
                    auto& detection = sampleResult.results[i];

                    // 获取边界框坐标 (x, y, w, h)
                    double x = detection.bbox[0];
                    double y = detection.bbox[1];
                    double w = detection.bbox[2];
                    double h = detection.bbox[3];

                    // 确保坐标在有效范围内
                    x = std::max(0.0, x);
                    y = std::max(0.0, y);
                    w = std::min(w, static_cast<double>(image.cols) - x);
                    h = std::min(h, static_cast<double>(image.rows) - y);

                    if (w <= 0 || h <= 0)
                        continue;

                    // 提取ROI区域
                    cv::Rect roi(static_cast<int>(x), static_cast<int>(y),
                        static_cast<int>(w), static_cast<int>(h));
                    // 创建新的Mat对象
                    cv::Mat roiMat = image(roi).clone();

                    // 使用识别模型进行推理
                    std::vector<cv::Mat> roiList = { roiMat };
                    Result recognizeResult = recognizeModel.InferBatch(roiList);

                    // 如果识别模型有结果，记录该模型的返回结果
                    if (!recognizeResult.sampleResults.empty() &&
                        !recognizeResult.sampleResults[0].results.empty())
                    {
                        // 获取识别模型的第一个结果
                        auto& topResult = recognizeResult.sampleResults[0].results[0];

                        // 更新原始检测的返回结果
                        detection.categoryName = topResult.categoryName;
                    }
                }
            }

            return result;
        }
        catch (const std::exception& ex)
        {
            std::cerr << "OCR inference failed: " << ex.what() << std::endl;
            throw;
        }
    }

    // 获取GPU信息
    json Utils::GetGpuInfo() {
        std::vector<std::map<std::string, json>> devices;
        auto& nvml = NvmlLibrary::Get();

        if (!nvml.IsLoaded()) {
            json ret;
            ret["code"] = 1;
            ret["message"] = "Failed to load NVML library.";
            return ret;
        }

        int result = nvml.Init();
        if (result != 0) {
            json ret;
            ret["code"] = 1;
            ret["message"] = "Failed to initialize NVML.";
            return ret;
        }

        unsigned int deviceCount = 0;
        result = nvml.DeviceGetCount(&deviceCount);
        if (result != 0) {
            nvml.Shutdown();
            json ret;
            ret["code"] = 2;
            ret["message"] = "Failed to get device count.";
            return ret;
        }

        for (unsigned int i = 0; i < deviceCount; i++) {
            nvmlDevice_t device;
            result = nvml.DeviceGetHandleByIndex(i, &device);
            if (result != 0) {
                continue; // 如果无法获取当前设备
            }

            char name[64];
            result = nvml.DeviceGetName(device, name, 64);
            if (result == 0) {
                std::map<std::string, json> deviceInfo;
                deviceInfo["device_id"] = i;
                deviceInfo["device_name"] = name;
                devices.push_back(deviceInfo);
            }
        }

        nvml.Shutdown();

        json ret;
        ret["code"] = 0;
        ret["message"] = "Success";
        ret["devices"] = devices;
        return ret;
    }

    // NVML库函数实现
    int Utils::nvmlInit() {
        return NvmlLibrary::Get().Init();
    }

    int Utils::nvmlShutdown() {
        return NvmlLibrary::Get().Shutdown();
    }

    int Utils::nvmlDeviceGetCount(unsigned int* deviceCount) {
        return NvmlLibrary::Get().DeviceGetCount(deviceCount);
    }

    int Utils::nvmlDeviceGetName(nvmlDevice_t device, char* name, unsigned int length) {
        return NvmlLibrary::Get().DeviceGetName(device, name, length);
    }

    int Utils::nvmlDeviceGetHandleByIndex(unsigned int index, nvmlDevice_t* device) {
        return NvmlLibrary::Get().DeviceGetHandleByIndex(index, device);
    }

    namespace {
        template <typename FunctionType>
        FunctionType RequireNativeApiFunction(FunctionType function, const char* functionName) {
            if (function == nullptr) {
                throw std::runtime_error(std::string(functionName) + " 不可用");
            }
            return function;
        }
    }

    const char* NativeApi::LoadModel(const char* configStr) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::lock_guard<std::mutex> modelLoadLock(g_modelLoadMu);
        if (configStr != nullptr) {
            const json config = json::parse(configStr, nullptr, false);
            if (config.is_object() && config.contains("model_path") &&
                config.at("model_path").is_string()) {
                DllLoader::EnsureForModel(config.at("model_path").get<std::string>());
            }
        }
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetLoadModelFunc(), "dlcv_load_model")(configStr);
    }

    const char* NativeApi::FreeModel(const char* configStr) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetFreeModelFunc(), "dlcv_free_model")(configStr);
    }

    const char* NativeApi::GetModelInfo(const char* configStr) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetModelInfoFunc(), "dlcv_get_model_info")(configStr);
    }

    const char* NativeApi::Infer(const char* configStr) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetInferFunc(), "dlcv_infer")(configStr);
    }

    void NativeApi::FreeModelResult(const char* configStr) {
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetFreeModelResultFunc(), "dlcv_free_model_result")(configStr);
    }

    void NativeApi::FreeResult(const char* resultPtr) {
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetFreeResultFunc(), "dlcv_free_result")(resultPtr);
    }

    void NativeApi::FreeAllModels() {
        Utils::FreeAllModels();
    }

    const char* NativeApi::GetDeviceInfo() {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetDeviceInfoFunc(), "dlcv_get_device_info")();
    }

    const char* NativeApi::GetGpuInfo() {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetGpuInfoFunc(), "dlcv_get_gpu_info")();
    }

    void NativeApi::KeepMaxClock() {
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetKeepMaxClockFunc(), "dlcv_keep_max_clock")();
    }

    void NativeApi::ResetMaxClock() {
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetResetMaxClockFunc(), "dlcv_reset_max_clock")();
    }

    void NativeApi::SetGpuMaxClock(bool verbose) {
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetSetGpuMaxClockFunc(), "dlcv_set_gpu_max_clock")(verbose);
    }

    void NativeApi::ResetGpuMaxClock(bool verbose) {
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetResetGpuMaxClockFunc(), "dlcv_reset_gpu_max_clock")(verbose);
    }

    const char* NativeApi::GetPowerSchemeGuid(int verbose) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(
            loader.GetPowerSchemeGuidFunc(),
            "dlcv_get_power_scheme_guid")(verbose);
    }

    int NativeApi::SetPowerSchemeGuid(const char* schemeGuid, int verbose) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(
            loader.GetSetPowerSchemeGuidFunc(),
            "dlcv_set_power_scheme_guid")(schemeGuid, verbose);
    }

    const char* NativeApi::GetPowerScheme(int verbose) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetPowerSchemeFunc(), "dlcv_get_power_scheme")(verbose);
    }

    int NativeApi::SetPowerScheme(const char* schemeName, int verbose) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(
            loader.GetSetPowerSchemeFunc(),
            "dlcv_set_power_scheme")(schemeName, verbose);
    }

    int NativeApi::SetCurrentProcessAffinityToBigCores(int verbose) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(
            loader.GetSetCurrentProcessAffinityToBigCoresFunc(),
            "dlcv_set_current_process_affinity_to_big_cores")(verbose);
    }

    int NativeApi::SetCurrentProcessPriorityHighest(
        int preferRealtime,
        int verbose,
        int bindBigCores) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(
            loader.GetSetCurrentProcessPriorityHighestFunc(),
            "dlcv_set_current_process_priority_highest")(preferRealtime, verbose, bindBigCores);
    }

    int NativeApi::LoadModelC(const char* modelPath, int deviceId) {
        std::lock_guard<std::mutex> modelLoadLock(g_modelLoadMu);
        if (modelPath == nullptr) {
            throw std::invalid_argument("model_path is null");
        }
        DllLoader::EnsureForModel(std::string(modelPath));
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetLoadModelCFunc(), "dlcv_load_model_c")(
            modelPath,
            deviceId);
    }

    int NativeApi::FreeModelC(int modelIndex) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetFreeModelCFunc(), "dlcv_free_model_c")(modelIndex);
    }

    DlcvCResult NativeApi::InferC(int modelIndex, const DlcvCImageList& imageList) {
        auto& loader = DllLoader::Instance();
        return RequireNativeApiFunction(loader.GetInferCFunc(), "dlcv_infer_c")(modelIndex, &imageList);
    }

    void NativeApi::FreeModelResultC(DlcvCResult& result) {
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetFreeModelResultCFunc(), "dlcv_free_model_result_c")(&result);
    }

}
