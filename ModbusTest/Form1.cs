using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLCV;
using System.Configuration;
using System.IO;
using System.Xml;

namespace ModbusTest
{
    public partial class Form1 : Form
    {
        private ModbusApi _modbusApi;
        private bool _isConnected = false;
        private string _configFilePath = Path.Combine(Application.StartupPath, "ModbusSettings.xml");

        public Form1()
        {
            InitializeComponent();
            // 初始化ModbusApi
            _modbusApi = new ModbusApi();
            // 加载串口列表
            RefreshSerialPorts();
        }

        private void RefreshSerialPorts()
        {
            cmbPort.Items.Clear();

            string[] ports = SerialPort.GetPortNames();
            if (ports.Length > 0)
            {
                cmbPort.Items.AddRange(ports);
                cmbPort.SelectedIndex = 0;
            }
        }

        private void BtnRefreshPorts_Click(object sender, EventArgs e)
        {
            RefreshSerialPorts();
            LogMessage("串口列表已刷新");
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                try
                {
                    if (cmbPort.SelectedItem == null)
                    {
                        MessageBox.Show("请选择串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // 获取停止位
                    StopBits stopBits = StopBits.One;
                    switch (cmbStopBits.SelectedItem.ToString())
                    {
                        case "One":
                            stopBits = StopBits.One;
                            break;
                        case "Two":
                            stopBits = StopBits.Two;
                            break;
                    }

                    // 获取校验位
                    Parity parity = Parity.None;
                    switch (cmbParity.SelectedItem.ToString())
                    {
                        case "None":
                            parity = Parity.None;
                            break;
                        case "Odd":
                            parity = Parity.Odd;
                            break;
                        case "Even":
                            parity = Parity.Even;
                            break;
                    }

                    // 设置串口
                    _modbusApi.SetSerialPort(
                        cmbPort.SelectedItem.ToString(),
                        Convert.ToInt32(cmbBaudRate.SelectedItem),
                        Convert.ToInt32(cmbDataBits.SelectedItem),
                        stopBits,
                        parity,
                        Convert.ToByte(nudDeviceId.Value)
                    );

                    // 打开串口
                    if (_modbusApi.Open())
                    {
                        _isConnected = true;
                        btnConnect.Text = "关闭串口";
                        LogMessage("串口已打开: " + cmbPort.SelectedItem.ToString());

                        // 启用操作控件
                        gbRegister.Enabled = true;
                        gbDirectOperations.Enabled = true;
                        
                        // 保存当前串口配置
                        SaveSettings();
                    }
                    else
                    {
                        MessageBox.Show("无法打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("打开串口时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                try
                {
                    _modbusApi.Close();
                    _isConnected = false;
                    btnConnect.Text = "打开串口";
                    LogMessage("串口已关闭");

                    // 禁用操作控件
                    gbRegister.Enabled = false;
                    gbDirectOperations.Enabled = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("关闭串口时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnSend_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("请先打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                ushort address = Convert.ToUInt16(nudAddress.Value);
                ushort value = Convert.ToUInt16(nudValue.Value);

                switch (cmbOperation.SelectedIndex)
                {
                    case 0: // 读线圈
                        LogMessage(string.Format("发送: 读线圈, 地址: 0x{0:X4}", address, value));

                        try
                        {
                            bool[] results = _modbusApi.Read(address, value);
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("接收:");
                            for (int i = 0; i < results.Length; i++)
                            {
                                sb.AppendLine(string.Format("线圈[0x{0:X4}] = {1}", address + i, results[i]));
                            }
                            LogMessage(sb.ToString());
                        }
                        catch (Exception ex)
                        {
                            LogMessage("错误: " + ex.Message);
                        }
                        break;

                    case 1: // 写单个线圈
                        LogMessage(string.Format("发送: 写线圈, 地址: 0x{0:X4}, 值: {1}", address, value));
                        try
                        {
                            bool result = _modbusApi.Write(address, value);
                            LogMessage("接收: " + (result ? "成功" : "失败"));
                        }
                        catch (Exception ex)
                        {
                            LogMessage("错误: " + ex.Message);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送命令时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnReadReg_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("请先打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                // 使用与寄存器操作相同的地址控件
                ushort address = Convert.ToUInt16(nudAddress.Value);

                LogMessage(string.Format("发送: 读寄存器, 地址: 0x{0:X4}", address));

                try
                {
                    bool[] results = _modbusApi.Read(address, 1);
                    LogMessage(string.Format("接收: 寄存器[0x{0:X4}] = {1}", address, results[0]));
                }
                catch (Exception ex)
                {
                    LogMessage("错误: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送命令时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnWriteReg_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("请先打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                // 使用与寄存器操作相同的地址控件
                ushort address = Convert.ToUInt16(nudAddress.Value);
                ushort value = Convert.ToUInt16(txtRegValue.Text, txtRegValue.Text.StartsWith("0x") ? 16 : 10);

                LogMessage(string.Format("发送: 写寄存器, 地址: 0x{0:X4}, 值: {1}", address, value));

                try
                {
                    bool result = _modbusApi.Write(address, value);
                    LogMessage("接收: " + (result ? "成功" : "失败"));
                }
                catch (Exception ex)
                {
                    LogMessage("错误: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送命令时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnReadFloat_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("请先打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                ushort address = Convert.ToUInt16(nudAddress.Value);

                LogMessage(string.Format("发送: 读浮点数, 地址: 0x{0:X4}", address));

                try
                {
                    float result = _modbusApi.ReadFloat(address);
                    txtFloatValue.Text = result.ToString("F3");
                    LogMessage(string.Format("接收: 浮点数[0x{0:X4}] = {1}", address, result));
                }
                catch (Exception ex)
                {
                    LogMessage("错误: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送命令时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnWriteFloat_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("请先打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                ushort address = Convert.ToUInt16(nudAddress.Value);
                float value = Convert.ToSingle(txtFloatValue.Text);

                LogMessage(string.Format("发送: 写浮点数, 地址: 0x{0:X4}, 值: {1}", address, value));

                try
                {
                    bool result = _modbusApi.WriteFloat(address, value);
                    LogMessage("接收: " + (result ? "成功" : "失败"));
                }
                catch (Exception ex)
                {
                    LogMessage("错误: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送命令时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnReadInt_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("请先打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                ushort address = Convert.ToUInt16(nudAddress.Value);

                LogMessage(string.Format("发送: 读整型值, 地址: 0x{0:X4}", address));

                try
                {
                    int[] results = _modbusApi.ReadHoldingRegisters(address, 1);
                    txtIntValue.Text = results[0].ToString();
                    LogMessage(string.Format("接收: 整型值[0x{0:X4}] = {1}", address, results[0]));
                }
                catch (Exception ex)
                {
                    LogMessage("错误: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送命令时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnWriteInt_Click(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("请先打开串口", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                ushort address = Convert.ToUInt16(nudAddress.Value);
                ushort value = Convert.ToUInt16(txtIntValue.Text);

                LogMessage(string.Format("发送: 写整型值, 地址: 0x{0:X4}, 值: {1}", address, value));

                try
                {
                    bool result = _modbusApi.WriteSingleRegister(address, value);
                    LogMessage("接收: " + (result ? "成功" : "失败"));
                }
                catch (Exception ex)
                {
                    LogMessage("错误: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送命令时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LogMessage(string message)
        {
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // 设置初始默认值
            if (cmbBaudRate.Items.Count > 0)
                cmbBaudRate.SelectedItem = 9600;

            if (cmbDataBits.Items.Count > 0)
                cmbDataBits.SelectedItem = 8;

            if (cmbStopBits.Items.Count > 0)
                cmbStopBits.SelectedIndex = 0; // "One"

            if (cmbParity.Items.Count > 0)
                cmbParity.SelectedIndex = 0; // "None"

            if (cmbOperation.Items.Count > 0)
                cmbOperation.SelectedIndex = 0;

            // 设置默认地址为500
            nudAddress.Value = 1280;
            txtRegValue.Text = "0";

            // 初始禁用操作控件
            gbRegister.Enabled = false;
            gbDirectOperations.Enabled = false;

            // 加载上次保存的串口配置
            LoadSettings();

            LogMessage("应用程序已启动");
        }

        private void btnClearLog_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
            LogMessage("日志已清除");
        }

        private void chkHex_CheckedChanged(object sender, EventArgs e)
        {
            // 更改数值输入框的显示格式
            nudAddress.Hexadecimal = chkHex.Checked;
            nudValue.Hexadecimal = chkHex.Checked;

            LogMessage(string.Format("显示格式已切换为: {0}", chkHex.Checked ? "十六进制" : "十进制"));
        }

        private void cmbPort_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 当选择新的串口时，设置默认参数
            if (cmbPort.SelectedItem != null)
            {
                // 设置默认波特率为9600
                if (cmbBaudRate.Items.Contains(9600))
                    cmbBaudRate.SelectedItem = 9600;

                // 设置默认数据位为8
                if (cmbDataBits.Items.Contains(8))
                    cmbDataBits.SelectedItem = 8;

                // 设置默认停止位为One
                if (cmbStopBits.Items.Contains("One"))
                    cmbStopBits.SelectedItem = "One";
                else
                    cmbStopBits.SelectedIndex = 0;

                // 设置默认校验位为None
                if (cmbParity.Items.Contains("None"))
                    cmbParity.SelectedItem = "None";
                else
                    cmbParity.SelectedIndex = 0;

                LogMessage(string.Format("已选择串口: {0}，默认参数已设置为9600,8,1,None", cmbPort.SelectedItem));
            }
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            // 窗口大小调整时调整控件布局
            // 这部分在Designer文件中已通过Anchor属性实现
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 如果串口已连接，先关闭串口
            if (_isConnected)
            {
                try
                {
                    _modbusApi.Close();
                    _isConnected = false;
                }
                catch (Exception ex)
                {
                    LogMessage("关闭串口时发生错误: " + ex.Message);
                }
            }
            
            // 保存当前串口配置
            SaveSettings();
        }

        // 保存串口配置到XML文件
        private void SaveSettings()
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                XmlElement root;

                // 如果文件存在，则加载现有文件
                if (File.Exists(_configFilePath))
                {
                    doc.Load(_configFilePath);
                    root = doc.DocumentElement;
                }
                else
                {
                    // 创建新的XML文档
                    root = doc.CreateElement("Settings");
                    doc.AppendChild(root);
                }

                // 保存串口配置
                SetSettingValue(doc, root, "Port", cmbPort.SelectedItem?.ToString() ?? "");
                SetSettingValue(doc, root, "BaudRate", cmbBaudRate.SelectedItem?.ToString() ?? "9600");
                SetSettingValue(doc, root, "DataBits", cmbDataBits.SelectedItem?.ToString() ?? "8");
                SetSettingValue(doc, root, "StopBits", cmbStopBits.SelectedItem?.ToString() ?? "One");
                SetSettingValue(doc, root, "Parity", cmbParity.SelectedItem?.ToString() ?? "None");
                SetSettingValue(doc, root, "DeviceId", nudDeviceId.Value.ToString());

                // 保存文件
                doc.Save(_configFilePath);
                LogMessage("串口配置已保存");
            }
            catch (Exception ex)
            {
                LogMessage("保存配置失败: " + ex.Message);
            }
        }

        // 在XML文档中设置配置项的值
        private void SetSettingValue(XmlDocument doc, XmlElement root, string key, string value)
        {
            XmlNode node = root.SelectSingleNode(key);
            if (node == null)
            {
                // 如果节点不存在，则创建新节点
                node = doc.CreateElement(key);
                root.AppendChild(node);
            }
            node.InnerText = value;
        }

        // 从XML文件加载串口配置
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(_configFilePath);
                    XmlElement root = doc.DocumentElement;

                    // 加载保存的串口设置
                    string portName = GetSettingValue(root, "Port", "");
                    string baudRate = GetSettingValue(root, "BaudRate", "9600");
                    string dataBits = GetSettingValue(root, "DataBits", "8");
                    string stopBits = GetSettingValue(root, "StopBits", "One");
                    string parity = GetSettingValue(root, "Parity", "None");
                    string deviceId = GetSettingValue(root, "DeviceId", "1");

                    // 设置串口
                    if (!string.IsNullOrEmpty(portName) && cmbPort.Items.Contains(portName))
                        cmbPort.SelectedItem = portName;

                    // 设置波特率
                    if (cmbBaudRate.Items.Contains(int.Parse(baudRate)))
                        cmbBaudRate.SelectedItem = int.Parse(baudRate);

                    // 设置数据位
                    if (cmbDataBits.Items.Contains(int.Parse(dataBits)))
                        cmbDataBits.SelectedItem = int.Parse(dataBits);

                    // 设置停止位
                    if (cmbStopBits.Items.Contains(stopBits))
                        cmbStopBits.SelectedItem = stopBits;
                    else
                        cmbStopBits.SelectedIndex = 0;

                    // 设置校验位
                    if (cmbParity.Items.Contains(parity))
                        cmbParity.SelectedItem = parity;
                    else
                        cmbParity.SelectedIndex = 0;

                    // 设置设备ID
                    if (!string.IsNullOrEmpty(deviceId))
                        nudDeviceId.Value = decimal.Parse(deviceId);

                    LogMessage("已加载上次保存的串口配置");
                }
            }
            catch (Exception ex)
            {
                LogMessage("加载配置失败: " + ex.Message);
            }
        }

        // 获取XML文档中配置项的值
        private string GetSettingValue(XmlElement root, string key, string defaultValue)
        {
            XmlNode node = root.SelectSingleNode(key);
            return node != null ? node.InnerText : defaultValue;
        }
    }
}
