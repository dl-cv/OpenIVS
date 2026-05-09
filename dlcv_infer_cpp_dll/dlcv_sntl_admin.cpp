#include "dlcv_sntl_admin.h"

#ifndef _WIN32
#include <dlfcn.h>
#endif

namespace {
    constexpr int VIRBOX_OK = 0;
    constexpr int VIRBOX_JSON = 2;

#ifdef _WIN32
#define DLCV_STDCALL __stdcall
#else
#define DLCV_STDCALL
#endif
    using VirboxClientOpenFunc = int(DLCV_STDCALL *)(void** ipc);
    using VirboxClientCloseFunc = int(DLCV_STDCALL *)(void* ipc);
    using VirboxGetAllDescriptionFunc = int(DLCV_STDCALL *)(void* ipc, int format, char** desc);
    using VirboxGetLicenseIdFunc = int(DLCV_STDCALL *)(void* ipc, int format, const char* desc, char** result);
    using VirboxGetDeviceInfoFunc = int(DLCV_STDCALL *)(void* ipc, const char* desc, char** result);
    using VirboxFreeFunc = void(DLCV_STDCALL *)(void* buffer);

#ifndef _WIN32
    void* LoadSharedLibrary(const std::string& name, const std::string& fallbackPath) {
        void* handle = dlopen(name.c_str(), RTLD_LAZY | RTLD_LOCAL);
        if (handle == nullptr && !fallbackPath.empty() && fallbackPath != name) {
            handle = dlopen(fallbackPath.c_str(), RTLD_LAZY | RTLD_LOCAL);
        }
        return handle;
    }

    void* ResolveSharedSymbol(void* handle, const char* symbolName) {
        return handle == nullptr ? nullptr : dlsym(handle, symbolName);
    }
#endif

#ifdef _WIN32
    inline void* ResolveSymbol(void* module, const char* name) {
        return GetProcAddress((HMODULE)module, name);
    }
#else
    inline void* ResolveSymbol(void* module, const char* name) {
        return dlsym(module, name);
    }
#endif

    struct VirboxControlApi {
#ifdef _WIN32
        HMODULE module = NULL;
#else
        void* module = nullptr;
#endif
        VirboxClientOpenFunc client_open = nullptr;
        VirboxClientCloseFunc client_close = nullptr;
        VirboxGetAllDescriptionFunc get_all_description = nullptr;
        VirboxGetLicenseIdFunc get_license_id = nullptr;
        VirboxGetDeviceInfoFunc get_device_info = nullptr;
        VirboxFreeFunc free_buffer = nullptr;

        VirboxControlApi() {
#ifdef _WIN32
            module = LoadLibraryA("slm_control.dll");
            if (module == NULL)
            {
                module = LoadLibraryA("C:\\dlcv\\bin\\slm_control.dll");
            }
            if (module == NULL)
            {
                return;
            }
#else
            module = LoadSharedLibrary("libslm_control.so", "/usr/local/dlcv/lib/libslm_control.so");
            if (module == nullptr)
            {
                return;
            }
#endif

            client_open = reinterpret_cast<VirboxClientOpenFunc>(ResolveSymbol(module, "slm_ctrl_client_open"));
            client_close = reinterpret_cast<VirboxClientCloseFunc>(ResolveSymbol(module, "slm_ctrl_client_close"));
            get_all_description = reinterpret_cast<VirboxGetAllDescriptionFunc>(ResolveSymbol(module, "slm_ctrl_get_all_description"));
            get_license_id = reinterpret_cast<VirboxGetLicenseIdFunc>(ResolveSymbol(module, "slm_ctrl_get_license_id"));
            get_device_info = reinterpret_cast<VirboxGetDeviceInfoFunc>(ResolveSymbol(module, "slm_ctrl_get_device_info"));
            free_buffer = reinterpret_cast<VirboxFreeFunc>(ResolveSymbol(module, "slm_ctrl_free"));

            if (!client_open || !client_close || !get_all_description || !get_license_id || !get_device_info || !free_buffer)
            {
#ifdef _WIN32
                FreeLibrary(module);
                module = NULL;
#else
                dlclose(module);
                module = nullptr;
#endif
                client_open = nullptr;
                client_close = nullptr;
                get_all_description = nullptr;
                get_license_id = nullptr;
                get_device_info = nullptr;
                free_buffer = nullptr;
            }
        }

        ~VirboxControlApi() {
#ifdef _WIN32
            if (module != NULL)
            {
                FreeLibrary(module);
            }
#else
            if (module != nullptr)
            {
                dlclose(module);
                module = nullptr;
            }
#endif
        }

        bool available() const {
#ifdef _WIN32
            return module != NULL;
#else
            return module != nullptr;
#endif
        }
    };

    VirboxControlApi& GetVirboxControlApi() {
        static VirboxControlApi api;
        return api;
    }

    nlohmann::json ParseJsonSafe(const std::string& text) {
        try
        {
            return nlohmann::json::parse(text);
        }
        catch (...)
        {
            return nlohmann::json();
        }
    }

    std::string ReadAndFree(VirboxControlApi& api, char* value) {
        std::string text = value ? value : "";
        if (value)
        {
            api.free_buffer(value);
        }
        return text;
    }

    std::vector<nlohmann::json> GetVirboxDescriptions(void* ipc, VirboxControlApi& api) {
        char* desc = nullptr;
        int status = api.get_all_description(ipc, VIRBOX_JSON, &desc);
        if (status != VIRBOX_OK)
        {
            return {};
        }

        nlohmann::json root = ParseJsonSafe(ReadAndFree(api, desc));
        if (root.is_object())
        {
            return { root };
        }
        if (!root.is_array())
        {
            return {};
        }

        std::vector<nlohmann::json> result;
        for (const auto& item : root)
        {
            if (item.is_object())
            {
                result.push_back(item);
            }
        }
        return result;
    }

    std::string FirstStringByKeys(const nlohmann::json& value, const std::vector<std::string>& keys) {
        if (value.is_object())
        {
            for (const auto& key : keys)
            {
                if (value.contains(key) && !value[key].is_null())
                {
                    if (value[key].is_string())
                    {
                        return value[key].get<std::string>();
                    }
                    if (value[key].is_number_integer())
                    {
                        return std::to_string(value[key].get<long long>());
                    }
                }
            }
            for (const auto& item : value.items())
            {
                std::string nested = FirstStringByKeys(item.value(), keys);
                if (!nested.empty())
                {
                    return nested;
                }
            }
        } else if (value.is_array())
        {
            for (const auto& item : value)
            {
                std::string nested = FirstStringByKeys(item, keys);
                if (!nested.empty())
                {
                    return nested;
                }
            }
        }
        return "";
    }

    void AddUnique(nlohmann::json& array, const std::string& value) {
        if (value.empty())
        {
            return;
        }
        for (const auto& item : array)
        {
            if (item.is_string() && item.get<std::string>() == value)
            {
                return;
            }
        }
        array.push_back(value);
    }

    void ExtractLicenseIds(const nlohmann::json& value, nlohmann::json& output) {
        if (value.is_number_integer())
        {
            AddUnique(output, std::to_string(value.get<long long>()));
            return;
        }
        if (value.is_string())
        {
            AddUnique(output, value.get<std::string>());
            return;
        }
        if (value.is_array())
        {
            for (const auto& item : value)
            {
                ExtractLicenseIds(item, output);
            }
            return;
        }
        if (value.is_object())
        {
            for (const auto& key : { "license_id", "licenseId", "licenseid", "lic_id" })
            {
                if (value.contains(key))
                {
                    ExtractLicenseIds(value[key], output);
                }
            }
            for (const auto& key : { "license_ids", "licenseIds", "licenses", "data", "result" })
            {
                if (value.contains(key))
                {
                    ExtractLicenseIds(value[key], output);
                }
            }
        }
    }

}

// Virbox类实现
nlohmann::json sntl_admin::Virbox::GetDeviceList() {
    nlohmann::json devices = nlohmann::json::array();
    auto& api = GetVirboxControlApi();
    if (!api.available())
    {
        return devices;
    }

    void* ipc = nullptr;
    if (api.client_open(&ipc) != VIRBOX_OK)
    {
        return devices;
    }

    try
    {
        for (const auto& desc : GetVirboxDescriptions(ipc, api))
        {
            try
            {
                char* info = nullptr;
                int status = api.get_device_info(ipc, desc.dump().c_str(), &info);
                if (status == VIRBOX_OK)
                {
                    nlohmann::json infoObj = ParseJsonSafe(ReadAndFree(api, info));
                    if (infoObj.is_object() && infoObj.contains("shell_num") && infoObj["shell_num"].is_string())
                    {
                        AddUnique(devices, infoObj["shell_num"].get<std::string>());
                    }
                }
            }
            catch (const std::exception& ex)
            {
                std::cerr << "Virbox GetDeviceList device error: " << ex.what() << std::endl;
            }
        }
    }
    catch (const std::exception& ex)
    {
        std::cerr << "Virbox GetDeviceList loop error: " << ex.what() << std::endl;
    }

    api.client_close(ipc);
    return devices;
}

nlohmann::json sntl_admin::Virbox::GetFeatureList() {
    nlohmann::json features = nlohmann::json::array();
    auto& api = GetVirboxControlApi();
    if (!api.available())
    {
        return features;
    }

    void* ipc = nullptr;
    if (api.client_open(&ipc) != VIRBOX_OK)
    {
        return features;
    }

    try
    {
        for (const auto& desc : GetVirboxDescriptions(ipc, api))
        {
            char* result = nullptr;
            int status = api.get_license_id(ipc, VIRBOX_JSON, desc.dump().c_str(), &result);
            if (status == VIRBOX_OK)
            {
                ExtractLicenseIds(ParseJsonSafe(ReadAndFree(api, result)), features);
            }
        }
    }
    catch (...)
    {
    }

    api.client_close(ipc);
    return features;
}

// SNTLUtils的静态常量定义
const std::string sntl_admin::SNTLUtils::DefaultScope =
"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>"
"<haspscope>"
"  <vendor id=\"26146\" />"
"</haspscope>";

const std::string sntl_admin::SNTLUtils::HaspIdFormat =
"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>"
"<admin>"
"  <hasp>"
"    <element name=\"haspid\" />"
"  </hasp>"
"</admin>";

const std::string sntl_admin::SNTLUtils::FeatureIdFormat =
"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>"
"<admin>"
"  <feature>"
"    <element name=\"featureid\"/>"
"    <element name=\"haspid\"/>"
"  </feature>"
"</admin>";

// SNTLDllLoader单例方法实现
sntl_admin::SNTLDllLoader& sntl_admin::SNTLDllLoader::Instance() {
    static SNTLDllLoader instance;
    return instance;
}

// SNTLUtils的静态方法实现
nlohmann::json sntl_admin::SNTLUtils::GetDeviceList() {
    try
    {
        SNTL sntl;
        return sntl.GetDeviceList();
    }
    catch (const std::exception&)
    {
    }
    return nlohmann::json::array();
}

nlohmann::json sntl_admin::SNTLUtils::GetFeatureList() {
    try
    {
        SNTL sntl;
        return sntl.GetFeatureList();
    }
    catch (const std::exception&)
    {
    }
    return nlohmann::json::array();
}

// DogUtils实现
sntl_admin::DogInfo sntl_admin::DogUtils::GetSentinelInfo() {
    try
    {
        SNTL sntl;
        auto devices = sntl.GetDeviceList();
        auto features = sntl.GetFeatureList();
        if (!devices.empty() || !features.empty())
        {
            return { DogProvider::Sentinel, devices, features };
        }
    }
    catch (...)
    {
    }
    return { DogProvider::Unknown, nlohmann::json::array(), nlohmann::json::array() };
}

sntl_admin::DogInfo sntl_admin::DogUtils::GetVirboxInfo() {
    try
    {
        Virbox virbox;
        auto devices = virbox.GetDeviceList();
        auto features = virbox.GetFeatureList();
        if (!devices.empty() || !features.empty())
        {
            return { DogProvider::Virbox, devices, features };
        }
    }
    catch (...)
    {
    }
    return { DogProvider::Unknown, nlohmann::json::array(), nlohmann::json::array() };
}

std::vector<sntl_admin::DogProvider> sntl_admin::DogUtils::GetAvailableProviders() {
    std::vector<DogProvider> providers;
    auto sentinel = GetSentinelInfo();
    if (sentinel.provider != DogProvider::Unknown) providers.push_back(DogProvider::Sentinel);
    auto virbox = GetVirboxInfo();
    if (virbox.provider != DogProvider::Unknown) providers.push_back(DogProvider::Virbox);
    return providers;
}

nlohmann::json sntl_admin::DogUtils::GetAllDogInfo() {
    nlohmann::json result;
    auto sentinel = GetSentinelInfo();
    auto virbox = GetVirboxInfo();
    result["sentinel"] = { {"devices", sentinel.devices}, {"features", sentinel.features} };
    result["virbox"] = { {"devices", virbox.devices}, {"features", virbox.features} };
    return result;
}

// 简单的XML解析到JSON的函数实现
nlohmann::json sntl_admin::ParseXmlToJson(const std::string& xml) {
    nlohmann::json result;

    try
    {
        // 非常简化的XML解析，仅适用于本例中的特定XML格式
        // 获取根节点名称
        std::regex rootRegex("<(\\w+)[^>]*>");
        std::smatch rootMatch;
        if (std::regex_search(xml, rootMatch, rootRegex) && rootMatch.size() > 1)
        {
            std::string rootName = rootMatch[1];
            result[rootName] = nlohmann::json::object();

            // 首先解析admin_status节点（无论在哪个响应中都会出现）
            std::regex statusCodeRegex("<admin_status>\\s*<code>([^<]+)</code>");
            std::smatch statusCodeMatch;
            if (std::regex_search(xml, statusCodeMatch, statusCodeRegex) && statusCodeMatch.size() > 1)
            {
                nlohmann::json statusJson;
                statusJson["code"] = statusCodeMatch[1];

                std::regex statusTextRegex("<admin_status>\\s*<code>[^<]+</code>\\s*<text>([^<]+)</text>");
                std::smatch statusTextMatch;
                if (std::regex_search(xml, statusTextMatch, statusTextRegex) && statusTextMatch.size() > 1)
                {
                    statusJson["text"] = statusTextMatch[1];
                }

                result[rootName]["admin_status"] = statusJson;
            }

            // 解析hasp节点
            std::regex haspRegex("<hasp[^>]*>[\\s\\S]*?<haspid>([^<]+)</haspid>[\\s\\S]*?</hasp>");
            std::string::const_iterator searchStart(xml.cbegin());
            std::smatch haspMatch;
            std::vector<nlohmann::json> hasps;

            while (std::regex_search(searchStart, xml.cend(), haspMatch, haspRegex))
            {
                nlohmann::json haspJson;
                haspJson["haspid"] = haspMatch[1];
                hasps.push_back(haspJson);
                searchStart = haspMatch.suffix().first;
            }

            if (hasps.size() == 1)
            {
                result[rootName]["hasp"] = hasps[0];
            } else if (hasps.size() > 1)
            {
                result[rootName]["hasp"] = hasps;
            }

            // 解析feature节点
            std::regex featureRegex("<feature[^>]*>[\\s\\S]*?<featureid>([^<]+)</featureid>[\\s\\S]*?<haspid>([^<]+)</haspid>[\\s\\S]*?</feature>");
            searchStart = xml.cbegin();
            std::smatch featureMatch;
            std::vector<nlohmann::json> features;

            while (std::regex_search(searchStart, xml.cend(), featureMatch, featureRegex))
            {
                nlohmann::json featureJson;
                featureJson["featureid"] = featureMatch[1];
                featureJson["haspid"] = featureMatch[2];
                features.push_back(featureJson);
                searchStart = featureMatch.suffix().first;
            }

            if (features.size() == 1)
            {
                result[rootName]["feature"] = features[0];
            } else if (features.size() > 1)
            {
                result[rootName]["feature"] = features;
            }
        }
    }
    catch (const std::exception& e)
    {
        result = nlohmann::json::object();
        result["error"] = "XML解析失败";
        result["message"] = e.what();
        result["raw_xml"] = xml;
    }

    return result;
}

// SNTL类的方法实现
std::string sntl_admin::SNTL::GetStatusDescription(int status) {
    switch (static_cast<SntlAdminStatus>(status))
    {
    case SntlAdminStatus::SNTL_ADMIN_STATUS_OK:
        return "操作成功";
    case SntlAdminStatus::SNTL_ADMIN_INSUF_MEM:
        return "内存不足";
    case SntlAdminStatus::SNTL_ADMIN_INVALID_CONTEXT:
        return "无效的上下文";
    case SntlAdminStatus::SNTL_ADMIN_LM_NOT_FOUND:
        return "未找到许可管理器";
    case SntlAdminStatus::SNTL_ADMIN_LM_TOO_OLD:
        return "许可管理器版本过旧";
    case SntlAdminStatus::SNTL_ADMIN_BAD_PARAMETERS:
        return "参数错误";
    case SntlAdminStatus::SNTL_ADMIN_LOCAL_NETWORK_ERR:
        return "本地网络错误";
    case SntlAdminStatus::SNTL_ADMIN_CANNOT_READ_FILE:
        return "无法读取文件";
    default:
        return "未知错误 (" + std::to_string(status) + ")";
    }
}

sntl_admin::SNTL::SNTL(const std::string& hostname, unsigned short port, const std::string& password) {
    int status = SNTLDllLoader::Instance().sntl_admin_context_new()(&m_context,
        hostname.empty() ? nullptr : hostname.c_str(),
        port,
        password.empty() ? nullptr : password.c_str());

    if (status != static_cast<int>(SntlAdminStatus::SNTL_ADMIN_STATUS_OK))
    {
        throw std::runtime_error("初始化SNTL失败，错误码：" + std::to_string(status) + "，" + GetStatusDescription(status));
    }
}

sntl_admin::SNTL::~SNTL() {
    Dispose();
}

void sntl_admin::SNTL::Dispose() {
    if (!m_disposed && m_context != nullptr)
    {
        int status = SNTLDllLoader::Instance().sntl_admin_context_delete()(m_context);
        if (status != static_cast<int>(SntlAdminStatus::SNTL_ADMIN_STATUS_OK))
        {
            std::cerr << "释放SNTL上下文失败，错误码：" << status << "，" << GetStatusDescription(status) << std::endl;
        }
        m_context = nullptr;
        m_disposed = true;
    }
}

nlohmann::json sntl_admin::SNTL::Get(const std::string& scope, const std::string& format) {
    char* info = nullptr;
    nlohmann::json result;

    try
    {
        int status = SNTLDllLoader::Instance().sntl_admin_get()(m_context, scope.c_str(), format.c_str(), &info);

        if (status != static_cast<int>(SntlAdminStatus::SNTL_ADMIN_STATUS_OK))
        {
            result["code"] = status;
            result["message"] = GetStatusDescription(status);
            return result;
        }

        // 将返回的XML信息转换为字符串
        std::string xmlResult = info ? info : "";

        // 释放资源
        if (info != nullptr)
        {
            SNTLDllLoader::Instance().sntl_admin_free()(info);
            info = nullptr;
        }

        // 将XML转换为JSON
        nlohmann::json dataJson = ParseXmlToJson(xmlResult);

        result["code"] = 0;
        result["message"] = "成功";
        result["data"] = dataJson;
    }
    catch (const std::exception& e)
    {
        // 释放资源
        if (info != nullptr)
        {
            SNTLDllLoader::Instance().sntl_admin_free()(info);
            info = nullptr;
        }

        result["code"] = -1;
        result["message"] = std::string("处理异常：") + e.what();
    }

    return result;
}

nlohmann::json sntl_admin::SNTL::GetSntlInfo() {
    // 使用默认的供应商范围XML和HASP ID格式XML
    std::string scope = SNTLUtils::DefaultScope;
    std::string format = SNTLUtils::HaspIdFormat;
    // 获取设备信息
    return Get(scope, format);
}

nlohmann::json sntl_admin::SNTL::GetDeviceList() {
    nlohmann::json deviceList = nlohmann::json::array();

    // 获取加密狗信息
    nlohmann::json sntlInfo = GetSntlInfo();

    // 检查是否获取成功
    if (sntlInfo["code"].get<int>() != 0)
    {
        return deviceList; // 如果失败，直接返回空列表
    }

    try
    {
        // 获取hasp节点
        nlohmann::json haspNode = sntlInfo["data"]["admin_response"]["hasp"];

        // 处理单个设备的情况
        if (haspNode.is_object())
        {
            std::string haspId = haspNode["haspid"].get<std::string>();
            deviceList.push_back(haspId);
        }
        // 处理多个设备的情况
        else if (haspNode.is_array())
        {
            for (const auto& hasp : haspNode)
            {
                std::string haspId = hasp["haspid"].get<std::string>();
                deviceList.push_back(haspId);
            }
        }

        return deviceList;
    }
    catch (const std::exception&)
    {
        return deviceList;
    }
}

nlohmann::json sntl_admin::SNTL::GetFeatureList() {
    nlohmann::json featureList = nlohmann::json::array();

    // 使用特性格式XML
    std::string scope = SNTLUtils::DefaultScope;
    std::string format = SNTLUtils::FeatureIdFormat;

    // 获取特性信息
    nlohmann::json sntlInfo = Get(scope, format);

    // 检查是否获取成功
    if (sntlInfo["code"].get<int>() != 0)
    {
        return featureList; // 如果失败，直接返回空列表
    }

    try
    {
        // 获取feature节点
        nlohmann::json featureNode = sntlInfo["data"]["admin_response"]["feature"];

        // 处理单个特性的情况
        if (featureNode.is_object())
        {
            featureList.push_back(featureNode["featureid"].get<std::string>());
        }
        // 处理多个特性的情况
        else if (featureNode.is_array())
        {
            for (const auto& feature : featureNode)
            {
                featureList.push_back(feature["featureid"].get<std::string>());
            }
        }

        return featureList;
    }
    catch (const std::exception&)
    {
        return featureList;
    }
}

nlohmann::json sntl_admin::SNTL::GetRawResponse(const std::string& xml) {
    return ParseXmlToJson(xml);
}

// SNTLDllLoader实现
void sntl_admin::SNTLDllLoader::CreateEmptyDelegates() {
    m_sntl_admin_context_new = [](void** context, const char* hostname, unsigned short port, const char* password) -> int {
        return SntlAdminStatus::SNTL_ADMIN_LM_NOT_FOUND;
        };

    m_sntl_admin_context_delete = [](void* context) -> int {
        return SntlAdminStatus::SNTL_ADMIN_STATUS_OK;
        };

    m_sntl_admin_get = [](void* context, const char* scope, const char* format, char** info) -> int {
        return SntlAdminStatus::SNTL_ADMIN_LM_NOT_FOUND;
        };

    m_sntl_admin_free = [](char* info) {};
}

void sntl_admin::SNTLDllLoader::LoadDll() {
#ifdef _WIN32
    hModule = LoadLibraryA(DllName.c_str());
    if (hModule == NULL)
    {
        hModule = LoadLibraryA(DllPath.c_str());
        if (hModule == NULL)
        {
            CreateEmptyDelegates();
            return;
        }
    }

    m_sntl_admin_context_new = (SntlAdminContextNewFunc)GetProcAddress(hModule, "sntl_admin_context_new");
    m_sntl_admin_context_delete = (SntlAdminContextDeleteFunc)GetProcAddress(hModule, "sntl_admin_context_delete");
    m_sntl_admin_get = (SntlAdminGetFunc)GetProcAddress(hModule, "sntl_admin_get");
    m_sntl_admin_free = (SntlAdminFreeFunc)GetProcAddress(hModule, "sntl_admin_free");

    if (!m_sntl_admin_context_new || !m_sntl_admin_context_delete || !m_sntl_admin_get || !m_sntl_admin_free)
    {
        FreeLibrary(hModule);
        hModule = NULL;
        CreateEmptyDelegates();
    }
#else
    hModule = LoadSharedLibrary(DllName, DllPath);
    if (hModule == nullptr)
    {
        CreateEmptyDelegates();
        return;
    }

    m_sntl_admin_context_new = (SntlAdminContextNewFunc)ResolveSharedSymbol(hModule, "sntl_admin_context_new");
    m_sntl_admin_context_delete = (SntlAdminContextDeleteFunc)ResolveSharedSymbol(hModule, "sntl_admin_context_delete");
    m_sntl_admin_get = (SntlAdminGetFunc)ResolveSharedSymbol(hModule, "sntl_admin_get");
    m_sntl_admin_free = (SntlAdminFreeFunc)ResolveSharedSymbol(hModule, "sntl_admin_free");

    if (!m_sntl_admin_context_new || !m_sntl_admin_context_delete || !m_sntl_admin_get || !m_sntl_admin_free)
    {
        dlclose(hModule);
        hModule = nullptr;
        CreateEmptyDelegates();
    }
#endif
}

sntl_admin::SNTLDllLoader::SNTLDllLoader() {
    LoadDll();
}

sntl_admin::SNTLDllLoader::~SNTLDllLoader() {
#ifdef _WIN32
    if (hModule != NULL)
    {
        FreeLibrary(hModule);
        hModule = NULL;
    }
#else
    if (hModule != nullptr)
    {
        dlclose(hModule);
        hModule = nullptr;
    }
#endif
}