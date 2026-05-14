#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"
#include "flow/utils/MaskRleUtils.h"

#include <chrono>
#include <cmath>
#include <cstdio>
#include <limits>
#include <algorithm>
#include <fstream>
#include <stdexcept>
#include <vector>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#endif

#include "opencv2/imgcodecs.hpp"
#include "opencv2/imgproc.hpp"

namespace dlcv_infer {
namespace flow {

static std::string GetFileNameWithoutExt(const std::string& path) {
    size_t pos = path.find_last_of("\\/");
    std::string name = (pos == std::string::npos) ? path : path.substr(pos + 1);
    size_t dot = name.find_last_of('.');
    if (dot == std::string::npos) return name;
    return name.substr(0, dot);
}

#ifdef _WIN32
static std::wstring Utf8ToWideForWin32(const std::string& path) {
    std::wstring widePath;
    if (!path.empty()) {
        const int len = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.c_str(), -1, nullptr, 0);
        if (len > 0) {
            widePath.resize(static_cast<size_t>(len - 1));
            MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.c_str(), -1, &widePath[0], len);
        }
    }
    return widePath;
}
#endif

static bool FileExists(const std::string& path) {
#ifdef _WIN32
    std::wstring widePath = Utf8ToWideForWin32(path);
    if (!widePath.empty()) {
        FILE* fp = nullptr;
        if (_wfopen_s(&fp, widePath.c_str(), L"rb") == 0 && fp != nullptr) {
            std::fclose(fp);
            return true;
        }
        return false;
    }
#endif
    std::ifstream ifs(path, std::ios::binary);
    return static_cast<bool>(ifs);
}

static cv::Mat PrepareDecodedImageForFlow(const cv::Mat& image) {
    if (image.empty()) {
        return {};
    }

    const int channels = image.channels();
    if (channels == 3) {
        cv::Mat rgb;
        cv::cvtColor(image, rgb, cv::COLOR_BGR2RGB);
        return rgb;
    }
    if (channels == 4) {
        cv::Mat rgb;
        cv::cvtColor(image, rgb, cv::COLOR_BGRA2RGB);
        return rgb;
    }

    // 灰度与其他通道按原语义透传，但复制一份避免悬空引用。
    return image.clone();
}

static cv::Mat ReadFromPathForFlow(const std::string& path) {
    cv::Mat decoded;
#ifdef _WIN32
    std::wstring widePath = Utf8ToWideForWin32(path);
    if (!widePath.empty()) {
        FILE* fp = nullptr;
        if (_wfopen_s(&fp, widePath.c_str(), L"rb") == 0 && fp != nullptr) {
            std::fseek(fp, 0, SEEK_END);
            const long size = std::ftell(fp);
            std::fseek(fp, 0, SEEK_SET);
            if (size > 0) {
                std::vector<unsigned char> raw(static_cast<size_t>(size));
                const size_t readCount = std::fread(raw.data(), 1, raw.size(), fp);
                if (readCount == raw.size()) {
                    decoded = cv::imdecode(raw, cv::IMREAD_UNCHANGED);
                }
            }
            std::fclose(fp);
        }
    }
#endif
    if (decoded.empty()) {
        decoded = cv::imread(path, cv::IMREAD_UNCHANGED);
    }
    if (decoded.empty()) {
        return {};
    }
    return PrepareDecodedImageForFlow(decoded);
}

static std::vector<std::string> ResolveFileList(const Json& props, ExecutionContext* ctx) {
    std::vector<std::string> files;

    // 1) 上下文优先
    if (ctx != nullptr) {
        const char* keys[] = { "frontend_selected_image_path", "selected_image_path", "img_path", "frontend_image_path" };
        for (const char* k : keys) {
            try {
                const std::string v = ctx->Get<std::string>(k, std::string());
                if (!v.empty()) { files.push_back(v); break; }
            } catch (...) {}
        }
    }

    // 2) 节点属性
    try {
        if (props.is_object()) {
            if (props.contains("path") && props.at("path").is_string()) {
                const std::string s = props.at("path").get<std::string>();
                if (!s.empty()) files.push_back(s);
            }
            if (props.contains("paths") && props.at("paths").is_array()) {
                for (const auto& one : props.at("paths")) {
                    if (!one.is_null()) {
                        const std::string s = one.is_string() ? one.get<std::string>() : one.dump();
                        if (!s.empty()) files.push_back(s);
                    }
                }
            }
        }
    } catch (...) {}

    return files;
}

static bool TryBuildContextBatchImages(
    ExecutionContext* ctx,
    std::vector<ModuleImage>& images,
    Json& results,
    bool includeFilepathField) {
    if (ctx == nullptr) return false;

    std::vector<cv::Mat> matsFromContext;
    try {
        matsFromContext = ctx->Get<std::vector<cv::Mat>>("frontend_image_mats", std::vector<cv::Mat>());
    } catch (...) {
        matsFromContext.clear();
    }
    if (matsFromContext.empty()) {
        try {
            matsFromContext = ctx->Get<std::vector<cv::Mat>>("frontend_image_mat_list", std::vector<cv::Mat>());
        } catch (...) {
            matsFromContext.clear();
        }
    }
    if (matsFromContext.empty()) return false;

    int ctxIndex = 0;
    for (const auto& mat : matsFromContext) {
        if (mat.empty()) {
            ctxIndex += 1;
            continue;
        }

        TransformationState st(mat.cols, mat.rows);
        ModuleImage wrap(mat, mat, st, ctxIndex);
        images.push_back(wrap);

        Json entry = Json::object();
        entry["type"] = "local";
        entry["index"] = ctxIndex;
        entry["origin_index"] = ctxIndex;
        entry["transform"] = st.ToJson();
        entry["sample_results"] = Json::array();
        entry["filename"] = std::string("frontend_mat_") + std::to_string(ctxIndex);
        if (includeFilepathField) {
            entry["filepath"] = "";
        }
        results.push_back(entry);
        ctxIndex += 1;
    }

    return !images.empty();
}

/// input/image
class InputImageModule final : public BaseInputModule {
public:
    using BaseInputModule::BaseInputModule;

    ModuleIO Generate() override {
        std::vector<ModuleImage> images;
        Json results = Json::array();

        // 优先从 ExecutionContext 注入的前端 Mat 列表读取
        try {
            if (TryBuildContextBatchImages(Context, images, results, true)) {
                try { ScalarOutputsByName["filename"] = "frontend_mat"; } catch (...) {}
                return ModuleIO(std::move(images), std::move(results), Json::array());
            }
        } catch (...) {}

        // 优先从 ExecutionContext 注入的前端 Mat 读取（接口约定为 RGB）
        try {
            if (Context != nullptr && Context->Has("frontend_image_mat")) {
                cv::Mat matFromContext = Context->Get<cv::Mat>("frontend_image_mat", cv::Mat());
                if (!matFromContext.empty()) {
                    cv::Mat frontendMat = matFromContext;
                    TransformationState st(frontendMat.cols, frontendMat.rows);
                    ModuleImage wrap(frontendMat, frontendMat, st, 0);
                    images.push_back(wrap);

                    Json entry = Json::object();
                    entry["type"] = "local";
                    entry["index"] = 0;
                    entry["origin_index"] = 0;
                    entry["transform"] = st.ToJson();
                    entry["sample_results"] = Json::array();
                    entry["filename"] = "frontend_mat";
                    entry["filepath"] = "";
                    results.push_back(entry);

                    try { ScalarOutputsByName["filename"] = "frontend_mat"; } catch (...) {}
                    return ModuleIO(std::move(images), std::move(results), Json::array());
                }
            }
        } catch (...) {}

        const auto files = ResolveFileList(Properties, Context);
        int index = 0;
        for (const auto& file : files) {
            try {
                if (!FileExists(file)) continue;
                cv::Mat rgb = ReadFromPathForFlow(file);
                if (rgb.empty()) continue;

                TransformationState st(rgb.cols, rgb.rows);
                ModuleImage wrap(rgb, rgb, st, index);
                images.push_back(wrap);

                Json entry = Json::object();
                entry["type"] = "local";
                entry["index"] = index;
                entry["origin_index"] = index;
                entry["transform"] = st.ToJson();
                entry["sample_results"] = Json::array();
                entry["filename"] = GetFileNameWithoutExt(file);
                entry["filepath"] = file;
                results.push_back(entry);
                index += 1;
            } catch (...) {
                // skip bad image
            }
        }

        try {
            if (images.size() == 1 && results.size() == 1 && results[0].is_object() && results[0].contains("filename")) {
                const std::string fn = results[0].at("filename").is_string() ? results[0].at("filename").get<std::string>() : std::string();
                if (!fn.empty()) ScalarOutputsByName["filename"] = fn;
            }
        } catch (...) {}

        return ModuleIO(std::move(images), std::move(results), Json::array());
    }
};

/// input/frontend_image
class InputFrontendImageModule final : public BaseInputModule {
public:
    using BaseInputModule::BaseInputModule;

    ModuleIO Generate() override {
        std::vector<ModuleImage> images;
        Json results = Json::array();

        // 优先从 ExecutionContext 注入的前端 Mat 列表读取
        try {
            if (TryBuildContextBatchImages(Context, images, results, false)) {
                return ModuleIO(std::move(images), std::move(results), Json::array());
            }
        } catch (...) {}

        // 优先从 ExecutionContext 注入前端图像 Mat（接口约定为 RGB）
        try {
            if (Context != nullptr && Context->Has("frontend_image_mat")) {
                cv::Mat matFromContext = Context->Get<cv::Mat>("frontend_image_mat", cv::Mat());
                if (!matFromContext.empty()) {
                    cv::Mat frontendMat = matFromContext;
                    TransformationState st(frontendMat.cols, frontendMat.rows);
                    ModuleImage wrap(frontendMat, frontendMat, st, 0);
                    images.push_back(wrap);

                    Json entry = Json::object();
                    entry["type"] = "local";
                    entry["index"] = 0;
                    entry["origin_index"] = 0;
                    entry["transform"] = st.ToJson();
                    entry["sample_results"] = Json::array();
                    entry["filename"] = "frontend_mat";
                    results.push_back(entry);
                    return ModuleIO(std::move(images), std::move(results), Json::array());
                }
            }
        } catch (...) {}

        // 回退到从路径读取，并统一整理为 Flow 约定的输入通道语义。
        std::string path;
        try {
            if (Properties.is_object() && Properties.contains("path") && Properties.at("path").is_string()) {
                path = Properties.at("path").get<std::string>();
            }
        } catch (...) {}
        if (path.empty()) {
            try { if (Context) path = Context->Get<std::string>("frontend_image_path", std::string()); } catch (...) {}
        }
        if (path.empty() || !FileExists(path)) {
            return ModuleIO(std::move(images), std::move(results), Json::array());
        }

        try {
            cv::Mat rgb = ReadFromPathForFlow(path);
            if (!rgb.empty()) {
                TransformationState st(rgb.cols, rgb.rows);
                ModuleImage wrap(rgb, rgb, st, 0);
                images.push_back(wrap);

                Json entry = Json::object();
                entry["type"] = "local";
                entry["index"] = 0;
                entry["origin_index"] = 0;
                entry["transform"] = st.ToJson();
                entry["sample_results"] = Json::array();
                entry["filename"] = path;
                results.push_back(entry);
            }
        } catch (...) {}

        return ModuleIO(std::move(images), std::move(results), Json::array());
    }
};

/// input/build_results
class InputBuildResultsModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& /*resultList*/) override {
        std::vector<ModuleImage> outImages;
        Json outResults = Json::array();

        ModuleImage used;
        cv::Mat img;
        int w = 0, h = 0;

        if (!imageList.empty() && !imageList[0].ImageObject.empty()) {
            used = imageList[0];
            img = used.ImageObject;
            w = img.cols; h = img.rows;
        } else {
            // 属性/上下文路径 > 纯色图
            std::string imagePath;
            try { if (Properties.is_object() && Properties.contains("image_path")) imagePath = Properties.at("image_path").get<std::string>(); } catch (...) {}
            if (imagePath.empty() && Context != nullptr) {
                const char* keys[] = { "frontend_selected_image_path", "selected_image_path", "img_path", "frontend_image_path" };
                for (const char* k : keys) {
                    try {
                        imagePath = Context->Get<std::string>(k, std::string());
                        if (!imagePath.empty()) break;
                    } catch (...) {}
                }
            }

            if (!imagePath.empty() && FileExists(imagePath)) {
                try {
                    img = ReadFromPathForFlow(imagePath);
                    if (!img.empty()) {
                        w = img.cols; h = img.rows;
                        TransformationState st(w, h);
                        used = ModuleImage(img, img, st, 0);
                    }
                } catch (...) { img.release(); }
            }
        }

        if (img.empty()) {
            int dw = 640, dh = 640;
            std::string colorStr = "0,255,0";
            try { dw = ReadInt("default_width", 640); } catch (...) {}
            try { dh = ReadInt("default_height", 640); } catch (...) {}
            try { colorStr = ReadString("default_color", "0,255,0"); } catch (...) {}
            int r = 0, g = 255, b = 0;
            try {
                int rr = 0, gg = 255, bb = 0;
#ifdef _WIN32
                sscanf_s(colorStr.c_str(), "%d,%d,%d", &rr, &gg, &bb);
#else
                sscanf(colorStr.c_str(), "%d,%d,%d", &rr, &gg, &bb);
#endif
                r = rr; g = gg; b = bb;
            } catch (...) { r = 0; g = 255; b = 0; }
            img = cv::Mat(dh, dw, CV_8UC3, cv::Scalar(r, g, b));
            w = dw; h = dh;
            TransformationState st(w, h);
            used = ModuleImage(img, img, st, 0);
        }

        outImages.push_back(used);

        int categoryId = ReadInt("category_id", 0);
        std::string categoryName = ReadString("category_name", std::string("测试对象"));
        double score = ReadDouble("score", 0.95);
        const bool withAngle = ReadBool("with_angle", false);
        const double angle = ReadDouble("angle", -100.0);

        // 兼容属性输入为 XYXY（bbox_x1~bbox_y2）或 XYWH（bbox_x/bbox_y/bbox_w/bbox_h）
        double x1 = ReadDouble("bbox_x1", 100.0);
        double y1 = ReadDouble("bbox_y1", 100.0);
        double x2 = ReadDouble("bbox_x2", 300.0);
        double y2 = ReadDouble("bbox_y2", 300.0);
        double bx = std::numeric_limits<double>::quiet_NaN();
        double by = std::numeric_limits<double>::quiet_NaN();
        double bwProp = std::numeric_limits<double>::quiet_NaN();
        double bhProp = std::numeric_limits<double>::quiet_NaN();
        try { if (Properties.is_object() && Properties.contains("bbox_x")) bx = Properties.at("bbox_x").get<double>(); } catch (...) {}
        try { if (Properties.is_object() && Properties.contains("bbox_y")) by = Properties.at("bbox_y").get<double>(); } catch (...) {}
        try { if (Properties.is_object() && Properties.contains("bbox_w")) bwProp = Properties.at("bbox_w").get<double>(); } catch (...) {}
        try { if (Properties.is_object() && Properties.contains("bbox_h")) bhProp = Properties.at("bbox_h").get<double>(); } catch (...) {}
        double cxProp = std::numeric_limits<double>::quiet_NaN();
        double cyProp = std::numeric_limits<double>::quiet_NaN();
        try { if (Properties.is_object() && Properties.contains("bbox_cx")) cxProp = Properties.at("bbox_cx").get<double>(); } catch (...) {}
        try { if (Properties.is_object() && Properties.contains("bbox_cy")) cyProp = Properties.at("bbox_cy").get<double>(); } catch (...) {}

        if (withAngle && !std::isnan(cxProp) && !std::isnan(cyProp) && !std::isnan(bwProp) && !std::isnan(bhProp)) {
            // 旋转框测试入口：bbox 使用 [cx, cy, w, h]，angle 为弧度。
        } else if (!std::isnan(bx) && !std::isnan(by) && !std::isnan(bwProp) && !std::isnan(bhProp)) {
            x1 = bx; y1 = by;
            x2 = bx + std::abs(bwProp);
            y2 = by + std::abs(bhProp);
        } else {
            if (x2 < x1) std::swap(x1, x2);
            if (y2 < y1) std::swap(y1, y2);
        }

        x1 = std::max(0.0, std::min(x1, static_cast<double>(w)));
        y1 = std::max(0.0, std::min(y1, static_cast<double>(h)));
        x2 = std::max(0.0, std::min(x2, static_cast<double>(w)));
        y2 = std::max(0.0, std::min(y2, static_cast<double>(h)));

        const double bw = std::max(1.0, x2 - x1);
        const double bh = std::max(1.0, y2 - y1);

        Json det = Json::object();
        det["category_id"] = categoryId;
        det["category_name"] = categoryName;
        det["score"] = score;
        det["with_bbox"] = true;
        if (withAngle && !std::isnan(cxProp) && !std::isnan(cyProp) && !std::isnan(bwProp) && !std::isnan(bhProp)) {
            det["bbox"] = Json::array({ cxProp, cyProp, std::max(1.0, std::abs(bwProp)), std::max(1.0, std::abs(bhProp)) });
            det["with_angle"] = true;
            det["angle"] = angle;
        } else {
            det["bbox"] = Json::array({ x1, y1, bw, bh });
            det["with_angle"] = false;
            det["angle"] = -100.0;
        }

        Json entry = Json::object();
        entry["type"] = "local";
        entry["originating_module"] = "input/build_results";
        entry["sample_results"] = Json::array({ det });
        entry["index"] = 0;
        entry["origin_index"] = used.OriginalIndex;
        entry["transform"] = used.TransformState.ToJson();

        outResults.push_back(entry);
        return ModuleIO(std::move(outImages), std::move(outResults), Json::array());
    }
};

// 注册
DLCV_FLOW_REGISTER_MODULE("input/image", InputImageModule)
DLCV_FLOW_REGISTER_MODULE("input/frontend_image", InputFrontendImageModule)
DLCV_FLOW_REGISTER_MODULE("input/build_results", InputBuildResultsModule)

} // namespace flow
} // namespace dlcv_infer

