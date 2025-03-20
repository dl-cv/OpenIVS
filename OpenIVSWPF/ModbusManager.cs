using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using DLCV;

namespace OpenIVSWPF.Managers
{
    /// <summary>
    /// Modbus通信单例管理器
    /// </summary>
    public class ModbusManager
    {
        #region 单例模式实现
        private static ModbusApi _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取ModbusManager的单例实例
        /// </summary>
        public static ModbusApi Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ModbusApi();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion
    }
} 