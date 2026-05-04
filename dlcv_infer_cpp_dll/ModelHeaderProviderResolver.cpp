#include "ModelHeaderProviderResolver.h"
#include <fstream>
#include <stdexcept>
#include <cctype>
#include "json/json.hpp"

namespace sntl_admin {

    static DogProvider ParseProviderFromHeaderJson(const nlohmann::json& headerJson) {
        if (!headerJson.contains("dog_provider")) {
            return DogProvider::Sentinel;
        }
        std::string provider = headerJson["dog_provider"].get<std::string>();
        for (auto& c : provider) {
            c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
        }
        if (provider == "sentinel") return DogProvider::Sentinel;
        if (provider == "virbox") return DogProvider::Virbox;
        throw std::runtime_error("invalid dog provider in header_json: " + provider);
    }

    DogProvider ResolveModelHeaderProvider(const std::wstring& modelPath) {
        std::ifstream file(modelPath);
        if (!file) {
            throw std::runtime_error("failed to open model file");
        }
        std::string header;
        std::string headerJsonStr;
        std::getline(file, header);
        std::getline(file, headerJsonStr);
        if (header != "DV") {
            throw std::runtime_error("invalid model format: missing DV header");
        }
        auto headerJson = nlohmann::json::parse(headerJsonStr);
        return ParseProviderFromHeaderJson(headerJson);
    }

    DogProvider ResolveModelHeaderProvider(const std::string& modelPathUtf8OrGbk) {
        // 在 Windows 下优先转为 wstring 打开，以支持非 ASCII 路径
#ifdef _WIN32
        int wlen = MultiByteToWideChar(CP_UTF8, 0, modelPathUtf8OrGbk.c_str(), -1, nullptr, 0);
        if (wlen > 0) {
            std::wstring wpath(wlen - 1, L'\0');
            MultiByteToWideChar(CP_UTF8, 0, modelPathUtf8OrGbk.c_str(), -1, &wpath[0], wlen);
            return ResolveModelHeaderProvider(wpath);
        }
#endif
        std::ifstream file(modelPathUtf8OrGbk);
        if (!file) {
            throw std::runtime_error("failed to open model file");
        }
        std::string header;
        std::string headerJsonStr;
        std::getline(file, header);
        std::getline(file, headerJsonStr);
        if (header != "DV") {
            throw std::runtime_error("invalid model format: missing DV header");
        }
        auto headerJson = nlohmann::json::parse(headerJsonStr);
        return ParseProviderFromHeaderJson(headerJson);
    }

} // namespace sntl_admin
