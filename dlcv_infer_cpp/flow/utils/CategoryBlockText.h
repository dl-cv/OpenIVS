#pragma once

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#else
#include <codecvt>
#include <locale>
#endif
#include "flow/FlowTypes.h"
#include "flow/utils/CategoryBlockUnicode.h"

namespace dlcv_infer {
namespace flow {
namespace category_block {

inline const Json& UnicodeData() {
    static const Json data = Json::parse(CategoryBlockUnicodeData);
    return data;
}

inline bool InRanges(const char* property, char32_t cp) {
    for (const auto& range : UnicodeData().at(property)) {
        if (cp < range[0].get<char32_t>()) return false;
        if (cp <= range[1].get<char32_t>()) return true;
    }
    return false;
}

inline bool CasedAt(const std::u32string& text, int index, int step) {
    for (; index >= 0 && index < static_cast<int>(text.size()); index += step)
        if (!InRanges("ignorable", text[index])) return InRanges("cased", text[index]);
    return false;
}

inline std::string Normalize(const std::string& value) {
    if (value.empty()) return value;
#ifdef _WIN32
    int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (!length) throw std::invalid_argument("category name is not UTF-8");
    std::wstring wide(length, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), &wide[0], length);
    std::u32string text;
    for (size_t i = 0; i < wide.size(); ++i) {
        char32_t cp = wide[i];
        if (cp >= 0xd800 && cp <= 0xdbff && i + 1 < wide.size())
            cp = 0x10000 + ((cp - 0xd800) << 10) + (wide[++i] - 0xdc00);
        text += cp;
    }
#else
    std::wstring_convert<std::codecvt_utf8<char32_t>, char32_t> utf8;
    std::u32string text;
    try {
        text = utf8.from_bytes(value);
    } catch (const std::range_error&) {
        throw std::invalid_argument("category name is not UTF-8");
    }
    if (utf8.converted() != value.size() || std::any_of(text.begin(), text.end(), [](char32_t cp) {
        return cp >= 0xd800 && cp <= 0xdfff;
    })) throw std::invalid_argument("category name is not UTF-8");
#endif
    size_t start = 0, end = text.size();
    while (start < end && InRanges("space", text[start])) ++start;
    while (end > start && InRanges("space", text[end - 1])) --end;
    std::u32string output;
    const auto& lower = UnicodeData().at("lower");
    for (size_t i = start; i < end; ++i) {
        char32_t cp = text[i];
        if (cp == 0x3a3 && CasedAt(text, static_cast<int>(i) - 1, -1) && !CasedAt(text, static_cast<int>(i) + 1, 1)) cp = 0x3c2;
        const auto found = lower.find(std::to_string(cp));
        if (found != lower.end()) for (const auto& mapped : *found) output += mapped.get<char32_t>();
        else output += cp;
    }
#ifdef _WIN32
    wide.clear();
    for (char32_t cp : output) {
        if (cp <= 0xffff) wide += static_cast<wchar_t>(cp);
        else {
            cp -= 0x10000;
            wide += static_cast<wchar_t>(0xd800 + (cp >> 10));
            wide += static_cast<wchar_t>(0xdc00 + (cp & 0x3ff));
        }
    }
    if (wide.empty()) return {};
    length = WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), nullptr, 0, nullptr, nullptr);
    std::string result(length, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), &result[0], length, nullptr, nullptr);
    return result;
#else
    return utf8.to_bytes(output);
#endif
}

inline std::string PythonFloat(const Json& value) {
    const double number = value.get<double>();
    if (std::isnan(number)) return "nan";
    if (std::isinf(number)) return number < 0 ? "-inf" : "inf";
    const std::string sign = std::signbit(number) ? "-" : "";
    if (number == 0) return sign + "0.0";
    std::string text = Json(std::abs(number)).dump();
    const auto e = text.find('e');
    int exponent = e == std::string::npos ? 0 : std::stoi(text.substr(e + 1));
    text = text.substr(0, e);
    auto point = text.find('.');
    if (point == std::string::npos) point = text.size();
    text.erase(std::remove(text.begin(), text.end(), '.'), text.end());
    auto leading = text.find_first_not_of('0');
    exponent += static_cast<int>(point) - static_cast<int>(leading) - 1;
    text.erase(0, leading);
    text.erase(text.find_last_not_of('0') + 1);
    if (exponent >= -4 && exponent < 16) {
        int position = exponent + 1;
        if (position <= 0) return sign + "0." + std::string(-position, '0') + text;
        if (position >= static_cast<int>(text.size())) return sign + text + std::string(position - text.size(), '0') + ".0";
        text.insert(position, ".");
        return sign + text;
    }
    std::string power = std::to_string(std::abs(exponent));
    if (power.size() < 2) power.insert(0, "0");
    return sign + text.substr(0, 1) + (text.size() > 1 ? "." + text.substr(1) : "") + "e" + (exponent < 0 ? "-" : "+") + power;
}

inline std::string PythonString(const Json& value) {
    if (value.is_string()) return value.get<std::string>();
    if (value.is_null()) return "None";
    if (value.is_boolean()) return value.get<bool>() ? "True" : "False";
    if (value.is_number_float()) return PythonFloat(value);
    return value.dump();
}

} // namespace category_block
} // namespace flow
} // namespace dlcv_infer
