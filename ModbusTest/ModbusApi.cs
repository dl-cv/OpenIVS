using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using EasyModbus;

namespace DLCV
{
    internal class ModbusApi
    {
        private ModbusClient _modbusClient;
        private readonly object _lockObject = new object();

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
            _modbusClient = new ModbusClient();
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
            if (_modbusClient.Connected)
            {
                _modbusClient.Disconnect();
            }

            _modbusClient = new ModbusClient(portName)
            {
                Baudrate = baudRate,
                Parity = parity,
                StopBits = stopBits,
                ConnectionTimeout = 1000,
                UnitIdentifier = deviceId
            };
        }

        /// <summary>
        /// 打开串口
        /// </summary>
        /// <returns>是否成功</returns>
        public bool Open()
        {
            try
            {
                if (_modbusClient.Connected)
                {
                    return true;
                }

                _modbusClient.Connect();
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
            if (_modbusClient.Connected)
            {
                _modbusClient.Disconnect();
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
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("串口未打开");
            }

            lock (_lockObject)
            {
                try
                {
                    // 使用EasyModbus库不需要直接构造Modbus帧
                    // 这里根据命令类型调用相应的方法
                    switch (command)
                    {
                        case 0x01: // 读线圈
                            bool[] coilValues = _modbusClient.ReadCoils(address, value);
                            return ConvertBoolArrayToResponse(coilValues, command);
                        case 0x05: // 写单个线圈
                            _modbusClient.WriteSingleCoil(address, value == 0xFF00);
                            byte[] response = new byte[8];
                            response[0] = _modbusClient.UnitIdentifier;
                            response[1] = command;
                            response[2] = (byte)(address >> 8);
                            response[3] = (byte)(address & 0xFF);
                            response[4] = (byte)(value >> 8);
                            response[5] = (byte)(value & 0xFF);
                            return response;
                        default:
                            throw new NotSupportedException("不支持的命令类型");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Modbus错误: {ex.Message}");
                    return null;
                }
            }
        }

        private byte[] ConvertBoolArrayToResponse(bool[] values, byte functionCode)
        {
            int byteCount = (values.Length + 7) / 8;
            byte[] response = new byte[3 + byteCount];
            response[0] = _modbusClient.UnitIdentifier;
            response[1] = functionCode;
            response[2] = (byte)byteCount;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                {
                    response[3 + i / 8] |= (byte)(1 << (i % 8));
                }
            }

            return response;
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
        /// 读线圈寄存器
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="count">读取数量</param>
        /// <returns>寄存器值</returns>
        public bool[] Read(UInt16 address, UInt16 count)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("串口未打开");
            }

            if (count > 2000)
            {
                throw new ArgumentOutOfRangeException("count", "读取数量不能超过2000");
            }

            try
            {
                return _modbusClient.ReadCoils(address, count);
            }
            catch (Exception ex)
            {
                throw new Exception("读取失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 写线圈寄存器
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="value">值（0xFF00表示ON，0x0000表示OFF）</param>
        /// <returns>是否成功</returns>
        public bool Write(UInt16 address, UInt16 value)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("串口未打开");
            }

            try
            {
                _modbusClient.WriteSingleCoil(address, value == 0xFF00);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取保持寄存器
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="count">寄存器数量</param>
        /// <returns>寄存器值数组</returns>
        public int[] ReadHoldingRegisters(UInt16 address, UInt16 count)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("串口未打开");
            }

            try
            {
                return _modbusClient.ReadHoldingRegisters(address, count);
            }
            catch (Exception ex)
            {
                throw new Exception("读取保持寄存器失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 写入单个保持寄存器
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="value">值</param>
        /// <returns>是否成功</returns>
        public bool WriteSingleRegister(UInt16 address, UInt16 value)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("串口未打开");
            }

            try
            {
                _modbusClient.WriteSingleRegister(address, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取浮点数值（占用2个寄存器）
        /// </summary>
        /// <param name="address">起始寄存器地址</param>
        /// <returns>浮点数值</returns>
        public float ReadFloat(UInt16 address)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("串口未打开");
            }

            try
            {
                int[] registers = _modbusClient.ReadHoldingRegisters(address, 2);
                byte[] bytes = new byte[4];
                bytes[0] = (byte)(registers[0] & 0xFF);
                bytes[1] = (byte)(registers[0] >> 8);
                bytes[2] = (byte)(registers[1] & 0xFF);
                bytes[3] = (byte)(registers[1] >> 8);
                return BitConverter.ToSingle(bytes, 0);
            }
            catch (Exception ex)
            {
                throw new Exception("读取浮点数失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 写入浮点数值（占用2个寄存器）
        /// </summary>
        /// <param name="address">起始寄存器地址</param>
        /// <param name="value">浮点数值</param>
        /// <returns>是否成功</returns>
        public bool WriteFloat(UInt16 address, float value)
        {
            if (!_modbusClient.Connected)
            {
                throw new InvalidOperationException("串口未打开");
            }

            try
            {
                byte[] bytes = BitConverter.GetBytes(value);
                int register1 = bytes[0] | (bytes[1] << 8);
                int register2 = bytes[2] | (bytes[3] << 8);

                int[] registers = new int[] { register1, register2 };
                _modbusClient.WriteMultipleRegisters(address, registers);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
