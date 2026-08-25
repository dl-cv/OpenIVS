#include "dlcv_infer.h"
#include "dlcv_sntl_admin.h"
#include "ImageInputUtils.h"
#include "flow/FlowGraphModel.h"
#include "flow/FlowPayloadTypes.h"
#include "flow/modules/ModelModules.h"
#include "flow/utils/MaskRleUtils.h"
#ifdef _WIN32
#include <Windows.h>
#include <share.h>
#else
#include <dlfcn.h>
#include <filesystem>
#include <iconv.h>
#include <link.h>
#include <unistd.h>
#endif
#include <algorithm>
#include <array>
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
#include <locale>
#include <mutex>
#include <stdexcept>
#include <system_error>
#include <unordered_map>
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

struct DvsUnpackResult {
    Json pipelineRoot = Json::object();
    std::string tempDir;
};

struct DvsTempCacheEntry {
    Json pipelineRoot = Json::object();
    size_t refCount = 0;
};

std::mutex g_dvsTempCacheMutex;
std::unordered_map<std::string, DvsTempCacheEntry> g_dvsTempCache;

static bool DeleteDirectoryRecursive(const std::string& dir) {
    if (dir.empty()) return true;
#ifdef _WIN32
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
#else
    std::error_code ec;
    fs::remove_all(dir, ec);
    return !ec;
#endif
}

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

class Sha256Digest final {
public:
    void Update(const void* data, size_t len) {
        if (data == nullptr || len == 0) return;
        const auto* bytes = static_cast<const unsigned char*>(data);
        _totalBytes += static_cast<std::uint64_t>(len);
        while (len > 0) {
            const size_t copySize = std::min(len, _buffer.size() - _bufferSize);
            std::memcpy(_buffer.data() + _bufferSize, bytes, copySize);
            _bufferSize += copySize;
            bytes += copySize;
            len -= copySize;
            if (_bufferSize == _buffer.size()) {
                Transform(_buffer.data());
                _bufferSize = 0;
            }
        }
    }

    std::string FinalHex() {
        const std::uint64_t bitLength = _totalBytes * 8ULL;
        _buffer[_bufferSize++] = 0x80;
        if (_bufferSize > 56) {
            while (_bufferSize < 64) _buffer[_bufferSize++] = 0;
            Transform(_buffer.data());
            _bufferSize = 0;
        }
        while (_bufferSize < 56) _buffer[_bufferSize++] = 0;
        for (int i = 7; i >= 0; --i) {
            _buffer[_bufferSize++] = static_cast<unsigned char>((bitLength >> (i * 8)) & 0xFFU);
        }
        Transform(_buffer.data());
        _bufferSize = 0;

        static const char* kHex = "0123456789abcdef";
        std::string out;
        out.reserve(64);
        for (const std::uint32_t value : _state) {
            for (int shift = 28; shift >= 0; shift -= 4) {
                out.push_back(kHex[(value >> shift) & 0x0FU]);
            }
        }
        return out;
    }

private:
    static std::uint32_t RotateRight(std::uint32_t value, unsigned int bits) {
        return (value >> bits) | (value << (32U - bits));
    }

    void Transform(const unsigned char* block) {
        static constexpr std::array<std::uint32_t, 64> kRoundConstants = {
            0x428a2f98U, 0x71374491U, 0xb5c0fbcfU, 0xe9b5dba5U,
            0x3956c25bU, 0x59f111f1U, 0x923f82a4U, 0xab1c5ed5U,
            0xd807aa98U, 0x12835b01U, 0x243185beU, 0x550c7dc3U,
            0x72be5d74U, 0x80deb1feU, 0x9bdc06a7U, 0xc19bf174U,
            0xe49b69c1U, 0xefbe4786U, 0x0fc19dc6U, 0x240ca1ccU,
            0x2de92c6fU, 0x4a7484aaU, 0x5cb0a9dcU, 0x76f988daU,
            0x983e5152U, 0xa831c66dU, 0xb00327c8U, 0xbf597fc7U,
            0xc6e00bf3U, 0xd5a79147U, 0x06ca6351U, 0x14292967U,
            0x27b70a85U, 0x2e1b2138U, 0x4d2c6dfcU, 0x53380d13U,
            0x650a7354U, 0x766a0abbU, 0x81c2c92eU, 0x92722c85U,
            0xa2bfe8a1U, 0xa81a664bU, 0xc24b8b70U, 0xc76c51a3U,
            0xd192e819U, 0xd6990624U, 0xf40e3585U, 0x106aa070U,
            0x19a4c116U, 0x1e376c08U, 0x2748774cU, 0x34b0bcb5U,
            0x391c0cb3U, 0x4ed8aa4aU, 0x5b9cca4fU, 0x682e6ff3U,
            0x748f82eeU, 0x78a5636fU, 0x84c87814U, 0x8cc70208U,
            0x90befffaU, 0xa4506cebU, 0xbef9a3f7U, 0xc67178f2U
        };

        std::array<std::uint32_t, 64> words{};
        for (size_t i = 0; i < 16; ++i) {
            const size_t offset = i * 4;
            words[i] = (static_cast<std::uint32_t>(block[offset]) << 24)
                | (static_cast<std::uint32_t>(block[offset + 1]) << 16)
                | (static_cast<std::uint32_t>(block[offset + 2]) << 8)
                | static_cast<std::uint32_t>(block[offset + 3]);
        }
        for (size_t i = 16; i < words.size(); ++i) {
            const std::uint32_t s0 = RotateRight(words[i - 15], 7)
                ^ RotateRight(words[i - 15], 18)
                ^ (words[i - 15] >> 3);
            const std::uint32_t s1 = RotateRight(words[i - 2], 17)
                ^ RotateRight(words[i - 2], 19)
                ^ (words[i - 2] >> 10);
            words[i] = words[i - 16] + s0 + words[i - 7] + s1;
        }

        std::uint32_t a = _state[0];
        std::uint32_t b = _state[1];
        std::uint32_t c = _state[2];
        std::uint32_t d = _state[3];
        std::uint32_t e = _state[4];
        std::uint32_t f = _state[5];
        std::uint32_t g = _state[6];
        std::uint32_t h = _state[7];

        for (size_t i = 0; i < words.size(); ++i) {
            const std::uint32_t sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
            const std::uint32_t choose = (e & f) ^ ((~e) & g);
            const std::uint32_t temp1 = h + sum1 + choose + kRoundConstants[i] + words[i];
            const std::uint32_t sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
            const std::uint32_t majority = (a & b) ^ (a & c) ^ (b & c);
            const std::uint32_t temp2 = sum0 + majority;

            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        _state[0] += a;
        _state[1] += b;
        _state[2] += c;
        _state[3] += d;
        _state[4] += e;
        _state[5] += f;
        _state[6] += g;
        _state[7] += h;
    }

    std::array<std::uint32_t, 8> _state = {
        0x6a09e667U, 0xbb67ae85U, 0x3c6ef372U, 0xa54ff53aU,
        0x510e527fU, 0x9b05688cU, 0x1f83d9abU, 0x5be0cd19U
    };
    std::array<unsigned char, 64> _buffer{};
    size_t _bufferSize = 0;
    std::uint64_t _totalBytes = 0;
};

std::string Sha256Hex(const void* data, size_t len) {
    Sha256Digest digest;
    digest.Update(data, len);
    return digest.FinalHex();
}

std::string ShortHash(const std::string& fullHash) {
    static constexpr size_t kShortHashLength = 16;
    return fullHash.substr(0, std::min(kShortHashLength, fullHash.size()));
}

std::string GetNormalizedDvsPathUtf8(const std::wstring& archivePathW) {
#ifdef _WIN32
    const DWORD required = GetFullPathNameW(archivePathW.c_str(), 0, nullptr, nullptr);
    if (required == 0) throw std::runtime_error("无法解析 DVS 完整路径");
    std::vector<wchar_t> buffer(static_cast<size_t>(required));
    if (GetFullPathNameW(archivePathW.c_str(), required, buffer.data(), nullptr) == 0) {
        throw std::runtime_error("无法解析 DVS 完整路径");
    }
    std::wstring normalizedPath(buffer.data());
    for (wchar_t& ch : normalizedPath) ch = static_cast<wchar_t>(std::towlower(ch));
    return dlcv_infer::convertWstringToUtf8(normalizedPath);
#else
    std::error_code ec;
    const fs::path fullPath = fs::absolute(fs::path(WideToUtf8Portable(archivePathW)), ec);
    if (ec) throw std::runtime_error("无法解析 DVS 完整路径");
    return fullPath.lexically_normal().string();
#endif
}

std::string GetDvsTempDir(const std::string& archiveIdentity) {
#ifdef _WIN32
    char tmpPath[MAX_PATH] = { 0 };
    DWORD n = GetTempPathA(MAX_PATH, tmpPath);
    if (n == 0 || n >= MAX_PATH) {
        throw std::runtime_error("failed to get temp directory");
    }
    const std::string processId = std::to_string(GetCurrentProcessId());
    return JoinPath(
        std::string(tmpPath),
        "DlcvDvs_" + archiveIdentity + "_" + processId);
#else
    std::error_code ec;
    const fs::path base = fs::temp_directory_path(ec);
    if (ec) {
        throw std::runtime_error("failed to get temp directory");
    }
    return (base / (
        "DlcvDvs_" + archiveIdentity
        + "_" + std::to_string(static_cast<long long>(getpid())))).string();
#endif
}

void CreateCleanDvsTempDir(const std::string& dir) {
    (void)DeleteDirectoryRecursive(dir);
#ifdef _WIN32
    if (CreateDirectoryA(dir.c_str(), nullptr) == 0) {
        throw std::runtime_error("failed to create temp directory");
    }
#else
    std::error_code ec;
    if (!fs::create_directory(fs::path(dir), ec) || ec) {
        throw std::runtime_error("failed to create temp directory");
    }
#endif
}

void ReleaseDvsTempDir(const std::string& dir) {
    if (dir.empty()) return;
    std::lock_guard<std::mutex> lock(g_dvsTempCacheMutex);
    const auto it = g_dvsTempCache.find(dir);
    if (it == g_dvsTempCache.end()) return;
    if (it->second.refCount > 0) --it->second.refCount;
    if (it->second.refCount == 0) {
        g_dvsTempCache.erase(it);
        (void)DeleteDirectoryRecursive(dir);
    }
}

std::string HashOpenFileAndRewind(FILE* fp) {
    if (fp == nullptr) throw std::runtime_error("file handle is null");
    Sha256Digest digest;
    std::vector<char> buffer(1024 * 1024);
    for (;;) {
        const size_t n = std::fread(buffer.data(), 1, buffer.size(), fp);
        if (n > 0) digest.Update(buffer.data(), n);
        if (n < buffer.size()) {
            if (std::ferror(fp)) throw std::runtime_error("failed to read dvst file content");
            break;
        }
    }
    if (std::fseek(fp, 0, SEEK_SET) != 0) {
        throw std::runtime_error("failed to rewind dvst file");
    }
    return digest.FinalHex();
}

void ReadExactOrThrow(
    FILE* fp,
    char* dst,
    size_t len,
    const std::string& errMsg) {
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

void RewritePipelineModelPath(
    Json& pipelineRoot,
    const std::unordered_map<std::string, std::string>& fileMap,
    const std::string& modelPoolPrefix) {
    if (!pipelineRoot.is_object() || !pipelineRoot.contains("nodes") || !pipelineRoot.at("nodes").is_array()) {
        throw std::runtime_error("pipeline.json missing nodes");
    }

    for (auto& node : pipelineRoot.at("nodes")) {
        if (!node.is_object()) continue;
        if (!node.contains("properties") || !node.at("properties").is_object()) continue;

        auto& props = node.at("properties");
        if (!props.contains("model_path") || !props.at("model_path").is_string()) continue;

        const std::string originalPath = props.at("model_path").get<std::string>();
        props["model_path_original"] = originalPath;
        const std::string originalName = GetFileNameOnly(originalPath);
        props["model_name"] = originalName.empty() ? originalPath : originalName;
        props["model_pool_key"] = modelPoolPrefix + "|model:" + ToLowerAscii(originalPath);

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

DvsUnpackResult UnpackDvsArchiveToTemp(const std::wstring& archivePathW) {
    const std::string normalizedPath = GetNormalizedDvsPathUtf8(archivePathW);
#ifdef _WIN32
    FILE* fp = nullptr;
    fp = _wfsopen(archivePathW.c_str(), L"rb", _SH_DENYWR);
    if (fp == nullptr) {
#else
    FILE* fp = std::fopen(WideToUtf8Portable(archivePathW).c_str(), "rb");
    if (fp == nullptr) {
#endif
        throw std::runtime_error("failed to open dvst file");
    }

    DvsUnpackResult out;
    try {
        const std::string contentHash = HashOpenFileAndRewind(fp);
        const std::string pathHash = Sha256Hex(normalizedPath.data(), normalizedPath.size());
        std::string identitySource = pathHash;
        identitySource.push_back('\0');
        identitySource += contentHash;
        const std::string archiveIdentity = Sha256Hex(identitySource.data(), identitySource.size());
        out.tempDir = GetDvsTempDir(archiveIdentity);

        std::lock_guard<std::mutex> cacheLock(g_dvsTempCacheMutex);
        const auto cached = g_dvsTempCache.find(out.tempDir);
        if (cached != g_dvsTempCache.end()) {
            out.pipelineRoot = cached->second.pipelineRoot;
            ++cached->second.refCount;
            std::fclose(fp);
            return out;
        }

        CreateCleanDvsTempDir(out.tempDir);
        TempDirGuard unpackGuard(out.tempDir);

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
                    ReadExactOrThrow(
                        fp,
                        &text[0],
                        static_cast<size_t>(size),
                        "failed to read pipeline.json");
                }
                out.pipelineRoot = Json::parse(text);
                gotPipeline = true;
            } else {
                std::string ext = GetExtensionWithDot(fileName);
                if (ext.empty()) ext = ".tmp";
                const std::string normalizedFileName = ToLowerAscii(fileName);
                const std::string fileHash = Sha256Hex(normalizedFileName.data(), normalizedFileName.size());
                const std::string safeName = "file_" + std::to_string(i) + "_" + ShortHash(fileHash) + ext;
                const std::string fullPath = JoinPath(out.tempDir, safeName);

                CopyStreamToFile(fp, fullPath, size);
                fileNameToTemp[ToLowerAscii(fileName)] = fullPath;
                fileNameToTemp[ToLowerAscii(GetFileNameOnly(fileName))] = fullPath;
            }
        }

        if (!gotPipeline) throw std::runtime_error("pipeline.json not found in dvst archive");
        const std::string modelPoolPrefix = "dvs:" + archiveIdentity;
        RewritePipelineModelPath(out.pipelineRoot, fileNameToTemp, modelPoolPrefix);
        WriteUtf8Text(JoinPath(out.tempDir, "pipeline.json"), out.pipelineRoot.dump());
        DvsTempCacheEntry cacheEntry;
        cacheEntry.pipelineRoot = out.pipelineRoot;
        cacheEntry.refCount = 1;
        g_dvsTempCache.emplace(out.tempDir, std::move(cacheEntry));
        unpackGuard.Release();
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

    // 底层 modelIndex 全局引用计数（解决底层 DLL content-hash dedup 导致同 modelIndex 被多对象共享的问题）
    static std::mutex g_modelIndexRefMu;
    static std::unordered_map<int, int> g_modelIndexRefCount;

    // dvst（流程模型）的 modelIndex 由本层自管理，从 10000 起递增，
    // 与底层 dvt 返回的 model_index（0 起递增）分区，避免上层按 modelIndex 索引时 dvst 与 dvt 撞键。
    static std::mutex g_flowModelIndexMu;
    static int g_nextFlowModelIndex = 10000;

    static int AllocateFlowModelIndex() {
        std::lock_guard<std::mutex> lk(g_flowModelIndexMu);
        return g_nextFlowModelIndex++;
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
    DllLoader* DllLoader::instance = nullptr;

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

        dlcv_load_model = (LoadModelFuncType)ResolveSymbol(hModule, "dlcv_load_model");
        dlcv_free_model = (FreeModelFuncType)ResolveSymbol(hModule, "dlcv_free_model");
        dlcv_get_model_info = (GetModelInfoFuncType)ResolveSymbol(hModule, "dlcv_get_model_info");
        dlcv_infer = (InferFuncType)ResolveSymbol(hModule, "dlcv_infer");
        dlcv_free_model_result = (FreeModelResultFuncType)ResolveSymbol(hModule, "dlcv_free_model_result");
        dlcv_free_result = (FreeResultFuncType)ResolveSymbol(hModule, "dlcv_free_result");
        dlcv_free_all_models = (FreeAllModelsFuncType)ResolveSymbol(hModule, "dlcv_free_all_models");
        dlcv_get_device_info = (GetDeviceInfoFuncType)ResolveSymbol(hModule, "dlcv_get_device_info");
        dlcv_keep_max_clock = (KeepMaxClockFuncType)ResolveSymbol(hModule, "dlcv_keep_max_clock");
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
        if (!instance)
        {
            instance = new DllLoader(AutoDetectProvider());
        }
        return *instance;
    }

    namespace {
        bool TryResolveExplicitProviderFromStream(std::istream& stream, sntl_admin::DogProvider& outProvider) {
            std::string header;
            std::string headerJsonStr;
            std::getline(stream, header);
            std::getline(stream, headerJsonStr);
            if (header != "DV") {
                throw std::runtime_error("invalid model format: missing DV header");
            }
            auto headerJson = nlohmann::json::parse(headerJsonStr);
            if (!headerJson.contains("dog_provider")) {
                return false;
            }
            std::string p = headerJson["dog_provider"].get<std::string>();
            for (auto& c : p) {
                c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
            }
            if (p == "sentinel") {
                outProvider = sntl_admin::DogProvider::Sentinel;
                return true;
            }
            if (p == "virbox") {
                outProvider = sntl_admin::DogProvider::Virbox;
                return true;
            }
            throw std::runtime_error("invalid dog provider in header_json: " + p);
        }
    }

    void DllLoader::EnsureForModel(const std::string& modelPath) {
#ifndef _WIN32
        // Linux 默认认为有加密狗，跳过 sntl_adminapi 检测
        (void)modelPath;
        return;
#endif
        std::wstring wpath = convertUtf8ToWstring(modelPath);
#ifdef _WIN32
        std::ifstream file(wpath);
#else
        std::ifstream file(WideToUtf8Portable(wpath));
#endif
        if (!file) {
            throw std::runtime_error("failed to open model file");
        }
        sntl_admin::DogProvider needed;
        if (!TryResolveExplicitProviderFromStream(file, needed)) {
            return;
        }
        if (instance && instance->dogProvider == needed) {
            return;
        }
        auto dogInfo = needed == sntl_admin::DogProvider::Sentinel
            ? sntl_admin::DogUtils::GetSentinelInfo()
            : sntl_admin::DogUtils::GetVirboxInfo();
        if (dogInfo.provider == sntl_admin::DogProvider::Unknown) {
            throw std::runtime_error(std::string("模型要求 provider ")
                + (needed == sntl_admin::DogProvider::Sentinel ? "Sentinel" : "Virbox")
                + "，但未检测到对应的加密狗设备或特性");
        }
        instance = new DllLoader(needed);
    }

    void DllLoader::EnsureForModel(const std::wstring& modelPath) {
#ifndef _WIN32
        // Linux 默认认为有加密狗，跳过 sntl_adminapi 检测
        (void)modelPath;
        return;
#endif
#ifdef _WIN32
        std::ifstream file(modelPath);
#else
        std::ifstream file(WideToUtf8Portable(modelPath));
#endif
        if (!file) {
            throw std::runtime_error("failed to open model file");
        }
        sntl_admin::DogProvider needed;
        if (!TryResolveExplicitProviderFromStream(file, needed)) {
            return;
        }
        if (instance && instance->dogProvider == needed) {
            return;
        }
        auto dogInfo = needed == sntl_admin::DogProvider::Sentinel
            ? sntl_admin::DogUtils::GetSentinelInfo()
            : sntl_admin::DogUtils::GetVirboxInfo();
        if (dogInfo.provider == sntl_admin::DogProvider::Unknown) {
            throw std::runtime_error(std::string("模型要求 provider ")
                + (needed == sntl_admin::DogProvider::Sentinel ? "Sentinel" : "Virbox")
                + "，但未检测到对应的加密狗设备或特性");
        }
        instance = new DllLoader(needed);
    }

    // Model类实现
    Model::Model() {}

    Model::Model(const std::string& modelPath, int device_id)
        : _deviceId(device_id) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        const std::wstring modelPathW = DecodeModelPathString(modelPath);
        const std::string modelPathUtf8 = convertWstringToUtf8(modelPathW);
        if (IsFlowArchivePath(modelPathUtf8)) {
            _isFlowGraphMode = true;
            _flowModel = new flow::FlowGraphModel();
            try {
                DvsUnpackResult unpack = UnpackDvsArchiveToTemp(modelPathW);
                _tempDir = unpack.tempDir;

                const std::string pipelinePath = JoinPath(unpack.tempDir, "pipeline.json");
                json report = _flowModel->Load(pipelinePath, device_id);
                int code = 1;
                try { code = report.contains("code") ? report.at("code").get<int>() : 1; } catch (...) { code = 1; }
                if (code != 0) {
                    throw std::runtime_error(report.dump());
                }
                modelIndex = AllocateFlowModelIndex();
                return;
            } catch (const std::exception& ex) {
                delete _flowModel;
                _flowModel = nullptr;
                if (!_tempDir.empty()) {
                    ReleaseDvsTempDir(_tempDir);
                    _tempDir.clear();
                }
                throw std::runtime_error(std::string("failed to load dvs model: ") + ex.what());
            }
        }

        DllLoader::EnsureForModel(modelPathUtf8);
        _dllLoader = &DllLoader::Instance();
        _loadedDogProvider = _dllLoader->GetDogProvider();
        _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();
        if (!_dllLoader->GetLoadModelFunc()) {
            throw std::runtime_error("未检测到授权");
        }

        json config;
        config["model_path"] = modelPathUtf8;
        config["device_id"] = device_id;

        std::string jsonStr = config.dump();

        const char* resultPtr = _dllLoader->GetLoadModelFunc()(jsonStr.c_str());
        std::string resultJson = std::string(resultPtr);
        json resultObject = json::parse(resultJson);
        if (resultObject.contains("model_index"))
        {
            modelIndex = resultObject["model_index"].get<int>();
        } else
        {
            _dllLoader->GetFreeResultFunc()(resultPtr);
            throw std::runtime_error("load model failed: " + resultObject.dump());
        }

        _dllLoader->GetFreeResultFunc()(resultPtr);

        if (modelIndex >= 0 && OwnModelIndex) {
            std::lock_guard<std::mutex> lk(g_modelIndexRefMu);
            g_modelIndexRefCount[modelIndex]++;
        }
    }

    Model::Model(const std::wstring& modelPath, int device_id)
        : _deviceId(device_id) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        const std::string modelPathUtf8 = convertWstringToUtf8(modelPath);
        if (IsFlowArchivePath(modelPathUtf8)) {
            _isFlowGraphMode = true;
            _flowModel = new flow::FlowGraphModel();
            try {
                DvsUnpackResult unpack = UnpackDvsArchiveToTemp(modelPath);
                _tempDir = unpack.tempDir;

                const std::string pipelinePath = JoinPath(unpack.tempDir, "pipeline.json");
                json report = _flowModel->Load(pipelinePath, device_id);
                int code = 1;
                try { code = report.contains("code") ? report.at("code").get<int>() : 1; } catch (...) { code = 1; }
                if (code != 0) {
                    throw std::runtime_error(report.dump());
                }
                modelIndex = AllocateFlowModelIndex();
                return;
            } catch (const std::exception& ex) {
                delete _flowModel;
                _flowModel = nullptr;
                if (!_tempDir.empty()) {
                    ReleaseDvsTempDir(_tempDir);
                    _tempDir.clear();
                }
                throw std::runtime_error(std::string("failed to load dvs model: ") + ex.what());
            }
        }

        DllLoader::EnsureForModel(modelPath);
        _dllLoader = &DllLoader::Instance();
        _loadedDogProvider = _dllLoader->GetDogProvider();
        _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();
        if (!_dllLoader->GetLoadModelFunc()) {
            throw std::runtime_error("未检测到授权");
        }

        json config;
        config["model_path"] = modelPathUtf8;
        config["device_id"] = device_id;

        std::string jsonStr = config.dump();

        const char* resultPtr = _dllLoader->GetLoadModelFunc()(jsonStr.c_str());
        std::string resultJson = std::string(resultPtr);
        json resultObject = json::parse(resultJson);
        if (resultObject.contains("model_index"))
        {
            modelIndex = resultObject["model_index"].get<int>();
        } else
        {
            _dllLoader->GetFreeResultFunc()(resultPtr);
            throw std::runtime_error("load model failed: " + resultObject.dump());
        }

        _dllLoader->GetFreeResultFunc()(resultPtr);

        if (modelIndex >= 0 && OwnModelIndex) {
            std::lock_guard<std::mutex> lk(g_modelIndexRefMu);
            g_modelIndexRefCount[modelIndex]++;
        }
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
        _tempDir = std::move(other._tempDir);
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
        other._tempDir.clear();
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
        _tempDir = std::move(other._tempDir);
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
        other._tempDir.clear();
        other._dllLoader = nullptr;
        other._loadedDogProvider = sntl_admin::DogProvider::Unknown;
        other._loadedNativeDllName.clear();
        return *this;
    }

    Model::~Model() {
        try { FreeModel(); } catch (...) {}
    }

    void Model::FreeModel() {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::unique_lock<std::shared_mutex> stateLock(_stateMutex);
        freeModelLocked();
    }

    void Model::freeModelLocked() {
        {
            std::lock_guard<std::mutex> modelInfoLock(_modelInfoMutex);
            _expectedChCache = -2;
            _hasCachedModelInfo = false;
            _cachedModelInfo = json();
        }
        if (_isFlowGraphMode) {
            delete _flowModel;
            _flowModel = nullptr;
            if (!_tempDir.empty()) {
                ReleaseDvsTempDir(_tempDir);
                _tempDir.clear();
            }
            modelIndex = -1;
            _isFlowGraphMode = false;
            return;
        }

        if (modelIndex == -1) {
            _isFlowGraphMode = false;
            return;
        }
        // 仅“借用”modelIndex 时，不释放底层模型；只把本对象标记为无效。
        if (!OwnModelIndex) {
            modelIndex = -1;
            _isFlowGraphMode = false;
            return;
        }

        bool shouldFreeUnderlying = false;
        {
            std::lock_guard<std::mutex> lk(g_modelIndexRefMu);
            auto it = g_modelIndexRefCount.find(modelIndex);
            if (it != g_modelIndexRefCount.end()) {
                it->second--;
                if (it->second <= 0) {
                    g_modelIndexRefCount.erase(it);
                    shouldFreeUnderlying = true;
                }
            } else {
                // 防御性处理：计数表中没有记录，但仍然需要释放
                shouldFreeUnderlying = true;
            }
        }

        if (shouldFreeUnderlying) {
            if (!_dllLoader) {
                throw std::runtime_error("DVT模型释放失败：未加载推理DLL");
            }
            json config;
            config["model_index"] = modelIndex;
            std::string jsonStr = config.dump();
            const char* resultPtr = _dllLoader->GetFreeModelFunc()(jsonStr.c_str());
            if (resultPtr == nullptr) {
                throw std::runtime_error("DVT模型释放未返回结果");
            }

            const auto freeResult = _dllLoader->GetFreeResultFunc();
            try {
                const std::string resultJson(resultPtr);
                const json resultObject = json::parse(resultJson);
                std::string message = "底层未返回错误说明";
                if (resultObject.is_object() && resultObject.contains("message")) {
                    const json& messageToken = resultObject.at("message");
                    message = messageToken.is_string() ? messageToken.get<std::string>() : messageToken.dump();
                }

                if (!resultObject.is_object() || !resultObject.contains("code")) {
                    throw std::runtime_error("DVT模型释放失败：" + message);
                }

                int code = 0;
                try {
                    code = resultObject.at("code").get<int>();
                } catch (...) {
                    throw std::runtime_error("DVT模型释放失败：" + message);
                }
                if (code != 0) {
                    throw std::runtime_error("DVT模型释放失败：" + message);
                }
            } catch (...) {
                freeResult(resultPtr);
                throw;
            }
            freeResult(resultPtr);
        }
        modelIndex = -1;
        _isFlowGraphMode = false;
    }

    json Model::getModelInfoLocked() {
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

                // 与 C# 对齐：普通模型路径下，mask 需要归一到 bbox 尺寸，
                // 否则 flow 的 mask_to_rbox 会把“整图 mask”再次叠加 bbox 偏移，导致旋转框偏大。
                if (!mask_img.empty() && bbox.size() >= 4)
                {
                    const int bbox_w = std::max(0, static_cast<int>(std::llround(std::abs(bbox[2]))));
                    const int bbox_h = std::max(0, static_cast<int>(std::llround(std::abs(bbox[3]))));
                    if (bbox_w > 0 && bbox_h > 0 &&
                        (mask_img.cols != bbox_w || mask_img.rows != bbox_h))
                    {
                        cv::Mat resized;
                        cv::resize(mask_img, resized, cv::Size(bbox_w, bbox_h), 0, 0, cv::INTER_NEAREST);
                        mask_img = resized;
                    }
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
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::shared_lock<std::shared_mutex> stateLock(_stateMutex);
        ClearLastInspectionStatuses();
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

    json Model::InferOneOutJson(const cv::Mat& image, const json& params_json) {
        flow::ModelLifecycleReadGuard lifecycleGuard;
        std::shared_lock<std::shared_mutex> stateLock(_stateMutex);
        ClearLastInspectionStatuses();
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
        DllLoader::EnsureForModel(modelPath);
        _dllLoader = &DllLoader::Instance();
        _loadedDogProvider = _dllLoader->GetDogProvider();
        _loadedNativeDllName = _dllLoader->GetLoadedNativeDllName();
        if (!_dllLoader->GetLoadModelFunc()) {
            throw std::runtime_error("未检测到授权");
        }

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

        const char* resultPtr = _dllLoader->GetLoadModelFunc()(jsonStr.c_str());
        std::string resultJson = std::string(resultPtr);
        json resultObject = json::parse(resultJson);
        if (resultObject.contains("model_index"))
        {
            modelIndex = resultObject["model_index"].get<int>();
        } else
        {
            _dllLoader->GetFreeResultFunc()(resultPtr);
            throw std::runtime_error("load sliding window model failed: " + resultObject.dump());
        }

        _dllLoader->GetFreeResultFunc()(resultPtr);
    }

    // Utils类实现
    std::string Utils::JsonToString(const json& j) {
        return j.dump(4); // 缩进为4
    }

    void Utils::FreeAllModels() {
        flow::ModelLifecycleWriteGuard lifecycleGuard;
        flow::ModelPool::Instance().Clear();
        auto& loader = DllLoader::Instance();
        if (loader.GetFreeAllModelsFunc())
        {
            loader.GetFreeAllModelsFunc()();
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
        flow::ModelLifecycleWriteGuard lifecycleGuard;
        flow::ModelPool::Instance().Clear();
        auto& loader = DllLoader::Instance();
        RequireNativeApiFunction(loader.GetFreeAllModelsFunc(), "dlcv_free_all_models")();
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
