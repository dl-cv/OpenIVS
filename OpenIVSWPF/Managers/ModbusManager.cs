using System;
using System.IO.Ports;
using System.Threading.Tasks;
using DLCV;

namespace OpenIVSWPF.Managers
{
    public class ModbusInstance
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

    /// <summary>
    /// Modbus初始化和通信管理类
    /// </summary>
    public class ModbusManager
    {
        private ModbusApi _modbusApi = ModbusInstance.Instance;
        private bool _isModbusConnected = false;
        private float _currentPosition = 0;
        private Action<string> _statusCallback;
        private Action<string> _deviceStatusCallback;
        private Action<float> _positionUpdateCallback;

        public bool IsConnected => _isModbusConnected;
        public float CurrentPosition => _currentPosition;

        public ModbusManager(Action<string> statusCallback, Action<string> deviceStatusCallback, Action<float> positionUpdateCallback)
        {
            _statusCallback = statusCallback;
            _deviceStatusCallback = deviceStatusCallback;
            _positionUpdateCallback = positionUpdateCallback;
        }

        /// <summary>
        /// 初始化Modbus设备
        /// </summary>
        /// <param name="settings">系统设置</param>
        public void InitializeModbus(Settings settings)
        {
            try
            {
                _statusCallback?.Invoke("正在初始化Modbus设备...");

                // 关闭已有连接
                if (_isModbusConnected && _modbusApi != null)
                {
                    _modbusApi.Close();
                    _isModbusConnected = false;
                }

                // 使用设置中的串口参数
                if (string.IsNullOrEmpty(settings.PortName))
                {
                    // 如果未设置串口，尝试获取第一个可用的串口
                    string[] ports = SerialPort.GetPortNames();
                    if (ports.Length == 0)
                    {
                        _statusCallback?.Invoke("未检测到串口设备");
                        _deviceStatusCallback?.Invoke("未检测到串口");
                        return;
                    }
                    settings.PortName = ports[0];
                }

                // 设置串口参数
                _modbusApi.SetSerialPort(
                    settings.PortName,  // 串口
                    settings.BaudRate,  // 波特率
                    settings.DataBits,  // 数据位
                    settings.StopBits,  // 停止位
                    settings.Parity,    // 校验位
                    (byte)settings.DeviceId   // 设备ID
                );

                // 打开串口
                if (_modbusApi.Open())
                {
                    _isModbusConnected = true;
                    _statusCallback?.Invoke($"Modbus设备已连接，串口：{settings.PortName}");
                    _deviceStatusCallback?.Invoke("已连接");

                    // 设置当前速度
                    _modbusApi.WriteFloat(0, settings.Speed);

                    // 读取当前位置
                    float currentPosition = _modbusApi.ReadFloat(32);
                    _currentPosition = currentPosition;
                    _positionUpdateCallback?.Invoke(currentPosition);
                }
                else
                {
                    _statusCallback?.Invoke("Modbus设备连接失败");
                    _deviceStatusCallback?.Invoke("连接失败");
                }
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"Modbus初始化错误：{ex.Message}");
                _deviceStatusCallback?.Invoke("初始化错误");
                throw;
            }
        }

        /// <summary>
        /// 移动到指定位置
        /// </summary>
        /// <param name="position">目标位置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>移动是否成功</returns>
        public async Task<bool> MoveToPositionAsync(float position, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                if (!_isModbusConnected)
                    return false;

                // 设置目标位置（地址8，浮点数）
                _statusCallback?.Invoke($"正在移动到位置：{position}");
                bool resultSetPosition = _modbusApi.WriteFloat(8, position);
                if (!resultSetPosition)
                {
                    _statusCallback?.Invoke("设置目标位置失败");
                    return false;
                }

                // 发送移动命令（地址50，整数2）
                bool resultCommand = _modbusApi.WriteSingleRegister(50, 2);
                if (!resultCommand)
                {
                    _statusCallback?.Invoke("发送移动命令失败");
                    return false;
                }

                // 等待移动完成（轮询当前位置）
                bool isReached = false;
                while (!isReached && !cancellationToken.IsCancellationRequested)
                {
                    // 读取当前位置（地址32，浮点数）
                    float currentPosition = _modbusApi.ReadFloat(32);
                    _currentPosition = currentPosition;

                    // 更新位置显示
                    _positionUpdateCallback?.Invoke(currentPosition);

                    // 判断是否到达目标位置（允许一定误差）
                    if (Math.Abs(currentPosition - position) < 1.0f)
                    {
                        isReached = true;
                    }
                    else
                    {
                        // 等待100ms再次检查
                        await Task.Delay(100, cancellationToken);
                    }
                }

                if (isReached)
                {
                    _statusCallback?.Invoke($"已到达位置：{position}");
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                _statusCallback?.Invoke("移动操作已取消");
                return false;
            }
            catch (OperationCanceledException)
            {
                _statusCallback?.Invoke("移动操作已取消");
                return false;
            }
            catch (Exception ex)
            {
                _statusCallback?.Invoke($"移动过程中发生错误：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭Modbus连接
        /// </summary>
        public void Close()
        {
            if (_isModbusConnected)
            {
                _modbusApi?.Close();
                _isModbusConnected = false;
            }
        }

        /// <summary>
        /// 发送停止命令
        /// </summary>
        public void SendStopCommand()
        {
            if (_isModbusConnected)
            {
                _modbusApi.WriteSingleRegister(50, 4); // 发送停止命令
            }
        }
    }
} 