#include "dlcv_infer.h"
#include "dlcv_sntl_admin.h"
#include "flow/FlowGraphModel.h"
#include "flow/utils/MaskRleUtils.h"
#include <Windows.h>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <random>
#include <stdexcept>
#include <unordered_map>
#include <utility>

namespace {

using Json = dlcv_infer::json;

struct DvsUnpackResult {
    Json pipelineRoot = Json::object();
    std::string tempDir;
};

class TempDirGuard final {
public:
    explicit TempDirGuard(std::string dir) : _dir(std::move(dir)) {}
    ~TempDirGuard() { CleanupNoexcept(); }
    void Release() { _dir.clear(); }
    TempDirGuard(const TempDirGuard&) = delete;
    TempDirGuard& operator=(const TempDirGuard&) = delete;
    TempDirGuard(TempDirGuard&&) = delete;
    TempDirGuard& operator=(TempDirGuard&&) = delete;

private:
    std::string _dir;

    static bool DeleteDirectoryRecursive(const std::string& dir) {
        if (dir.empty()) return true;

        WIN32_FIND_DATAA ffd;
        const std::string pattern = dir + "\\*";
        HANDLE hFind = FindFirstFileA(pattern.c_str(), &ffd);
        if (hFind != INVALID_HANDLE_VALUE) {
            do {
                const std::string name = ffd.cFileName;
                if (name == "." || name == "..") continue;
                const std::string path = dir + "\\" + name;

                if ((ffd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
                    (void)DeleteDirectoryRecursive(path);
                } else {
                    SetFileAttributesA(path.c_str(), FILE_ATTRIBUTE_NORMAL);
                    (void)DeleteFileA(path.c_str());
                }
            } while (FindNextFileA(hFind, &ffd) != 0);
            FindClose(hFind);
        }

        SetFileAttributesA(dir.c_str(), FILE_ATTRIBUTE_NORMAL);
        return RemoveDirectoryA(dir.c_str()) != 0;
    }

    void CleanupNoexcept() {
        if (_dir.empty()) return;
        try { (void)DeleteDirectoryRecursive(_dir); } catch (...) {}
        _dir.clear();
    }
};

std::string ToLowerAscii(std::string s) {
    for (size_t i = 0; i < s.size(); i++) {
        const unsigned char ch = static_cast<unsigned char>(s[i]);
        if (ch >= 'A' && ch <= 'Z') s[i] = static_cast<char>(ch - 'A' + 'a');
    }
    return s;
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
           EndsWithIgnoreCase(pathUtf8, ".dvso") ||
           EndsWithIgnoreCase(pathUtf8, ".dvsp");
}

std::string JoinPath(const std::string& a, const std::string& b) {
    if (a.empty()) return b;
    if (b.empty()) return a;
    const char tail = a.back();
    if (tail == '\\' || tail == '/') return a + b;
    return a + "\\" + b;
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

std::string RandomHex(size_t len) {
    static std::mt19937_64 rng{ std::random_device{}() };
    static const char* kHex = "0123456789abcdef";

    std::string out;
    out.reserve(len);
    for (size_t i = 0; i < len; i++) {
        out.push_back(kHex[static_cast<size_t>(rng() & 0xF)]);
    }
    return out;
}

std::string CreateTempDir() {
    char tmpPath[MAX_PATH] = { 0 };
    DWORD n = GetTempPathA(MAX_PATH, tmpPath);
    if (n == 0 || n >= MAX_PATH) {
        throw std::runtime_error("failed to get temp directory");
    }

    for (int retry = 0; retry < 8; retry++) {
        const std::string dir = JoinPath(std::string(tmpPath), "DlcvDvs_" + RandomHex(24));
        if (CreateDirectoryA(dir.c_str(), nullptr) != 0) return dir;
    }
    throw std::runtime_error("failed to create temp directory");
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

void CopyStreamToFile(FILE* fp, const std::string& outPath, long long bytes) {
    if (bytes < 0) throw std::runtime_error("invalid file size in dvst archive");
    std::ofstream ofs(outPath, std::ios::binary);
    if (!ofs) throw std::runtime_error("failed to write temp model file: " + outPath);

    std::vector<char> buffer(1024 * 1024);
    long long remaining = bytes;
    while (remaining > 0) {
        const size_t chunk = static_cast<size_t>(std::min<long long>(remaining, static_cast<long long>(buffer.size())));
        const size_t n = std::fread(buffer.data(), 1, chunk, fp);
        if (n != chunk) throw std::runtime_error("failed to read dvst file content");
        ofs.write(buffer.data(), static_cast<std::streamsize>(chunk));
        if (!ofs) throw std::runtime_error("failed to write temp model file: " + outPath);
        remaining -= static_cast<long long>(chunk);
    }
}

void RewritePipelineModelPath(Json& pipelineRoot, const std::unordered_map<std::string, std::string>& fileMap) {
    if (!pipelineRoot.is_object() || !pipelineRoot.contains("nodes") || !pipelineRoot.at("nodes").is_array()) {
        throw std::runtime_error("pipeline.json missing nodes");
    }

    for (auto& node : pipelineRoot.at("nodes")) {
        if (!node.is_object()) continue;
        if (!node.contains("properties") || !node.at("properties").is_object()) continue;

        auto& props = node.at("properties");
        if (!props.contains("model_path") || !props.at("model_path").is_string()) continue;

        const std::string originalPath = props.at("model_path").get<std::string>();
        auto it = fileMap.find(ToLowerAscii(originalPath));
        if (it != fileMap.end()) {
            props["model_path"] = it->second;
            continue;
        }

        const std::string fileName = GetFileNameOnly(originalPath);
        it = fileMap.find(ToLowerAscii(fileName));
        if (it != fileMap.end()) {
            props["model_path"] = it->second;
        }
    }
}

void WriteUtf8Text(const std::string& path, const std::string& content) {
    std::ofstream ofs(path, std::ios::binary);
    if (!ofs) throw std::runtime_error("failed to write file: " + path);
    ofs.write(content.data(), static_cast<std::streamsize>(content.size()));
    if (!ofs) throw std::runtime_error("failed to write file: " + path);
}

DvsUnpackResult UnpackDvsArchiveToTemp(const std::string& archivePathGbk) {
    const std::wstring archivePathW = dlcv_infer::convertGbkToWstring(archivePathGbk);
    FILE* fp = nullptr;
    if (_wfopen_s(&fp, archivePathW.c_str(), L"rb") != 0 || fp == nullptr) {
        throw std::runtime_error("failed to open dvst file");
    }

    DvsUnpackResult out;
    try {
        char magic[3] = { 0 };
        ReadExactOrThrow(fp, magic, 3, "failed to read dvst magic");
        if (!(magic[0] == 'D' && magic[1] == 'V' && magic[2] == '\n')) {
            throw std::runtime_error("invalid dvst format: missing DV header");
        }

        const std::string headerLine = ReadLineOrThrow(fp);
        const Json header = Json::parse(headerLine);
        if (!header.is_object() ||
            !header.contains("file_list") || !header.at("file_list").is_array() ||
            !header.contains("file_size") || !header.at("file_size").is_array() ||
            header.at("file_list").size() != header.at("file_size").size()) {
            throw std::runtime_error("invalid dvst header: file_list/file_size mismatch");
        }

        out.tempDir = CreateTempDir();
        TempDirGuard unpackGuard(out.tempDir);
        std::unordered_map<std::string, std::string> fileNameToTemp;
        bool gotPipeline = false;

        const auto& fileList = header.at("file_list");
        const auto& fileSize = header.at("file_size");
        for (size_t i = 0; i < fileList.size(); i++) {
            if (!fileList.at(i).is_string()) throw std::runtime_error("invalid dvst header: file_list item is not string");

            const std::string fileName = fileList.at(i).get<std::string>();
            const long long size = ReadFileSizeFromJson(fileSize.at(i));
            if (size < 0) throw std::runtime_error("invalid file size in dvst header");

            if (ToLowerAscii(fileName) == "pipeline.json") {
                std::string text(static_cast<size_t>(size), '\0');
                if (size > 0) {
                    ReadExactOrThrow(fp, &text[0], static_cast<size_t>(size), "failed to read pipeline.json");
                }
                out.pipelineRoot = Json::parse(text);
                gotPipeline = true;
            } else {
                std::string ext = GetExtensionWithDot(fileName);
                if (ext.empty()) ext = ".tmp";
                const std::string safeName = RandomHex(32) + ext;
                const std::string fullPath = JoinPath(out.tempDir, safeName);

                CopyStreamToFile(fp, fullPath, size);
                fileNameToTemp[ToLowerAscii(fileName)] = fullPath;
                fileNameToTemp[ToLowerAscii(GetFileNameOnly(fileName))] = fullPath;
            }
        }

        if (!gotPipeline) throw std::runtime_error("pipeline.json not found in dvst archive");
        RewritePipelineModelPath(out.pipelineRoot, fileNameToTemp);
        unpackGuard.Release();
    } catch (...) {
        std::fclose(fp);
        throw;
    }

    std::fclose(fp);
    return out;
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

double ComputeFlowArea(const Json& entry, const cv::Mat& mask, const std::vector<double>& bbox) {
    if (entry.is_object() && entry.contains("area")) {
        return ReadJsonNumber(entry.at("area"), 0.0);
    }

    if (entry.is_object() && entry.contains("mask_rle") && entry.at("mask_rle").is_object()) {
        try { return dlcv_infer::flow::CalculateMaskArea(entry.at("mask_rle")); } catch (...) {}
    }

    if (!mask.empty()) {
        return static_cast<double>(cv::countNonZero(mask));
    }

    if (bbox.size() >= 4) {
        return std::max(0.0, bbox[2]) * std::max(0.0, bbox[3]);
    }
    return 0.0;
}

std::vector<dlcv_infer::ObjectResult> ConvertFlowResultListToObjects(const Json& flowResultList) {
    std::vector<dlcv_infer::ObjectResult> out;
    if (!flowResultList.is_array()) return out;

    for (const auto& entry : flowResultList) {
        if (!entry.is_object()) continue;

        const int categoryId = entry.value("category_id", 0);
        const std::string categoryNameUtf8 = entry.value("category_name", std::string());
        const float score = static_cast<float>(ReadJsonNumber(entry.contains("score") ? entry.at("score") : Json(), 0.0));

        bool withBbox = false;
        bool withAngle = false;
        float angle = -100.0f;
        bool isRotated = false;
        std::vector<double> bbox = ParseFlowBboxToModel(entry, withBbox, withAngle, angle, isRotated);

        cv::Mat mask = BuildMaskFromFlowEntry(entry, bbox, isRotated);
        const bool withMask = !mask.empty();
        const float area = static_cast<float>(ComputeFlowArea(entry, mask, bbox));

        out.emplace_back(
            categoryId,
            dlcv_infer::convertUtf8ToGbk(categoryNameUtf8),
            score,
            area,
            bbox,
            withMask,
            mask,
            withBbox,
            withAngle,
            angle
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

Json NormalizeFlowOneOutJson(const Json& flowResultList) {
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

        cv::Mat mask = BuildMaskFromFlowEntry(entry, bbox, isRotated);
        Json maskPoints = Json::array();
        if (!mask.empty() && bbox.size() >= 4 && !isRotated) {
            maskPoints = MaskToPointsJson(mask, bbox[0], bbox[1]);
        }

        if (!maskPoints.empty()) {
            out["mask"] = maskPoints;
            out["with_mask"] = true;
        } else {
            out["mask"] = Json::object({ {"height", -1}, {"mask_ptr", 0}, {"width", -1} });
            out["with_mask"] = false;
        }

        out["area"] = ComputeFlowArea(entry, mask, bbox);
        normalized.push_back(out);
    }
    return normalized;
}

} // namespace

namespace dlcv_infer {

    std::wstring convertStringToWstring(const std::string& inputString) {
        int len = MultiByteToWideChar(CP_ACP, 0, inputString.c_str(), -1, nullptr, 0);
        std::vector<wchar_t> str(len);
        MultiByteToWideChar(CP_ACP, 0, inputString.c_str(), -1, &str[0], len);
        return std::wstring(str.begin(), str.end() - 1);
    }

    std::string convertWstringToString(const std::wstring& inputWstring) {
        int len = WideCharToMultiByte(CP_ACP, 0, inputWstring.c_str(), -1, nullptr, 0, nullptr, nullptr);
        std::vector<char> str(len);
        WideCharToMultiByte(CP_ACP, 0, inputWstring.c_str(), -1, &str[0], len, nullptr, nullptr);
        return std::string(str.begin(), str.end() - 1);
    }

    std::string convertWstringToUtf8(const std::wstring& inputWstring) {
        int len = WideCharToMultiByte(CP_UTF8, 0, inputWstring.c_str(), -1, nullptr, 0, nullptr, nullptr);
        std::vector<char> str(len);
        WideCharToMultiByte(CP_UTF8, 0, inputWstring.c_str(), -1, &str[0], len, nullptr, nullptr);
        return std::string(str.begin(), str.end() - 1);
    }

    std::wstring convertUtf8ToWstring(const std::string& inputUtf8) {
        int len = MultiByteToWideChar(CP_UTF8, 0, inputUtf8.c_str(), -1, nullptr, 0);
        std::vector<wchar_t> str(len);
        MultiByteToWideChar(CP_UTF8, 0, inputUtf8.c_str(), -1, &str[0], len);
        return std::wstring(str.begin(), str.end() - 1);
    }

    std::string convertWstringToGbk(const std::wstring& inputWstring) {
        int len = WideCharToMultiByte(936, 0, inputWstring.c_str(), -1, nullptr, 0, nullptr, nullptr);
        std::vector<char> str(len);
        WideCharToMultiByte(936, 0, inputWstring.c_str(), -1, &str[0], len, nullptr, nullptr);
        return std::string(str.begin(), str.end() - 1);
    }

    std::wstring convertGbkToWstring(const std::string& inputGbk) {
        int len = MultiByteToWideChar(936, 0, inputGbk.c_str(), -1, nullptr, 0);
        std::vector<wchar_t> str(len);
        MultiByteToWideChar(936, 0, inputGbk.c_str(), -1, &str[0], len);
        return std::wstring(str.begin(), str.end() - 1);
    }

    std::string convertUtf8ToGbk(const std::string& inputUtf8) {
        std::wstring wstr = convertUtf8ToWstring(inputUtf8);
        return convertWstringToGbk(wstr);
    }

    std::string convertGbkToUtf8(const std::string& inputGbk) {
        std::wstring wstr = convertGbkToWstring(inputGbk);
        return convertWstringToUtf8(wstr);
    }

    // DllLoader类实现
    DllLoader* DllLoader::instance = nullptr;

    DllLoader::DllLoader() {
        LoadDll();
    }

    void DllLoader::LoadDll() {
        json feature_list = json::array();
        try
        {
            sntl_admin::SNTL sntl;
            feature_list = sntl.GetFeatureList();

            if (std::find_if(feature_list.begin(), feature_list.end(),
                [](const json& item) { return item.get<std::string>() == "1"; }) != feature_list.end())
            {
                // 使用默认DLL
            } else if (std::find_if(feature_list.begin(), feature_list.end(),
                [](const json& item) { return item.get<std::string>() == "2"; }) != feature_list.end())
            {
                dllName = dllName2;
                dllPath = dllPath2;
            }
        }
        catch (...)
        {
            // 如果获取功能列表失败，则使用默认的DLL路径
        }

        // 加载DLL
#ifdef _WIN32
        hModule = LoadLibraryA(dllName.c_str());
        if (!hModule)
        {
            hModule = LoadLibraryA(dllPath.c_str());
            if (!hModule)
            {
                throw std::runtime_error("failed to load dll");
            }
        }

        // 获取函数指针
        dlcv_load_model = (LoadModelFuncType)GetProcAddress((HMODULE)hModule, "dlcv_load_model");
        dlcv_free_model = (FreeModelFuncType)GetProcAddress((HMODULE)hModule, "dlcv_free_model");
        dlcv_get_model_info = (GetModelInfoFuncType)GetProcAddress((HMODULE)hModule, "dlcv_get_model_info");
        dlcv_infer = (InferFuncType)GetProcAddress((HMODULE)hModule, "dlcv_infer");
        dlcv_free_model_result = (FreeModelResultFuncType)GetProcAddress((HMODULE)hModule, "dlcv_free_model_result");
        dlcv_free_result = (FreeResultFuncType)GetProcAddress((HMODULE)hModule, "dlcv_free_result");
        dlcv_free_all_models = (FreeAllModelsFuncType)GetProcAddress((HMODULE)hModule, "dlcv_free_all_models");
        dlcv_get_device_info = (GetDeviceInfoFuncType)GetProcAddress((HMODULE)hModule, "dlcv_get_device_info");
#else
        // Linux下的DLL加载实现
        // ...
#endif
    }

    DllLoader& DllLoader::Instance() {
        if (!instance)
        {
            instance = new DllLoader();
        }
        return *instance;
    }

    // Model类实现
    Model::Model() {}

    Model::Model(const std::string& modelPath, int device_id)
        : _deviceId(device_id) {
        const std::string modelPathUtf8 = convertGbkToUtf8(modelPath);

        if (IsFlowArchivePath(modelPathUtf8)) {
            _isFlowGraphMode = true;
            _flowModel = new flow::FlowGraphModel();
            try {
                DvsUnpackResult unpack = UnpackDvsArchiveToTemp(modelPath);
                TempDirGuard tempGuard(unpack.tempDir);

                const std::string pipelinePath = JoinPath(unpack.tempDir, "pipeline.json");
                WriteUtf8Text(pipelinePath, unpack.pipelineRoot.dump());

                json report = _flowModel->Load(pipelinePath, device_id);
                int code = 1;
                try { code = report.contains("code") ? report.at("code").get<int>() : 1; } catch (...) { code = 1; }
                if (code != 0) {
                    throw std::runtime_error(report.dump());
                }
                modelIndex = 1; // dvst 模式下仅作为“已加载”标记
                return;
            } catch (const std::exception& ex) {
                delete _flowModel;
                _flowModel = nullptr;
                throw std::runtime_error(std::string("failed to load dvs model: ") + ex.what());
            }
        }

        json config;
        config["model_path"] = modelPathUtf8;
        config["device_id"] = device_id;

        std::string jsonStr = config.dump();

        void* resultPtr = DllLoader::Instance().GetLoadModelFunc()(jsonStr.c_str());
        std::string resultJson = std::string(static_cast<const char*>(resultPtr));
        json resultObject = json::parse(resultJson);
        if (resultObject.contains("model_index"))
        {
            modelIndex = resultObject["model_index"].get<int>();
        } else
        {
            DllLoader::Instance().GetFreeResultFunc()(resultPtr);
            throw std::runtime_error("load model failed: " + resultObject.dump());
        }

        DllLoader::Instance().GetFreeResultFunc()(resultPtr);
    }

    Model::~Model() {
        try { FreeModel(); } catch (...) {}
    }

    void Model::FreeModel() {
        if (_isFlowGraphMode) {
            delete _flowModel;
            _flowModel = nullptr;
            modelIndex = -1;
            return;
        }

        if (modelIndex == -1) {
            return;
        }
        // 仅“借用”modelIndex 时，不释放底层模型；只把本对象标记为无效。
        if (!OwnModelIndex) {
            modelIndex = -1;
            return;
        }
        json config;
        config["model_index"] = modelIndex;

        std::string jsonStr = config.dump();
        void* resultPtr = DllLoader::Instance().GetFreeModelFunc()(jsonStr.c_str());
        std::string resultJson = std::string(static_cast<const char*>(resultPtr));
        json resultObject = json::parse(resultJson);
        DllLoader::Instance().GetFreeResultFunc()(resultPtr);
        modelIndex = -1;
    }

    json Model::GetModelInfo() {
        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");
            return _flowModel->GetModelInfo();
        }

        json config;
        config["model_index"] = modelIndex;

        std::string jsonStr = config.dump();
        void* resultPtr = DllLoader::Instance().GetModelInfoFunc()(jsonStr.c_str());
        std::string resultJson = std::string(static_cast<const char*>(resultPtr));
        json resultObject = json::parse(resultJson);
        DllLoader::Instance().GetFreeResultFunc()(resultPtr);
        return resultObject;
    }

    std::pair<json, void*> Model::InferInternal(const std::vector<cv::Mat>& images, const json& params_json) {
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
            void* resultPtr = DllLoader::Instance().GetInferFunc()(jsonStr.c_str());
            std::string resultJson = std::string(static_cast<const char*>(resultPtr));
            json resultObject = json::parse(resultJson);

            // 检查是否返回错误
            if (resultObject.contains("code") && resultObject["code"].get<int>() != 0)
            {
                DllLoader::Instance().GetFreeModelResultFunc()(resultPtr);
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

                results.emplace_back(categoryId, categoryName, score, area, bbox, withMask, mask_img, withBbox, withAngle, angle);
            }

            sampleResults.emplace_back(results);
        }

        return Result(sampleResults);
    }

    Result Model::Infer(const cv::Mat& image, const json& params_json) {
        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");
            if (image.empty()) throw std::invalid_argument("image is empty");

            json flowResults = _flowModel->InferOneOutJson(image, params_json);

            std::vector<SampleResult> sampleResults;
            sampleResults.emplace_back(ConvertFlowResultListToObjects(flowResults));
            return Result(std::move(sampleResults));
        }

        // 将单张图像放入列表中传递
        auto resultTuple = InferInternal({ image }, params_json);

        try
        {
            Result result = ParseToStructResult(resultTuple.first);
            // 完成后释放结果
            DllLoader::Instance().GetFreeModelResultFunc()(resultTuple.second);
            return result;
        }
        catch (...)
        {
            // 发生异常时也需要释放结果
            DllLoader::Instance().GetFreeModelResultFunc()(resultTuple.second);
            throw;
        }
    }

    Result Model::InferBatch(const std::vector<cv::Mat>& image_list, const json& params_json) {
        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");

            std::vector<SampleResult> sampleResults;
            sampleResults.reserve(image_list.size());
            for (const auto& image : image_list) {
                if (image.empty()) {
                    sampleResults.emplace_back(std::vector<ObjectResult>{});
                    continue;
                }
                json flowResults = _flowModel->InferOneOutJson(image, params_json);
                sampleResults.emplace_back(ConvertFlowResultListToObjects(flowResults));
            }
            return Result(std::move(sampleResults));
        }

        auto resultTuple = InferInternal(image_list, params_json);

        try
        {
            Result result = ParseToStructResult(resultTuple.first);
            // 完成后释放结果
            DllLoader::Instance().GetFreeModelResultFunc()(resultTuple.second);
            return result;
        }
        catch (...)
        {
            // 发生异常时也需要释放结果
            DllLoader::Instance().GetFreeModelResultFunc()(resultTuple.second);
            throw;
        }
    }

    json Model::InferOneOutJson(const cv::Mat& image, const json& params_json) {
        if (_isFlowGraphMode) {
            if (!_flowModel) throw std::runtime_error("dvs model not loaded");
            if (image.empty()) throw std::invalid_argument("image is empty");

            json flowResults = _flowModel->InferOneOutJson(image, params_json);
            return NormalizeFlowOneOutJson(flowResults);
        }

        auto resultTuple = InferInternal({ image }, params_json);

        try
        {
            json results = resultTuple.first["sample_results"][0]["results"];

            for (auto& result : results)
            {
                std::vector<double> bbox = result["bbox"].get<std::vector<double>>();
                bool withMask = result["with_mask"].get<bool>();

                auto mask = result["mask"];
                int mask_width = mask["width"].get<int>();
                int mask_height = mask["height"].get<int>();
                int width = static_cast<int>(bbox[2]);
                int height = static_cast<int>(bbox[3]);

                if (withMask)
                {
                    void* mask_ptr = reinterpret_cast<void*>(static_cast<uintptr_t>(mask["mask_ptr"].get<uint64_t>()));
                    cv::Mat mask_img(mask_height, mask_width, CV_8UC1, mask_ptr);

                    if (mask_img.cols != width || mask_img.rows != height)
                    {
                        cv::resize(mask_img, mask_img, cv::Size(width, height));
                    }

                    std::vector<std::vector<cv::Point>> contours;
                    cv::findContours(mask_img, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

                    json pointsJson = json::array();
                    if (!contours.empty())
                    {
                        for (const auto& point : contours[0])
                        {
                            json point_obj;
                            point_obj["x"] = static_cast<int>(point.x + bbox[0]);
                            point_obj["y"] = static_cast<int>(point.y + bbox[1]);
                            pointsJson.push_back(point_obj);
                        }
                    }
                    result["mask"] = pointsJson;
                }
            }

            // 完成后释放结果
            DllLoader::Instance().GetFreeModelResultFunc()(resultTuple.second);
            return results;
        }
        catch (...)
        {
            // 发生异常时也需要释放结果
            DllLoader::Instance().GetFreeModelResultFunc()(resultTuple.second);
            throw;
        }
    }

    // SlidingWindowModel类实现
    SlidingWindowModel::SlidingWindowModel(
        const std::string& modelPath,
        int device_id,
        int small_img_width,
        int small_img_height,
        int horizontal_overlap,
        int vertical_overlap,
        float threshold,
        float iou_threshold,
        float combine_ios_threshold) {
        json config;
        config["type"] = "sliding_window_pipeline";
        config["model_path"] = modelPath;
        config["device_id"] = device_id;
        config["small_img_width"] = small_img_width;
        config["small_img_height"] = small_img_height;
        config["horizontal_overlap"] = horizontal_overlap;
        config["vertical_overlap"] = vertical_overlap;
        config["threshold"] = threshold;
        config["iou_threshold"] = iou_threshold;
        config["combine_ios_threshold"] = combine_ios_threshold;

        std::string jsonStr = config.dump();

        void* resultPtr = DllLoader::Instance().GetLoadModelFunc()(jsonStr.c_str());
        std::string resultJson = std::string(static_cast<const char*>(resultPtr));
        json resultObject = json::parse(resultJson);
        if (resultObject.contains("model_index"))
        {
            modelIndex = resultObject["model_index"].get<int>();
        } else
        {
            DllLoader::Instance().GetFreeResultFunc()(resultPtr);
            throw std::runtime_error("load sliding window model failed: " + resultObject.dump());
        }

        DllLoader::Instance().GetFreeResultFunc()(resultPtr);
    }

    // Utils类实现
    std::string Utils::JsonToString(const json& j) {
        return j.dump(4); // 缩进为4
    }

    void Utils::FreeAllModels() {
        DllLoader::Instance().GetFreeAllModelsFunc()();
    }

    json Utils::GetDeviceInfo() {
        void* resultPtr = DllLoader::Instance().GetDeviceInfoFunc()();
        std::string resultJson = std::string(static_cast<const char*>(resultPtr));
        json resultObject = json::parse(resultJson);
        DllLoader::Instance().GetFreeResultFunc()(resultPtr);
        return resultObject;
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

        int result = nvmlInit();
        if (result != 0)
        {
            json ret;
            ret["code"] = 1;
            ret["message"] = "Failed to initialize NVML.";
            return ret;
        }

        unsigned int deviceCount = 0;
        result = nvmlDeviceGetCount(&deviceCount);
        if (result != 0)
        {
            nvmlShutdown();
            json ret;
            ret["code"] = 2;
            ret["message"] = "Failed to get device count.";
            return ret;
        }

        for (unsigned int i = 0; i < deviceCount; i++)
        {
            nvmlDevice_t device;
            result = nvmlDeviceGetHandleByIndex(i, &device);
            if (result != 0)
            {
                continue; // 如果无法获取当前设备
            }

            char name[64];
            result = nvmlDeviceGetName(device, name, 64);
            if (result == 0)
            {
                std::map<std::string, json> deviceInfo;
                deviceInfo["device_id"] = i;
                deviceInfo["device_name"] = name;
                devices.push_back(deviceInfo);
            }
        }

        nvmlShutdown();

        json ret;
        ret["code"] = 0;
        ret["message"] = "Success";
        ret["devices"] = devices;
        return ret;
    }

#ifdef _WIN32
    // NVML库函数实现
    int Utils::nvmlInit() {
        typedef int (*NvmlInitFunc)();
        HMODULE hModule = LoadLibraryA("nvml.dll");
        if (!hModule) return -1;
        NvmlInitFunc func = (NvmlInitFunc)GetProcAddress(hModule, "nvmlInit");
        if (!func) return -1;
        return func();
    }

    int Utils::nvmlShutdown() {
        typedef int (*NvmlShutdownFunc)();
        HMODULE hModule = LoadLibraryA("nvml.dll");
        if (!hModule) return -1;
        NvmlShutdownFunc func = (NvmlShutdownFunc)GetProcAddress(hModule, "nvmlShutdown");
        if (!func) return -1;
        return func();
    }

    int Utils::nvmlDeviceGetCount(unsigned int* deviceCount) {
        typedef int (*NvmlDeviceGetCountFunc)(unsigned int*);
        HMODULE hModule = LoadLibraryA("nvml.dll");
        if (!hModule) return -1;
        NvmlDeviceGetCountFunc func = (NvmlDeviceGetCountFunc)GetProcAddress(hModule, "nvmlDeviceGetCount");
        if (!func) return -1;
        return func(deviceCount);
    }

    int Utils::nvmlDeviceGetName(nvmlDevice_t device, char* name, unsigned int length) {
        typedef int (*NvmlDeviceGetNameFunc)(nvmlDevice_t, char*, unsigned int);
        HMODULE hModule = LoadLibraryA("nvml.dll");
        if (!hModule) return -1;
        NvmlDeviceGetNameFunc func = (NvmlDeviceGetNameFunc)GetProcAddress(hModule, "nvmlDeviceGetName");
        if (!func) return -1;
        return func(device, name, length);
    }

    int Utils::nvmlDeviceGetHandleByIndex(unsigned int index, nvmlDevice_t* device) {
        typedef int (*NvmlDeviceGetHandleByIndexFunc)(unsigned int, nvmlDevice_t*);
        HMODULE hModule = LoadLibraryA("nvml.dll");
        if (!hModule) return -1;
        NvmlDeviceGetHandleByIndexFunc func = (NvmlDeviceGetHandleByIndexFunc)GetProcAddress(hModule, "nvmlDeviceGetHandleByIndex");
        if (!func) return -1;
        return func(index, device);
    }
#endif

}