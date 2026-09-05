#pragma once

#include <string>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#else
#include <filesystem>
#include <system_error>
#endif

namespace dlcv_infer {
namespace flow {

inline void EnsureDirExists(const std::string& dir) {
    if (dir.empty()) return;
#ifdef _WIN32
    std::string path;
    path.reserve(dir.size());
    for (size_t i = 0; i < dir.size(); i++) {
        const char c = dir[i];
        path.push_back(c);
        if (c == '\\' || c == '/') {
            CreateDirectoryA(path.c_str(), nullptr);
        }
    }
    CreateDirectoryA(dir.c_str(), nullptr);
#else
    std::error_code ec;
    std::filesystem::create_directories(dir, ec);
#endif
}

inline std::string JoinPath(const std::string& a, const std::string& b) {
    if (a.empty()) return b;
    if (b.empty()) return a;
    const char last = a.back();
    if (last == '\\' || last == '/') return a + b;
#ifdef _WIN32
    return a + "\\" + b;
#else
    return a + "/" + b;
#endif
}

} // namespace flow
} // namespace dlcv_infer
