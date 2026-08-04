using System;
using System.IO;
using System.Net.Sockets;
using DLCV.SequenceGraph;

namespace OpenIVS2.Services
{
    public sealed class TcpModbusClient : IModbusClient
    {
        private readonly object _sync = new object();
        private TcpClient _client;
        private NetworkStream _stream;
        private string _host;
        private int _port;
        private byte _deviceId;
        private ushort _transactionId;

        public bool Connect(string host, int port, byte deviceId)
        {
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
                if (port <= 0) port = 502;
                if (IsConnected() && string.Equals(_host, host, StringComparison.OrdinalIgnoreCase) &&
                    _port == port && _deviceId == deviceId)
                    return true;

                CloseInternal();
                var client = new TcpClient { NoDelay = true, ReceiveTimeout = 1000, SendTimeout = 1000 };
                var connect = client.BeginConnect(host, port, null, null);
                try
                {
                    if (!connect.AsyncWaitHandle.WaitOne(1000))
                        throw new TimeoutException("Modbus TCP 连接超时: " + host + ":" + port);
                    client.EndConnect(connect);
                }
                catch
                {
                    client.Close();
                    throw;
                }
                finally
                {
                    connect.AsyncWaitHandle.Close();
                }

                _client = client;
                _stream = client.GetStream();
                _host = host;
                _port = port;
                _deviceId = deviceId;
                return true;
            }
        }

        public void Close()
        {
            lock (_sync) CloseInternal();
        }

        public ushort ReadHoldingRegister(ushort address)
        {
            return ReadHoldingRegisters(address, 1)[0];
        }

        public ushort[] ReadHoldingRegisters(ushort address, ushort count)
        {
            if (count < 1 || count > 125) throw new ArgumentOutOfRangeException("count");
            lock (_sync)
            {
                var response = SendRequest(3, address, count);
                var byteCount = response[1];
                if (byteCount != count * 2 || response.Length != byteCount + 2)
                    throw new IOException("Modbus TCP 读取响应长度无效");
                var values = new ushort[count];
                for (var i = 0; i < count; i++)
                    values[i] = ReadUInt16(response, 2 + i * 2);
                return values;
            }
        }

        public void WriteSingleRegister(ushort address, ushort value)
        {
            lock (_sync)
            {
                var response = SendRequest(6, address, value);
                if (response.Length != 5 || ReadUInt16(response, 1) != address || ReadUInt16(response, 3) != value)
                    throw new IOException("Modbus TCP 写入响应与请求不一致");
            }
        }

        private byte[] SendRequest(byte function, ushort address, ushort value)
        {
            if (!IsConnected()) throw new InvalidOperationException("Modbus TCP 尚未连接");
            var transactionId = unchecked(++_transactionId);
            var request = new byte[12];
            WriteUInt16(request, 0, transactionId);
            WriteUInt16(request, 2, 0);
            WriteUInt16(request, 4, 6);
            request[6] = _deviceId;
            request[7] = function;
            WriteUInt16(request, 8, address);
            WriteUInt16(request, 10, value);

            try
            {
                _stream.Write(request, 0, request.Length);
                var header = ReadExact(7);
                if (ReadUInt16(header, 0) != transactionId || ReadUInt16(header, 2) != 0 || header[6] != _deviceId)
                    throw new IOException("Modbus TCP 响应头无效");
                var remaining = ReadUInt16(header, 4) - 1;
                if (remaining < 2 || remaining > 254) throw new IOException("Modbus TCP 响应长度无效");
                var response = ReadExact(remaining);
                if ((response[0] & 0x80) != 0)
                    throw new IOException("Modbus TCP 异常响应: " + response[1]);
                if (response[0] != function) throw new IOException("Modbus TCP 功能码不匹配");
                return response;
            }
            catch
            {
                CloseInternal();
                throw;
            }
        }

        private byte[] ReadExact(int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new IOException("Modbus TCP 连接已断开");
                offset += read;
            }
            return buffer;
        }

        private bool IsConnected()
        {
            return _client != null && _client.Connected && _stream != null;
        }

        private void CloseInternal()
        {
            try { if (_stream != null) _stream.Dispose(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            _stream = null;
            _client = null;
            _host = null;
            _port = 0;
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }
    }
}
