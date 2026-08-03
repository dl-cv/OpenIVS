using System;
using System.IO.Ports;
using DLCV;
using DLCV.SequenceGraph;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    public sealed class SerialModbusClient : IModbusClient
    {
        private readonly object _sync = new object();
        private readonly AppSettings _settings;
        private readonly ModbusApi _api = new ModbusApi();
        private bool _connected;

        public SerialModbusClient(AppSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException("settings");
        }

        public bool Connect(string host, int port, byte deviceId)
        {
            lock (_sync)
            {
                if (_connected) return true;
                StopBits stopBits;
                Parity parity;
                if (!Enum.TryParse(_settings.StopBits, true, out stopBits)) stopBits = StopBits.One;
                if (!Enum.TryParse(_settings.Parity, true, out parity)) parity = Parity.None;
                _api.SetSerialPort(_settings.PortName, _settings.BaudRate, _settings.DataBits, stopBits, parity, (byte)_settings.DeviceId);
                _connected = _api.Open();
                return _connected;
            }
        }

        public void Close()
        {
            lock (_sync)
            {
                if (_connected) _api.Close();
                _connected = false;
            }
        }

        public ushort ReadHoldingRegister(ushort address)
        {
            return ReadHoldingRegisters(address, 1)[0];
        }

        public ushort[] ReadHoldingRegisters(ushort address, ushort count)
        {
            lock (_sync)
            {
                EnsureConnected();
                var values = _api.ReadHoldingRegisters(address, count);
                var result = new ushort[values.Length];
                for (var i = 0; i < values.Length; i++) result[i] = unchecked((ushort)values[i]);
                return result;
            }
        }

        public void WriteSingleRegister(ushort address, ushort value)
        {
            lock (_sync)
            {
                EnsureConnected();
                if (!_api.WriteSingleRegister(address, value))
                    throw new InvalidOperationException("PLC 寄存器写入失败: " + address);
            }
        }

        private void EnsureConnected()
        {
            if (!_connected && !Connect(null, 0, (byte)_settings.DeviceId))
                throw new InvalidOperationException("PLC 串口连接失败: " + _settings.PortName);
        }
    }
}
