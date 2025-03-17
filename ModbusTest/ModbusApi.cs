using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

namespace DLCV
{
    internal class ModbusApi
    {
        private SerialPort _serialPort;
        private byte _deviceId = 2;
        private readonly object _lockObject = new object();
        private readonly int _readTimeout = 1000;
        private readonly int _writeTimeout = 1000;

        /// <summary>
        /// 获取所有可用的串口名称
        /// </summary>
        /// <returns>串口名称列表</returns>
        public static List<string> GetPortNames()
        {
            return SerialPort.GetPortNames().ToList();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ModbusApi()
        {
            _serialPort = new SerialPort();
        }

        /// <summary>
        /// 设置串口参数
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="dataBits">数据位</param>
        /// <param name="stopBits">停止位</param>
        /// <param name="parity">校验位</param>
        /// <param name="deviceId">设备ID</param>
        public void SetSerialPort(string portName, int baudRate = 9600, int dataBits = 8,
            StopBits stopBits = StopBits.One, Parity parity = Parity.None, byte deviceId = 1)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort = new SerialPort
            {
                PortName = portName,
                BaudRate = baudRate,
                DataBits = dataBits,
                StopBits = stopBits,
                Parity = parity,
                ReadTimeout = _readTimeout,
                WriteTimeout = _writeTimeout
            };

            _deviceId = deviceId;
        }

        /// <summary>
        /// 打开串口
        /// </summary>
        /// <returns>是否成功</returns>
        public bool Open()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    return true;
                }

                _serialPort.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public void Close()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        /// <summary>
        /// 发送自定义指令
        /// </summary>
        /// <param name="command">命令类型</param>
        /// <param name="address">地址</param>
        /// <param name="value">值</param>
        /// <returns>响应数据</returns>
        public byte[] SendCommand(byte command, UInt16 address, UInt16 value)
        {
            if (!_serialPort.IsOpen)
            {
                throw new InvalidOperationException("串口未打开");
            }

            byte[] frame = new byte[8];
            frame[0] = _deviceId;       // 设备ID
            frame[1] = command;         // 命令类型
            frame[2] = (byte)(address >> 8);   // 地址高位
            frame[3] = (byte)(address & 0xFF); // 地址低位
            frame[4] = (byte)(value >> 8);     // 值高位
            frame[5] = (byte)(value & 0xFF);   // 值低位

            // 计算CRC16校验码
            UInt16 crc = CalculateCRC16(frame, 0, 6);
            frame[6] = (byte)(crc & 0xFF);     // CRC低位
            frame[7] = (byte)(crc >> 8);       // CRC高位

            lock (_lockObject)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // 发送数据
                _serialPort.Write(frame, 0, frame.Length);

                // 读取响应
                Thread.Sleep(100); // 等待设备响应

                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead == 0)
                {
                    return null;
                }

                byte[] response = new byte[bytesToRead];
                _serialPort.Read(response, 0, bytesToRead);
                return response;
            }
        }

        /// <summary>
        /// 将字节数组转换为十六进制字符串
        /// </summary>
        /// <param name="data">字节数组</param>
        /// <returns>十六进制字符串</returns>
        public static string BytesToHexString(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder(data.Length * 3);
            foreach (byte b in data)
            {
                sb.Append(b.ToString("X2") + " ");
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 读寄存器
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="count">读取数量</param>
        /// <returns>寄存器值</returns>
        public bool[] Read(UInt16 address, UInt16 count)
        {
            if (count > 2000)
            {
                throw new ArgumentOutOfRangeException("count", "读取数量不能超过2000");
            }
            // 发送读线圈命令
            byte[] response = SendCommand(0x01, address, count);

            if (response == null || response.Length < 5)
            {
                throw new Exception("未收到响应或响应数据长度不足");
            }

            if (response[0] != _deviceId || response[1] != 0x01)
            {
                throw new Exception("响应数据格式错误");
            }

            int byteCount = response[2];
            if (response.Length < 3 + byteCount + 2)
            {
                throw new Exception("响应数据长度不匹配");
            }

            // 验证CRC
            UInt16 responseCrc = (UInt16)((response[3 + byteCount + 1] << 8) | response[3 + byteCount]);
            UInt16 calculatedCrc = CalculateCRC16(response, 0, 3 + byteCount);
            if (responseCrc != calculatedCrc)
            {
                throw new Exception("CRC校验失败");
            }

            bool[] readValue = new bool[count];
            for (int i = 0; i < count; i++)
            {
                readValue[i] = (response[3 + i / 8] & (1 << (i % 8))) != 0;
            }

            return readValue;
        }

        /// <summary>
        /// 写寄存器
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="value">值（true表示ON，false表示OFF）</param>
        /// <returns>是否成功</returns>
        public bool Write(UInt16 address, UInt16 writeValue)
        {
            // 发送写单个线圈命令
            byte[] response = SendCommand(0x05, address, writeValue);

            // 验证响应
            if (response == null || response.Length < 8)
            {
                return false;
            }

            if (response[0] != _deviceId || response[1] != 0x05)
            {
                return false;
            }

            UInt16 responseAddress = (UInt16)((response[2] << 8) | response[3]);
            UInt16 responseValue = (UInt16)((response[4] << 8) | response[5]);

            return responseAddress == address && responseValue == writeValue;
        }

        /// <summary>
        /// 计算CRC16校验码
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="start">起始位置</param>
        /// <param name="length">长度</param>
        /// <returns>CRC16校验码</returns>
        public static UInt16 CalculateCRC16(byte[] data, int start, int length)
        {
            UInt16 crc = 0xFFFF;

            for (int i = start; i < start + length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }
    }
}
