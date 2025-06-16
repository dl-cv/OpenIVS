using System;
using System.IO;
using System.Windows.Forms;
using DlcvDvpHttpApi;
using dlcv_infer_csharp;

namespace DlcvDvpHttpApiTest
{
    public partial class Form1 : Form
    {
        private DlcvDvpHttpApi.Model _model;

        public Form1()
        {
            InitializeComponent();
            UpdateStatus("就绪状态");
        }

        private void btnSelectModel_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择模型文件";
                dialog.Filter = "模型文件 (*.dvp)|*.dvp|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtModelPath.Text = dialog.FileName;
                }
            }
        }

        private void btnSelectImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择图像文件";
                dialog.Filter = "图像文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtImagePath.Text = dialog.FileName;
                }
            }
        }

        private void btnLoadModel_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(txtModelPath.Text))
                {
                    MessageBox.Show("请先选择模型文件路径！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!File.Exists(txtModelPath.Text))
                {
                    MessageBox.Show("模型文件不存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                UpdateStatus("正在加载模型...");

                // 释放之前的模型
                if (_model != null)
                {
                    try
                    {
                        _model.Dispose();
                    }
                    catch (Exception ex)
                    {
                        AppendResult($"释放旧模型时出现警告: {ex.Message}");
                    }
                    _model = null;
                }

                // 创建新模型实例
                _model = new DlcvDvpHttpApi.Model(txtModelPath.Text, txtServerUrl.Text);
                
                AppendResult($"[{DateTime.Now:HH:mm:ss}] 模型加载成功");
                AppendResult($"模型路径: {txtModelPath.Text}");
                AppendResult($"服务器地址: {txtServerUrl.Text}");
                UpdateStatus("模型已加载");

                MessageBox.Show("模型加载成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus("模型加载失败");
                AppendResult($"[{DateTime.Now:HH:mm:ss}] 模型加载失败: {ex.Message}");
                
                // 检查是否是后端服务未启动的错误
                if (ex.Message.Contains("检测到后端未启动"))
                {
                    MessageBox.Show($"{ex.Message}\n\n后端服务正在启动中，请稍等片刻后重试。", "后端服务启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    AppendResult("提示：后端服务正在启动中，请等待10秒钟后重新尝试加载模型");
                }
                else
                {
                    MessageBox.Show($"模型加载失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnGetModelInfo_Click(object sender, EventArgs e)
        {
            try
            {
                if (_model == null)
                {
                    MessageBox.Show("请先加载模型！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                UpdateStatus("正在获取模型信息...");

                var modelInfo = _model.GetModelInfo();
                AppendResult($"[{DateTime.Now:HH:mm:ss}] 模型信息:");
                AppendResult(Utils.jsonToString(modelInfo));

                UpdateStatus("模型信息获取完成");
            }
            catch (Exception ex)
            {
                UpdateStatus("获取模型信息失败");
                AppendResult($"[{DateTime.Now:HH:mm:ss}] 获取模型信息失败: {ex.Message}");
                
                // 检查是否是网络连接相关的错误
                if (ex.Message.Contains("连接") || ex.Message.Contains("网络") || ex.Message.Contains("超时"))
                {
                    MessageBox.Show($"无法连接到后端服务：{ex.Message}\n\n请确认后端服务是否正常运行。", "连接错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show($"获取模型信息失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnFreeModel_Click(object sender, EventArgs e)
        {
            try
            {
                if (_model == null)
                {
                    MessageBox.Show("没有加载的模型需要释放！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                UpdateStatus("正在释放模型...");

                _model.Dispose();
                _model = null;

                AppendResult($"[{DateTime.Now:HH:mm:ss}] 模型已释放");
                UpdateStatus("模型已释放");

                MessageBox.Show("模型释放成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus("模型释放失败");
                AppendResult($"[{DateTime.Now:HH:mm:ss}] 模型释放失败: {ex.Message}");
                MessageBox.Show($"模型释放失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnInfer_Click(object sender, EventArgs e)
        {
            try
            {
                if (_model == null)
                {
                    MessageBox.Show("请先加载模型！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(txtImagePath.Text))
                {
                    MessageBox.Show("请先选择图像文件！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!File.Exists(txtImagePath.Text))
                {
                    MessageBox.Show("图像文件不存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                UpdateStatus("正在进行推理...");

                var startTime = DateTime.Now;
                
                // 进行推理
                Utils.CSharpResult result = _model.Infer(txtImagePath.Text);
                
                var endTime = DateTime.Now;
                var duration = (endTime - startTime).TotalMilliseconds;

                AppendResult($"[{DateTime.Now:HH:mm:ss}] 推理完成，耗时: {duration:F2} ms");
                AppendResult($"图像路径: {txtImagePath.Text}");
                AppendResult($"检测结果:");

                if (result.SampleResults != null && result.SampleResults.Count > 0)
                {
                    var sampleResult = result.SampleResults[0];
                    if (sampleResult.Results != null && sampleResult.Results.Count > 0)
                    {
                        AppendResult($"共检测到 {sampleResult.Results.Count} 个对象:");
                        for (int i = 0; i < sampleResult.Results.Count; i++)
                        {
                            var obj = sampleResult.Results[i];
                            AppendResult($"  对象 {i + 1}: {obj.ToString()}");
                        }
                    }
                    else
                    {
                        AppendResult("  未检测到任何对象");
                    }
                }
                else
                {
                    AppendResult("  推理结果为空");
                }

                AppendResult(""); // 空行分隔

                UpdateStatus("推理完成");
                MessageBox.Show($"推理完成！耗时: {duration:F2} ms", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus("推理失败");
                AppendResult($"[{DateTime.Now:HH:mm:ss}] 推理失败: {ex.Message}");
                
                // 检查是否是网络连接相关的错误
                if (ex.Message.Contains("连接") || ex.Message.Contains("网络") || ex.Message.Contains("超时"))
                {
                    MessageBox.Show($"无法连接到后端服务：{ex.Message}\n\n请确认后端服务是否正常运行。", "连接错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show($"推理失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(UpdateStatus), message);
                return;
            }

            toolStripStatusLabel.Text = message;
        }

        private void AppendResult(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(AppendResult), message);
                return;
            }

            txtResults.AppendText(message + Environment.NewLine);
            txtResults.SelectionStart = txtResults.Text.Length;
            txtResults.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_model != null)
                {
                    _model.Dispose();
                    _model = null;
                }
            }
            catch (Exception ex)
            {
                // 记录但不阻止关闭
                Console.WriteLine($"关闭窗体时释放模型失败: {ex.Message}");
            }

            base.OnFormClosing(e);
        }
    }
}
