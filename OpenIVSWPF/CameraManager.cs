using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using DLCV.Camera;

namespace OpenIVSWPF.Managers
{
    /// <summary>
    /// 相机管理单例类
    /// </summary>
    public class CameraInstance
    {
        #region 单例模式实现
        private static CameraManager _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取CameraInstance的单例实例
        /// </summary>
        public static CameraManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new CameraManager();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion
    }
} 