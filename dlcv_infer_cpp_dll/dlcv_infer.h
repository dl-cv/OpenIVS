#pragma once

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <string>
#include <vector>
#include <memory>
#include <functional>
#include <map>
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
    typedef void* (*FreeModelFuncType)(const char* config_str);
    typedef void* (*GetModelInfoFuncType)(const char* config_str);
    typedef void* (*InferFuncType)(const char* config_str);
    typedef void (*FreeModelResultFuncType)(void* result_ptr);
    typedef void (*FreeResultFuncType)(void* result_ptr);
    typedef void (*FreeAllModelsFuncType)();
    typedef void* (*GetDeviceInfoFuncType)();
    typedef void* (*KeepMaxClockFuncType)();

#ifdef DLCV_INFER_CPP_DLL_EXPORTS
    // DLL 加载器（内部使用）
    class DllLoader {
    private:
        std::string dllName;
        std::string dllPath;
        std::string dllDevPath;
        std::string dllExePath;
        void* hModule = nullptr;
        sntl_admin::DogProvider dogProvider;

        // 函数指针
        LoadModelFuncType dlcv_load_model = nullptr;
        FreeModelFuncType dlcv_free_model = nullptr;
        GetModelInfoFuncType dlcv_get_model_info = nullptr;
        InferFuncType dlcv_infer = nullptr;
        FreeModelResultFuncType dlcv_free_model_result = nullptr;
        FreeResultFuncType dlcv_free_result = nullptr;
        FreeAllModelsFuncType dlcv_free_all_models = nullptr;
        GetDeviceInfoFuncType dlcv_get_device_info = nullptr;
        KeepMaxClockFuncType dlcv_keep_max_clock = nullptr;

        // 加载 DLL
        void LoadDll();

        static DllLoader* instance;
        DllLoader(sntl_admin::DogProvider provider);

    public:
        sntl_admin::DogProvider GetDogProvider() const { return dogProvider; }
        std::string GetLoadedNativeDllName() const { return dllName; }

        static DllLoader& Instance();
        static void EnsureForModel(const std::string& modelPath);
        static void EnsureForModel(const std::wstring& modelPath);

        /// <summary>
        /// 自动检测当前插入的加密狗，按 Sentinel 优先、Virbox 第二返回 Provider。
        /// 若均未检测到，默认返回 Sentinel。
        /// </summary>
        static sntl_admin::DogProvider AutoDetectProvider();

        LoadModelFuncType GetLoadModelFunc() const {
            return dlcv_load_model;
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

        ObjectResult(int id, const std::string& name, float s, float a,
            const std::vector<double>& b, bool wm, const cv::Mat& m,
            bool wb = true, bool wa = false, float ang = -100.0f)
            : categoryId(id), categoryName(name), score(s), area(a),
            bbox(b), withMask(wm), mask(m),
            withBbox(wb), withAngle(wa), angle(ang) {}
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
        /// - false：析构/FreeModel 时不会释放底层模型（用于“共享/借用 modelIndex”的场景）
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

        Result Infer(const cv::Mat& image, const json& params_json = nullptr);

        Result InferBatch(const std::vector<cv::Mat>& image_list, const json& params_json = nullptr);

        json InferOneOutJson(const cv::Mat& image, const json& params_json = nullptr);

        static void GetLastInferTiming(double& dlcvInferMs, double& totalInferMs);
        static std::vector<FlowNodeTiming> GetLastFlowNodeTimings();

    private:
        bool _isFlowGraphMode = false;
        int _deviceId = 0;
        flow::FlowGraphModel* _flowModel = nullptr;
        int _expectedChCache = -2;

        int resolveEffectiveInputCh();
        std::vector<cv::Mat> prepareInferInputBatch(const std::vector<cv::Mat>& images);
    protected:
        DllLoader* _dllLoader = nullptr;
        sntl_admin::DogProvider _loadedDogProvider = sntl_admin::DogProvider::Unknown;
        std::string _loadedNativeDllName;

    public:
        sntl_admin::DogProvider LoadedDogProvider() const { return _loadedDogProvider; }
        std::string LoadedNativeDllName() const { return _loadedNativeDllName; }
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
