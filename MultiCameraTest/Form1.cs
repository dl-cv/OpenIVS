using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLCV.Camera;
using MultiCameraTest;

namespace CameraManagerTest
{
    public partial class Form1 : Form
    {
        private List<DeviceInfoWrapper> _deviceList;
        private List<CameraTabView> _cameraTabs = new List<CameraTabView>();

        // 线程安全保护
        private readonly object _lockObject = new object();

        // 设备枚举限制 - 避免同时多个线程枚举设备
        private static readonly SemaphoreSlim _deviceEnumerationSemaphore = new SemaphoreSlim(1, 1);

        // 相机连接限制 - 避免同时连接过多相机导致资源冲突
        private static readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(3, 3);

        // 相机管理器字典，用于跟踪已连接的相机
        private readonly Dictionary<string, CameraManager> _connectedCameras = new Dictionary<string, CameraManager>();

        public Form1()
        {
            InitializeComponent();

            // 设置窗体最小大小，确保UI有足够空间
            this.MinimumSize = new Size(1550, 1050);
            this.Size = new Size(1600, 1100);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 确保所有控件已正确初始化
            if (_comboDevices == null || _tabCameras == null)
            {
                MessageBox.Show("控件初始化失败，程序可能无法正常工作", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 绑定窗体事件
            this.FormClosing += Form1_FormClosing;
            this.Load += Form1_Load;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                // 异步刷新设备列表
                RefreshDeviceListAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // 停止所有相机并释放资源
                List<CameraManager> managersToDispose = new List<CameraManager>();

                lock (_lockObject)
                {
                    // 收集所有相机管理器
                    managersToDispose.AddRange(_connectedCameras.Values);
                    _connectedCameras.Clear();
                }

                // 在锁外释放资源
                foreach (var manager in managersToDispose)
                {
                    try
                    {
                        manager.StopGrabbing();
                        manager.DisconnectDevice();
                        manager.Dispose();
                    }
                    catch { }
                }

                // 释放所有相机标签页
                foreach (var tab in _cameraTabs)
                {
                    try
                    {
                        tab.Dispose();
                    }
                    catch { }
                }

                // 释放SDK资源
                CameraUtils.ReleaseSDK();
            }
            catch { }
        }

        private void RefreshDeviceList(CameraManager manager)
        {
            // 保留旧方法以兼容，但标记为过时
            try
            {
                _comboDevices.Items.Clear();
                _deviceList = manager.RefreshDeviceList();

                if (_deviceList.Count == 0)
                {
                    _comboDevices.Items.Add("未检测到设备");
                    _comboDevices.SelectedIndex = 0;
                    _comboDevices.Enabled = false;
                    _btnAddCamera.Enabled = false;
                    _btnLoadAllCameras.Enabled = false;
                    UpdateStatus("未检测到相机设备");
                }
                else
                {
                    foreach (var device in _deviceList)
                    {
                        _comboDevices.Items.Add(device.ToString());
                    }
                    _comboDevices.SelectedIndex = 0;
                    _comboDevices.Enabled = true;
                    _btnAddCamera.Enabled = true;
                    _btnLoadAllCameras.Enabled = true;
                    UpdateStatus($"检测到 {_deviceList.Count} 个相机设备");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取设备列表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnAddCamera.Enabled = false;
                _btnLoadAllCameras.Enabled = false;
            }
        }

        /// <summary>
        /// 异步刷新设备列表（线程安全版本）
        /// </summary>
        private async void RefreshDeviceListAsync()
        {
            // 限制设备枚举并发，避免底层驱动冲突
            await _deviceEnumerationSemaphore.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        using (var tempManager = new CameraManager())
                        {
                            var devices = tempManager.RefreshDeviceList();

                            // 在UI线程更新控件
                            this.BeginInvoke(new Action(() =>
                            {
                                lock (_lockObject)
                                {
                                    _deviceList = devices;
                                    UpdateDeviceComboBox();
                                }
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show($"获取设备列表失败: {ex.Message}", "错误",
                                          MessageBoxButtons.OK, MessageBoxIcon.Error);
                            _btnAddCamera.Enabled = false;
                            _btnLoadAllCameras.Enabled = false;
                        }));
                    }
                });
            }
            finally
            {
                _deviceEnumerationSemaphore.Release();
            }
        }

        /// <summary>
        /// 更新设备下拉框（必须在UI线程调用）
        /// </summary>
        private void UpdateDeviceComboBox()
        {
            _comboDevices.Items.Clear();

            if (_deviceList == null || _deviceList.Count == 0)
            {
                _comboDevices.Items.Add("未检测到设备");
                _comboDevices.SelectedIndex = 0;
                _comboDevices.Enabled = false;
                _btnAddCamera.Enabled = false;
                _btnLoadAllCameras.Enabled = false;
                UpdateStatus("未检测到相机设备");
            }
            else
            {
                foreach (var device in _deviceList)
                {
                    _comboDevices.Items.Add(device.ToString());
                }
                _comboDevices.SelectedIndex = 0;
                _comboDevices.Enabled = true;
                _btnAddCamera.Enabled = true;
                _btnLoadAllCameras.Enabled = true;
                UpdateStatus($"检测到 {_deviceList.Count} 个相机设备");
            }
        }

        private async void BtnAddCamera_Click(object sender, EventArgs e)
        {
            int selectedIndex = _comboDevices.SelectedIndex;
            if (selectedIndex < 0 || _deviceList == null || selectedIndex >= _deviceList.Count)
            {
                MessageBox.Show("请选择有效的相机设备", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var deviceInfo = _deviceList[selectedIndex];
            var cameraId = deviceInfo.SerialNumber;

            // 检查相机是否已连接
            lock (_lockObject)
            {
                if (_connectedCameras.ContainsKey(cameraId))
                {
                    MessageBox.Show("该相机已连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            // 禁用按钮防止重复点击
            _btnAddCamera.Enabled = false;
            _btnLoadAllCameras.Enabled = false;

            try
            {
                UpdateStatus($"正在连接相机 {deviceInfo}...");

                // 异步连接相机
                bool success = await ConnectCameraAsync(deviceInfo);

                if (success)
                {
                    UpdateStatus($"相机 {deviceInfo} 连接成功");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连接相机失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("相机连接失败");
            }
            finally
            {
                // 重新启用按钮
                _btnAddCamera.Enabled = true;
                _btnLoadAllCameras.Enabled = true;
            }
        }

        /// <summary>
        /// 异步连接相机（线程安全版本）
        /// </summary>
        private async Task<bool> ConnectCameraAsync(DeviceInfoWrapper deviceInfo)
        {
            if (deviceInfo == null)
                return false;

            var cameraId = deviceInfo.SerialNumber;

            // 限制并发连接数量
            await _connectionSemaphore.WaitAsync();
            try
            {
                CameraTabView cameraTabView = null;
                CameraManager cameraManager = null;

                try
                {
                    // 在UI线程创建标签页
                    await this.InvokeAsync(() =>
                    {
                        cameraTabView = new CameraTabView(cameraId, deviceInfo.ToString(), this);
                    });

                    // 限制设备枚举，避免冲突
                    await _deviceEnumerationSemaphore.WaitAsync();
                    try
                    {
                        // 创建并连接相机管理器
                        cameraManager = new CameraManager();
                        bool connected = cameraManager.ConnectDevice(deviceInfo);

                        if (!connected)
                        {
                            throw new Exception("CameraManager.ConnectDevice(deviceInfo) returned false.");
                        }

                        // 锁定资源，更新状态
                        lock (_lockObject)
                        {
                            // 再次检查是否已连接（避免竞态条件）
                            if (_connectedCameras.ContainsKey(cameraId))
                            {
                                cameraManager.DisconnectDevice();
                                cameraManager.Dispose();
                                throw new Exception("Camera was connected by another thread in the meantime.");
                            }

                            _connectedCameras[cameraId] = cameraManager;
                        }

                        // 设置相机管理器到标签页
                        cameraTabView.SetCameraManager(cameraManager);

                        // 订阅图像更新事件
                        cameraManager.ImageUpdated += (sender, e) =>
                        {
                            cameraTabView.OnImageUpdated(e.Image);
                        };

                        // 在UI线程添加标签页
                        await this.InvokeAsync(() =>
                        {
                            var tabPage = new TabPage(deviceInfo.ToString());
                            cameraTabView.Dock = DockStyle.Fill;
                            tabPage.Controls.Add(cameraTabView);

                            lock (_lockObject)
                            {
                                _cameraTabs.Add(cameraTabView);
                            }
                            _tabCameras.TabPages.Add(tabPage);
                            _tabCameras.SelectedTab = tabPage;
                            _btnRemoveCamera.Enabled = true;
                        });

                        // 开始采集
                        //cameraManager.StartGrabbing();

                        return true;
                    }
                    finally
                    {
                        _deviceEnumerationSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"连接相机 {deviceInfo} (ID: {cameraId}) 失败: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine(errorMsg);

                    // 清理资源
                    if (cameraManager != null)
                    {
                        try
                        {
                            lock (_lockObject)
                            {
                                _connectedCameras.Remove(cameraId);
                            }
                            cameraManager.DisconnectDevice();
                            cameraManager.Dispose();
                        }
                        catch { }
                    }

                    if (cameraTabView != null)
                    {
                        await this.InvokeAsync(() =>
                        {
                            cameraTabView.Dispose();
                        });
                    }

                    // 直接抛出异常，不要包装新的异常
                    throw;
                }
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        /// <summary>
        /// 连接单个相机的辅助方法，用于批量加载
        /// </summary>
        private async Task<(DeviceInfoWrapper deviceInfo, bool success, string error)> ConnectSingleCameraAsync(DeviceInfoWrapper deviceInfo)
        {
            try
            {
                UpdateStatus($"正在连接相机 {deviceInfo}...");
                bool success = await ConnectCameraAsync(deviceInfo);
                return (deviceInfo, success, null);
            }
            catch (Exception ex)
            {
                return (deviceInfo, false, ex.Message);
            }
        }

        private async void BtnRemoveCamera_Click(object sender, EventArgs e)
        {
            if (_tabCameras.SelectedTab == null || _cameraTabs.Count == 0)
                return;

            try
            {
                // 找到当前选中的标签页
                var tabPage = _tabCameras.SelectedTab;
                CameraTabView cameraTabView = null;
                string cameraId = null;

                lock (_lockObject)
                {
                    // Find the UserControl within the TabPage's controls
                    cameraTabView = tabPage.Controls.OfType<CameraTabView>().FirstOrDefault();
                    if (cameraTabView == null)
                        return;

                    cameraId = cameraTabView.CameraId;
                }

                string deviceName = cameraTabView.DeviceName;

                // 禁用按钮防止重复操作
                _btnRemoveCamera.Enabled = false;
                UpdateStatus($"正在断开相机 {deviceName}...");

                // 异步释放资源
                await Task.Run(() =>
                {
                    try
                    {
                        CameraManager cameraManager = null;

                        // 获取并移除相机管理器
                        lock (_lockObject)
                        {
                            if (_connectedCameras.TryGetValue(cameraId, out cameraManager))
                            {
                                _connectedCameras.Remove(cameraId);
                            }
                        }

                        // 在锁外释放资源，避免阻塞
                        if (cameraManager != null)
                        {
                            try
                            {
                                cameraManager.StopGrabbing();
                                cameraManager.DisconnectDevice();
                                cameraManager.Dispose();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"释放相机资源时出错: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"移除相机失败: {ex.Message}");
                    }
                });

                // 在UI线程更新界面
                lock (_lockObject)
                {
                    _cameraTabs.Remove(cameraTabView);
                }
                _tabCameras.TabPages.Remove(tabPage);
                cameraTabView.Dispose();

                // 更新按钮状态
                if (_cameraTabs.Count == 0)
                {
                    _btnRemoveCamera.Enabled = false;
                }
                else
                {
                    _btnRemoveCamera.Enabled = true;
                }

                UpdateStatus($"已移除相机: {deviceName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"移除相机失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnRemoveCamera.Enabled = _cameraTabs.Count > 0;
            }
        }

        private async void BtnLoadAllCameras_Click(object sender, EventArgs e)
        {
            if (_deviceList == null || _deviceList.Count == 0)
            {
                MessageBox.Show("未检测到可用的相机设备", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // --- 开始诊断日志 ---
            System.Diagnostics.Debug.WriteLine("\n--- [诊断] 开始加载所有相机... ---");
            var deviceIds = _deviceList.Select(d => d.SerialNumber).ToList();
            System.Diagnostics.Debug.WriteLine($"[诊断] 发现 {_deviceList.Count} 个设备: [{string.Join(", ", deviceIds)}]");

            var duplicateIds = deviceIds.GroupBy(id => id)
                                        .Where(g => g.Count() > 1)
                                        .Select(g => g.Key).ToList();
            if (duplicateIds.Any())
            {
                System.Diagnostics.Debug.WriteLine($"[诊断] !!! 警告: 检测到重复的设备序列号: [{string.Join(", ", duplicateIds)}] !!!");
            }
            // --- 结束诊断日志 ---

            // 禁用相关按钮
            _btnAddCamera.Enabled = false;
            _btnLoadAllCameras.Enabled = false;
            _btnRemoveCamera.Enabled = false;

            UpdateStatus("开始加载所有相机...");

            int successCount = 0;
            int failCount = 0;
            var failedDevices = new List<string>();

            // 保存当前可用设备列表快照
            List<DeviceInfoWrapper> deviceSnapshot;
            lock (_lockObject)
            {
                deviceSnapshot = new List<DeviceInfoWrapper>(_deviceList);
            }

            // 使用并发任务加载所有相机，但受限于_connectionSemaphore
            var tasks = new List<Task<(DeviceInfoWrapper deviceInfo, bool success, string error)>>();

            foreach (var deviceInfo in deviceSnapshot)
            {
                var cameraId = deviceInfo.SerialNumber;
                System.Diagnostics.Debug.WriteLine($"[诊断] 正在处理设备: {deviceInfo} (S/N: {cameraId})");

                // 检查相机是否已连接
                lock (_lockObject)
                {
                    if (_connectedCameras.ContainsKey(cameraId))
                    {
                        UpdateStatus($"相机 {deviceInfo} 已存在，跳过");
                        System.Diagnostics.Debug.WriteLine($"[诊断] --> 跳过 (已连接): {deviceInfo}");
                        continue;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[诊断] --> 为 {deviceInfo} 创建连接任务");
                // 创建异步连接任务
                var task = ConnectSingleCameraAsync(deviceInfo);

                tasks.Add(task);
            }

            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);

            System.Diagnostics.Debug.WriteLine("[诊断] --- 所有连接任务完成，正在处理结果... ---");
            // 统计结果
            foreach (var (deviceInfo, success, error) in results)
            {
                if (success)
                {
                    successCount++;
                    UpdateStatus($"相机 {deviceInfo} 连接成功 ({successCount}/{deviceSnapshot.Count})");
                    System.Diagnostics.Debug.WriteLine($"[诊断] --> 结果: 成功 - {deviceInfo}");
                }
                else
                {
                    failCount++;
                    string errorMsg = string.IsNullOrEmpty(error) ? "未知错误" : error;
                    failedDevices.Add($"相机 {deviceInfo}: {errorMsg}");
                    UpdateStatus($"相机 {deviceInfo} 连接失败 ({failCount} 个失败)");
                    System.Diagnostics.Debug.WriteLine($"[诊断] --> 结果: 失败 - {deviceInfo}. 原因: {errorMsg}");
                }
            }

            // 显示结果
            string resultMessage = $"全部加载完成!\n成功: {successCount} 个\n失败: {failCount} 个";
            if (failedDevices.Count > 0)
            {
                resultMessage += "\n\n失败的设备:\n" + string.Join("\n", failedDevices);
            }

            MessageBox.Show(resultMessage, "加载结果", MessageBoxButtons.OK,
                          failCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            UpdateStatus($"全部加载完成: 成功 {successCount} 个，失败 {failCount} 个");

            // 重新启用按钮
            _btnAddCamera.Enabled = true;
            _btnLoadAllCameras.Enabled = true;
            if (_cameraTabs.Count > 0)
            {
                _btnRemoveCamera.Enabled = true;
            }
        }

        // 保留这些方法，但仅供兼容性使用，不再实际使用
        private void BtnConnect_Click(object sender, EventArgs e) { }
        private void BtnStartGrab_Click(object sender, EventArgs e) { }
        private void BtnStopGrab_Click(object sender, EventArgs e) { }
        private void ComboTriggerMode_SelectedIndexChanged(object sender, EventArgs e) { }
        private void BtnTriggerOnce_Click(object sender, EventArgs e) { }
        private void BtnStartContinuous_Click(object sender, EventArgs e) { }
        private void BtnStopContinuous_Click(object sender, EventArgs e) { }

        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), message);
                return;
            }
            _lblStatus.Text = $"状态: {message}";
        }

        #region 相机间同步方法

        /// <summary>
        /// 同步曝光时间到其他相机
        /// </summary>
        /// <param name="sourceTab">源相机标签页</param>
        /// <param name="exposureTime">曝光时间</param>
        public void SyncExposureToOtherCameras(CameraTabView sourceTab, float exposureTime)
        {
            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var tab in _cameraTabs)
                {
                    if (tab == sourceTab || tab.CameraManager?.ActiveDevice == null)
                        continue;

                    try
                    {
                        bool success = tab.CameraManager.SetExposureTime(exposureTime);
                        if (success)
                        {
                            successCount++;
                            // 更新UI显示
                            if (tab._txtExposureTime != null)
                                tab._txtExposureTime.Text = exposureTime.ToString();
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                string message = $"曝光时间同步完成: 成功{successCount}个，失败{failCount}个";
                UpdateStatus(message);
                MessageBox.Show(message, "同步结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步曝光时间失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 同步白平衡到其他相机
        /// </summary>
        /// <param name="sourceTab">源相机标签页</param>
        /// <param name="red">红色比例</param>
        /// <param name="green">绿色比例</param>
        /// <param name="blue">蓝色比例</param>
        public void SyncWhiteBalanceToOtherCameras(CameraTabView sourceTab, float red, float green, float blue)
        {
            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var tab in _cameraTabs)
                {
                    if (tab == sourceTab || tab.CameraManager?.ActiveDevice == null)
                        continue;

                    try
                    {
                        bool success = tab.CameraManager.SetBalanceRatio(red, green, blue);
                        if (success)
                        {
                            successCount++;
                            // 更新UI显示 (int格式)
                            if (tab._txtRedRatio != null && tab._txtGreenRatio != null && tab._txtBlueRatio != null)
                            {
                                tab._txtRedRatio.Text = ((int)red).ToString();
                                tab._txtGreenRatio.Text = ((int)green).ToString();
                                tab._txtBlueRatio.Text = ((int)blue).ToString();
                            }
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                string message = $"白平衡同步完成: 成功{successCount}个，失败{failCount}个";
                UpdateStatus(message);
                MessageBox.Show(message, "同步结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步白平衡失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 同步ROI到其他相机
        /// </summary>
        /// <param name="sourceTab">源相机标签页</param>
        /// <param name="offsetX">X偏移</param>
        /// <param name="offsetY">Y偏移</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        public void SyncROIToOtherCameras(CameraTabView sourceTab, int offsetX, int offsetY, int width, int height)
        {
            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var tab in _cameraTabs)
                {
                    if (tab == sourceTab || tab.CameraManager?.ActiveDevice == null)
                        continue;

                    try
                    {
                        // 停止采集
                        bool wasGrabbing = tab.CameraManager.ActiveDevice.IsGrabbing;
                        if (wasGrabbing)
                            tab.CameraManager.StopGrabbing();

                        bool success = tab.CameraManager.SetROI(offsetX, offsetY, width, height);

                        // 恢复采集
                        if (wasGrabbing)
                            tab.CameraManager.StartGrabbing();

                        if (success)
                        {
                            successCount++;
                            // 更新UI显示为实际设置的ROI值（16倍数对齐后的值）
                            var (actualX, actualY, actualWidth, actualHeight) = tab.CameraManager.GetROI();
                            if (actualWidth > 0 && actualHeight > 0 &&
                                tab._txtOffsetX != null && tab._txtOffsetY != null &&
                                tab._txtWidth != null && tab._txtHeight != null)
                            {
                                tab._txtOffsetX.Text = actualX.ToString();
                                tab._txtOffsetY.Text = actualY.ToString();
                                tab._txtWidth.Text = actualWidth.ToString();
                                tab._txtHeight.Text = actualHeight.ToString();
                            }
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                string message = $"ROI同步完成: 成功{successCount}个，失败{failCount}个";
                UpdateStatus(message);
                MessageBox.Show(message, "同步结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion
    }

    /// <summary>
    /// Control扩展方法
    /// </summary>
    public static class ControlExtensions
    {
        /// <summary>
        /// 异步调用方法（在控件的线程上执行）
        /// </summary>
        public static Task InvokeAsync(this Control control, Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            if (control.InvokeRequired)
            {
                control.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        action();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }));
            }
            else
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }

            return tcs.Task;
        }
    }
}
