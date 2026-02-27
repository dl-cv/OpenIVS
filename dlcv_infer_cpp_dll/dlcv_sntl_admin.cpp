#include "dlcv_sntl_admin.h"

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
        nlohmann::json deviceList = sntl.GetDeviceList();
        return deviceList;
    }
    catch (const std::exception&)
    {
        return nlohmann::json::array();
    }
}

nlohmann::json sntl_admin::SNTLUtils::GetFeatureList() {
    try
    {
        SNTL sntl;
        nlohmann::json featureList = sntl.GetFeatureList();
        return featureList;
    }
    catch (const std::exception&)
    {
        return nlohmann::json::array();
    }
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
            std::regex haspRegex("<hasp>\\s*<haspid>([^<]+)</haspid>\\s*</hasp>");
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
            std::regex featureRegex("<feature>\\s*<featureid>([^<]+)</featureid>\\s*<haspid>([^<]+)</haspid>\\s*</feature>");
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
    hModule = LoadLibraryA(DllName.c_str());
    if (hModule == NULL)
    {
        // 如果当前目录下的DLL加载失败，尝试加载指定路径的DLL
        hModule = LoadLibraryA(DllPath.c_str());
        if (hModule == NULL)
        {
            // DLL加载失败，创建空的代理方法
            CreateEmptyDelegates();
            return;
        }
    }

    // 获取函数指针
    m_sntl_admin_context_new = (SntlAdminContextNewFunc)GetProcAddress(hModule, "sntl_admin_context_new");
    m_sntl_admin_context_delete = (SntlAdminContextDeleteFunc)GetProcAddress(hModule, "sntl_admin_context_delete");
    m_sntl_admin_get = (SntlAdminGetFunc)GetProcAddress(hModule, "sntl_admin_get");
    m_sntl_admin_free = (SntlAdminFreeFunc)GetProcAddress(hModule, "sntl_admin_free");

    // 检查函数指针是否获取成功，失败则创建空的代理
    if (!m_sntl_admin_context_new || !m_sntl_admin_context_delete || !m_sntl_admin_get || !m_sntl_admin_free)
    {
        CreateEmptyDelegates();
    }
}

sntl_admin::SNTLDllLoader::SNTLDllLoader() {
    LoadDll();
}

sntl_admin::SNTLDllLoader::~SNTLDllLoader() {
    if (hModule != NULL)
    {
        FreeLibrary(hModule);
        hModule = NULL;
    }
}