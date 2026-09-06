#define DLCV_NATIVE_C_API_SKIP_INFER_EXPORT
#define dlcv_infer dlcv_infer_json_impl
#include "dlcv_infer_c_api.h"
#undef dlcv_infer
#undef DLCV_NATIVE_C_API_SKIP_INFER_EXPORT
#include "dlcv_infer.h"
#include "flow/modules/ModelModules.h"

#if defined(_WIN32)
#pragma comment(linker, "/export:dlcv_infer=dlcv_infer_json_impl")
#endif

#include <opencv2/core.hpp>

#include <windows.h>

#include <cctype>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <iomanip>
#include <limits>
#include <memory>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

struct CApiModelEntry {
    std::shared_ptr<dlcv_infer::Model> model;
    dlcv_infer::DllLoader* loader = nullptr;
    int modelIndex = -1;
    bool serializeInfer = false;
    bool nativeOwner = false;
    std::mutex modelMutex;
    std::mutex inferMutex;

    bool HasSharedIndexFunctions() const {
        return loader != nullptr && loader->GetIndexTypeFunc() != nullptr &&
            loader->GetModelInfoByIndexFunc() != nullptr &&
            loader->GetRegisterFlowFunc() != nullptr && loader->GetFlowInfoFunc() != nullptr &&
            loader->GetFreeFlowFunc() != nullptr && loader->GetBindIndexFunc() != nullptr &&
            loader->GetUnbindIndexFunc() != nullptr && loader->GetFreeStringFunc() != nullptr;
    }

    std::shared_ptr<dlcv_infer::Model> GetModel() {
        std::lock_guard<std::mutex> lock(modelMutex);
        if (!model) {
            if (loader == nullptr) throw std::runtime_error("模型缺少所属 DLL");
            model = std::make_shared<dlcv_infer::Model>();
            model->SetPreferredDllLoader(loader);
            model->modelIndex = modelIndex;
            model->OwnModelIndex = false;
        }
        // 保留实例及所属 DLL，绑定或恢复失败后仍使用同一模块重试。
        (void)model->GetModelInfo();
        return model;
    }
};

static std::unordered_map<int, std::shared_ptr<CApiModelEntry>> g_models;
static std::mutex g_modelsMutex;
enum class NativeJsonReleaseKind {
    DeleteArray,
    ModelResult,
    Result
};

struct NativeJsonAllocation {
    std::vector<unsigned char*> maskBuffers;
    dlcv_infer::DllLoader* loader = nullptr;
    NativeJsonReleaseKind releaseKind = NativeJsonReleaseKind::DeleteArray;

    NativeJsonAllocation() = default;
    explicit NativeJsonAllocation(std::vector<unsigned char*> buffers)
        : maskBuffers(std::move(buffers)) {}
    NativeJsonAllocation(
        dlcv_infer::DllLoader* sourceLoader,
        NativeJsonReleaseKind sourceReleaseKind)
        : loader(sourceLoader), releaseKind(sourceReleaseKind) {}
    NativeJsonAllocation(const NativeJsonAllocation&) = delete;
    NativeJsonAllocation& operator=(const NativeJsonAllocation&) = delete;
    NativeJsonAllocation(NativeJsonAllocation&&) noexcept = default;
    NativeJsonAllocation& operator=(NativeJsonAllocation&&) noexcept = default;
    ~NativeJsonAllocation() {
        for (unsigned char* buffer : maskBuffers) delete[] buffer;
    }

    void KeepMaskBuffersAllocated() noexcept {
        maskBuffers.clear();
    }
};
static std::unordered_map<const char*, NativeJsonAllocation> g_nativeJsonAllocations;
static std::mutex g_nativeJsonAllocationsMutex;
static thread_local std::string g_lastError;

static void RecordNativeApiFailure(const char* apiName, const char* detail) noexcept;

static std::wstring GetDlcvCapiDebugLogPath() {
    wchar_t tempDirectory[MAX_PATH + 1] = {0};
    const DWORD length = GetTempPathW(MAX_PATH + 1, tempDirectory);
    if (length == 0 || length > MAX_PATH) return {};
    return std::wstring(tempDirectory, length) + L"dlcvInfer_c_api_debug.log";
}


static int CheckedCCount(size_t count) {
    if (count > static_cast<size_t>(std::numeric_limits<int>::max())) {
        throw std::length_error("result count exceeds C API range");
    }
    return static_cast<int>(count);
}

static void ReleaseCResultMemory(DlcvCResult* result) noexcept {
    if (result == nullptr) return;
    delete[] result->message;
    result->message = nullptr;
    if (result->sample_results != nullptr) {
        if (result->n > 0) {
            for (int i = 0; i < result->n; ++i) {
                DlcvCSampleResult& sample = result->sample_results[i];
                if (sample.results != nullptr) {
                    if (sample.n > 0) {
                        for (int j = 0; j < sample.n; ++j) {
                            DlcvCObjectResult& object = sample.results[j];
                            delete[] object.category_name;
                            object.category_name = nullptr;
                            if (object.mask.mask_ptr != 0) {
                                delete[] reinterpret_cast<unsigned char*>(
                                    static_cast<uintptr_t>(object.mask.mask_ptr));
                                object.mask.mask_ptr = 0;
                            }
                            object.mask.height = 0;
                            object.mask.width = 0;
                        }
                    }
                    delete[] sample.results;
                    sample.results = nullptr;
                }
                sample.n = 0;
            }
        }
        delete[] result->sample_results;
        result->sample_results = nullptr;
    }
    result->n = 0;
}

static void SetCResultError(DlcvCResult& result, const char* message) noexcept {
    ReleaseCResultMemory(&result);
    result.code = -1;
    try {
        if (message == nullptr) message = "";
        const size_t size = std::strlen(message) + 1;
        result.message = new char[size];
        std::memcpy(result.message, message, size);
    } catch (...) {
        result.message = nullptr;
    }
}

static const char* ValidateCImage(const DlcvCImage& image) noexcept {
    if (image.data_ptr == 0) return "invalid image data";
    if (image.height <= 0 || image.width <= 0) return "invalid image size";
    if (image.channel <= 0 || image.channel > CV_CN_MAX) return "invalid image type";

    const size_t width = static_cast<size_t>(image.width);
    const size_t height = static_cast<size_t>(image.height);
    const size_t channel = static_cast<size_t>(image.channel);
    if (width > std::numeric_limits<size_t>::max() / height) return "invalid image size";
    const size_t pixels = width * height;
    if (pixels > std::numeric_limits<size_t>::max() / channel) return "invalid image size";
    return nullptr;
}

static std::vector<cv::Mat> BuildCImageList(const DlcvCImageList* imageList) {
    if (imageList == nullptr || imageList->n <= 0 || imageList->images == nullptr) {
        throw std::invalid_argument("invalid image list");
    }

    std::vector<cv::Mat> images;
    images.reserve(static_cast<size_t>(imageList->n));
    for (int i = 0; i < imageList->n; ++i) {
        const DlcvCImage& image = imageList->images[i];
        const char* validationError = ValidateCImage(image);
        if (validationError != nullptr) throw std::invalid_argument(validationError);
        try {
            images.emplace_back(
                image.height,
                image.width,
                CV_MAKETYPE(CV_8U, image.channel),
                reinterpret_cast<void*>(static_cast<uintptr_t>(image.data_ptr)));
        } catch (const cv::Exception&) {
            throw std::invalid_argument("invalid image size");
        }
    }
    return images;
}

static void SetLastErrorMessage(const std::string& message) {
    g_lastError = message;
}

static void ClearLastErrorMessage() {
    g_lastError.clear();
}

static bool IsFlowModelPath(const std::string& modelPath) {
    const auto endsWithIgnoreCase = [&modelPath](const char* suffix) {
        const size_t suffixLength = std::strlen(suffix);
        if (modelPath.size() < suffixLength) return false;
        const size_t offset = modelPath.size() - suffixLength;
        for (size_t i = 0; i < suffixLength; ++i) {
            const unsigned char left = static_cast<unsigned char>(modelPath[offset + i]);
            const unsigned char right = static_cast<unsigned char>(suffix[i]);
            if (std::tolower(left) != std::tolower(right)) return false;
        }
        return true;
    };
    return endsWithIgnoreCase(".dvst") || endsWithIgnoreCase(".dvso") || endsWithIgnoreCase(".dvsp");
}

static const char* AllocateNativeJsonResult(
    const dlcv_infer::json& value,
    std::vector<unsigned char*> maskBuffers = {}) {
    NativeJsonAllocation allocation(std::move(maskBuffers));
    const std::string serialized = value.dump();
    char* result = new char[serialized.size() + 1];
    std::memcpy(result, serialized.c_str(), serialized.size() + 1);

    try {
        std::lock_guard<std::mutex> lock(g_nativeJsonAllocationsMutex);
        const auto inserted = g_nativeJsonAllocations.emplace(result, std::move(allocation));
        if (!inserted.second) throw std::runtime_error("原生 JSON 返回指针重复");
    } catch (...) {
        delete[] result;
        throw;
    }
    return result;
}

static void TrackNativeJsonResult(
    const char* result,
    dlcv_infer::DllLoader& loader,
    NativeJsonReleaseKind releaseKind) {
    if (result == nullptr) return;
    NativeJsonAllocation allocation(&loader, releaseKind);
    try {
        std::lock_guard<std::mutex> lock(g_nativeJsonAllocationsMutex);
        const auto inserted = g_nativeJsonAllocations.emplace(result, std::move(allocation));
        if (!inserted.second) throw std::runtime_error("原生 JSON 返回指针重复");
    } catch (...) {
        if (releaseKind == NativeJsonReleaseKind::ModelResult) {
            loader.GetFreeModelResultFunc()(result);
        } else {
            loader.GetFreeResultFunc()(result);
        }
        throw;
    }
}

static bool ReleaseNativeJsonResult(const char* result, bool releaseMaskBuffers) noexcept {
    if (result == nullptr) return true;

    NativeJsonAllocation allocation;
    {
        std::lock_guard<std::mutex> lock(g_nativeJsonAllocationsMutex);
        const auto it = g_nativeJsonAllocations.find(result);
        if (it == g_nativeJsonAllocations.end()) return false;
        allocation = std::move(it->second);
        g_nativeJsonAllocations.erase(it);
    }

    if (!releaseMaskBuffers) allocation.KeepMaskBuffersAllocated();
    try {
        if (allocation.releaseKind == NativeJsonReleaseKind::DeleteArray) {
            delete[] result;
        } else if (allocation.releaseKind == NativeJsonReleaseKind::ModelResult) {
            allocation.loader->GetFreeModelResultFunc()(result);
        } else {
            allocation.loader->GetFreeResultFunc()(result);
        }
    } catch (const std::exception& ex) {
        RecordNativeApiFailure("release_native_json_result", ex.what());
    } catch (...) {
        RecordNativeApiFailure("release_native_json_result", "unknown exception");
    }
    return true;
}

static dlcv_infer::json MakeNativeStatus(int code, const std::string& message) {
    return dlcv_infer::json{
        { "code", code },
        { "message", message }
    };
}

static bool TryParseNativeConfig(const char* configStr, dlcv_infer::json& config) noexcept {
    if (configStr == nullptr) return false;
    try {
        config = dlcv_infer::json::parse(configStr);
        return config.is_object();
    } catch (...) {
        return false;
    }
}

static bool TryReadModelIndex(const dlcv_infer::json& config, int& modelIndex) noexcept {
    try {
        if (!config.is_object() || !config.contains("model_index")) return false;
        const auto& value = config.at("model_index");
        if (!value.is_number_integer()) return false;
        modelIndex = value.get<int>();
        return true;
    } catch (...) {
        return false;
    }
}

static bool TryReadFlowModelPath(
    const dlcv_infer::json& config,
    std::string& modelPath) noexcept {
    try {
        if (!config.is_object() || !config.contains("model_path") ||
            !config.at("model_path").is_string()) {
            return false;
        }
        modelPath = config.at("model_path").get<std::string>();
        return IsFlowModelPath(modelPath);
    } catch (...) {
        return false;
    }
}

static dlcv_infer::json AddNativeModelInfoStatus(dlcv_infer::json modelInfo) {
    if (!modelInfo.is_object()) {
        modelInfo = dlcv_infer::json{ { "model_info", std::move(modelInfo) } };
    }
    modelInfo["code"] = 0;
    modelInfo["message"] = "Successfully got model info.";
    return modelInfo;
}

static std::shared_ptr<CApiModelEntry> FindModelEntry(int modelIndex) {
    std::lock_guard<std::mutex> lock(g_modelsMutex);
    const auto it = g_models.find(modelIndex);
    return it == g_models.end() ? nullptr : it->second;
}

static void StoreModelEntry(int modelIndex, const std::shared_ptr<CApiModelEntry>& entry) {
    std::lock_guard<std::mutex> lock(g_modelsMutex);
    const auto inserted = g_models.emplace(modelIndex, entry);
    if (!inserted.second) {
        throw std::runtime_error("model_index 已由本 C API 使用");
    }
}

static void EraseModelEntry(int modelIndex, const std::shared_ptr<CApiModelEntry>& expected) {
    std::lock_guard<std::mutex> lock(g_modelsMutex);
    const auto it = g_models.find(modelIndex);
    if (it != g_models.end() && it->second == expected) {
        g_models.erase(it);
    }
}

template <typename Func>
static const char* InvokeTrackedNativeJson(
    dlcv_infer::DllLoader& loader,
    Func func,
    const char* configStr,
    NativeJsonReleaseKind releaseKind) {
    if (func == nullptr) {
        throw std::domain_error("dlcv_infer 缺少原生 JSON 接口");
    }
    const char* result = func(configStr);
    TrackNativeJsonResult(result, loader, releaseKind);
    return result;
}

static bool IsNativeSuccessResult(const char* result) {
    if (result == nullptr) return false;
    try {
        const dlcv_infer::json response = dlcv_infer::json::parse(result);
        return response.is_object() && response.value("code", 1) == 0;
    } catch (...) {
        return false;
    }
}

static std::shared_ptr<CApiModelEntry> CreateSharedModelEntry(
    int modelIndex,
    dlcv_infer::DllLoader& loader,
    int indexType) {
    if (indexType != 1 && indexType != 2) {
        throw std::runtime_error("共享 index 类型查询返回未知值");
    }
    auto entry = std::make_shared<CApiModelEntry>();
    entry->loader = &loader;
    entry->modelIndex = modelIndex;
    entry->serializeInfer = indexType == 2;
    return entry;
}

static std::shared_ptr<CApiModelEntry> FindOrRestoreSharedModelEntry(int modelIndex) {
    std::shared_ptr<CApiModelEntry> entry;
    {
        std::lock_guard<std::mutex> lock(g_modelsMutex);
        const auto it = g_models.find(modelIndex);
        if (it != g_models.end()) {
            entry = it->second;
        } else {
            int indexType = 0;
            auto* loader = &dlcv_infer::DllLoader::ResolveForIndex(modelIndex, indexType);
            entry = CreateSharedModelEntry(modelIndex, *loader, indexType);
            // 先保存唯一选定的 DLL，再建立共享持有；失败时保留记录。
            g_models.emplace(modelIndex, entry);
        }
    }
    // 本地原生加载已知所属 DLL；缺少共享接口的旧 SDK 保留原 JSON 调用。
    if (!entry->nativeOwner || entry->HasSharedIndexFunctions()) {
        (void)entry->GetModel();
    }
    return entry;
}

static std::shared_ptr<CApiModelEntry> GetStructuredModelEntry(int modelIndex) {
    if (modelIndex < 0) throw std::runtime_error("model not found");
    auto entry = FindOrRestoreSharedModelEntry(modelIndex);
    (void)entry->GetModel();
    return entry;
}

static int ReadNativeImageDepth(const dlcv_infer::json& imageInfo) {
    if (!imageInfo.contains("dtype")) return CV_8U;
    const auto& dtype = imageInfo.at("dtype");
    if (!dtype.is_string()) throw std::invalid_argument("dtype 必须是字符串");
    const std::string value = dtype.get<std::string>();
    if (value == "uint8") return CV_8U;
    if (value == "uint16") return CV_16U;
    if (value == "float32") return CV_32F;
    throw std::invalid_argument("Unsupported dtype.");
}

static std::vector<cv::Mat> ParseNativeImageList(const dlcv_infer::json& config) {
    if (!config.contains("image_list") || !config.at("image_list").is_array()) {
        throw std::invalid_argument("image_list 必须是数组");
    }

    const auto& imageList = config.at("image_list");
    if (imageList.empty()) throw std::invalid_argument("image_list 不能为空");

    std::vector<cv::Mat> images;
    images.reserve(imageList.size());
    for (const auto& imageInfo : imageList) {
        if (!imageInfo.is_object()) throw std::invalid_argument("image_list 元素必须是对象");
        const int width = imageInfo.at("width").get<int>();
        const int height = imageInfo.at("height").get<int>();
        const int channels = imageInfo.at("channels").get<int>();
        const uint64_t imagePtr = imageInfo.at("image_ptr").get<uint64_t>();
        if (width <= 0 || height <= 0 || channels <= 0 || channels > CV_CN_MAX || imagePtr == 0) {
            throw std::invalid_argument("image_list 包含无效图像");
        }
        const int type = CV_MAKETYPE(ReadNativeImageDepth(imageInfo), channels);
        images.emplace_back(
            height,
            width,
            type,
            reinterpret_cast<void*>(static_cast<uintptr_t>(imagePtr)));
    }
    return images;
}

static dlcv_infer::json BuildNativeInferParams(const dlcv_infer::json& config) {
    dlcv_infer::json params = config;
    params.erase("model_index");
    params.erase("image_list");
    return params;
}

static dlcv_infer::json BuildNativeObjectResult(
    const dlcv_infer::ObjectResult& object,
    std::vector<unsigned char*>& maskBuffers) {
    dlcv_infer::json bbox = dlcv_infer::json::array();
    for (double value : object.bbox) bbox.push_back(value);

    dlcv_infer::json mask = {
        { "mask_ptr", 0 },
        { "height", -1 },
        { "width", -1 }
    };
    if (object.withMask && !object.mask.empty()) {
        cv::Mat continuousMask = object.mask.isContinuous() ? object.mask : object.mask.clone();
        const size_t byteCount = continuousMask.total() * continuousMask.elemSize();
        std::unique_ptr<unsigned char[]> maskBuffer(new unsigned char[byteCount]);
        std::memcpy(maskBuffer.get(), continuousMask.data, byteCount);
        maskBuffers.push_back(maskBuffer.get());
        const uint64_t maskPointer = static_cast<uint64_t>(
            reinterpret_cast<uintptr_t>(maskBuffer.release()));
        mask["mask_ptr"] = maskPointer;
        mask["height"] = continuousMask.rows;
        mask["width"] = continuousMask.cols;
    }

    dlcv_infer::json result = {
        { "category_id", object.categoryId },
        { "category_name", dlcv_infer::convertGbkToUtf8(object.categoryName) },
        { "score", object.score },
        { "area", object.area },
        { "bbox", std::move(bbox) },
        { "with_mask", object.withMask && !object.mask.empty() },
        { "mask", std::move(mask) },
        { "with_bbox", object.withBbox },
        { "with_angle", object.withAngle },
        { "angle", object.withAngle ? object.angle : -100.0f },
        { "with_mean", object.withMean },
        { "foreground_mean", object.foregroundMean },
        { "background_mean", object.backgroundMean }
    };
    return result;
}

static const char* InferFlowModelWithNativeJson(
    const std::shared_ptr<CApiModelEntry>& entry,
    const dlcv_infer::json& config) {
    std::unique_lock<std::mutex> inferLock(entry->inferMutex);
    const std::vector<cv::Mat> images = ParseNativeImageList(config);
    const dlcv_infer::Result inferResult = entry->model->InferBatch(images, BuildNativeInferParams(config));

    std::vector<unsigned char*> maskBuffers;
    try {
        dlcv_infer::json sampleResults = dlcv_infer::json::array();
        for (const auto& sample : inferResult.sampleResults) {
            dlcv_infer::json results = dlcv_infer::json::array();
            for (const auto& object : sample.results) {
                results.push_back(BuildNativeObjectResult(object, maskBuffers));
            }
            sampleResults.push_back(dlcv_infer::json{ { "results", std::move(results) } });
        }

        dlcv_infer::json response = {
            { "code", 0 },
            { "message", "Success" },
            { "sample_results", std::move(sampleResults) }
        };
        return AllocateNativeJsonResult(response, std::move(maskBuffers));
    } catch (...) {
        for (unsigned char* buffer : maskBuffers) delete[] buffer;
        throw;
    }
}

static void AppendCapiDebugLog(const char* format, ...) {
    std::time_t now = std::time(nullptr);
    std::tm localTime{};
#if defined(_WIN32)
    localtime_s(&localTime, &now);
#else
    localtime_r(&now, &localTime);
#endif

    char message[4096] = {0};
    va_list args;
    va_start(args, format);
    vsnprintf(message, sizeof(message), format, args);
    va_end(args);

    const std::wstring logPath = GetDlcvCapiDebugLogPath();
    if (logPath.empty()) return;

    FILE* fp = nullptr;
    if (_wfopen_s(&fp, logPath.c_str(), L"a") == 0 && fp != nullptr) {
        fprintf(fp, "%04d-%02d-%02d %02d:%02d:%02d [dlcvInferCAPI] %s\n",
            localTime.tm_year + 1900,
            localTime.tm_mon + 1,
            localTime.tm_mday,
            localTime.tm_hour,
            localTime.tm_min,
            localTime.tm_sec,
            message);
        fclose(fp);
    }
}

static std::string BytesToHex(const std::string& value) {
    std::ostringstream oss;
    oss << std::uppercase << std::hex << std::setfill('0');
    for (size_t i = 0; i < value.size(); ++i) {
        if (i > 0) {
            oss << ' ';
        }
        oss << std::setw(2) << static_cast<unsigned int>(static_cast<unsigned char>(value[i]));
    }
    return oss.str();
}

static std::string DescribeModelPathBytes(const std::string& modelPath) {
    std::ostringstream oss;
    oss << "pathBytesHex=[" << BytesToHex(modelPath) << "]";
    try {
        const std::wstring utf8W = dlcv_infer::convertUtf8ToWstring(modelPath);
        const std::string utf8RoundTrip = dlcv_infer::convertWstringToUtf8(utf8W);
        oss << ", utf8Valid=" << (utf8RoundTrip == modelPath ? "true" : "false")
            << ", utf8Decoded=\"" << utf8RoundTrip << "\"";
    } catch (...) {
        oss << ", utf8Decoded=<exception>";
    }
    try {
        oss << ", gbkDecodedUtf8=\"" << dlcv_infer::convertGbkToUtf8(modelPath) << "\"";
    } catch (...) {
        oss << ", gbkDecodedUtf8=<exception>";
    }
    return oss.str();
}

static void ReplaceResultMessage(DlcvCResult& result, const char* message) {
    if (result.message != nullptr) {
        delete[] result.message;
        result.message = nullptr;
    }
    try {
        if (message == nullptr) message = "";
        const size_t size = std::strlen(message) + 1;
        result.message = new char[size];
        std::memcpy(result.message, message, size);
    } catch (...) {
        result.message = nullptr;
    }
}

static void NormalizeNativeCompatibleResult(DlcvCResult& result) {
    if (result.code == 0) {
        ReplaceResultMessage(result, "Success");
        if (result.sample_results == nullptr || result.n <= 0) return;
        for (int i = 0; i < result.n; ++i) {
            DlcvCSampleResult& sample = result.sample_results[i];
            if (sample.results == nullptr || sample.n <= 0) continue;
            for (int j = 0; j < sample.n; ++j) {
                DlcvCObjectResult& object = sample.results[j];
                if (!object.with_bbox) {
                    object.x = -1.0f;
                    object.y = -1.0f;
                    object.w = -1.0f;
                    object.h = -1.0f;
                }
                if (!object.with_mask) {
                    object.mask.mask_ptr = 0;
                    object.mask.height = -1;
                    object.mask.width = -1;
                }
                if (!object.with_angle) {
                    object.angle = -100.0f;
                }
            }
        }
        return;
    }

    if (result.message != nullptr && std::strcmp(result.message, "model not found") == 0) {
        result.code = 2;
        ReplaceResultMessage(result, "Model not found.");
    } else if (result.message != nullptr && std::strcmp(result.message, "invalid image list") == 0) {
        result.code = 1;
        ReplaceResultMessage(result, "Invalid image list.");
    } else if (result.message != nullptr && std::strcmp(result.message, "invalid image data") == 0) {
        result.code = 1;
        ReplaceResultMessage(result, "Invalid image data.");
    } else if (result.message != nullptr && std::strcmp(result.message, "invalid image size") == 0) {
        result.code = 1;
        ReplaceResultMessage(result, "Invalid image size.");
    } else if (result.message != nullptr && std::strcmp(result.message, "invalid image type") == 0) {
        result.code = 1;
        ReplaceResultMessage(result, "Invalid image type.");
    } else {
        result.code = 1;
    }
}

static void RecordNativeApiFailure(const char* apiName, const char* detail) noexcept {
    char message[4096] = {0};
#if defined(_WIN32)
    _snprintf_s(message, sizeof(message), _TRUNCATE, "%s failed: %s", apiName, detail);
#else
    snprintf(message, sizeof(message), "%s failed: %s", apiName, detail);
#endif
    try {
        SetLastErrorMessage(message);
        AppendCapiDebugLog("%s", message);
    } catch (...) {
        try {
            g_lastError = "native api failed";
        } catch (...) {
        }
    }
}

template <typename Invoke>
static const char* CallNativeString(const char* apiName, Invoke&& invoke) noexcept {
    try {
        return invoke();
    } catch (const std::exception& ex) {
        RecordNativeApiFailure(apiName, ex.what());
    } catch (...) {
        RecordNativeApiFailure(apiName, "unknown exception");
    }
    return nullptr;
}

template <typename Invoke>
static int CallNativeInt(const char* apiName, Invoke&& invoke) noexcept {
    try {
        return invoke();
    } catch (const std::exception& ex) {
        RecordNativeApiFailure(apiName, ex.what());
    } catch (...) {
        RecordNativeApiFailure(apiName, "unknown exception");
    }
    return -1;
}

template <typename Invoke>
static void CallNativeVoid(const char* apiName, Invoke&& invoke) noexcept {
    try {
        invoke();
    } catch (const std::exception& ex) {
        RecordNativeApiFailure(apiName, ex.what());
    } catch (...) {
        RecordNativeApiFailure(apiName, "unknown exception");
    }
}

extern "C" {

int dlcv_infer_cpp_load_model_c(const char* model_path, int device_id) {
    dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
    ClearLastErrorMessage();
    if (!model_path) {
        SetLastErrorMessage("model_path is null");
        AppendCapiDebugLog("load_model failed: %s", g_lastError.c_str());
        return -1;
    }

    const std::string modelPath(model_path);
    const std::string pathDiagnostics = DescribeModelPathBytes(modelPath);
    AppendCapiDebugLog("load_model begin: device_id=%d, path=%s, %s",
        device_id,
        modelPath.c_str(),
        pathDiagnostics.c_str());

    try {
        const bool isFlowModel = IsFlowModelPath(modelPath);
        auto model = std::make_shared<dlcv_infer::Model>(modelPath, device_id);
        int idx = model->modelIndex;
        if (idx < 0) {
            SetLastErrorMessage("load model returned negative modelIndex: " + std::to_string(idx) + "; " + pathDiagnostics);
            AppendCapiDebugLog("load_model failed: %s", g_lastError.c_str());
            return -1;
        }
        auto entry = std::make_shared<CApiModelEntry>();
        entry->model = std::move(model);
        entry->loader = entry->model->LoadedDllLoader();
        entry->modelIndex = idx;
        entry->serializeInfer = isFlowModel;
        std::lock_guard<std::mutex> lock(g_modelsMutex);
        g_models[idx] = std::move(entry);
        ClearLastErrorMessage();
        AppendCapiDebugLog("load_model success: modelIndex=%d", idx);
        return idx;
    } catch (const std::exception& ex) {
        SetLastErrorMessage(std::string("load model exception: ") + ex.what() + "; " + pathDiagnostics);
        AppendCapiDebugLog("load_model failed: %s", g_lastError.c_str());
        return -1;
    } catch (...) {
        SetLastErrorMessage("load model unknown exception; " + pathDiagnostics);
        AppendCapiDebugLog("load_model failed: %s", g_lastError.c_str());
        return -1;
    }
}

const char* dlcv_infer_cpp_get_last_error_c() {
    return g_lastError.c_str();
}

int dlcv_infer_cpp_free_model_c(int model_index) {
    dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
    const std::shared_ptr<CApiModelEntry> entry = FindModelEntry(model_index);
    if (!entry) return -1;

    try {
        if (entry->nativeOwner) {
            if (entry->loader == nullptr) {
                SetLastErrorMessage("本地模型缺少所属 DLL");
                return -1;
            }
            const std::string config = dlcv_infer::json{ { "model_index", model_index } }.dump();
            const char* result = entry->loader->GetFreeModelFunc()(config.c_str());
            const bool success = IsNativeSuccessResult(result);
            if (result != nullptr) entry->loader->GetFreeResultFunc()(result);
            if (!success) {
                SetLastErrorMessage("底层模型释放失败");
                return -1;
            }
        }
        EraseModelEntry(model_index, entry);
        return 0;
    } catch (const std::exception& ex) {
        SetLastErrorMessage(ex.what());
    } catch (...) {
        SetLastErrorMessage("unknown error");
    }
    return -1;
}

DlcvCResult dlcv_infer_cpp_infer_c(int model_index, const DlcvCImageList* image_list) {
    return dlcv_infer_cpp_infer_with_params_c(model_index, image_list, nullptr);
}

DlcvCResult dlcv_infer_cpp_infer_with_params_c(
    int model_index,
    const DlcvCImageList* image_list,
    const char* params_json) {
    dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
    DlcvCResult result{};
    result.code = -1;

    try {
        std::vector<cv::Mat> mats = BuildCImageList(image_list);

        const auto entry = GetStructuredModelEntry(model_index);

        std::unique_lock<std::mutex> inferLock(entry->inferMutex, std::defer_lock);
        if (entry->serializeInfer) {
            inferLock.lock();
        }

        dlcv_infer::json params = dlcv_infer::json::object();
        if (params_json != nullptr && params_json[0] != '\0') {
            params = dlcv_infer::json::parse(params_json);
            if (!params.is_object()) {
                throw std::invalid_argument("params_json 必须是 JSON 对象");
            }
        }

        dlcv_infer::Result cppResult =
            entry->model->InferBatchPreservingOriginalMask(mats, params);

        result.message = new char[sizeof("success")];
        std::memcpy(result.message, "success", sizeof("success"));
        const int sampleCount = CheckedCCount(cppResult.sampleResults.size());
        if (sampleCount > 0) {
            result.sample_results = new DlcvCSampleResult[sampleCount]{};
            result.n = sampleCount;
            for (int i = 0; i < sampleCount; ++i) {
                const auto& sample = cppResult.sampleResults[i];
                DlcvCSampleResult& sr = result.sample_results[i];
                const int objectCount = CheckedCCount(sample.results.size());
                if (objectCount > 0) {
                    sr.results = new DlcvCObjectResult[objectCount]{};
                    sr.n = objectCount;
                    for (int j = 0; j < objectCount; ++j) {
                        const auto& obj = sample.results[j];
                        DlcvCObjectResult& o = sr.results[j];
                        o.category_id = obj.categoryId;
                        o.category_name = new char[obj.categoryName.size() + 1];
                        std::memcpy(
                            o.category_name,
                            obj.categoryName.c_str(),
                            obj.categoryName.size() + 1);
                        o.score = obj.score;
                        o.with_bbox = obj.withBbox;
                        o.area = obj.area;
                        if (obj.bbox.size() >= 4) {
                            o.x = static_cast<float>(obj.bbox[0]);
                            o.y = static_cast<float>(obj.bbox[1]);
                            o.w = static_cast<float>(obj.bbox[2]);
                            o.h = static_cast<float>(obj.bbox[3]);
                        }
                        o.with_mask = obj.withMask;
                        if (obj.withMask && !obj.mask.empty()) {
                            cv::Mat maskClone = obj.mask.clone();
                            const size_t bytes = maskClone.total() * maskClone.elemSize();
                            unsigned char* maskData = new unsigned char[bytes];
                            std::memcpy(maskData, maskClone.data, bytes);
                            o.mask.mask_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(maskData));
                            o.mask.width = maskClone.cols;
                            o.mask.height = maskClone.rows;
                        }
                        o.with_angle = obj.withAngle;
                        o.angle = obj.angle;
                        o.with_mean = obj.withMean;
                        o.foreground_mean = obj.foregroundMean;
                        o.background_mean = obj.backgroundMean;
                    }
                }
            }
        }
        result.code = 0;
    } catch (const std::exception& ex) {
        SetCResultError(result, ex.what());
    } catch (...) {
        SetCResultError(result, "unknown error");
    }

    return result;
}

void dlcv_infer_cpp_free_model_result_c(DlcvCResult* result) {
    if (result == nullptr) return;
    ReleaseCResultMemory(result);
    result->code = 0;
}

const char* dlcv_infer_cpp_get_model_info_c(int model_index) {
    dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
    ClearLastErrorMessage();
    try {
        const auto entry = GetStructuredModelEntry(model_index);
        const std::string value = entry->model->GetModelInfo().dump();
        char* result = new char[value.size() + 1];
        std::memcpy(result, value.c_str(), value.size() + 1);
        return result;
    } catch (const std::exception& ex) {
        SetLastErrorMessage(ex.what());
    } catch (...) {
        SetLastErrorMessage("unknown error");
    }
    return nullptr;
}

const char* dlcv_infer_cpp_infer_json_c(
    int model_index,
    const DlcvCImage* image,
    const char* params_json) {
    dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
    ClearLastErrorMessage();
    if (image == nullptr) {
        SetLastErrorMessage("invalid image data");
        return nullptr;
    }
    const char* validationError = ValidateCImage(*image);
    if (validationError != nullptr) {
        SetLastErrorMessage(validationError);
        return nullptr;
    }

    cv::Mat mat;
    try {
        mat = cv::Mat(
            image->height,
            image->width,
            CV_MAKETYPE(CV_8U, image->channel),
            reinterpret_cast<void*>(static_cast<uintptr_t>(image->data_ptr)));
    } catch (const cv::Exception&) {
        SetLastErrorMessage("invalid image size");
        return nullptr;
    }

    try {
        const auto entry = GetStructuredModelEntry(model_index);
        std::unique_lock<std::mutex> inferLock(entry->inferMutex, std::defer_lock);
        if (entry->serializeInfer) inferLock.lock();

        dlcv_infer::json params = dlcv_infer::json::object();
        if (params_json != nullptr && params_json[0] != '\0') {
            params = dlcv_infer::json::parse(params_json);
            if (!params.is_object()) {
                throw std::invalid_argument("params_json 必须是 JSON 对象");
            }
        }
        const std::string value = entry->model->InferOneOutJson(mat, params).dump();
        char* result = new char[value.size() + 1];
        std::memcpy(result, value.c_str(), value.size() + 1);
        return result;
    } catch (const std::exception& ex) {
        SetLastErrorMessage(ex.what());
    } catch (...) {
        SetLastErrorMessage("unknown error");
    }
    return nullptr;
}

const char* dlcv_infer_cpp_get_all_dog_info_c() {
    ClearLastErrorMessage();
    try {
        const std::string value = dlcv_infer::GetAllDogInfo().dump();
        char* result = new char[value.size() + 1];
        std::memcpy(result, value.c_str(), value.size() + 1);
        return result;
    } catch (const std::exception& ex) {
        SetLastErrorMessage(ex.what());
    } catch (...) {
        SetLastErrorMessage("unknown error");
    }
    return nullptr;
}

void dlcv_infer_cpp_free_string_c(const char* value) {
    delete[] value;
}

void dlcv_infer_cpp_free_all_models_c() {
    CallNativeVoid("dlcv_free_all_models", []() {
        dlcv_infer::flow::ModelLifecycleWriteGuard lifecycleGuard;
        std::unordered_map<int, std::shared_ptr<CApiModelEntry>> models;
        {
            std::lock_guard<std::mutex> lock(g_modelsMutex);
            models.swap(g_models);
        }
        models.clear();
        dlcv_infer::Utils::FreeAllModels();
    });
}

int DLCV_C_NATIVE_CALL dlcv_load_model_c(const char* model_path, int device_id) {
    return dlcv_infer_cpp_load_model_c(model_path, device_id);
}

int DLCV_C_NATIVE_CALL dlcv_free_model_c(int model_index) {
    return dlcv_infer_cpp_free_model_c(model_index);
}

DlcvCResult DLCV_C_NATIVE_CALL dlcv_infer_c(
    int model_index,
    const DlcvCImageList* image_list) {
    DlcvCResult result = dlcv_infer_cpp_infer_c(model_index, image_list);
    NormalizeNativeCompatibleResult(result);
    return result;
}

void DLCV_C_NATIVE_CALL dlcv_free_model_result_c(DlcvCResult* result) {
    if (result == nullptr) return;
    const int originalCode = result->code;
    dlcv_infer_cpp_free_model_result_c(result);
    result->code = originalCode;
}

const char* DLCV_NATIVE_C_CALL dlcv_load_model(const char* config_str) {
    dlcv_infer::json config;
    std::string modelPath;
    if (!TryParseNativeConfig(config_str, config) || !TryReadFlowModelPath(config, modelPath)) {
        return CallNativeString("dlcv_load_model", [config_str]() {
            try {
                dlcv_infer::DllLoader& nativeLoader = dlcv_infer::DllLoader::Instance();
                const char* resultPtr = InvokeTrackedNativeJson(
                    nativeLoader,
                    nativeLoader.GetLoadModelFunc(),
                    config_str,
                    NativeJsonReleaseKind::Result);
                if (resultPtr == nullptr) return resultPtr;

                int modelIndex = -1;
                try {
                    const dlcv_infer::json response = dlcv_infer::json::parse(resultPtr);
                    if (!response.is_object() || !response.contains("model_index") ||
                        !response.at("model_index").is_number_integer()) {
                        return resultPtr;
                    }
                    if (response.value("code", 0) != 0) return resultPtr;
                    modelIndex = response.at("model_index").get<int>();
                    if (modelIndex < 0) return resultPtr;
                } catch (...) {
                    return resultPtr;
                }

                const auto getIndexType = nativeLoader.GetIndexTypeFunc();
                if (getIndexType != nullptr) {
                    const int indexType = getIndexType(modelIndex);
                    if (indexType != 0 && indexType != 1 && indexType != 2) {
                        ReleaseNativeJsonResult(resultPtr, true);
                        throw std::runtime_error("共享 index 类型查询返回未知值");
                    }
                    if (indexType == 2) {
                        const std::string freeConfig = dlcv_infer::json{
                            { "model_index", modelIndex }
                        }.dump();
                        const char* freeResult = nativeLoader.GetFreeModelFunc()(freeConfig.c_str());
                        if (freeResult != nullptr) nativeLoader.GetFreeResultFunc()(freeResult);
                        ReleaseNativeJsonResult(resultPtr, true);
                        return AllocateNativeJsonResult(MakeNativeStatus(
                            1,
                            "普通模型返回了流程索引。"));
                    }
                }

                auto entry = std::make_shared<CApiModelEntry>();
                entry->loader = &nativeLoader;
                entry->modelIndex = modelIndex;
                entry->nativeOwner = true;
                try {
                    StoreModelEntry(modelIndex, entry);
                } catch (...) {
                    const std::string freeConfig = dlcv_infer::json{
                        { "model_index", modelIndex }
                    }.dump();
                    const char* freeResult = nativeLoader.GetFreeModelFunc()(freeConfig.c_str());
                    if (freeResult != nullptr) nativeLoader.GetFreeResultFunc()(freeResult);
                    ReleaseNativeJsonResult(resultPtr, true);
                    throw;
                }
                return resultPtr;
            } catch (const std::exception& ex) {
                return AllocateNativeJsonResult(MakeNativeStatus(1, ex.what()));
            }
        });
    }

    return CallNativeString("dlcv_load_model", [config, modelPath]() {
        try {
            if (config.contains("type") &&
                (!config.at("type").is_string() || config.at("type").get<std::string>() != "Model")) {
                return AllocateNativeJsonResult(MakeNativeStatus(1, "Unsupported type."));
            }
            const int deviceId = config.value("device_id", 0);
            const int modelIndex = dlcv_infer_cpp_load_model_c(modelPath.c_str(), deviceId);
            if (modelIndex < 0) {
                const char* lastError = dlcv_infer_cpp_get_last_error_c();
                const std::string message = lastError != nullptr && lastError[0] != '\0'
                    ? lastError
                    : "load model failed";
                return AllocateNativeJsonResult(MakeNativeStatus(1, message));
            }
            dlcv_infer::json response = MakeNativeStatus(0, "Successfully loaded model.");
            response["model_index"] = modelIndex;
            return AllocateNativeJsonResult(response);
        } catch (const std::exception& ex) {
            return AllocateNativeJsonResult(MakeNativeStatus(1, ex.what()));
        }
    });
}

const char* DLCV_NATIVE_C_CALL dlcv_free_model(const char* config_str) {
    dlcv_infer::json config;
    int modelIndex = -1;
    if (!TryParseNativeConfig(config_str, config) || !TryReadModelIndex(config, modelIndex)) {
        return CallNativeString("dlcv_free_model", [config_str]() {
            dlcv_infer::DllLoader& loader = dlcv_infer::DllLoader::Instance();
            return InvokeTrackedNativeJson(
                loader,
                loader.GetFreeModelFunc(),
                config_str,
                NativeJsonReleaseKind::Result);
        });
    }

    return CallNativeString("dlcv_free_model", [config_str, modelIndex]() {
        try {
            dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
            const std::shared_ptr<CApiModelEntry> entry =
                FindOrRestoreSharedModelEntry(modelIndex);
            if (entry->nativeOwner) {
                if (entry->loader == nullptr) {
                    return AllocateNativeJsonResult(MakeNativeStatus(1, "本地模型缺少所属 DLL"));
                }
                const char* result = InvokeTrackedNativeJson(
                    *entry->loader,
                    entry->loader->GetFreeModelFunc(),
                    config_str,
                    NativeJsonReleaseKind::Result);
                if (IsNativeSuccessResult(result)) EraseModelEntry(modelIndex, entry);
                return result;
            }

            if (dlcv_infer_cpp_free_model_c(modelIndex) != 0) {
                return AllocateNativeJsonResult(MakeNativeStatus(2, "Model not found."));
            }
            return AllocateNativeJsonResult(MakeNativeStatus(0, "Successfully freed model."));
        } catch (const std::exception& ex) {
            return AllocateNativeJsonResult(MakeNativeStatus(1, ex.what()));
        }
    });
}

const char* DLCV_NATIVE_C_CALL dlcv_get_model_info(const char* config_str) {
    dlcv_infer::json config;
    std::string modelPath;
    int modelIndex = -1;
    const bool parsed = TryParseNativeConfig(config_str, config);
    const bool hasFlowPath = parsed && TryReadFlowModelPath(config, modelPath);
    const bool hasModelIndex = parsed && TryReadModelIndex(config, modelIndex);
    if (!hasFlowPath && !hasModelIndex) {
        return CallNativeString("dlcv_get_model_info", [config_str]() {
            dlcv_infer::DllLoader& loader = dlcv_infer::DllLoader::Instance();
            return InvokeTrackedNativeJson(
                loader,
                loader.GetModelInfoFunc(),
                config_str,
                NativeJsonReleaseKind::Result);
        });
    }

    return CallNativeString("dlcv_get_model_info", [config, modelPath, hasFlowPath, modelIndex, config_str]() {
        try {
            dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
            if (hasFlowPath) {
                const int deviceId = config.value("device_id", 0);
                dlcv_infer::Model model(modelPath, deviceId);
                return AllocateNativeJsonResult(AddNativeModelInfoStatus(model.GetModelInfo()));
            }

            const std::shared_ptr<CApiModelEntry> entry =
                FindOrRestoreSharedModelEntry(modelIndex);
            if (!entry->serializeInfer) {
                if (entry->loader == nullptr) {
                    return AllocateNativeJsonResult(MakeNativeStatus(1, "模型缺少所属 DLL"));
                }
                const std::string nativeConfig = dlcv_infer::json{
                    { "model_index", modelIndex }
                }.dump();
                return InvokeTrackedNativeJson(
                    *entry->loader,
                    entry->loader->GetModelInfoFunc(),
                    nativeConfig.c_str(),
                    NativeJsonReleaseKind::Result);
            }
            return AllocateNativeJsonResult(AddNativeModelInfoStatus(entry->model->GetModelInfo()));
        } catch (const std::exception& ex) {
            return AllocateNativeJsonResult(MakeNativeStatus(1, ex.what()));
        }
    });
}

extern "C" const char* DLCV_NATIVE_C_CALL dlcv_infer_json_impl(const char* config_str) {
    dlcv_infer::json config;
    int modelIndex = -1;
    if (!TryParseNativeConfig(config_str, config) || !TryReadModelIndex(config, modelIndex)) {
        return CallNativeString("dlcv_infer", [config_str]() {
            dlcv_infer::DllLoader& loader = dlcv_infer::DllLoader::Instance();
            return InvokeTrackedNativeJson(
                loader,
                loader.GetInferFunc(),
                config_str,
                NativeJsonReleaseKind::ModelResult);
        });
    }

    return CallNativeString("dlcv_infer", [config_str, modelIndex]() {
        try {
            dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
            const std::shared_ptr<CApiModelEntry> entry =
                FindOrRestoreSharedModelEntry(modelIndex);
            if (!entry->serializeInfer) {
                if (entry->loader == nullptr) {
                    return AllocateNativeJsonResult(MakeNativeStatus(1, "模型缺少所属 DLL"));
                }
                return InvokeTrackedNativeJson(
                    *entry->loader,
                    entry->loader->GetInferFunc(),
                    config_str,
                    NativeJsonReleaseKind::ModelResult);
            }

            const dlcv_infer::json config = dlcv_infer::json::parse(config_str);
            return InferFlowModelWithNativeJson(entry, config);
        } catch (const std::exception& ex) {
            return AllocateNativeJsonResult(MakeNativeStatus(1, ex.what()));
        }
    });
}

void DLCV_NATIVE_C_CALL dlcv_free_model_result(const char* config_str) {
    if (ReleaseNativeJsonResult(config_str, true)) return;
    CallNativeVoid("dlcv_free_model_result", [config_str]() {
        dlcv_infer::NativeApi::FreeModelResult(config_str);
    });
}

void DLCV_NATIVE_C_CALL dlcv_free_result(const char* config_str) {
    if (ReleaseNativeJsonResult(config_str, false)) return;
    CallNativeVoid("dlcv_free_result", [config_str]() {
        dlcv_infer::NativeApi::FreeResult(config_str);
    });
}

void DLCV_NATIVE_C_CALL dlcv_free_all_models() {
    CallNativeVoid("dlcv_free_all_models", []() {
        dlcv_infer::flow::ModelLifecycleWriteGuard lifecycleGuard;
        {
            std::lock_guard<std::mutex> lock(g_modelsMutex);
            g_models.clear();
        }
        dlcv_infer::Utils::FreeAllModels();
    });
}

const char* DLCV_NATIVE_C_CALL dlcv_get_device_info() {
    return CallNativeString("dlcv_get_device_info", []() {
        return dlcv_infer::NativeApi::GetDeviceInfo();
    });
}

const char* DLCV_NATIVE_C_CALL dlcv_get_gpu_info() {
    return CallNativeString("dlcv_get_gpu_info", []() {
        return dlcv_infer::NativeApi::GetGpuInfo();
    });
}

void DLCV_NATIVE_C_CALL dlcv_keep_max_clock() {
    CallNativeVoid("dlcv_keep_max_clock", []() {
        dlcv_infer::NativeApi::KeepMaxClock();
    });
}

void DLCV_NATIVE_C_CALL dlcv_reset_max_clock() {
    CallNativeVoid("dlcv_reset_max_clock", []() {
        dlcv_infer::NativeApi::ResetMaxClock();
    });
}

void DLCV_NATIVE_C_CALL dlcv_set_gpu_max_clock(bool verbose) {
    CallNativeVoid("dlcv_set_gpu_max_clock", [verbose]() {
        dlcv_infer::NativeApi::SetGpuMaxClock(verbose);
    });
}

void DLCV_NATIVE_C_CALL dlcv_reset_gpu_max_clock(bool verbose) {
    CallNativeVoid("dlcv_reset_gpu_max_clock", [verbose]() {
        dlcv_infer::NativeApi::ResetGpuMaxClock(verbose);
    });
}

const char* DLCV_NATIVE_C_CALL dlcv_get_power_scheme_guid(int verbose) {
    return CallNativeString("dlcv_get_power_scheme_guid", [verbose]() {
        return dlcv_infer::NativeApi::GetPowerSchemeGuid(verbose);
    });
}

int DLCV_NATIVE_C_CALL dlcv_set_power_scheme_guid(const char* scheme_guid, int verbose) {
    return CallNativeInt("dlcv_set_power_scheme_guid", [scheme_guid, verbose]() {
        return dlcv_infer::NativeApi::SetPowerSchemeGuid(scheme_guid, verbose);
    });
}

const char* DLCV_NATIVE_C_CALL dlcv_get_power_scheme(int verbose) {
    return CallNativeString("dlcv_get_power_scheme", [verbose]() {
        return dlcv_infer::NativeApi::GetPowerScheme(verbose);
    });
}

int DLCV_NATIVE_C_CALL dlcv_set_power_scheme(const char* scheme_name, int verbose) {
    return CallNativeInt("dlcv_set_power_scheme", [scheme_name, verbose]() {
        return dlcv_infer::NativeApi::SetPowerScheme(scheme_name, verbose);
    });
}

int DLCV_NATIVE_C_CALL dlcv_set_current_process_affinity_to_big_cores(int verbose) {
    return CallNativeInt("dlcv_set_current_process_affinity_to_big_cores", [verbose]() {
        return dlcv_infer::NativeApi::SetCurrentProcessAffinityToBigCores(verbose);
    });
}

int DLCV_NATIVE_C_CALL dlcv_set_current_process_priority_highest(
    int prefer_realtime,
    int verbose,
    int bind_big_cores) {
    return CallNativeInt("dlcv_set_current_process_priority_highest", [
        prefer_realtime,
        verbose,
        bind_big_cores]() {
        return dlcv_infer::NativeApi::SetCurrentProcessPriorityHighest(
            prefer_realtime,
            verbose,
            bind_big_cores);
    });
}

}
