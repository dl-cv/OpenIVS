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

#include <cctype>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <iomanip>
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
    bool serializeInfer = false;
    std::mutex inferMutex;
};

static std::unordered_map<int, std::shared_ptr<CApiModelEntry>> g_models;
static std::mutex g_modelsMutex;
struct NativeJsonAllocation {
    std::vector<void*> maskBuffers;

    NativeJsonAllocation() = default;
    explicit NativeJsonAllocation(std::vector<void*> buffers)
        : maskBuffers(std::move(buffers)) {}
    NativeJsonAllocation(const NativeJsonAllocation&) = delete;
    NativeJsonAllocation& operator=(const NativeJsonAllocation&) = delete;
    NativeJsonAllocation(NativeJsonAllocation&&) noexcept = default;
    NativeJsonAllocation& operator=(NativeJsonAllocation&&) noexcept = default;
    ~NativeJsonAllocation() {
        for (void* buffer : maskBuffers) std::free(buffer);
    }

    void KeepMaskBuffersAllocated() noexcept {
        maskBuffers.clear();
    }
};
static std::unordered_map<const char*, NativeJsonAllocation> g_nativeJsonAllocations;
static std::mutex g_nativeJsonAllocationsMutex;
static thread_local std::string g_lastError;
static const char* kDlcvCapiDebugLogPath = "C:\\ProgramData\\dlcvInfer_c_api_debug.log";

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
    std::vector<void*> maskBuffers = {}) {
    const std::string serialized = value.dump();
    char* result = static_cast<char*>(std::malloc(serialized.size() + 1));
    if (result == nullptr) {
        for (void* buffer : maskBuffers) std::free(buffer);
        throw std::bad_alloc();
    }
    std::memcpy(result, serialized.c_str(), serialized.size() + 1);

    NativeJsonAllocation allocation(std::move(maskBuffers));
    try {
        std::lock_guard<std::mutex> lock(g_nativeJsonAllocationsMutex);
        const auto inserted = g_nativeJsonAllocations.emplace(result, std::move(allocation));
        if (!inserted.second) throw std::runtime_error("原生 JSON 返回指针重复");
    } catch (...) {
        std::free(result);
        throw;
    }
    return result;
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
    std::free(const_cast<char*>(result));
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

static bool IsFlowModelIndex(int modelIndex) noexcept {
    return modelIndex >= 10000;
}

static dlcv_infer::json AddNativeModelInfoStatus(dlcv_infer::json modelInfo) {
    if (!modelInfo.is_object()) {
        modelInfo = dlcv_infer::json{ { "model_info", std::move(modelInfo) } };
    }
    modelInfo["code"] = 0;
    modelInfo["message"] = "Successfully got model info.";
    return modelInfo;
}

static std::shared_ptr<CApiModelEntry> FindFlowModelEntry(int modelIndex) {
    std::lock_guard<std::mutex> lock(g_modelsMutex);
    const auto it = g_models.find(modelIndex);
    if (it == g_models.end() || !it->second || !it->second->serializeInfer) return nullptr;
    return it->second;
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
    std::vector<void*>& maskBuffers) {
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
        void* maskBuffer = std::malloc(byteCount);
        if (maskBuffer == nullptr) throw std::bad_alloc();
        std::memcpy(maskBuffer, continuousMask.data, byteCount);
        maskBuffers.push_back(maskBuffer);
        mask["mask_ptr"] = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(maskBuffer));
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

    std::vector<void*> maskBuffers;
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
        for (void* buffer : maskBuffers) std::free(buffer);
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

    FILE* fp = nullptr;
    if (fopen_s(&fp, kDlcvCapiDebugLogPath, "a") == 0 && fp != nullptr) {
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
        std::free(result.message);
        result.message = nullptr;
    }
    result.message = _strdup(message);
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
        auto model = std::make_shared<dlcv_infer::Model>(modelPath, device_id);
        int idx = model->modelIndex;
        if (idx < 0) {
            SetLastErrorMessage("load model returned negative modelIndex: " + std::to_string(idx) + "; " + pathDiagnostics);
            AppendCapiDebugLog("load_model failed: %s", g_lastError.c_str());
            return -1;
        }
        auto entry = std::make_shared<CApiModelEntry>();
        entry->model = std::move(model);
        entry->serializeInfer = IsFlowModelPath(modelPath);
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
    std::lock_guard<std::mutex> lock(g_modelsMutex);
    auto it = g_models.find(model_index);
    if (it == g_models.end()) return -1;
    g_models.erase(it);
    return 0;
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

    if (!image_list || image_list->n <= 0 || !image_list->images) {
        result.message = _strdup("invalid image list");
        return result;
    }

    std::shared_ptr<CApiModelEntry> entry;
    {
        std::lock_guard<std::mutex> lock(g_modelsMutex);
        auto it = g_models.find(model_index);
        if (it == g_models.end()) {
            result.message = _strdup("model not found");
            return result;
        }
        entry = it->second;
    }

    std::unique_lock<std::mutex> inferLock(entry->inferMutex, std::defer_lock);
    if (entry->serializeInfer) {
        inferLock.lock();
    }

    try {
        std::vector<cv::Mat> mats;
        mats.reserve(image_list->n);
        for (int i = 0; i < image_list->n; ++i) {
            const DlcvCImage& img = image_list->images[i];
            if (!img.data_ptr || img.height <= 0 || img.width <= 0 || img.channel <= 0) {
                result.message = _strdup("invalid image data");
                return result;
            }
            int type = CV_8UC(img.channel);
            cv::Mat mat(img.height, img.width, type, reinterpret_cast<void*>(static_cast<uintptr_t>(img.data_ptr)));
            mats.push_back(mat);
        }

        dlcv_infer::json params = dlcv_infer::json::object();
        if (params_json != nullptr && params_json[0] != '\0') {
            params = dlcv_infer::json::parse(params_json);
            if (!params.is_object()) {
                throw std::invalid_argument("params_json 必须是 JSON 对象");
            }
        }

        dlcv_infer::Result cppResult = entry->model->InferBatch(mats, params);

        result.code = 0;
        result.message = _strdup("success");
        result.n = static_cast<int>(cppResult.sampleResults.size());
        if (result.n > 0) {
            result.sample_results = static_cast<DlcvCSampleResult*>(std::malloc(sizeof(DlcvCSampleResult) * result.n));
            std::memset(result.sample_results, 0, sizeof(DlcvCSampleResult) * result.n);
            for (int i = 0; i < result.n; ++i) {
                const auto& sample = cppResult.sampleResults[i];
                DlcvCSampleResult& sr = result.sample_results[i];
                sr.n = static_cast<int>(sample.results.size());
                if (sr.n > 0) {
                    sr.results = static_cast<DlcvCObjectResult*>(std::malloc(sizeof(DlcvCObjectResult) * sr.n));
                    std::memset(sr.results, 0, sizeof(DlcvCObjectResult) * sr.n);
                    for (int j = 0; j < sr.n; ++j) {
                        const auto& obj = sample.results[j];
                        DlcvCObjectResult& o = sr.results[j];
                        o.category_id = obj.categoryId;
                        o.category_name = _strdup(obj.categoryName.c_str());
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
                            size_t bytes = maskClone.total() * maskClone.elemSize();
                            unsigned char* maskData = static_cast<unsigned char*>(std::malloc(bytes));
                            std::memcpy(maskData, maskClone.data, bytes);
                            o.mask.mask_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(maskData));
                            o.mask.width = maskClone.cols;
                            o.mask.height = maskClone.rows;
                        } else {
                            o.mask.mask_ptr = 0;
                            o.mask.width = 0;
                            o.mask.height = 0;
                        }
                        o.with_angle = obj.withAngle;
                        o.angle = obj.angle;
                        o.with_mean = obj.withMean;
                        o.foreground_mean = obj.foregroundMean;
                        o.background_mean = obj.backgroundMean;
                    }
                } else {
                    sr.results = nullptr;
                }
            }
        } else {
            result.sample_results = nullptr;
        }
    } catch (const std::exception& ex) {
        result.code = -1;
        result.message = _strdup(ex.what());
    } catch (...) {
        result.code = -1;
        result.message = _strdup("unknown error");
    }

    return result;
}

void dlcv_infer_cpp_free_model_result_c(DlcvCResult* result) {
    if (!result) return;
    if (result->message) {
        std::free(result->message);
        result->message = nullptr;
    }
    if (result->sample_results && result->n > 0) {
        for (int i = 0; i < result->n; ++i) {
            DlcvCSampleResult& sr = result->sample_results[i];
            if (sr.results && sr.n > 0) {
                for (int j = 0; j < sr.n; ++j) {
                    DlcvCObjectResult& o = sr.results[j];
                    if (o.category_name) {
                        std::free(o.category_name);
                        o.category_name = nullptr;
                    }
                    if (o.with_mask && o.mask.mask_ptr) {
                        std::free(reinterpret_cast<void*>(static_cast<uintptr_t>(o.mask.mask_ptr)));
                        o.mask.mask_ptr = 0;
                    }
                }
                std::free(sr.results);
                sr.results = nullptr;
            }
        }
        std::free(result->sample_results);
        result->sample_results = nullptr;
    }
    result->n = 0;
    result->code = 0;
}

const char* dlcv_infer_cpp_get_model_info_c(int model_index) {
    dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
    ClearLastErrorMessage();
    std::shared_ptr<CApiModelEntry> entry;
    {
        std::lock_guard<std::mutex> lock(g_modelsMutex);
        const auto it = g_models.find(model_index);
        if (it == g_models.end()) {
            SetLastErrorMessage("model not found");
            return nullptr;
        }
        entry = it->second;
    }

    try {
        const std::string value = entry->model->GetModelInfo().dump();
        return _strdup(value.c_str());
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
    if (image == nullptr || image->data_ptr == 0 || image->height <= 0 || image->width <= 0 || image->channel <= 0) {
        SetLastErrorMessage("invalid image data");
        return nullptr;
    }

    std::shared_ptr<CApiModelEntry> entry;
    {
        std::lock_guard<std::mutex> lock(g_modelsMutex);
        const auto it = g_models.find(model_index);
        if (it == g_models.end()) {
            SetLastErrorMessage("model not found");
            return nullptr;
        }
        entry = it->second;
    }

    std::unique_lock<std::mutex> inferLock(entry->inferMutex, std::defer_lock);
    if (entry->serializeInfer) {
        inferLock.lock();
    }

    try {
        const int type = CV_8UC(image->channel);
        cv::Mat mat(
            image->height,
            image->width,
            type,
            reinterpret_cast<void*>(static_cast<uintptr_t>(image->data_ptr)));
        dlcv_infer::json params = dlcv_infer::json::object();
        if (params_json != nullptr && params_json[0] != '\0') {
            params = dlcv_infer::json::parse(params_json);
            if (!params.is_object()) {
                throw std::invalid_argument("params_json 必须是 JSON 对象");
            }
        }
        const std::string value = entry->model->InferOneOutJson(mat, params).dump();
        return _strdup(value.c_str());
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
        return _strdup(value.c_str());
    } catch (const std::exception& ex) {
        SetLastErrorMessage(ex.what());
    } catch (...) {
        SetLastErrorMessage("unknown error");
    }
    return nullptr;
}

void dlcv_infer_cpp_free_string_c(const char* value) {
    std::free(const_cast<char*>(value));
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
        dlcv_infer::NativeApi::FreeAllModels();
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
            return dlcv_infer::NativeApi::LoadModel(config_str);
        });
    }

    return CallNativeString("dlcv_load_model", [config, modelPath]() {
        try {
            if (config.contains("type") &&
                (!config.at("type").is_string() || config.at("type").get<std::string>() != "Model")) {
                return AllocateNativeJsonResult(MakeNativeStatus(1, "Unsupported type."));
            }
            // 流程模型会在 Model 构造期间加载内部模型并执行底层预热。
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
    if (!TryParseNativeConfig(config_str, config) ||
        !TryReadModelIndex(config, modelIndex) ||
        !IsFlowModelIndex(modelIndex)) {
        return CallNativeString("dlcv_free_model", [config_str]() {
            return dlcv_infer::NativeApi::FreeModel(config_str);
        });
    }

    return CallNativeString("dlcv_free_model", [config_str]() {
        try {
            const dlcv_infer::json config = dlcv_infer::json::parse(config_str);
            const int modelIndex = config.at("model_index").get<int>();
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
    const bool hasFlowIndex = parsed && TryReadModelIndex(config, modelIndex) && IsFlowModelIndex(modelIndex);
    if (!hasFlowPath && !hasFlowIndex) {
        return CallNativeString("dlcv_get_model_info", [config_str]() {
            return dlcv_infer::NativeApi::GetModelInfo(config_str);
        });
    }

    return CallNativeString("dlcv_get_model_info", [config, modelPath, hasFlowPath]() {
        try {
            dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
            if (hasFlowPath) {
                const int deviceId = config.value("device_id", 0);
                dlcv_infer::Model model(modelPath, deviceId);
                return AllocateNativeJsonResult(AddNativeModelInfoStatus(model.GetModelInfo()));
            }
            const int modelIndex = config.at("model_index").get<int>();
            const std::shared_ptr<CApiModelEntry> entry = FindFlowModelEntry(modelIndex);
            if (!entry) return AllocateNativeJsonResult(MakeNativeStatus(2, "Model not found."));
            return AllocateNativeJsonResult(AddNativeModelInfoStatus(entry->model->GetModelInfo()));
        } catch (const std::exception& ex) {
            return AllocateNativeJsonResult(MakeNativeStatus(1, ex.what()));
        }
    });
}

extern "C" const char* DLCV_NATIVE_C_CALL dlcv_infer_json_impl(const char* config_str) {
    dlcv_infer::json config;
    int modelIndex = -1;
    const bool hasFlowIndex = TryParseNativeConfig(config_str, config) &&
        TryReadModelIndex(config, modelIndex) && IsFlowModelIndex(modelIndex);
    if (!hasFlowIndex) {
        return CallNativeString("dlcv_infer", [config_str]() {
            return dlcv_infer::NativeApi::Infer(config_str);
        });
    }

    return CallNativeString("dlcv_infer", [config_str]() {
        try {
            dlcv_infer::flow::ModelLifecycleReadGuard lifecycleGuard;
            const dlcv_infer::json config = dlcv_infer::json::parse(config_str);
            int modelIndex = -1;
            if (!TryReadModelIndex(config, modelIndex)) {
                return AllocateNativeJsonResult(MakeNativeStatus(1, "model_index is invalid"));
            }
            const std::shared_ptr<CApiModelEntry> entry = FindFlowModelEntry(modelIndex);
            if (!entry) return AllocateNativeJsonResult(MakeNativeStatus(2, "Model not found."));
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
        dlcv_infer::NativeApi::FreeAllModels();
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
