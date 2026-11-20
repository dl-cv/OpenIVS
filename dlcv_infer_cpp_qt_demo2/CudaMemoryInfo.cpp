#include "CudaMemoryInfo.h"

#include <windows.h>
#include <pdh.h>
#include <pdhmsg.h>

#include <cmath>
#include <cwchar>
#include <string>
#include <vector>

#pragma comment(lib, "pdh.lib")

namespace {

constexpr double kBytesPerMegabyte = 1024.0 * 1024.0;

using NvmlInitFn = int (*)();
using NvmlShutdownFn = int (*)();
using NvmlDeviceGetHandleByIndexFn = int (*)(unsigned int, void**);
using NvmlDeviceGetMemoryInfoFn = int (*)(void*, void*);

struct NvmlMemory {
    unsigned long long total;
    unsigned long long free;
    unsigned long long used;
};

// WDDM 下 NVML 的进程显存字段不可用，用性能计数器读取本进程在显卡上的独占显存。
bool tryGetProcessUsedBytes(unsigned long long& usedBytes) {
    usedBytes = 0;
    PDH_HQUERY query = nullptr;
    if (PdhOpenQueryW(nullptr, 0, &query) != ERROR_SUCCESS) {
        return false;
    }

    const std::wstring pattern = L"\\GPU Process Memory(pid_"
        + std::to_wstring(GetCurrentProcessId()) + L"_*)\\Dedicated Usage";

    DWORD pathListLength = 0;
    PDH_STATUS status = PdhExpandWildCardPathW(nullptr, pattern.c_str(), nullptr, &pathListLength, 0);
    if (status == ERROR_SUCCESS) {
        // 当前进程没有显卡显存计数器实例，本程序占用为零。
        PdhCloseQuery(query);
        return true;
    }
    if (status != PDH_MORE_DATA || pathListLength == 0) {
        PdhCloseQuery(query);
        return false;
    }

    // pathListLength 是字符数，结尾另留两个空字符用于结束标志。
    std::vector<wchar_t> pathList(static_cast<size_t>(pathListLength) + 2, L'\0');
    status = PdhExpandWildCardPathW(nullptr, pattern.c_str(), pathList.data(), &pathListLength, 0);
    if (status != ERROR_SUCCESS) {
        PdhCloseQuery(query);
        return false;
    }

    std::vector<PDH_HCOUNTER> counters;
    for (const wchar_t* path = pathList.data(); *path != L'\0'; path += std::wcslen(path) + 1) {
        PDH_HCOUNTER counter = nullptr;
        if (PdhAddEnglishCounterW(query, path, 0, &counter) == ERROR_SUCCESS) {
            counters.push_back(counter);
        }
    }
    if (counters.empty()) {
        PdhCloseQuery(query);
        return true;
    }

    bool succeeded = false;
    if (PdhCollectQueryData(query) == ERROR_SUCCESS) {
        for (PDH_HCOUNTER counter : counters) {
            PDH_FMT_COUNTERVALUE value = {};
            if (PdhGetFormattedCounterValue(counter, PDH_FMT_LARGE, nullptr, &value) == ERROR_SUCCESS
                && value.CStatus == PDH_CSTATUS_VALID_DATA
                && value.largeValue > 0) {
                usedBytes += static_cast<unsigned long long>(value.largeValue);
                succeeded = true;
            }
        }
    }

    PdhCloseQuery(query);
    return succeeded;
}

bool tryGetDeviceTotalBytes(int deviceIndex, unsigned long long& totalBytes) {
    totalBytes = 0;
    HMODULE module = LoadLibraryW(L"nvml.dll");
    if (module == nullptr) {
        return false;
    }

    const auto init = reinterpret_cast<NvmlInitFn>(GetProcAddress(module, "nvmlInit_v2"));
    const auto shutdown = reinterpret_cast<NvmlShutdownFn>(GetProcAddress(module, "nvmlShutdown"));
    const auto getHandle = reinterpret_cast<NvmlDeviceGetHandleByIndexFn>(
        GetProcAddress(module, "nvmlDeviceGetHandleByIndex_v2"));
    const auto getMemory = reinterpret_cast<NvmlDeviceGetMemoryInfoFn>(
        GetProcAddress(module, "nvmlDeviceGetMemoryInfo"));

    bool succeeded = false;
    if (init != nullptr && shutdown != nullptr && getHandle != nullptr && getMemory != nullptr && init() == 0) {
        void* device = nullptr;
        if (getHandle(static_cast<unsigned int>(deviceIndex), &device) == 0 && device != nullptr) {
            NvmlMemory memory = {};
            if (getMemory(device, &memory) == 0 && memory.total > 0) {
                totalBytes = memory.total;
                succeeded = true;
            }
        }
        shutdown();
    }

    FreeLibrary(module);
    return succeeded;
}

}  // namespace

namespace cuda_memory {

QString formatSize(unsigned long long bytes) {
    const double megabytes = std::round(static_cast<double>(bytes) / kBytesPerMegabyte * 100.0) / 100.0;
    if (megabytes < 1024.0) {
        return QString::number(megabytes, 'f', 2) + QStringLiteral(" MB");
    }
    return QString::number(megabytes / 1024.0, 'f', 2) + QStringLiteral(" GB");
}

QString describe(int deviceIndex) {
    unsigned long long usedBytes = 0;
    if (!tryGetProcessUsedBytes(usedBytes)) {
        return QStringLiteral("CUDA 显存: 不可用");
    }

    unsigned long long totalBytes = 0;
    if (tryGetDeviceTotalBytes(deviceIndex, totalBytes)) {
        return QStringLiteral("CUDA 显存: 本程序 %1 / 共 %2")
            .arg(formatSize(usedBytes), formatSize(totalBytes));
    }
    return QStringLiteral("CUDA 显存: 本程序 %1").arg(formatSize(usedBytes));
}

}  // namespace cuda_memory
