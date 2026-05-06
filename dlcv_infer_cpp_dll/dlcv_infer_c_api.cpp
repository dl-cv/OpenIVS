#include "dlcv_infer_c_api.h"
#include "dlcv_infer.h"
#include "ImageInputUtils.h"
#include <opencv2/core.hpp>
#include <unordered_map>
#include <memory>
#include <mutex>
#include <cstring>
#include <cstdlib>
#include <stdexcept>

static std::unordered_map<int, std::shared_ptr<dlcv_infer::Model>> g_models;
static std::mutex g_modelsMutex;

extern "C" {

int dlcv_infer_cpp_load_model_c(const char* model_path, int device_id) {
    if (!model_path) return -1;
    try {
        auto model = std::make_shared<dlcv_infer::Model>(std::string(model_path), device_id);
        int idx = model->modelIndex;
        if (idx < 0) return -1;
        std::lock_guard<std::mutex> lock(g_modelsMutex);
        g_models[idx] = model;
        return idx;
    } catch (...) {
        return -1;
    }
}

int dlcv_infer_cpp_free_model_c(int model_index) {
    std::lock_guard<std::mutex> lock(g_modelsMutex);
    auto it = g_models.find(model_index);
    if (it == g_models.end()) return -1;
    g_models.erase(it);
    return 0;
}

DlcvCResult dlcv_infer_cpp_infer_c(int model_index, const DlcvCImageList* image_list) {
    DlcvCResult result{};
    result.code = -1;

    if (!image_list || image_list->n <= 0 || !image_list->images) {
        result.message = _strdup("invalid image list");
        return result;
    }

    std::shared_ptr<dlcv_infer::Model> model;
    {
        std::lock_guard<std::mutex> lock(g_modelsMutex);
        auto it = g_models.find(model_index);
        if (it == g_models.end()) {
            result.message = _strdup("model not found");
            return result;
        }
        model = it->second;
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

        dlcv_infer::Result cppResult = model->InferBatch(mats);

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

}
