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
        DogProvider out;
        if (TryResolveExplicitProvider(modelPath, out)) {
            return out;
        }
        return DogProvider::Sentinel;
    }

    DogProvider ResolveModelHeaderProvider(const std::string& modelPathUtf8OrGbk) {
        DogProvider out;
        if (TryResolveExplicitProvider(modelPathUtf8OrGbk, out)) {
            return out;
        }
        return DogProvider::Sentinel;
    }

    static bool TryResolveExplicitProviderFromStream(std::istream& stream, DogProvider& outProvider) {
        std::string header;
        std::string headerJsonStr;
        std::getline(stream, header);
        std::getline(stream, headerJsonStr);
        if (header != "DV") {
            throw std::runtime_error("invalid model format: missing DV header");
        }
        auto headerJson = nlohmann::json::parse(headerJsonStr);
        if (!headerJson.contains("dog_provider")) {
            return false;
        }
        outProvider = ParseProviderFromHeaderJson(headerJson);
        return true;
    }

    bool TryResolveExplicitProvider(const std::wstring& modelPath, DogProvider& outProvider) {
        std::ifstream file(modelPath);
        if (!file) {
            throw std::runtime_error("failed to open model file");
        }
        return TryResolveExplicitProviderFromStream(file, outProvider);
    }

    bool TryResolveExplicitProvider(const std::string& modelPath, DogProvider& outProvider) {
#ifdef _WIN32
        int wlen = MultiByteToWideChar(CP_UTF8, 0, modelPath.c_str(), -1, nullptr, 0);
        if (wlen > 0) {
            std::wstring wpath(wlen - 1, L'\0');
            MultiByteToWideChar(CP_UTF8, 0, modelPath.c_str(), -1, &wpath[0], wlen);
            return TryResolveExplicitProvider(wpath, outProvider);
        }
#endif
        std::ifstream file(modelPath);
        if (!file) {
            throw std::runtime_error("failed to open model file");
        }
        return TryResolveExplicitProviderFromStream(file, outProvider);
    }

} // namespace sntl_admin
