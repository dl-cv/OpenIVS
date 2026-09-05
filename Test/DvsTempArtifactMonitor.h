#pragma once

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cctype>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_set>
#include <vector>

#include "json/json.hpp"

namespace dvs_test {

inline std::wstring Lower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t ch) {
        return static_cast<wchar_t>(towlower(ch));
    });
    return value;
}

inline std::unordered_set<std::wstring> ReadArchiveFileNames(const std::wstring& archivePath) {
    std::ifstream stream(std::filesystem::path(archivePath), std::ios::binary);
    if (!stream) throw std::runtime_error("无法读取流程归档");
    std::string magic;
    std::string headerLine;
    if (!std::getline(stream, magic) || !std::getline(stream, headerLine)) {
        throw std::runtime_error("流程归档头信息不完整");
    }
    if (magic != "DV" && magic != "DV\r") {
        throw std::runtime_error("流程归档缺少 DV 文件头");
    }
    const nlohmann::json header = nlohmann::json::parse(headerLine);
    if (!header.contains("file_list") || !header["file_list"].is_array()) {
        throw std::runtime_error("流程归档缺少 file_list");
    }

    std::unordered_set<std::wstring> names;
    for (const auto& item : header["file_list"]) {
        if (!item.is_string()) continue;
        const std::string utf8 = item.get<std::string>();
        const int count = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()), nullptr, 0);
        if (count <= 0) continue;
        std::wstring wide(static_cast<size_t>(count), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()), wide.data(), count);
        names.insert(Lower(std::filesystem::path(wide).filename().wstring()));
    }
    return names;
}

class TempArtifactMonitor {
public:
    explicit TempArtifactMonitor(std::unordered_set<std::wstring> archiveFileNames)
        : archiveFileNames_(std::move(archiveFileNames)) {}

    ~TempArtifactMonitor() {
        Stop();
    }

    void Start() {
        if (worker_.joinable()) return;
        wchar_t buffer[MAX_PATH + 1] = {};
        const DWORD length = GetTempPathW(MAX_PATH, buffer);
        if (length == 0 || length > MAX_PATH) throw std::runtime_error("无法读取系统临时目录");
        tempRoot_ = std::filesystem::path(buffer);
        baseline_ = Scan();
        stopping_.store(false);
        worker_ = std::thread([this]() { Run(); });
        std::unique_lock<std::mutex> lock(readyMutex_);
        readyCondition_.wait(lock, [this]() { return ready_; });
    }

    void Stop() {
        if (!worker_.joinable()) return;
        stopping_.store(true);
        worker_.join();
        CaptureNew(Scan());
    }

    bool HasArtifacts() const {
        std::lock_guard<std::mutex> lock(foundMutex_);
        return !found_.empty();
    }

    std::string DescribeUtf8() const {
        std::lock_guard<std::mutex> lock(foundMutex_);
        std::ostringstream output;
        bool first = true;
        for (const auto& path : found_) {
            if (!first) output << "; ";
            first = false;
            output << path.u8string();
        }
        return output.str();
    }

private:
    using PathSet = std::set<std::filesystem::path>;

    bool HasDvsPrefix(const std::filesystem::path& path) const {
        const std::wstring name = Lower(path.filename().wstring());
        return name.rfind(L"dlcvdvs_", 0) == 0;
    }

    bool IsArchiveFile(const std::filesystem::path& path) const {
        return archiveFileNames_.find(Lower(path.filename().wstring())) != archiveFileNames_.end();
    }

    PathSet Scan() const {
        PathSet paths;
        std::error_code error;
        std::filesystem::directory_iterator iterator(
            tempRoot_, std::filesystem::directory_options::skip_permission_denied, error);
        const std::filesystem::directory_iterator end;
        for (; !error && iterator != end; iterator.increment(error)) {
            const auto path = iterator->path();
            std::error_code typeError;
            if (iterator->is_directory(typeError) && HasDvsPrefix(path)) {
                paths.insert(path);
                std::filesystem::recursive_directory_iterator child(
                    path, std::filesystem::directory_options::skip_permission_denied, typeError);
                const std::filesystem::recursive_directory_iterator childEnd;
                for (; !typeError && child != childEnd; child.increment(typeError)) {
                    if (child->is_regular_file(typeError)) paths.insert(child->path());
                }
            } else if (iterator->is_regular_file(typeError) && IsArchiveFile(path)) {
                paths.insert(path);
            }
        }
        return paths;
    }

    void CaptureNew(const PathSet& current) {
        std::lock_guard<std::mutex> lock(foundMutex_);
        for (const auto& path : current) {
            if (baseline_.find(path) == baseline_.end()) found_.insert(path);
        }
    }

    void Run() {
        {
            std::lock_guard<std::mutex> lock(readyMutex_);
            ready_ = true;
        }
        readyCondition_.notify_all();
        while (!stopping_.load()) {
            CaptureNew(Scan());
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        CaptureNew(Scan());
    }

    std::unordered_set<std::wstring> archiveFileNames_;
    std::filesystem::path tempRoot_;
    PathSet baseline_;
    mutable std::mutex foundMutex_;
    PathSet found_;
    std::atomic<bool> stopping_{false};
    std::thread worker_;
    std::mutex readyMutex_;
    std::condition_variable readyCondition_;
    bool ready_ = false;
};

inline bool HasExplicitDvspUnsupportedMessage(const std::string& message) {
    std::string lower = message;
    std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    const bool mentionsDvsp = lower.find("dvsp") != std::string::npos;
    const bool unsupported = lower.find("unsupported") != std::string::npos
        || lower.find("not support") != std::string::npos
        || message.find("不支持") != std::string::npos;
    return mentionsDvsp && unsupported;
}

}  // namespace dvs_test
