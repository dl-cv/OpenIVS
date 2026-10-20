#include "flow/BaseModule.h"
#include "flow/ModuleRegistry.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <iomanip>
#include <limits>
#include <locale>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace dlcv_infer {
namespace flow {
namespace {

int AlgorithmIndex(const Json& value, const char* name, bool positive = false) {
    if (!value.is_number_integer()) throw std::invalid_argument(std::string(name) + " 必须为整数");
    const double number = value.get<double>();
    if (number < (positive ? 1 : 0) || number > (std::numeric_limits<int>::max)())
        throw std::invalid_argument(std::string(name) + " 超出有效范围");
    return static_cast<int>(number);
}

// Python 的 score / rules.value 接受数值字符串，但不接受 bool 或非有限值。
double AlgorithmNumber(const Json& value) {
    double number;
    if (value.is_number()) number = value.get<double>();
    else if (value.is_string()) {
        std::istringstream stream(value.get<std::string>());
        stream.imbue(std::locale::classic());
        if (!(stream >> number)) throw std::invalid_argument("必须为有限数值");
        stream >> std::ws;
        if (!stream.eof()) throw std::invalid_argument("必须为有限数值");
    } else throw std::invalid_argument("必须为有限数值");
    if (!std::isfinite(number)) throw std::invalid_argument("必须为有限数值");
    return number;
}

std::vector<double> AlgorithmInverse(const Json& entry, const ModuleImage& original) {
    if (!entry.contains("transform") || entry.at("transform").is_null()) return {1, 0, 0, 0, 1, 0};
    const auto& transform = entry.at("transform");
    if (!transform.is_object() || !transform.contains("original_width") || !transform.contains("original_height"))
        throw std::invalid_argument("transform 缺少原图尺寸");
    if (AlgorithmIndex(transform.at("original_width"), "transform.original_width", true) != original.OriginalImage.cols ||
        AlgorithmIndex(transform.at("original_height"), "transform.original_height", true) != original.OriginalImage.rows)
        throw std::invalid_argument("transform 原图尺寸不一致");
    for (const auto& field : {"output_size", "crop_box"}) {
        if (!transform.contains(field)) continue;
        const auto& values = transform.at(field);
        const size_t count = std::string(field) == "output_size" ? 2 : 4;
        if (!values.is_array() || values.size() != count) throw std::invalid_argument("transform 尺寸格式无效");
        for (size_t i = 0; i < count; ++i) {
            // 与 Python read_state 一致：裁剪起点非负，所有宽高严格为正。
            AlgorithmIndex(values[i], field, !(count == 4 && i < 2));
        }
    }
    std::vector<double> affine{1, 0, 0, 0, 1, 0};
    if (transform.contains("affine_2x3")) {
        const auto& values = transform.at("affine_2x3");
        if (!values.is_array() || values.size() != 6) throw std::invalid_argument("transform.affine_2x3 必须包含六个数值");
        for (size_t i = 0; i < 6; ++i) {
            if (!values[i].is_number()) throw std::invalid_argument("transform.affine_2x3 必须为数值数组");
            affine[i] = AlgorithmNumber(values[i]);
        }
    }
    const double determinant = affine[0] * affine[4] - affine[1] * affine[3];
    if (!std::isfinite(determinant) || std::abs(determinant) < 1e-12)
        throw std::invalid_argument("transform.affine_2x3 不可逆");
    // C++ 的 affine_2x3 已包含裁剪平移，与 OutputModules::BuildTC2O 一致。
    auto inverse = TransformationState::Inverse2x3(affine);
    for (double value : inverse)
        if (!std::isfinite(value)) throw std::invalid_argument("transform 逆变换不是有限数值");
    return inverse;
}

bool SameAlgorithmOriginal(const cv::Mat& left, const cv::Mat& right) {
    // cv::Mat 按值传递但共享原图存储；不按像素内容、OriginalIndex 或 transform 合并。
    return left.data == right.data && left.rows == right.rows && left.cols == right.cols &&
        left.type() == right.type() && left.step[0] == right.step[0];
}

struct AlgorithmDetection {
    const Json* Value;
    std::vector<double> Inverse;
};

struct AlgorithmGroup {
    ModuleImage Original;
    std::vector<AlgorithmDetection> Detections;
};

size_t AlgorithmSelectGroup(const std::vector<ModuleImage>& images, const std::vector<size_t>& groups, const Json& entry) {
    int index = -1, origin = -1;
    if (entry.contains("index") && !entry.at("index").is_null()) index = AlgorithmIndex(entry.at("index"), "index");
    if (entry.contains("image_index") && !entry.at("image_index").is_null()) {
        const int alias = AlgorithmIndex(entry.at("image_index"), "image_index");
        if (index >= 0 && index != alias) throw std::invalid_argument("index 与 image_index 冲突");
        index = alias;
    }
    if (entry.contains("origin_index") && !entry.at("origin_index").is_null())
        origin = AlgorithmIndex(entry.at("origin_index"), "origin_index");
    if (index >= 0 && static_cast<size_t>(index) < images.size()) {
        // C++ MergeResults 按值重包并同步更新 entry，不会原位覆盖共享 wrapper 的索引。
        if (origin >= 0 && images[static_cast<size_t>(index)].OriginalIndex != origin)
            throw std::invalid_argument("index 与 origin_index 指向不同原图");
        return groups[static_cast<size_t>(index)];
    }
    size_t candidate = images.size();
    if (origin >= 0 || index < 0) {
        for (size_t i = 0; i < images.size(); ++i) {
            if (origin >= 0 && images[i].OriginalIndex != origin) continue;
            if (candidate != images.size() && candidate != groups[i])
                throw std::invalid_argument("无法确定结果所属原图");
            candidate = groups[i];
        }
    }
    if (candidate == images.size()) throw std::invalid_argument("无法确定结果所属原图");
    return candidate;
}

std::vector<AlgorithmGroup> AlgorithmGroups(const std::vector<ModuleImage>& images, const Json& entries, bool allowNullDetections) {
    std::vector<AlgorithmGroup> groups;
    std::vector<size_t> imageGroups;
    for (const auto& image : images) {
        const auto& original = image.OriginalImage;
        if (original.empty() || original.dims != 2) throw std::invalid_argument("OriginalImage 必须为非空二维图像");
        if (image.OriginalIndex < 0) throw std::invalid_argument("OriginalIndex 必须为非负整数");
        size_t group = 0;
        for (; group < groups.size(); ++group)
            if (SameAlgorithmOriginal(original, groups[group].Original.OriginalImage)) break;
        if (group == groups.size()) groups.push_back({ModuleImage(original, original,
            TransformationState(original.cols, original.rows), image.OriginalIndex), {}});
        imageGroups.push_back(group);
    }
    if (entries.is_null()) return groups;
    if (!entries.is_array()) throw std::invalid_argument("result_list 必须为数组");
    for (const auto& entry : entries) {
        if (!entry.is_object()) throw std::invalid_argument("result_list 必须包含对象");
        if (!entry.contains("type") || entry.at("type") != "local") continue;
        auto& group = groups.at(AlgorithmSelectGroup(images, imageGroups, entry));
        const auto inverse = AlgorithmInverse(entry, group.Original);
        if (!entry.contains("sample_results")) continue;
        const auto& detections = entry.at("sample_results");
        if (allowNullDetections && detections.is_null()) continue;
        if (!detections.is_array()) throw std::invalid_argument("sample_results 必须为数组");
        for (const auto& detection : detections) {
            if (!detection.is_object()) throw std::invalid_argument("sample_results 必须包含对象");
            group.Detections.push_back({&detection, inverse});
        }
    }
    return groups;
}

std::array<double, 2> AlgorithmCenter(const AlgorithmDetection& detection) {
    const auto& value = *detection.Value;
    if (!value.contains("bbox")) throw std::invalid_argument("检测结果缺少 bbox");
    const auto& bbox = value.at("bbox");
    std::array<double, 5> values{};
    bool rotated = false, xyxy = false;
    if (bbox.is_array() && (bbox.size() == 4 || bbox.size() == 5)) {
        for (size_t i = 0; i < bbox.size(); ++i) values[i] = AlgorithmNumber(bbox[i]);
        rotated = bbox.size() == 5;
        if (!rotated && value.contains("with_angle") && value.at("with_angle") == true) {
            if (!value.contains("angle")) throw std::invalid_argument("旋转框缺少 angle");
            values[4] = AlgorithmNumber(value.at("angle"));
            rotated = values[4] > -99.0;
        }
    } else if (bbox.is_object()) {
        xyxy = bbox.contains("x1") && bbox.contains("y1") && bbox.contains("x2") && bbox.contains("y2");
        const std::array<const char*, 4> keys = xyxy ? std::array<const char*, 4>{"x1", "y1", "x2", "y2"} :
            std::array<const char*, 4>{"cx", "cy", "w", "h"};
        for (size_t i = 0; i < keys.size(); ++i) {
            if (!bbox.contains(keys[i])) throw std::invalid_argument("bbox 格式无效");
            values[i] = AlgorithmNumber(bbox.at(keys[i]));
        }
        rotated = !xyxy;
        if (rotated) values[4] = AlgorithmNumber(bbox.value("angle", bbox.value("theta", Json(0.0))));
    } else throw std::invalid_argument("bbox 必须包含 4/5 项坐标");
    if (xyxy ? (values[2] <= values[0] || values[3] <= values[1]) : (values[2] <= 0 || values[3] <= 0))
        throw std::invalid_argument("bbox 宽高必须大于零");
    // 保持 double 中心，不经过整数外接框或 float32 角点。
    const double x = xyxy ? values[0] / 2 + values[2] / 2 : (rotated ? values[0] : values[0] + values[2] / 2);
    const double y = xyxy ? values[1] / 2 + values[3] / 2 : (rotated ? values[1] : values[1] + values[3] / 2);
    const auto& t = detection.Inverse;
    const std::array<double, 2> center{t[0] * x + t[1] * y + t[2], t[3] * x + t[4] * y + t[5]};
    if (!std::isfinite(center[0]) || !std::isfinite(center[1])) throw std::invalid_argument("bbox 原图中心必须有限");
    return center;
}

void AppendAlgorithmClassification(ModuleIO& output, const AlgorithmGroup& group, bool ok, const std::string& label,
    const std::string& reason, bool distance) {
    const auto& image = group.Original;
    const int width = image.OriginalImage.cols, height = image.OriginalImage.rows;
    const Json bbox = distance ? Json::array({width / 2.0, height / 2.0, double(width), double(height), 0.0}) :
        Json::array({0, 0, width, height});
    output.ResultList.push_back({{"type", "local"},
        {"originating_module", distance ? "post_process/offset_distance" : "post_process/sequence_check"},
        {"index", output.ImageList.size()}, {"origin_index", image.OriginalIndex},
        {"transform", image.TransformState.ToJson()}, {"ok", ok}, {"reason", reason},
        {"sample_results", Json::array({{{"category_name", label}, {"category_id", ok ? 0 : 1}, {"score", 1.0}, {"bbox", bbox}}})}});
    output.ImageList.push_back(image);
}

std::string AlgorithmSequenceText(const Json& sequence) {
    if (sequence.empty()) return "（空）";
    std::string text;
    for (size_t i = 0; i < sequence.size(); ++i) {
        if (i != 0) text += " → ";
        text += sequence[i].get<std::string>();
    }
    return text;
}

class SequenceCheckModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const Json categories = Properties.value("categories", Json::array());
        const Json direction = Properties.value("direction", Json("horizontal"));
        if (!categories.is_array()) throw std::invalid_argument("categories 必须为有序字符串列表");
        for (const auto& name : categories)
            if (!name.is_string()) throw std::invalid_argument("categories 必须为有序字符串列表");
        if (direction != "horizontal" && direction != "vertical") throw std::invalid_argument("direction 必须为 horizontal 或 vertical");
        const size_t axis = direction == "horizontal" ? 0 : 1;
        ModuleIO output;
        for (const auto& group : AlgorithmGroups(imageList, resultList, true)) {
            std::vector<std::pair<double, std::string>> ordered;
            for (const auto& detection : group.Detections) {
                const auto& value = *detection.Value;
                if (!value.contains("category_name") || !value.at("category_name").is_string())
                    throw std::invalid_argument("检测结果必须包含字符串 category_name");
                ordered.emplace_back(AlgorithmCenter(detection)[axis], value.at("category_name").get<std::string>());
            }
            std::stable_sort(ordered.begin(), ordered.end(), [](const auto& a, const auto& b) { return a.first < b.first; });
            Json sequence = Json::array();
            for (const auto& item : ordered) sequence.push_back(item.second);
            const bool ok = sequence == categories;
            const std::string reason = "期望顺序：" + AlgorithmSequenceText(categories) +
                "\n当前顺序：" + AlgorithmSequenceText(sequence);
            AppendAlgorithmClassification(output, group, ok, ok ? "OK" : "NG", reason, false);
        }
        return output;
    }
};

class OffsetDistanceModule final : public BaseModule {
public:
    using BaseModule::BaseModule;

    ModuleIO Process(const std::vector<ModuleImage>& imageList, const Json& resultList) override {
        const Json rules = Properties.value("rules", Json::array());
        if (!rules.is_array()) throw std::invalid_argument("rules 必须为规则数组");
        std::vector<std::pair<std::string, double>> parsed;
        for (const auto& rule : rules) {
            if (!rule.is_object() || !rule.contains("operator") ||
                (rule.at("operator") != ">" && rule.at("operator") != "<") || !rule.contains("value"))
                throw std::invalid_argument("rules 只支持 >/< 和非负有限 value");
            const double threshold = AlgorithmNumber(rule.at("value"));
            if (threshold < 0) throw std::invalid_argument("rules.value 必须非负");
            parsed.emplace_back(rule.at("operator").get<std::string>(), threshold);
        }
        ModuleIO output;
        for (const auto& group : AlgorithmGroups(imageList, resultList, false)) {
            const double distance = Distance(group);
            bool ok = std::isfinite(distance);
            for (const auto& rule : parsed) ok = ok && (rule.first == ">" ? distance > rule.second : distance < rule.second);
            std::string text = "Nan";
            if (std::isfinite(distance)) {
                std::ostringstream stream;
                stream.imbue(std::locale::classic());
                stream << std::fixed << std::setprecision(6) << distance;
                text = stream.str();
                text.erase(text.find_last_not_of('0') + 1);
                if (text.back() == '.') text.pop_back();
            }
            const std::string reason = std::isfinite(distance) ? "距离：" + text + " px" : "距离：NaN";
            AppendAlgorithmClassification(output, group, ok, std::string(ok ? "OK-" : "NG-") + text, reason, true);
        }
        return output;
    }

private:
    static double Distance(const AlgorithmGroup& group) {
        const double nan = std::numeric_limits<double>::quiet_NaN();
        std::map<std::string, std::pair<double, const AlgorithmDetection*>> top;
        try {
            for (const auto& detection : group.Detections) {
                const auto& value = *detection.Value;
                if (!value.contains("category_name") || !value.at("category_name").is_string() ||
                    value.at("category_name").get<std::string>().empty() || !value.contains("score")) return nan;
                const std::string category = value.at("category_name").get<std::string>();
                const double score = AlgorithmNumber(value.at("score"));
                const auto found = top.find(category);
                if (found == top.end() || score > found->second.first) top[category] = {score, &detection};
            }
            if (top.size() != 2) return nan;
            auto item = top.begin();
            const auto a = AlgorithmCenter(*item->second.second);
            const auto b = AlgorithmCenter(*(++item)->second.second);
            const double distance = std::hypot(a[0] - b[0], a[1] - b[1]);
            return std::isfinite(distance) ? distance : nan;
        } catch (const std::invalid_argument&) {
            return nan;
        }
    }
};

} // namespace

DLCV_FLOW_REGISTER_MODULE("post_process/sequence_check", SequenceCheckModule)
// 仅兼容旧流程类型；分类输出统一使用新主标识。
DLCV_FLOW_REGISTER_MODULE("post_process/wire_sequence_check", SequenceCheckModule)
DLCV_FLOW_REGISTER_MODULE("post_process/offset_distance", OffsetDistanceModule)

} // namespace flow
} // namespace dlcv_infer
