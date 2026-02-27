#pragma once

#include <string>
#include <memory>
#include <vector>
#include <functional>
#include <windows.h>
#include <iostream>
#include <sstream>
#include <regex>

// 对于nlohmann/json的支持
#ifdef __has_include
#  if __has_include(<nlohmann/json.hpp>)
#    include <nlohmann/json.hpp>
#  elif __has_include("nlohmann/json.hpp")
#    include "nlohmann/json.hpp"
#  elif __has_include(<json/json.hpp>)
#    include <json/json.hpp>
#  elif __has_include("json/json.hpp")
#    include "json/json.hpp"
#  else
#    error "找不到nlohmann/json或json/json.hpp！"
#  endif
#else
#  include "json/json.hpp"  // 默认路径
#endif

namespace sntl_admin {

    // 定义SNTL状态码枚举
    enum SntlAdminStatus {
        SNTL_ADMIN_STATUS_OK = 0,
        SNTL_ADMIN_INSUF_MEM = 3,
        SNTL_ADMIN_INVALID_CONTEXT = 6001,
        SNTL_ADMIN_LM_NOT_FOUND = 6002,
        SNTL_ADMIN_LM_TOO_OLD = 6003,
        SNTL_ADMIN_BAD_PARAMETERS = 6004,
        SNTL_ADMIN_LOCAL_NETWORK_ERR = 6005,
        SNTL_ADMIN_CANNOT_READ_FILE = 6006
    };

    // DLL函数指针类型
    typedef int (*SntlAdminContextNewFunc)(void** context, const char* hostname, unsigned short port, const char* password);
    typedef int (*SntlAdminContextDeleteFunc)(void* context);
    typedef int (*SntlAdminGetFunc)(void* context, const char* scope, const char* format, char** info);
    typedef void (*SntlAdminFreeFunc)(char* info);

    class SNTLDllLoader {
    private:
        const std::string DllName = "sntl_adminapi_windows_x64.dll";
        const std::string DllPath = "C:\\dlcv\\bin\\sntl_adminapi_windows_x64.dll";
        HMODULE hModule = NULL;

        // 函数指针
        SntlAdminContextNewFunc m_sntl_admin_context_new = nullptr;
        SntlAdminContextDeleteFunc m_sntl_admin_context_delete = nullptr;
        SntlAdminGetFunc m_sntl_admin_get = nullptr;
        SntlAdminFreeFunc m_sntl_admin_free = nullptr;

        // 创建空的代理方法
        void CreateEmptyDelegates();

        // 加载DLL
        void LoadDll();

        SNTLDllLoader();
        ~SNTLDllLoader();

    public:
        // 单例模式
        static SNTLDllLoader& Instance();

        // 获取函数指针
        SntlAdminContextNewFunc sntl_admin_context_new() const {
            return m_sntl_admin_context_new;
        }
        SntlAdminContextDeleteFunc sntl_admin_context_delete() const {
            return m_sntl_admin_context_delete;
        }
        SntlAdminGetFunc sntl_admin_get() const {
            return m_sntl_admin_get;
        }
        SntlAdminFreeFunc sntl_admin_free() const {
            return m_sntl_admin_free;
        }
    };

    // 工具类，提供静态方法和常量
    class SNTLUtils {
    public:
        // 默认的供应商范围XML
        static const std::string DefaultScope;

        // 获取HASP ID的格式XML
        static const std::string HaspIdFormat;

        // 获取特性ID和HASP ID的格式XML
        static const std::string FeatureIdFormat;

        // 获取设备列表
        static nlohmann::json GetDeviceList();

        // 获取特性列表
        static nlohmann::json GetFeatureList();
    };

    // 简单的XML解析到JSON的函数声明
    nlohmann::json ParseXmlToJson(const std::string& xml);

    // SNTL主类
    class SNTL {
    private:
        void* m_context = nullptr;
        bool m_disposed = false;

        // 获取状态码描述
        std::string GetStatusDescription(int status);

    public:
        // 构造函数
        SNTL(const std::string& hostname = "", unsigned short port = 0, const std::string& password = "");

        // 析构函数
        ~SNTL();

        // 手动释放资源
        void Dispose();

        // 获取SNTL信息
        nlohmann::json Get(const std::string& scope, const std::string& format);

        // 获取SNTL信息
        nlohmann::json GetSntlInfo();

        // 获取加密狗ID列表
        nlohmann::json GetDeviceList();

        // 获取特性ID和对应的HASP ID列表
        nlohmann::json GetFeatureList();

        // 用于调试的方法，直接返回XML解析后的JSON
        nlohmann::json GetRawResponse(const std::string& xml);
    };

} // namespace sntl_admin
