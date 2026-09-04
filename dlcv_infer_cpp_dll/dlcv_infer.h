#pragma once

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <string>
#include <vector>
#include <memory>
#include <cstddef>
#include <functional>
#include <map>
#include <mutex>
#include <fstream>
#include <iostream>
#include <algorithm>
#include "json/json.hpp"
#include "opencv2/imgcodecs.hpp"
#include "opencv2/imgproc.hpp"
#include "dlcv_sntl_admin.h"

// DLL 导出/导入宏（用于本项目生成的 dlcv_infer_cpp_dll）
#if defined(_WIN32) || defined(__CYGWIN__)
#  ifdef DLCV_INFER_CPP_DLL_EXPORTS
#    define DLCV_INFER_CPP_DLL_API __declspec(dllexport)
#  else
#    define DLCV_INFER_CPP_DLL_API __declspec(dllimport)
#  endif
#else
#  define DLCV_INFER_CPP_DLL_API
#endif

namespace dlcv_infer {

    class DllLoader;

    namespace flow {
        class FlowGraphModel;
        class ModelPool;
    }

    DLCV_INFER_CPP_DLL_API std::wstring convertStringToWstring(const std::string& inputString);
    DLCV_INFER_CPP_DLL_API std::string convertWstringToString(const std::wstring& inputWstring);
    DLCV_INFER_CPP_DLL_API std::string convertWstringToUtf8(const std::wstring& inputWstring);
    DLCV_INFER_CPP_DLL_API std::wstring convertUtf8ToWstring(const std::string& inputUtf8);
    DLCV_INFER_CPP_DLL_API std::string convertWstringToGbk(const std::wstring& inputWstring);
    DLCV_INFER_CPP_DLL_API std::wstring convertGbkToWstring(const std::string& inputGbk);

    DLCV_INFER_CPP_DLL_API std::string convertUtf8ToGbk(const std::string& inputUtf8);
    DLCV_INFER_CPP_DLL_API std::string convertGbkToUtf8(const std::string& inputGbk);

    // 使用 nlohmann/json
    using json = nlohmann::json;

    DLCV_INFER_CPP_DLL_API json GetAllDogInfo();

#ifndef NVML_TYPES_H
#define NVML_TYPES_H

    // NVIDIA Management Library (NVML) 类型定义

#ifdef __cplusplus
    extern "C" {
#endif

        typedef void* nvmlDevice_t;

#ifdef __cplusplus
    }
#endif

#endif // NVML_TYPES_H 

    // 外部 DLL 接口函数类型定义
    typedef void* (*LoadModelFuncType)(const char* config_str);
    typedef void* (*LoadModelBinaryFuncType)(const unsigned char* model_data, size_t model_size, const char* config_str);
    typedef void* (*FreeModelFuncType)(const char* config_str);
    typedef void* (*GetModelInfoFuncType)(const char* config_str);
    typedef void* (*InferFuncType)(const char* config_str);
    typedef void (*FreeModelResultFuncType)(void* result_ptr);
    typedef void (*FreeResultFuncType)(void* result_ptr);
    typedef void (*FreeAllModelsFuncType)();
    typedef void* (*GetDeviceInfoFuncType)();
    typedef void* (*KeepMaxClockFuncType)();
    typedef int (*GetIndexTypeFuncType)(int index);
    typedef const char* (*GetModelInfoByIndexFuncType)(int modelIndex);
    typedef int (*RegisterFlowFuncType)(const char* flowJson);
    typedef const char* (*GetFlowInfoFuncType)(int flowIndex);
    typedef int (*FreeFlowFuncType)(int flowIndex);
    typedef int (*BindIndexFuncType)(int index);
    typedef int (*UnbindIndexFuncType)(int index);
    typedef FreeResultFuncType FreeStringFuncType;

#ifdef DLCV_INFER_CPP_DLL_EXPORTS
    // DLL 加载器（内部使用）
    class DllLoader {
    private:
        std::string dllName;
        std::string dllPath;
        std::string dllDevPath;
        void* hModule = nullptr;
        sntl_admin::DogProvider dogProvider;

        // 函数指针
        LoadModelFuncType dlcv_load_model = nullptr;
        LoadModelBinaryFuncType dlcv_load_model_binary = nullptr;
        FreeModelFuncType dlcv_free_model = nullptr;
        GetModelInfoFuncType dlcv_get_model_info = nullptr;
        InferFuncType dlcv_infer = nullptr;
        FreeModelResultFuncType dlcv_free_model_result = nullptr;
        FreeResultFuncType dlcv_free_result = nullptr;
        FreeAllModelsFuncType dlcv_free_all_models = nullptr;
        GetDeviceInfoFuncType dlcv_get_device_info = nullptr;
        KeepMaxClockFuncType dlcv_keep_max_clock = nullptr;
        GetIndexTypeFuncType dlcv_get_index_type_c = nullptr;
        GetModelInfoByIndexFuncType dlcv_get_model_info_c = nullptr;
        RegisterFlowFuncType dlcv_register_flow_c = nullptr;
        GetFlowInfoFuncType dlcv_get_flow_info_c = nullptr;
        FreeFlowFuncType dlcv_free_flow_c = nullptr;
        BindIndexFuncType dlcv_bind_index_c = nullptr;
        UnbindIndexFuncType dlcv_unbind_index_c = nullptr;
        FreeStringFuncType dlcv_free_string = nullptr;

        // 加载 DLL
        void LoadDll();
        static DllLoader& GetOrCreateForProvider(sntl_admin::DogProvider provider);

        DllLoader(sntl_admin::DogProvider provider);

    public:
        sntl_admin::DogProvider GetDogProvider() const { return dogProvider; }
        std::string GetLoadedNativeDllName() const { return dllName; }

        static DllLoader& Instance();
        static DllLoader& GetExistingOrDefaultSentinel();
        static DllLoader& ResolveForIndex(int index, int& indexType);
        static DllLoader& EnsureForModel(const std::string& modelPath);
        static DllLoader& EnsureForModel(const std::wstring& modelPath);
        static DllLoader& ForModelBuffer(const unsigned char* modelData, size_t modelSize);

        /// <summary>
        /// 自动检测当前插入的加密狗，按 Sentinel 优先、Virbox 第二返回 Provider。
        /// 若均未检测到，返回 Unknown，不加载任何推理 DLL。
        /// </summary>
        static sntl_admin::DogProvider AutoDetectProvider();

        LoadModelFuncType GetLoadModelFunc() const {
            return dlcv_load_model;
        }
        LoadModelBinaryFuncType GetLoadModelBinaryFunc() const {
            return dlcv_load_model_binary;
        }
        FreeModelFuncType GetFreeModelFunc() const {
            return dlcv_free_model;
        }
        GetModelInfoFuncType GetModelInfoFunc() const {
            return dlcv_get_model_info;
        }
        InferFuncType GetInferFunc() const {
            return dlcv_infer;
        }
        FreeModelResultFuncType GetFreeModelResultFunc() const {
            return dlcv_free_model_result;
        }
        FreeResultFuncType GetFreeResultFunc() const {
            return dlcv_free_result;
        }
        FreeAllModelsFuncType GetFreeAllModelsFunc() const {
            return dlcv_free_all_models;
        }
        GetDeviceInfoFuncType GetDeviceInfoFunc() const {
            return dlcv_get_device_info;
        }
        KeepMaxClockFuncType GetKeepMaxClockFunc() const {
            return dlcv_keep_max_clock;
        }
        GetIndexTypeFuncType GetIndexTypeFunc() const { return dlcv_get_index_type_c; }
        GetModelInfoByIndexFuncType GetModelInfoByIndexFunc() const { return dlcv_get_model_info_c; }
        RegisterFlowFuncType GetRegisterFlowFunc() const { return dlcv_register_flow_c; }
        GetFlowInfoFuncType GetFlowInfoFunc() const { return dlcv_get_flow_info_c; }
        FreeFlowFuncType GetFreeFlowFunc() const { return dlcv_free_flow_c; }
        BindIndexFuncType GetBindIndexFunc() const { return dlcv_bind_index_c; }
        UnbindIndexFuncType GetUnbindIndexFunc() const { return dlcv_unbind_index_c; }
        FreeStringFuncType GetFreeStringFunc() const { return dlcv_free_string; }
    };
#endif

    // 用于存储推理结果的结构体
    // 注意：这些结构体会出现在导出函数(Model::Infer 等)的签名中，但结构体本身不导出，
    // 以避免 C4251（导出类/结构体含 STL/cv::Mat 成员）警告。
    struct ObjectResult {
        int categoryId;
        std::string categoryName;
        float score;
        float area;
        std::vector<double> bbox;
        bool withMask;
        cv::Mat mask;
        bool withBbox;
        bool withAngle;
        float angle;
        bool withMean;
        double foregroundMean;
        double backgroundMean;

        ObjectResult(int id, const std::string& name, float s, float a,
            const std::vector<double>& b, bool wm, const cv::Mat& m,
            bool wb = true, bool wa = false, float ang = -100.0f)
            : ObjectResult(id, name, s, a, b, wm, m, wb, wa, ang, false, 0.0, 0.0) {}

        ObjectResult(int id, const std::string& name, float s, float a,
            const std::vector<double>& b, bool wm, const cv::Mat& m,
            bool wb, bool wa, float ang,
            bool wmean, double fgMean, double bgMean)
            : categoryId(id), categoryName(name), score(s), area(a),
            bbox(b), withMask(wm), mask(m),
            withBbox(wb), withAngle(wa), angle(ang),
            withMean(wmean), foregroundMean(fgMean), backgroundMean(bgMean) {}
    };

    struct SampleResult {
        std::vector<ObjectResult> results;

        explicit SampleResult(std::vector<ObjectResult> r) : results(std::move(r)) {}
    };

    struct Result {
        std::vector<SampleResult> sampleResults;

        explicit Result(std::vector<SampleResult> sr) : sampleResults(std::move(sr)) {}
    };

    struct FlowNodeTiming {
        int nodeId = -1;
        std::string nodeType;
        std::string nodeTitle;
        double elapsedMs = 0.0;
    };

    // 模型封装
#pragma warning(push)
#pragma warning(disable: 4251)
    class DLCV_INFER_CPP_DLL_API Model {
    protected:
        // 内部推理
        std::pair<json, void*> InferInternal(const std::vector<cv::Mat>& images, const json& params_json);

        // 解析推理结果
        Result ParseToStructResult(const json& resultObject);

    public:
        int modelIndex = -1;
        /// <summary>
        /// 是否拥有 modelIndex 对应底层模型的释放权。
        /// - true（默认）：析构/FreeModel 时会释放底层模型
        /// - false：不直接调用底层模型释放接口；共享索引对象释放时减少自身增加的使用计数
        /// </summary>
        bool OwnModelIndex = true;

        Model();

        Model(const std::string& modelPath, int device_id);

        // Windows 下推荐直接传 UTF-16 路径（std::wstring），内部会按本地代码页(GBK/936)转换后再加载，
        // 以避免调用侧手动做字符串编码转换导致路径乱码。
        Model(const std::wstring& modelPath, int device_id);

        Model(const Model&) = delete;
        Model& operator=(const Model&) = delete;

        Model(Model&& other) noexcept;
        Model& operator=(Model&& other) noexcept;

        virtual ~Model();

        void FreeModel();

        json GetModelInfo();

        // 获取流程模型的完整信息。
        json GetDvsModelInfo();

        Result Infer(const cv::Mat& image, const json& params_json = nullptr);

        Result InferBatch(const std::vector<cv::Mat>& image_list, const json& params_json = nullptr);

        json InferOneOutJson(const cv::Mat& image, const json& params_json = nullptr);

        static void GetLastInferTiming(double& dlcvInferMs, double& totalInferMs);
        static std::vector<FlowNodeTiming> GetLastFlowNodeTimings();
        // 读取当前线程最近一次推理的流程判定状态；必须与 Infer/InferBatch/
        // InferOneOutJson 在同一线程调用。未产生判定状态时返回 false。
        static bool GetLastInspectionStatus(
            bool& ok,
            std::vector<std::string>& reasons,
            size_t sampleIndex = 0);

    private:
#ifdef DLCV_INFER_CPP_DLL_EXPORTS
        friend class flow::ModelPool;
        friend class SlidingWindowModel;
        Model(
            std::shared_ptr<const std::vector<unsigned char>> modelData,
            const std::string& modelName,
            int device_id);
#endif

        bool _isFlowGraphMode = false;
        int _deviceId = 0;
        flow::FlowGraphModel* _flowModel = nullptr;
        int _expectedChCache = -2;
        bool _hasCachedModelInfo = false;
        json _cachedModelInfo;
        bool _indexBound = false;
        bool _indexReady = false;
        bool _ownsNativeModelIndex = false;
        bool _ownsRegisteredFlowIndex = false;
        std::mutex _indexStateMu;
        // DVS 模式：持有临时目录路径，确保在 Model 对象存活期间文件不被删除
        std::string _tempDir;

        void EnsureBoundIndexReady();
        void LoadFlowArchiveAndRegister(const std::wstring& modelPath, int deviceId);
        void RestoreFlowFromSharedInfo(const json& flowInfo);
        bool UnbindCurrentIndexNoexcept();
        int resolveEffectiveInputCh();
        std::vector<cv::Mat> prepareInferInputBatch(const std::vector<cv::Mat>& images);
    protected:
        DllLoader* _dllLoader = nullptr;
        sntl_admin::DogProvider _loadedDogProvider = sntl_admin::DogProvider::Unknown;
        std::string _loadedNativeDllName;

    public:
        sntl_admin::DogProvider LoadedDogProvider() const { return _loadedDogProvider; }
        std::string LoadedNativeDllName() const { return _loadedNativeDllName; }
#ifdef DLCV_INFER_CPP_DLL_EXPORTS
        DllLoader* LoadedDllLoader() const { return _dllLoader; }
#endif
    };
#pragma warning(pop)

#ifdef DLCV_INFER_CPP_DLL_EXPORTS
    // 滑动窗口模型（内部使用，如需对外可再单独开放）
    class SlidingWindowModel : public Model {
    public:
        SlidingWindowModel(
            const std::string& modelPath,
            int device_id,
            int small_img_width = 832,
            int small_img_height = 704,
            int horizontal_overlap = 16,
            int vertical_overlap = 16,
            float threshold = 0.5f,
            float iou_threshold = 0.2f,
            float combine_ios_threshold = 0.2f);
    };
#endif

    /// <summary>
    /// 工具类：静态方法集合。
    /// 注意：FreeAllModels 会释放底层 dlcv_infer.dll 中的所有已加载模型，属于全局操作。
    /// </summary>
    class DLCV_INFER_CPP_DLL_API Utils {
    public:
        static std::string JsonToString(const json& j);

        /// <summary>
        /// 释放底层推理 DLL 中的全部已加载模型（全局释放）。
        /// </summary>
        static void FreeAllModels();

        static json GetDeviceInfo();

        // OCR 推理
        static Result OcrInfer(Model& detectModel, Model& recognizeModel, const cv::Mat& image);

        // 获取 GPU 信息
        static json GetGpuInfo();
        static void KeepMaxClock();

        // NVML API 封装
        static int nvmlInit();
        static int nvmlShutdown();
        static int nvmlDeviceGetCount(unsigned int* deviceCount);
        static int nvmlDeviceGetName(nvmlDevice_t device, char* name, unsigned int length);
        static int nvmlDeviceGetHandleByIndex(unsigned int index, nvmlDevice_t* device);
    };
}

extern "C" {
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_load_c(const wchar_t* model_path, int device_id);
    DLCV_INFER_CPP_DLL_API const char* dlcv_shared_index_test_infer_c(int index, const wchar_t* image_path);
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_free_c(int index);
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_resolve_c(int index);
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_register_flow_c(int model_index);
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_index_rules_c();
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_double_load_free_c(const wchar_t* model_path, int device_id);
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_double_flow_load_free_c(const wchar_t* model_path, int device_id);
    DLCV_INFER_CPP_DLL_API int dlcv_shared_index_test_empty_flow_after_provider_c(
        const wchar_t* provider_model_path,
        const wchar_t* flow_path,
        int device_id);
    DLCV_INFER_CPP_DLL_API const char* dlcv_shared_index_test_info_c(int index);
    DLCV_INFER_CPP_DLL_API void dlcv_shared_index_test_free_string_c(const char* result);
}
