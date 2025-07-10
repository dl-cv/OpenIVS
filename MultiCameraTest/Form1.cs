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

namespace CameraManagerTest
{
    public partial class Form1 : Form
    {
        private List<DeviceInfoWrapper> _deviceList;
        private List<CameraTab> _cameraTabs = new List<CameraTab>();
        
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
            this.MinimumSize = new Size(1130, 850);
            this.Size = new Size(1130, 850);
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
                return await Task.Run(async () =>
                {
                    CameraTab cameraTab = null;
                    CameraManager cameraManager = null;
                    
                    try
                    {
                        // 在UI线程创建标签页
                        await this.InvokeAsync(() =>
                        {
                            cameraTab = new CameraTab(cameraId, deviceInfo.ToString());
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
                            cameraTab.SetCameraManager(cameraManager);
                            
                            // 订阅图像更新事件
                            cameraManager.ImageUpdated += (sender, e) =>
                            {
                                cameraTab.OnImageUpdated(e.Image);
                            };
                            
                            // 在UI线程添加标签页
                            await this.InvokeAsync(() =>
                            {
                                lock (_lockObject)
                                {
                                    _cameraTabs.Add(cameraTab);
                                }
                                _tabCameras.TabPages.Add(cameraTab.TabPage);
                                _tabCameras.SelectedTab = cameraTab.TabPage;
                                _btnRemoveCamera.Enabled = true;
                            });
                            
                            // 开始采集
                            cameraManager.StartGrabbing();
                            
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
                        
                        if (cameraTab != null)
                        {
                            await this.InvokeAsync(() =>
                            {
                                cameraTab.Dispose();
                            });
                        }
                        
                        // 重新抛出异常，以便上层调用者可以捕获并处理
                        throw new Exception(errorMsg, ex);
                    }
                });
            }
            finally
            {
                _connectionSemaphore.Release();
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
                CameraTab cameraTab = null;
                string cameraId = null;
                
                lock (_lockObject)
                {
                    cameraTab = _cameraTabs.FirstOrDefault(c => c.TabPage == tabPage);
                    if (cameraTab == null)
                        return;
                    
                    cameraId = cameraTab.CameraId;
                }
                
                string deviceName = cameraTab.DeviceName;
                
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
                    _cameraTabs.Remove(cameraTab);
                }
                _tabCameras.TabPages.Remove(tabPage);
                cameraTab.Dispose();
                
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
                
                // 检查相机是否已连接
                lock (_lockObject)
                {
                    if (_connectedCameras.ContainsKey(cameraId))
                    {
                        UpdateStatus($"相机 {deviceInfo} 已存在，跳过");
                        continue;
                    }
                }
                
                // 创建异步连接任务
                var task = Task.Run(async () =>
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
                });
                
                tasks.Add(task);
            }
            
            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);
            
            // 统计结果
            foreach (var (deviceInfo, success, error) in results)
            {
                if (success)
                {
                    successCount++;
                    UpdateStatus($"相机 {deviceInfo} 连接成功 ({successCount}/{deviceSnapshot.Count})");
                }
                else
                {
                    failCount++;
                    string errorMsg = string.IsNullOrEmpty(error) ? "未知错误" : error;
                    failedDevices.Add($"相机 {deviceInfo}: {errorMsg}");
                    UpdateStatus($"相机 {deviceInfo} 连接失败 ({failCount} 个失败)");
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
        public void SyncExposureToOtherCameras(CameraTab sourceTab, float exposureTime)
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
        public void SyncWhiteBalanceToOtherCameras(CameraTab sourceTab, float red, float green, float blue)
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
        public void SyncROIToOtherCameras(CameraTab sourceTab, int offsetX, int offsetY, int width, int height)
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

    // 为每个相机创建一个标签页类
    public class CameraTab : IDisposable
    {
        public TabPage TabPage { get; private set; }
        public CameraManager CameraManager { get; private set; }
        public string CameraId { get; private set; }
        public string DeviceName { get; private set; }
        
        private PictureBox _pictureBox;
        private GroupBox _groupCamera, _groupExposure, _groupWhiteBalance, _groupROI;
        private ComboBox _comboTriggerMode;
        private Button _btnStartGrab, _btnStopGrab;
        private Button _btnTriggerOnce;
        private Label _lblInterval, _lblTriggerMode;
        private TextBox _txtInterval;
        private Button _btnStartContinuous, _btnStopContinuous;
        
        // 曝光时间控件
        private Label _lblExposureTime;
        internal TextBox _txtExposureTime;
        private Button _btnSetExposure, _btnSyncExposure;
        
        // 白平衡控件
        private Button _btnAutoWhiteBalance, _btnSyncWhiteBalance;
        private Label _lblRedRatio, _lblGreenRatio, _lblBlueRatio;
        internal TextBox _txtRedRatio, _txtGreenRatio, _txtBlueRatio;
        
        // ROI控件
        private Label _lblOffsetX, _lblOffsetY, _lblWidth, _lblHeight;
        internal TextBox _txtOffsetX, _txtOffsetY, _txtWidth, _txtHeight;
        private Button _btnSetROI, _btnSyncROI, _btnRestoreROI;
        
        // ROI绘制相关
        private bool _isDrawingROI = false;
        private Point _roiStartPoint;
        private Rectangle _roiRectangle;
        
        // 线程安全
        private readonly object _imageLock = new object();
        
        public CameraTab(string cameraId, string deviceName)
        {
            CameraId = cameraId;
            DeviceName = deviceName;
            
            // 创建标签页
            TabPage = new TabPage(deviceName);
            
            // 初始化控件
            InitializeControls();
        }
        
        /// <summary>
        /// 设置相机管理器（必须在连接成功后调用）
        /// </summary>
        public void SetCameraManager(CameraManager cameraManager)
        {
            CameraManager = cameraManager;
            
            // 启用控件
            _groupCamera.Enabled = true;
            _groupExposure.Enabled = true;
            _groupWhiteBalance.Enabled = true;
            _groupROI.Enabled = true;
            
            // 初始化参数显示
            InitializeParameterDisplay();
        }
        
        /// <summary>
        /// 处理图像更新（线程安全）
        /// </summary>
        public void OnImageUpdated(System.Drawing.Image image)
        {
            if (image == null || _pictureBox == null)
                return;
            
            try
            {
                // 复制图像以避免线程问题
                System.Drawing.Image imageCopy = null;
                lock (_imageLock)
                {
                    imageCopy = (System.Drawing.Image)image.Clone();
                }
                
                // 在UI线程更新图像
                if (_pictureBox.InvokeRequired)
                {
                    _pictureBox.BeginInvoke(new Action(() =>
                    {
                        UpdatePictureBox(imageCopy);
                    }));
                }
                else
                {
                    UpdatePictureBox(imageCopy);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"图像更新失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新PictureBox显示（必须在UI线程调用）
        /// </summary>
        private void UpdatePictureBox(System.Drawing.Image newImage)
        {
            try
            {
                // 释放旧图像
                var oldImage = _pictureBox.Image;
                _pictureBox.Image = newImage;
                oldImage?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新PictureBox失败: {ex.Message}");
                newImage?.Dispose();
            }
        }
        
        private void InitializeControls()
        {
            // 设置标签页大小 - 参考右侧窗口的合理大小
            TabPage.Size = new Size(1400, 800);
            
            // 创建图像显示控件 - 调整到更大的显示区域
            _pictureBox = new PictureBox()
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(10, 160),
                Size = new Size(TabPage.Width - 20, TabPage.Height - 170),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            
            // 相机控制组
            InitializeCameraControlGroup();
            
            // 曝光时间组
            InitializeExposureGroup();
            
            // 白平衡组
            InitializeWhiteBalanceGroup();
            
            // ROI组
            InitializeROIGroup();
            
            // 初始状态下禁用所有控制组（相机未连接）
            _groupCamera.Enabled = false;
            _groupExposure.Enabled = false;
            _groupWhiteBalance.Enabled = false;
            _groupROI.Enabled = false;
            
            // 添加控件到标签页
            TabPage.Controls.Add(_groupCamera);
            TabPage.Controls.Add(_groupExposure);
            TabPage.Controls.Add(_groupWhiteBalance);
            TabPage.Controls.Add(_groupROI);
            TabPage.Controls.Add(_pictureBox);
            
            // 绑定事件
            BindEvents();
        }
        
        private void InitializeCameraControlGroup()
        {
            _groupCamera = new GroupBox()
            {
                Text = "相机控制",
                Location = new Point(10, 10),
                Size = new Size(240, 145),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _lblTriggerMode = new Label()
            {
                Text = "触发模式:",
                Location = new Point(15, 25),
                AutoSize = true
            };
            
            _comboTriggerMode = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(15, 45),
                Size = new Size(180, 26)
            };
            
            _btnStartGrab = new Button()
            {
                Text = "开始采集",
                Location = new Point(15, 80),
                Size = new Size(85, 30)
            };
            
            _btnStopGrab = new Button()
            {
                Text = "停止采集",
                Location = new Point(110, 80),
                Size = new Size(85, 30),
                Enabled = false
            };
            
            _btnTriggerOnce = new Button()
            {
                Text = "单次触发",
                Location = new Point(15, 115),
                Size = new Size(85, 30),
                Enabled = false
            };
            
            _lblInterval = new Label()
            {
                Text = "间隔(ms):",
                Location = new Point(110, 120),
                AutoSize = true,
                Enabled = false
            };
            
            _txtInterval = new TextBox()
            {
                Text = "1000",
                Location = new Point(165, 117),
                Size = new Size(55, 23),
                Enabled = false
            };
            
            _btnStartContinuous = new Button()
            {
                Text = "开始",
                Location = new Point(15, 150),
                Size = new Size(70, 26),
                Enabled = false
            };
            
            _btnStopContinuous = new Button()
            {
                Text = "停止",
                Location = new Point(95, 150),
                Size = new Size(70, 26),
                Enabled = false
            };
            
            // 添加触发模式选项
            _comboTriggerMode.Items.Add("关闭触发");
            _comboTriggerMode.Items.Add("软触发");
            _comboTriggerMode.Items.Add("Line0");
            _comboTriggerMode.Items.Add("Line1");
            _comboTriggerMode.SelectedIndex = 0;
            
            // 添加控件到组
            _groupCamera.Controls.Add(_lblTriggerMode);
            _groupCamera.Controls.Add(_comboTriggerMode);
            _groupCamera.Controls.Add(_btnStartGrab);
            _groupCamera.Controls.Add(_btnStopGrab);
            _groupCamera.Controls.Add(_btnTriggerOnce);
            _groupCamera.Controls.Add(_lblInterval);
            _groupCamera.Controls.Add(_txtInterval);
            _groupCamera.Controls.Add(_btnStartContinuous);
            _groupCamera.Controls.Add(_btnStopContinuous);
        }
        
        private void InitializeExposureGroup()
        {
            _groupExposure = new GroupBox()
            {
                Text = "曝光时间设置",
                Location = new Point(260, 10),
                Size = new Size(240, 145),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _lblExposureTime = new Label()
            {
                Text = "曝光时间(μs):",
                Location = new Point(15, 25),
                AutoSize = true
            };
            
            _txtExposureTime = new TextBox()
            {
                Text = "10000",
                Location = new Point(15, 45),
                Size = new Size(100, 23)
            };
            
            _btnSetExposure = new Button()
            {
                Text = "设置",
                Location = new Point(125, 44),
                Size = new Size(50, 26)
            };
            
            _btnSyncExposure = new Button()
            {
                Text = "同步到其他相机",
                Location = new Point(15, 75),
                Size = new Size(120, 26)
            };
            
            var lblExposureRange = new Label()
            {
                Text = "范围: 0~33000μs",
                Location = new Point(15, 110),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            
            // 添加控件到组
            _groupExposure.Controls.Add(_lblExposureTime);
            _groupExposure.Controls.Add(_txtExposureTime);
            _groupExposure.Controls.Add(_btnSetExposure);
            _groupExposure.Controls.Add(_btnSyncExposure);
            _groupExposure.Controls.Add(lblExposureRange);
        }
        
        private void InitializeWhiteBalanceGroup()
        {
            _groupWhiteBalance = new GroupBox()
            {
                Text = "白平衡设置",
                Location = new Point(510, 10),
                Size = new Size(240, 140),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _btnAutoWhiteBalance = new Button()
            {
                Text = "一键白平衡",
                Location = new Point(15, 25),
                Size = new Size(100, 30)
            };
            
            _btnSyncWhiteBalance = new Button()
            {
                Text = "同步到其他相机",
                Location = new Point(125, 25),
                Size = new Size(100, 30)
            };
            
            var lblRatioRange = new Label()
            {
                Text = "比例值范围: 1~16376",
                Location = new Point(15, 115),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            
            _lblRedRatio = new Label()
            {
                Text = "红色(int):",
                Location = new Point(15, 65),
                AutoSize = true
            };
            
            _txtRedRatio = new TextBox()
            {
                Location = new Point(15, 85),
                Size = new Size(60, 23),
                ReadOnly = true
            };
            
            _lblGreenRatio = new Label()
            {
                Text = "绿色(int):",
                Location = new Point(85, 65),
                AutoSize = true
            };
            
            _txtGreenRatio = new TextBox()
            {
                Location = new Point(85, 85),
                Size = new Size(60, 23),
                ReadOnly = true
            };
            
            _lblBlueRatio = new Label()
            {
                Text = "蓝色(int):",
                Location = new Point(155, 65),
                AutoSize = true
            };
            
            _txtBlueRatio = new TextBox()
            {
                Location = new Point(155, 85),
                Size = new Size(60, 23),
                ReadOnly = true
            };
            
            // 添加控件到组
            _groupWhiteBalance.Controls.Add(_btnAutoWhiteBalance);
            _groupWhiteBalance.Controls.Add(_btnSyncWhiteBalance);
            _groupWhiteBalance.Controls.Add(lblRatioRange);
            _groupWhiteBalance.Controls.Add(_lblRedRatio);
            _groupWhiteBalance.Controls.Add(_txtRedRatio);
            _groupWhiteBalance.Controls.Add(_lblGreenRatio);
            _groupWhiteBalance.Controls.Add(_txtGreenRatio);
            _groupWhiteBalance.Controls.Add(_lblBlueRatio);
            _groupWhiteBalance.Controls.Add(_txtBlueRatio);
        }
        
        private void InitializeROIGroup()
        {
            _groupROI = new GroupBox()
            {
                Text = "ROI设置",
                Location = new Point(760, 10),
                Size = new Size(320, 145),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            
            _lblOffsetX = new Label()
            {
                Text = "X偏移:",
                Location = new Point(15, 25),
                Size = new Size(45, 20)
            };
            
            _txtOffsetX = new TextBox()
            {
                Location = new Point(60, 22),
                Size = new Size(60, 23)
            };
            
            _lblOffsetY = new Label()
            {
                Text = "Y偏移:",
                Location = new Point(130, 25),
                Size = new Size(45, 20)
            };
            
            _txtOffsetY = new TextBox()
            {
                Location = new Point(175, 22),
                Size = new Size(40, 23)
            };
            
            _lblWidth = new Label()
            {
                Text = "宽度:",
                Location = new Point(15, 55),
                Size = new Size(45, 20)
            };
            
            _txtWidth = new TextBox()
            {
                Location = new Point(60, 52),
                Size = new Size(60, 23)
            };
            
            _lblHeight = new Label()
            {
                Text = "高度:",
                Location = new Point(130, 55),
                Size = new Size(45, 20)
            };
            
            _txtHeight = new TextBox()
            {
                Location = new Point(175, 52),
                Size = new Size(40, 23)
            };
            
            _btnSetROI = new Button()
            {
                Text = "设置ROI",
                Location = new Point(15, 85),
                Size = new Size(70, 26)
            };
            
            _btnSyncROI = new Button()
            {
                Text = "同步ROI",
                Location = new Point(90, 85),
                Size = new Size(70, 26)
            };
            
            _btnRestoreROI = new Button()
            {
                Text = "还原ROI",
                Location = new Point(15, 115),
                Size = new Size(70, 26)
            };
            
            var lblROITip = new Label()
            {
                Text = "提示: 点击设置ROI可画框选择区域",
                Location = new Point(170, 118),
                Size = new Size(140, 20),
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei", 8)
            };
            
            // 添加控件到组
            _groupROI.Controls.Add(_lblOffsetX);
            _groupROI.Controls.Add(_txtOffsetX);
            _groupROI.Controls.Add(_lblOffsetY);
            _groupROI.Controls.Add(_txtOffsetY);
            _groupROI.Controls.Add(_lblWidth);
            _groupROI.Controls.Add(_txtWidth);
            _groupROI.Controls.Add(_lblHeight);
            _groupROI.Controls.Add(_txtHeight);
            _groupROI.Controls.Add(_btnSetROI);
            _groupROI.Controls.Add(_btnSyncROI);
            _groupROI.Controls.Add(_btnRestoreROI);
            _groupROI.Controls.Add(lblROITip);
        }
        
        private void BindEvents()
        {
            // 原有事件
            _comboTriggerMode.SelectedIndexChanged += ComboTriggerMode_SelectedIndexChanged;
            _btnStartGrab.Click += BtnStartGrab_Click;
            _btnStopGrab.Click += BtnStopGrab_Click;
            _btnTriggerOnce.Click += BtnTriggerOnce_Click;
            _btnStartContinuous.Click += BtnStartContinuous_Click;
            _btnStopContinuous.Click += BtnStopContinuous_Click;
            
            // 曝光时间事件
            _btnSetExposure.Click += BtnSetExposure_Click;
            _btnSyncExposure.Click += BtnSyncExposure_Click;
            
            // 白平衡事件
            _btnAutoWhiteBalance.Click += BtnAutoWhiteBalance_Click;
            _btnSyncWhiteBalance.Click += BtnSyncWhiteBalance_Click;
            
            // ROI事件
            _btnSetROI.Click += BtnSetROI_Click;
            _btnSyncROI.Click += BtnSyncROI_Click;
            _btnRestoreROI.Click += BtnRestoreROI_Click;
            
            // PictureBox鼠标事件（用于ROI绘制）
            _pictureBox.MouseDown += PictureBox_MouseDown;
            _pictureBox.MouseMove += PictureBox_MouseMove;
            _pictureBox.MouseUp += PictureBox_MouseUp;
            _pictureBox.Paint += PictureBox_Paint;
        }
        
        private void InitializeParameterDisplay()
        {
            try
            {
                if (CameraManager?.ActiveDevice == null)
                {
                    Console.WriteLine("InitializeParameterDisplay: 相机未连接");
                    return;
                }

                Console.WriteLine($"初始化相机参数显示: {DeviceName}");

                // 初始化曝光时间显示
                Console.WriteLine("正在获取曝光时间...");
                float exposureTime = CameraManager.GetExposureTime();
                if (exposureTime > 0)
                {
                    _txtExposureTime.Text = exposureTime.ToString("F0");
                    Console.WriteLine($"曝光时间初始化: {exposureTime}μs");
                }
                else
                {
                    _txtExposureTime.Text = "10000"; // 默认值
                    Console.WriteLine("曝光时间获取失败，使用默认值10000μs");
                }

                // 初始化白平衡显示 (海康相机BalanceRatio是int类型)
                Console.WriteLine("正在获取白平衡比例...");
                var (red, green, blue) = CameraManager.GetBalanceRatio();
                if (red > 0 && green > 0 && blue > 0)
                {
                    _txtRedRatio.Text = ((int)red).ToString();
                    _txtGreenRatio.Text = ((int)green).ToString();
                    _txtBlueRatio.Text = ((int)blue).ToString();
                    Console.WriteLine($"白平衡比例初始化: R={red:F0}, G={green:F0}, B={blue:F0}");
                }
                else
                {
                    _txtRedRatio.Text = "1024";
                    _txtGreenRatio.Text = "1024";
                    _txtBlueRatio.Text = "1024";
                    Console.WriteLine("白平衡比例获取失败，使用默认值1024");
                }

                // 初始化ROI显示
                Console.WriteLine("正在获取ROI参数...");
                UpdateROIControls();
                
                Console.WriteLine("参数显示初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitializeParameterDisplay异常: {ex.Message}");
            }
        }
        
        // 实现与原来相同的功能，但为每个相机独立
        private void BtnStartGrab_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                bool success = CameraManager.StartGrabbing();
                if (success)
                {
                    _btnStartGrab.Enabled = false;
                    _btnStopGrab.Enabled = true;
                }
                else
                {
                    MessageBox.Show("开始采集失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"开始采集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnStopGrab_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    return;
                }
                
                CameraManager.StopGrabbing();
                _btnStartGrab.Enabled = true;
                _btnStopGrab.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止采集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void ComboTriggerMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                int index = _comboTriggerMode.SelectedIndex;
                
                // 若相机未连接，仅设置UI状态，不执行相机操作
                bool isCameraConnected = CameraManager != null && CameraManager.ActiveDevice != null;
                
                // 根据不同模式设置UI状态
                switch (index)
                {
                    case 0: // 关闭触发
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Off);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 1: // 软触发
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Software);
                        
                        _btnTriggerOnce.Enabled = isCameraConnected;
                        _lblInterval.Enabled = isCameraConnected;
                        _txtInterval.Enabled = isCameraConnected;
                        _btnStartContinuous.Enabled = isCameraConnected;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 2: // Line0
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line0);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                    case 3: // Line1
                        if (isCameraConnected)
                            CameraManager.SetTriggerMode(TriggerConfig.TriggerMode.Line1);
                        
                        _btnTriggerOnce.Enabled = false;
                        _lblInterval.Enabled = false;
                        _txtInterval.Enabled = false;
                        _btnStartContinuous.Enabled = false;
                        _btnStopContinuous.Enabled = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置触发模式失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnTriggerOnce_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                CameraManager.TriggerOnce();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"软触发失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async void BtnStartContinuous_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (!int.TryParse(_txtInterval.Text, out int interval) || interval <= 0)
                {
                    MessageBox.Show("请输入有效的间隔时间", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _btnStartContinuous.Enabled = false;
                _btnStopContinuous.Enabled = true;

                await CameraManager.StartContinuousTriggerAsync(interval);
            }
            catch (Exception ex)
            {
                _btnStartContinuous.Enabled = true;
                _btnStopContinuous.Enabled = false;
                MessageBox.Show($"开始连续触发失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnStopContinuous_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    return;
                }
                
                CameraManager.StopContinuousTrigger();
                _btnStartContinuous.Enabled = true;
                _btnStopContinuous.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止连续触发失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        #region 新增功能事件处理
        
        // 曝光时间设置事件
        private void BtnSetExposure_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (!float.TryParse(_txtExposureTime.Text, out float exposureTime) || exposureTime <= 0)
                {
                    MessageBox.Show("请输入有效的曝光时间值", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                if (exposureTime > 33000)
                {
                    MessageBox.Show("曝光时间不能超过33000微秒(33ms)", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                bool success = CameraManager.SetExposureTime(exposureTime);
                if (success)
                {
                    MessageBox.Show($"曝光时间设置成功: {exposureTime}μs", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("曝光时间设置失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置曝光时间失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnSyncExposure_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                float exposureTime = CameraManager.GetExposureTime();
                if (exposureTime <= 0)
                {
                    MessageBox.Show("获取当前曝光时间失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                // 调用Form1的同步方法
                SyncExposureToOtherCameras(this, exposureTime);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步曝光时间失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // 白平衡设置事件
        private void BtnAutoWhiteBalance_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                bool success = CameraManager.ExecuteBalanceWhiteAuto();
                if (success)
                {
                    // 获取白平衡结果 (int类型显示)
                    var (red, green, blue) = CameraManager.GetBalanceRatio();
                    _txtRedRatio.Text = ((int)red).ToString();
                    _txtGreenRatio.Text = ((int)green).ToString();
                    _txtBlueRatio.Text = ((int)blue).ToString();
                    
                    MessageBox.Show($"一键白平衡执行成功\nR:{(int)red} G:{(int)green} B:{(int)blue}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("一键白平衡执行失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"白平衡设置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnSyncWhiteBalance_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                var (red, green, blue) = CameraManager.GetBalanceRatio();
                if (red <= 0 || green <= 0 || blue <= 0)
                {
                    MessageBox.Show("获取当前白平衡值失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                // 调用Form1的同步方法
                SyncWhiteBalanceToOtherCameras(this, red, green, blue);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步白平衡失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // ROI设置事件
        private void BtnSetROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (_isDrawingROI)
                {
                    // 如果正在画框，退出画框模式
                    _isDrawingROI = false;
                    _btnSetROI.Text = "设置ROI";
                    _pictureBox.Cursor = Cursors.Default;
                    MessageBox.Show("已退出画框模式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // 进入画框模式
                    _isDrawingROI = true;
                    _btnSetROI.Text = "退出画框";
                    _pictureBox.Cursor = Cursors.Cross;
                    MessageBox.Show("请在图像上拖拽鼠标画出ROI区域", "设置ROI", 
                                  MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnSyncROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                var (offsetX, offsetY, width, height) = CameraManager.GetROI();
                if (width <= 0 || height <= 0)
                {
                    MessageBox.Show("获取当前ROI失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                // 调用Form1的同步方法
                SyncROIToOtherCameras(this, offsetX, offsetY, width, height);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnRestoreROI_Click(object sender, EventArgs e)
        {
            try
            {
                if (CameraManager == null || CameraManager.ActiveDevice == null)
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                // 停止采集
                bool wasGrabbing = CameraManager.ActiveDevice.IsGrabbing;
                if (wasGrabbing)
                    CameraManager.StopGrabbing();
                
                bool success = CameraManager.RestoreMaxROI();
                
                // 恢复采集
                if (wasGrabbing)
                    CameraManager.StartGrabbing();
                
                if (success)
                {
                    // 更新UI显示为实际的ROI值
                    UpdateROIControls();
                    
                    // 获取实际设置的ROI值用于消息显示
                    var (actualX, actualY, actualWidth, actualHeight) = CameraManager.GetROI();
                    if (actualWidth > 0 && actualHeight > 0)
                    {
                        MessageBox.Show($"ROI已恢复到最大分辨率\n实际值: ({actualX}, {actualY}, {actualWidth}, {actualHeight})", 
                                      "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("ROI已恢复到最大分辨率", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show("恢复ROI失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"恢复ROI失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void ApplyROISettings(int offsetX, int offsetY, int width, int height)
        {
            try
            {
                // 停止采集
                bool wasGrabbing = CameraManager.ActiveDevice.IsGrabbing;
                if (wasGrabbing)
                    CameraManager.StopGrabbing();
                
                bool success = CameraManager.SetROI(offsetX, offsetY, width, height);
                
                // 恢复采集
                if (wasGrabbing)
                    CameraManager.StartGrabbing();
                
                if (success)
                {
                    // 更新界面显示为实际设置的ROI值（16倍数对齐后的值）
                    UpdateROIControls();
                    
                    // 获取实际设置的ROI值用于消息显示
                    var (actualX, actualY, actualWidth, actualHeight) = CameraManager.GetROI();
                    if (actualWidth > 0 && actualHeight > 0)
                    {
                        MessageBox.Show($"ROI设置成功\n请求值: ({offsetX}, {offsetY}, {width}, {height})\n实际值: ({actualX}, {actualY}, {actualWidth}, {actualHeight})", 
                                      "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("ROI设置成功，但无法获取实际设置值", "成功", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("ROI设置失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用ROI设置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // PictureBox鼠标事件（ROI绘制）
        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (!_isDrawingROI || e.Button != MouseButtons.Left)
                return;
                
            _roiStartPoint = e.Location;
            _roiRectangle = new Rectangle();
        }
        
        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawingROI || e.Button != MouseButtons.Left)
                return;
                
            int x = Math.Min(_roiStartPoint.X, e.X);
            int y = Math.Min(_roiStartPoint.Y, e.Y);
            int width = Math.Abs(e.X - _roiStartPoint.X);
            int height = Math.Abs(e.Y - _roiStartPoint.Y);
            
            _roiRectangle = new Rectangle(x, y, width, height);
            _pictureBox.Invalidate(); // 触发重绘
        }
        
        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_isDrawingROI || e.Button != MouseButtons.Left)
                return;
                
            if (_roiRectangle.Width > 10 && _roiRectangle.Height > 10)
            {
                // 将PictureBox坐标转换为图像坐标
                if (_pictureBox.Image != null)
                {
                    var imageROI = ConvertPictureBoxROIToImageROI(_roiRectangle);
                    _txtOffsetX.Text = imageROI.X.ToString();
                    _txtOffsetY.Text = imageROI.Y.ToString();
                    _txtWidth.Text = imageROI.Width.ToString();
                    _txtHeight.Text = imageROI.Height.ToString();
                    
                    // 询问用户是否应用当前ROI设置
                    var result = MessageBox.Show(
                        $"检测到ROI区域: ({imageROI.X}, {imageROI.Y}, {imageROI.Width}, {imageROI.Height})\n\n是否按照当前ROI设置应用到相机？", 
                        "确认ROI设置", 
                        MessageBoxButtons.YesNo, 
                        MessageBoxIcon.Question);
                    
                    if (result == DialogResult.Yes)
                    {
                        // 应用ROI设置
                        ApplyROISettings(imageROI.X, imageROI.Y, imageROI.Width, imageROI.Height);
                    }
                    
                    // 退出画框模式
                    _isDrawingROI = false;
                    _btnSetROI.Text = "设置ROI";
                    _pictureBox.Cursor = Cursors.Default;
                }
            }
            
            _roiRectangle = new Rectangle();
            _pictureBox.Invalidate(); // 清除绘制
        }
        
        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (_isDrawingROI && _roiRectangle.Width > 0 && _roiRectangle.Height > 0)
            {
                using (var pen = new Pen(Color.Red, 2))
                {
                    e.Graphics.DrawRectangle(pen, _roiRectangle);
                }
            }
        }
        
        #endregion
        
        #region 辅助方法
        
        private Rectangle ConvertPictureBoxROIToImageROI(Rectangle pictureBoxROI)
        {
            if (_pictureBox.Image == null)
                return pictureBoxROI;
                
            // 获取图像在PictureBox中的实际显示区域
            var imageRect = GetImageRectangleInPictureBox();
            
            // 计算缩放比例
            float scaleX = (float)_pictureBox.Image.Width / imageRect.Width;
            float scaleY = (float)_pictureBox.Image.Height / imageRect.Height;
            
            // 转换坐标
            int imageX = (int)((pictureBoxROI.X - imageRect.X) * scaleX);
            int imageY = (int)((pictureBoxROI.Y - imageRect.Y) * scaleY);
            int imageWidth = (int)(pictureBoxROI.Width * scaleX);
            int imageHeight = (int)(pictureBoxROI.Height * scaleY);
            
            // 确保坐标在图像范围内
            imageX = Math.Max(0, Math.Min(imageX, _pictureBox.Image.Width - 1));
            imageY = Math.Max(0, Math.Min(imageY, _pictureBox.Image.Height - 1));
            imageWidth = Math.Min(imageWidth, _pictureBox.Image.Width - imageX);
            imageHeight = Math.Min(imageHeight, _pictureBox.Image.Height - imageY);
            
            return new Rectangle(imageX, imageY, imageWidth, imageHeight);
        }
        
        private Rectangle GetImageRectangleInPictureBox()
        {
            if (_pictureBox.Image == null)
                return new Rectangle(0, 0, _pictureBox.Width, _pictureBox.Height);
                
            float imageAspect = (float)_pictureBox.Image.Width / _pictureBox.Image.Height;
            float containerAspect = (float)_pictureBox.Width / _pictureBox.Height;
            
            if (imageAspect > containerAspect)
            {
                // 图像比容器更宽，以宽度为准
                int displayHeight = (int)(_pictureBox.Width / imageAspect);
                int yOffset = (_pictureBox.Height - displayHeight) / 2;
                return new Rectangle(0, yOffset, _pictureBox.Width, displayHeight);
            }
            else
            {
                // 图像比容器更高，以高度为准
                int displayWidth = (int)(_pictureBox.Height * imageAspect);
                int xOffset = (_pictureBox.Width - displayWidth) / 2;
                return new Rectangle(xOffset, 0, displayWidth, _pictureBox.Height);
            }
        }
        
        private void UpdateROIControls()
        {
            try
            {
                if (CameraManager?.ActiveDevice != null)
                {
                    Console.WriteLine("正在更新ROI控件显示...");
                    var (offsetX, offsetY, width, height) = CameraManager.GetROI();
                    
                    if (width > 0 && height > 0)
                    {
                        _txtOffsetX.Text = offsetX.ToString();
                        _txtOffsetY.Text = offsetY.ToString();
                        _txtWidth.Text = width.ToString();
                        _txtHeight.Text = height.ToString();
                        Console.WriteLine($"ROI控件更新: X={offsetX}, Y={offsetY}, W={width}, H={height}");
                    }
                    else
                    {
                        // 如果获取失败，尝试获取最大分辨率作为默认值
                        var (maxWidth, maxHeight) = CameraManager.GetMaxResolution();
                        _txtOffsetX.Text = "0";
                        _txtOffsetY.Text = "0";
                        _txtWidth.Text = maxWidth > 0 ? maxWidth.ToString() : "1280";
                        _txtHeight.Text = maxHeight > 0 ? maxHeight.ToString() : "1024";
                        Console.WriteLine($"ROI获取失败，使用默认值: X=0, Y=0, W={_txtWidth.Text}, H={_txtHeight.Text}");
                    }
                }
                else
                {
                    Console.WriteLine("UpdateROIControls: 相机未连接");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateROIControls异常: {ex.Message}");
            }
        }
        
        // 同步方法（需要访问Form1中的其他CameraTab）
        private void SyncExposureToOtherCameras(CameraTab sourceTab, float exposureTime)
        {
            var form1 = TabPage.Parent?.Parent as Form1;
            form1?.SyncExposureToOtherCameras(sourceTab, exposureTime);
        }
        
        private void SyncWhiteBalanceToOtherCameras(CameraTab sourceTab, float red, float green, float blue)
        {
            var form1 = TabPage.Parent?.Parent as Form1;
            form1?.SyncWhiteBalanceToOtherCameras(sourceTab, red, green, blue);
        }
        
        private void SyncROIToOtherCameras(CameraTab sourceTab, int offsetX, int offsetY, int width, int height)
        {
            var form1 = TabPage.Parent?.Parent as Form1;
            form1?.SyncROIToOtherCameras(sourceTab, offsetX, offsetY, width, height);
        }
        
        #endregion

        public void Dispose()
        {
            try
            {
                // 停止所有活动
                CameraManager?.StopContinuousTrigger();
                CameraManager?.StopGrabbing();
                CameraManager?.DisconnectDevice();
                CameraManager?.Dispose();
                
                // 释放图像资源
                if (_pictureBox?.Image != null)
                {
                    _pictureBox.Image.Dispose();
                    _pictureBox.Image = null;
                }
            }
            catch { }
        }
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
