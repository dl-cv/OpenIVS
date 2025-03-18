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

namespace ModbusTest
{
    public partial class Form1 : Form
    {
        private ModbusApi _modbusApi;
        private bool _isConnected = false;

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
    }
}
