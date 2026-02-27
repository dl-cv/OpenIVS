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

#include "opencv2/imgcodecs.hpp"

namespace dlcv_infer {
namespace flow {

static std::string GetFileNameWithoutExt(const std::string& path) {
    size_t pos = path.find_last_of("\\/");
    std::string name = (pos == std::string::npos) ? path : path.substr(pos + 1);
    size_t dot = name.find_last_of('.');
    if (dot == std::string::npos) return name;
    return name.substr(0, dot);
}

static bool FileExists(const std::string& path) {
    std::ifstream ifs(path, std::ios::binary);
    return static_cast<bool>(ifs);
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

/// input/image
class InputImageModule final : public BaseInputModule {
public:
    using BaseInputModule::BaseInputModule;

    ModuleIO Generate() override {
        std::vector<ModuleImage> images;
        Json results = Json::array();

        // 优先从 ExecutionContext 注入的前端 BGR Mat 读取
        try {
            if (Context != nullptr && Context->Has("frontend_image_mat")) {
                cv::Mat matFromContext = Context->Get<cv::Mat>("frontend_image_mat", cv::Mat());
                if (!matFromContext.empty()) {
                    cv::Mat bgr = matFromContext;
                    TransformationState st(bgr.cols, bgr.rows);
                    ModuleImage wrap(bgr, bgr, st, 0);
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
                cv::Mat bgr = cv::imread(file, cv::IMREAD_COLOR);
                if (bgr.empty()) continue;

                TransformationState st(bgr.cols, bgr.rows);
                ModuleImage wrap(bgr, bgr, st, index);
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

        // 优先从 ExecutionContext 注入前端图像 Mat（BGR）
        try {
            if (Context != nullptr && Context->Has("frontend_image_mat")) {
                cv::Mat matFromContext = Context->Get<cv::Mat>("frontend_image_mat", cv::Mat());
                if (!matFromContext.empty()) {
                    cv::Mat bgr = matFromContext;
                    TransformationState st(bgr.cols, bgr.rows);
                    ModuleImage wrap(bgr, bgr, st, 0);
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

        // 回退到从路径读取（BGR）
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
            cv::Mat bgr = cv::imread(path, cv::IMREAD_COLOR);
            if (!bgr.empty()) {
                TransformationState st(bgr.cols, bgr.rows);
                ModuleImage wrap(bgr, bgr, st, 0);
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
                    img = cv::imread(imagePath, cv::IMREAD_COLOR);
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
                sscanf_s(colorStr.c_str(), "%d,%d,%d", &rr, &gg, &bb);
                r = rr; g = gg; b = bb;
            } catch (...) { r = 0; g = 255; b = 0; }
            img = cv::Mat(dh, dw, CV_8UC3, cv::Scalar(b, g, r));
            w = dw; h = dh;
            TransformationState st(w, h);
            used = ModuleImage(img, img, st, 0);
        }

        outImages.push_back(used);

        int categoryId = ReadInt("category_id", 0);
        std::string categoryName = ReadString("category_name", std::string("测试对象"));
        double score = ReadDouble("score", 0.95);

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

        if (!std::isnan(bx) && !std::isnan(by) && !std::isnan(bwProp) && !std::isnan(bhProp)) {
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
        det["bbox"] = Json::array({ x1, y1, bw, bh });
        det["with_bbox"] = true;
        det["with_angle"] = false;
        det["angle"] = -100.0;

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

