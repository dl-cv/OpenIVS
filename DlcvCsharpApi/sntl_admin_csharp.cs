using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace sntl_admin_csharp
{
    // 定义SNTL状态码枚举
    public enum SntlAdminStatus
    {
        SNTL_ADMIN_STATUS_OK = 0,
        SNTL_ADMIN_INSUF_MEM = 3,
        SNTL_ADMIN_INVALID_CONTEXT = 6001,
        SNTL_ADMIN_LM_NOT_FOUND = 6002,
        SNTL_ADMIN_LM_TOO_OLD = 6003,
        SNTL_ADMIN_BAD_PARAMETERS = 6004,
        SNTL_ADMIN_LOCAL_NETWORK_ERR = 6005,
        SNTL_ADMIN_CANNOT_READ_FILE = 6006
    }

    public class SNTLDllLoader
    {
        private const string DllName = "sntl_adminapi_windows_x64.dll";
        private const string DllPath = @"C:\dlcv\bin\sntl_adminapi_windows_x64.dll";
        private const CallingConvention calling_method = CallingConvention.Cdecl;

        // 定义导入方法的委托
        [UnmanagedFunctionPointer(calling_method)]
        public delegate int SntlAdminContextNewDelegate(ref IntPtr context, string hostname, ushort port, string password);
        public SntlAdminContextNewDelegate sntl_admin_context_new;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate int SntlAdminContextDeleteDelegate(IntPtr context);
        public SntlAdminContextDeleteDelegate sntl_admin_context_delete;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate int SntlAdminGetDelegate(IntPtr context, string scope, string format, ref IntPtr info);
        public SntlAdminGetDelegate sntl_admin_get;

        [UnmanagedFunctionPointer(calling_method)]
        public delegate void SntlAdminFreeDelegate(IntPtr info);
        public SntlAdminFreeDelegate sntl_admin_free;

        private void LoadDll()
        {
            IntPtr hModule = LoadLibrary(DllName);
            if (hModule == IntPtr.Zero)
            {
                // 如果当前目录下的 DLL 加载失败，尝试加载指定路径的 DLL
                hModule = LoadLibrary(DllPath);
                if (hModule == IntPtr.Zero)
                {
                    throw new Exception("无法加载 SNTL DLL");
                }
            }

            // 获取函数指针
            sntl_admin_context_new = GetDelegate<SntlAdminContextNewDelegate>(hModule, "sntl_admin_context_new");
            sntl_admin_context_delete = GetDelegate<SntlAdminContextDeleteDelegate>(hModule, "sntl_admin_context_delete");
            sntl_admin_get = GetDelegate<SntlAdminGetDelegate>(hModule, "sntl_admin_get");
            sntl_admin_free = GetDelegate<SntlAdminFreeDelegate>(hModule, "sntl_admin_free");
        }

        private T GetDelegate<T>(IntPtr hModule, string procedureName) where T : Delegate
        {
            IntPtr pAddressOfFunctionToCall = GetProcAddress(hModule, procedureName);
            return (T)Marshal.GetDelegateForFunctionPointer(pAddressOfFunctionToCall, typeof(T));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        private static readonly Lazy<SNTLDllLoader> _instance = new Lazy<SNTLDllLoader>(() => new SNTLDllLoader());

        public static SNTLDllLoader Instance => _instance.Value;
        private SNTLDllLoader()
        {
            LoadDll();
        }
    }

    public class SNTL : IDisposable
    {
        private IntPtr _context = IntPtr.Zero;
        private bool _disposed = false;

        /// <summary>
        /// 初始化SNTL类，创建上下文
        /// </summary>
        /// <param name="hostname">主机名</param>
        /// <param name="port">端口号</param>
        /// <param name="password">密码</param>
        public SNTL(string hostname = "", ushort port = 0, string password = "")
        {
            int status = SNTLDllLoader.Instance.sntl_admin_context_new(ref _context, hostname, port, password);

            if (status != (int)SntlAdminStatus.SNTL_ADMIN_STATUS_OK)
            {
                throw new Exception($"初始化SNTL失败，错误码：{status}，{GetStatusDescription(status)}");
            }
        }

        /// <summary>
        /// 获取SNTL信息
        /// </summary>
        /// <param name="scope">作用域XML</param>
        /// <param name="format">格式XML</param>
        /// <returns>JSON格式的信息</returns>
        public JObject Get(string scope, string format)
        {
            IntPtr info = IntPtr.Zero;
            int status = SNTLDllLoader.Instance.sntl_admin_get(_context, scope, format, ref info);

            try
            {
                if (status != (int)SntlAdminStatus.SNTL_ADMIN_STATUS_OK)
                {
                    return new JObject
                    {
                        ["code"] = status,
                        ["message"] = GetStatusDescription(status)
                    };
                }

                // 将返回的XML信息转换为字符串
                string xmlResult = Marshal.PtrToStringAnsi(info);

                // 将XML转换为JSON
                JObject dataJson = XmlToJson(xmlResult);

                return new JObject
                {
                    ["code"] = 0,
                    ["message"] = "成功",
                    ["data"] = dataJson
                };
            }
            finally
            {
                // 释放资源
                if (info != IntPtr.Zero)
                {
                    SNTLDllLoader.Instance.sntl_admin_free(info);
                }
            }
        }

        /// <summary>
        /// 将XML转换为JSON对象
        /// </summary>
        private JObject XmlToJson(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return new JObject();
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                string jsonText = JsonConvert.SerializeXmlNode(doc);
                return JObject.Parse(jsonText);
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["error"] = "XML解析失败",
                    ["message"] = ex.Message,
                    ["raw_xml"] = xml
                };
            }
        }

        /// <summary>
        /// 获取状态码描述
        /// </summary>
        private string GetStatusDescription(int status)
        {
            switch ((SntlAdminStatus)status)
            {
                case SntlAdminStatus.SNTL_ADMIN_STATUS_OK:
                    return "操作成功";
                case SntlAdminStatus.SNTL_ADMIN_INSUF_MEM:
                    return "内存不足";
                case SntlAdminStatus.SNTL_ADMIN_INVALID_CONTEXT:
                    return "无效的上下文";
                case SntlAdminStatus.SNTL_ADMIN_LM_NOT_FOUND:
                    return "未找到许可管理器";
                case SntlAdminStatus.SNTL_ADMIN_LM_TOO_OLD:
                    return "许可管理器版本过旧";
                case SntlAdminStatus.SNTL_ADMIN_BAD_PARAMETERS:
                    return "参数错误";
                case SntlAdminStatus.SNTL_ADMIN_LOCAL_NETWORK_ERR:
                    return "本地网络错误";
                case SntlAdminStatus.SNTL_ADMIN_CANNOT_READ_FILE:
                    return "无法读取文件";
                default:
                    return $"未知错误 ({status})";
            }
        }

        public JObject GetSntlInfo()
        {
            // 使用默认的供应商范围XML和HASP ID格式XML
            string scope = SNTLUtils.DefaultScope;
            string format = SNTLUtils.HaspIdFormat;
            // 获取设备信息
            return Get(scope, format);
        }

        /// <summary>
        /// 获取加密狗ID列表
        /// </summary>
        /// <returns>包含加密狗ID列表的对象</returns>
        public JArray GetDeviceList()
        {
            JArray deviceList = new JArray();

            // 获取加密狗信息
            JObject sntlInfo = GetSntlInfo();

            // 检查是否获取成功
            if (sntlInfo["code"].Value<int>() != 0)
            {
                return deviceList; // 如果失败，直接返回错误信息
            }

            try
            {
                // 获取hasp节点
                JToken haspNode = sntlInfo["data"]["admin_response"]["hasp"];

                // 处理单个设备的情况
                if (haspNode is JObject)
                {
                    string haspId = haspNode["haspid"].ToString();
                    deviceList.Add(haspId);
                }
                // 处理多个设备的情况
                else if (haspNode is JArray haspArray)
                {
                    foreach (var hasp in haspArray)
                    {
                        string haspId = hasp["haspid"].ToString();
                        deviceList.Add(haspId);
                    }
                }

                return deviceList;
            }
            catch (Exception ex)
            {
                return deviceList;
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_context != IntPtr.Zero)
                {
                    int status = SNTLDllLoader.Instance.sntl_admin_context_delete(_context);
                    if (status != (int)SntlAdminStatus.SNTL_ADMIN_STATUS_OK)
                    {
                        Console.WriteLine($"释放SNTL上下文失败，错误码：{status}，{GetStatusDescription(status)}");
                    }
                    _context = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        ~SNTL()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    public class SNTLUtils
    {
        /// <summary>
        /// 默认的供应商范围XML
        /// </summary>
        public static string DefaultScope =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
            "<haspscope>" +
            "  <vendor id=\"26146\" />" +
            "</haspscope>";

        /// <summary>
        /// 获取HASP ID的格式XML
        /// </summary>
        public static string HaspIdFormat =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
            "<admin>" +
            "  <hasp>" +
            "    <element name=\"haspid\" />" +
            "  </hasp>" +
            "</admin>";

        public static JArray GetDeviceList()
        {
            SNTL sntl = new SNTL();
            JArray deviceList = sntl.GetDeviceList();
            sntl.Dispose();
            return deviceList;
        }
    }
}