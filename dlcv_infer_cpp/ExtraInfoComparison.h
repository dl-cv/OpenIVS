#pragma once

#include <cmath>
#include <cstddef>
#include <json/json.hpp>

namespace dlcv_demo {

inline bool ExtraInfoEquals(const nlohmann::json& left, const nlohmann::json& right, double tolerance) {
    if (left.is_number() && right.is_number()) {
        if (left.is_number_integer() && right.is_number_integer()) return left == right;
        const double leftValue = left.get<double>();
        const double rightValue = right.get<double>();
        return std::isfinite(leftValue) && std::isfinite(rightValue) &&
            std::abs(leftValue - rightValue) <= tolerance;
    }
    if (left.type() != right.type()) return false;
    if (left.is_object()) {
        if (left.size() != right.size()) return false;
        for (auto it = left.begin(); it != left.end(); ++it) {
            const auto value = right.find(it.key());
            if (value == right.end() || !ExtraInfoEquals(it.value(), *value, tolerance)) return false;
        }
        return true;
    }
    if (left.is_array()) {
        if (left.size() != right.size()) return false;
        for (std::size_t index = 0; index < left.size(); ++index) {
            if (!ExtraInfoEquals(left[index], right[index], tolerance)) return false;
        }
        return true;
    }
    return left == right;
}

} // namespace dlcv_demo
