#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <string>
#include <vector>

#include "json/json.hpp"
#include "opencv2/core.hpp"

namespace dlcv_infer {
namespace flow {

using Json = nlohmann::json;

/// <summary>
/// 表示从 Original -> Current 的几何变换状态（仿射 2x3）以及尺寸信息。
/// 对齐 OpenIVS/DlcvCsharpApi/Base.cs 中的 TransformationState。
/// </summary>
struct TransformationState final {
    int OriginalWidth = 0;
    int OriginalHeight = 0;
    std::vector<int> CropBox;                 // [x,y,w,h] or empty
    std::vector<double> AffineMatrix2x3;      // len=6, original -> current; empty means identity
    std::vector<int> OutputSize;              // [w,h] or empty

    TransformationState() = default;
    TransformationState(int originalW, int originalH)
        : OriginalWidth(originalW), OriginalHeight(originalH) {}

    TransformationState Clone() const { return *this; }

    static std::vector<double> To3x3(const std::vector<double>& a2x3) {
        if (a2x3.size() != 6) {
            return { 1,0,0, 0,1,0, 0,0,1 };
        }
        return { a2x3[0], a2x3[1], a2x3[2],
                 a2x3[3], a2x3[4], a2x3[5],
                 0, 0, 1 };
    }

    static std::vector<double> To2x3(const std::vector<double>& a3x3) {
        if (a3x3.size() != 9) {
            return { 1,0,0, 0,1,0 };
        }
        return { a3x3[0], a3x3[1], a3x3[2],
                 a3x3[3], a3x3[4], a3x3[5] };
    }

    static std::vector<double> Mul3x3(const std::vector<double>& A, const std::vector<double>& B) {
        // C = A * B
        if (A.size() != 9 || B.size() != 9) {
            return { 1,0,0, 0,1,0, 0,0,1 };
        }
        std::vector<double> C(9, 0.0);
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                C[r * 3 + c] =
                    A[r * 3 + 0] * B[0 * 3 + c] +
                    A[r * 3 + 1] * B[1 * 3 + c] +
                    A[r * 3 + 2] * B[2 * 3 + c];
            }
        }
        return C;
    }

    static std::vector<double> Inverse2x3(const std::vector<double>& a2x3) {
        if (a2x3.size() != 6) {
            return { 1,0,0, 0,1,0 };
        }
        const double a = a2x3[0], b = a2x3[1], tx = a2x3[2];
        const double c = a2x3[3], d = a2x3[4], ty = a2x3[5];
        const double det = a * d - b * c;
        if (std::abs(det) < 1e-12) {
            return { 1,0,0, 0,1,0 };
        }
        const double invDet = 1.0 / det;
        const double ia = d * invDet;
        const double ib = -b * invDet;
        const double ic = -c * invDet;
        const double id = a * invDet;
        const double itx = -(ia * tx + ib * ty);
        const double ity = -(ic * tx + id * ty);
        return { ia, ib, itx, ic, id, ity };
    }

    /// <summary>
    /// 由当前状态派生子状态：current->new 的 2x3 与 original->current 复合为 original->new。
    /// </summary>
    TransformationState DeriveChild(const std::vector<double>& currentToNew2x3, int newWidth, int newHeight) const {
        std::vector<double> parent2x3 = (AffineMatrix2x3.size() == 6) ? AffineMatrix2x3 : std::vector<double>{ 1,0,0, 0,1,0 };
        std::vector<double> child2x3 = (currentToNew2x3.size() == 6) ? currentToNew2x3 : std::vector<double>{ 1,0,0, 0,1,0 };

        const std::vector<double> parent3x3 = To3x3(parent2x3);
        const std::vector<double> child3x3 = To3x3(child2x3);
        const std::vector<double> composed3x3 = Mul3x3(child3x3, parent3x3); // original -> new

        TransformationState out;
        out.OriginalWidth = OriginalWidth;
        out.OriginalHeight = OriginalHeight;
        out.CropBox = CropBox;
        out.AffineMatrix2x3 = To2x3(composed3x3);
        out.OutputSize = { newWidth, newHeight };
        return out;
    }

    Json ToJson() const {
        Json d = Json::object();
        d["original_width"] = OriginalWidth;
        d["original_height"] = OriginalHeight;
        if (CropBox.size() >= 4) d["crop_box"] = CropBox;
        if (AffineMatrix2x3.size() == 6) d["affine_2x3"] = AffineMatrix2x3;
        if (OutputSize.size() >= 2) d["output_size"] = OutputSize;
        return d;
    }

    static TransformationState FromJson(const Json& d) {
        TransformationState st;
        if (!d.is_object()) return st;
        try { st.OriginalWidth = d.value("original_width", 0); } catch (...) { st.OriginalWidth = 0; }
        try { st.OriginalHeight = d.value("original_height", 0); } catch (...) { st.OriginalHeight = 0; }
        try {
            if (d.contains("crop_box") && d["crop_box"].is_array() && d["crop_box"].size() >= 4) {
                st.CropBox.clear();
                for (size_t i = 0; i < 4; i++) st.CropBox.push_back(d["crop_box"][i].get<int>());
            }
        } catch (...) { st.CropBox.clear(); }
        try {
            if (d.contains("affine_2x3") && d["affine_2x3"].is_array() && d["affine_2x3"].size() >= 6) {
                st.AffineMatrix2x3.clear();
                for (size_t i = 0; i < 6; i++) st.AffineMatrix2x3.push_back(d["affine_2x3"][i].get<double>());
            }
        } catch (...) { st.AffineMatrix2x3.clear(); }
        try {
            if (d.contains("output_size") && d["output_size"].is_array() && d["output_size"].size() >= 2) {
                st.OutputSize = { d["output_size"][0].get<int>(), d["output_size"][1].get<int>() };
            }
        } catch (...) { st.OutputSize.clear(); }
        return st;
    }
};

/// <summary>
/// 携带图像对象与变换状态的包装器
/// </summary>
struct ModuleImage final {
    cv::Mat ImageObject;          // 当前图（可能是裁剪/旋转后的）
    cv::Mat OriginalImage;        // 原图（用于回写到原图坐标）
    TransformationState TransformState;
    int OriginalIndex = 0;

    ModuleImage() = default;
    ModuleImage(const cv::Mat& imageObject, const cv::Mat& originalImage, const TransformationState& ts, int originalIndex = 0)
        : ImageObject(imageObject), OriginalImage(originalImage.empty() ? imageObject : originalImage), TransformState(ts), OriginalIndex(originalIndex) {}

    const cv::Mat& GetImage() const { return ImageObject; }

    Json ToMeta() const {
        Json m = Json::object();
        m["origin_index"] = OriginalIndex;
        m["transform"] = TransformState.ToJson();
        return m;
    }
};

/// <summary>
/// 模块输入/输出对（便于承载额外通道）。TemplateList 使用 JSON 数组承载，避免强耦合。
/// </summary>
struct ModuleChannel final {
    std::vector<ModuleImage> ImageList;
    Json ResultList = Json::array();
    Json TemplateList = Json::array();

    ModuleChannel() = default;
    ModuleChannel(std::vector<ModuleImage> images, Json results, Json templates = Json::array())
        : ImageList(std::move(images)), ResultList(std::move(results)), TemplateList(std::move(templates)) {}
};

/// <summary>
/// 模块 I/O：统一强类型图像序列与 JSON 结果序列
/// </summary>
struct ModuleIO final {
    std::vector<ModuleImage> ImageList;
    Json ResultList = Json::array();
    Json TemplateList = Json::array();

    ModuleIO() = default;
    ModuleIO(std::vector<ModuleImage> images, Json results, Json templates = Json::array())
        : ImageList(std::move(images)), ResultList(std::move(results)), TemplateList(std::move(templates)) {}
};

inline int ClampToInt(double v, int dv = 0) {
    if (std::isnan(v) || std::isinf(v)) return dv;
    if (v > static_cast<double>(INT32_MAX)) return INT32_MAX;
    if (v < static_cast<double>(INT32_MIN)) return INT32_MIN;
    return static_cast<int>(std::llround(v));
}

} // namespace flow
} // namespace dlcv_infer

