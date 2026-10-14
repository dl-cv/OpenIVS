using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DlcvDemo
{
    public partial class MainWindow
    {
        private async Task<JObject> RunJsonResultUiChecksAsync()
        {
            string originalJson = lastJsonText;
            string originalSummary = lastSummaryText;
            bool originalView = showJsonResult;
            string originalModelPath = model_path;
            string originalImagePath = image_path;
            string directory = Path.GetDirectoryName(Path.GetFullPath(uiTestOptions.SaveJsonPath));
            string temporaryPrefix = Path.Combine(directory, "json-check-" + Guid.NewGuid().ToString("N"));
            string largePath = temporaryPrefix + "-large.json";
            string modelPath = temporaryPrefix + "-model.dvt";
            string imagePath = temporaryPrefix + "-image.png";
            string emptyPath = temporaryPrefix + "-empty.json";
            string missingPath = Path.Combine(temporaryPrefix + "-missing", "result.json");
            try
            {
                RequireJsonCheck(!string.IsNullOrEmpty(originalJson), "真实推理未生成 JSON。");
                await SaveJsonToPathAsync(uiTestOptions.SaveJsonPath);
                long savedBytes = CheckSavedJson(uiTestOptions.SaveJsonPath, originalJson);
                richTextBox1.Clear();
                var timer = Stopwatch.StartNew();
                richTextBox1.Text = originalJson;
                timer.Stop();
                double baselinePreviewMs = timer.Elapsed.TotalMilliseconds;
                string fullPreview = richTextBox1.Text;
                RequireJsonCheck(NormalizeJsonCheckText(fullPreview) == NormalizeJsonCheckText(originalJson), "完整文本显示改变了真实 JSON。");
                timer.Restart();
                richTextBox1.SelectAll();
                string fullSelected = richTextBox1.SelectedText;
                timer.Stop();
                double baselineSelectAllMs = timer.Elapsed.TotalMilliseconds;
                RequireJsonCheck(fullSelected == fullPreview, "完整文本全选内容不一致。");
                richTextBox1.Clear();
                showJsonResult = true;
                timer.Restart();
                ApplyResultView();
                timer.Stop();
                double previewMs = timer.Elapsed.TotalMilliseconds;
                string preview = richTextBox1.Text;
                CheckJsonPreview(originalJson, preview);
                RequireJsonCheck(button_save_json.Enabled, "存在 JSON 时保存按钮未启用。");
                savingJson = true;
                UpdateResultViewButton();
                var closing = new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.UserClosing, false);
                Form1_FormClosing(this, closing);
                RequireJsonCheck(closing.Cancel && !button_save_json.Enabled, "保存期间允许关闭窗口或重复保存。");
                savingJson = false;
                UpdateResultViewButton();
                timer.Restart();
                richTextBox1.SelectAll();
                string selected = richTextBox1.SelectedText;
                timer.Stop();
                double selectAllMs = timer.Elapsed.TotalMilliseconds;
                RequireJsonCheck(selected == preview, "全选内容与显示内容不一致。");

                button_result_view_Click(this, EventArgs.Empty);
                string expectedSummary = string.IsNullOrEmpty(originalSummary) ? I18n.T("暂无汇总数据") : originalSummary;
                RequireJsonCheck(!showJsonResult && NormalizeJsonCheckText(richTextBox1.Text)
                    == NormalizeJsonCheckText(expectedSummary), "切换后未显示原汇总。");
                button_result_view_Click(this, EventArgs.Empty);
                RequireJsonCheck(showJsonResult && richTextBox1.Text == preview
                    && ReferenceEquals(lastJsonText, originalJson)
                    && ReferenceEquals(lastSummaryText, originalSummary), "结果切换改变了推理缓存。");

                bool unsupportedModelRejected = false;
                object originalModel = model;
                try
                {
                    LoadModelFromPath(temporaryPrefix + ".dvsp", uiTestOptions.DeviceId, false);
                }
                catch (NotSupportedException)
                {
                    unsupportedModelRejected = true;
                }
                RequireJsonCheck(unsupportedModelRejected && ReferenceEquals(lastJsonText, originalJson)
                    && ReferenceEquals(lastSummaryText, originalSummary) && ReferenceEquals((object)model, originalModel)
                    && model_path == originalModelPath && image_path == originalImagePath
                    && showJsonResult && richTextBox1.Text == preview && button_save_json.Enabled,
                    "不支持的模型格式改变了原结果或已加载模型。");

                var samples = new[]
                {
                    string.Empty,
                    "{\"内容\":\"中文😀\"}",
                    new string('中', 32768),
                    new string('中', 32769),
                    new string('中', 32767) + "😀尾部",
                    new string('中', 32766) + "😀尾部"
                };
                foreach (string sample in samples)
                {
                    CheckJsonPreview(sample, BuildJsonPreview(sample));
                }

                lastJsonText = "{\"内容\":\"" + new string('x', 2 * 1024 * 1024) + "中文😀\"}";
                lastSummaryText = "大型 JSON 保存验证";
                showJsonResult = true;
                timer.Restart();
                ApplyResultView();
                timer.Stop();
                double largePreviewMs = timer.Elapsed.TotalMilliseconds;
                string largePreview = richTextBox1.Text;
                CheckJsonPreview(lastJsonText, largePreview);
                RequireJsonCheck(button_save_json.Enabled, "大型 JSON 保存按钮未启用。");
                timer.Restart();
                richTextBox1.SelectAll();
                string largeSelected = richTextBox1.SelectedText;
                timer.Stop();
                double largeSelectAllMs = timer.Elapsed.TotalMilliseconds;
                RequireJsonCheck(largeSelected == largePreview, "大型 JSON 全选内容不一致。");
                File.WriteAllText(largePath, "旧结果", new UTF8Encoding(false, true));
                string largeJson = lastJsonText;
                Task saving = SaveJsonToPathAsync(largePath);
                lastJsonText = "{\"内容\":\"保存期间的新结果\"}";
                await saving;
                long largeSavedBytes = CheckSavedJson(largePath, largeJson);
                int largeJsonCharacters = largeJson.Length;

                ClearResultData();
                RequireJsonCheck(string.IsNullOrEmpty(lastJsonText) && string.IsNullOrEmpty(lastSummaryText)
                    && !button_save_json.Enabled, "清空缓存后保存按钮仍可用。");
                await RequireJsonSaveFailureAsync(emptyPath, typeof(InvalidOperationException));
                RequireJsonCheck(!File.Exists(emptyPath), "空缓存保存产生了文件。");

                lastJsonText = originalJson;
                lastSummaryText = originalSummary;
                ApplyResultView();
                await RequireJsonSaveFailureAsync(directory, typeof(IOException), typeof(UnauthorizedAccessException), typeof(ArgumentException));
                await RequireJsonSaveFailureAsync(missingPath, typeof(DirectoryNotFoundException));
                RequireJsonCheck(!Directory.Exists(Path.GetDirectoryName(missingPath)), "保存失败时创建了目录。");

                // 使用临时输入检查覆盖保护，避免错误实现触及真实模型和图片。
                File.WriteAllText(modelPath, "模型输入保护验证", new UTF8Encoding(false, true));
                File.WriteAllText(imagePath, "图片输入保护验证", new UTF8Encoding(false, true));
                model_path = modelPath;
                image_path = imagePath;
                await RequireJsonSaveFailureAsync(modelPath, typeof(ArgumentException), typeof(InvalidOperationException), typeof(IOException));
                await RequireJsonSaveFailureAsync(imagePath, typeof(ArgumentException), typeof(InvalidOperationException), typeof(IOException));
                RequireJsonCheck(File.ReadAllText(modelPath, new UTF8Encoding(false, true)) == "模型输入保护验证"
                    && File.ReadAllText(imagePath, new UTF8Encoding(false, true)) == "图片输入保护验证", "保存改变了输入文件。");

                int rejectedPaths = CheckSaveJsonArguments(directory, temporaryPrefix);
                return new JObject
                {
                    ["passed"] = true,
                    ["json_characters"] = originalJson.Length,
                    ["preview_characters"] = preview.Length,
                    ["baseline_preview_characters"] = fullPreview.Length,
                    ["baseline_preview_ms"] = baselinePreviewMs,
                    ["baseline_select_all_ms"] = baselineSelectAllMs,
                    ["preview_ms"] = previewMs,
                    ["select_all_ms"] = selectAllMs,
                    ["saved_bytes"] = savedBytes,
                    ["saved_utf8_without_bom"] = true,
                    ["saved_json_equal"] = true,
                    ["close_during_save_blocked"] = true,
                    ["saved_cache_snapshot"] = true,
                    ["existing_result_overwritten"] = true,
                    ["result_switch_preserved_cache"] = true,
                    ["unsupported_model_preserved_result"] = true,
                    ["preview_sample_count"] = samples.Length,
                    ["large_json_characters"] = largeJsonCharacters,
                    ["large_preview_characters"] = largePreview.Length,
                    ["large_preview_ms"] = largePreviewMs,
                    ["large_select_all_ms"] = largeSelectAllMs,
                    ["large_saved_bytes"] = largeSavedBytes,
                    ["empty_save_rejected"] = true,
                    ["invalid_save_paths_rejected"] = true,
                    ["input_files_protected"] = true,
                    ["invalid_arguments_rejected"] = rejectedPaths
                };
            }
            finally
            {
                savingJson = false;
                model_path = originalModelPath;
                image_path = originalImagePath;
                lastJsonText = originalJson;
                lastSummaryText = originalSummary;
                showJsonResult = originalView;
                ApplyResultView();
                foreach (string path in new[] { largePath, modelPath, imagePath, emptyPath, missingPath })
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                string missingDirectory = Path.GetDirectoryName(missingPath);
                if (Directory.Exists(missingDirectory)) Directory.Delete(missingDirectory);
            }
        }

        private static void CheckJsonPreview(string json, string preview)
        {
            if (json.Length <= 32768)
            {
                RequireJsonCheck(NormalizeJsonCheckText(preview) == NormalizeJsonCheckText(json), "小型 JSON 预览改变了内容。");
                return;
            }
            int length = 32768;
            if (char.IsHighSurrogate(json[length - 1]) && char.IsLowSurrogate(json[length])) length--;
            string expected = NormalizeJsonCheckText(json.Substring(0, length));
            string actual = NormalizeJsonCheckText(preview);
            RequireJsonCheck(actual.Length > expected.Length && actual.Length <= expected.Length + 1024
                && actual.EndsWith(expected, StringComparison.Ordinal), "大型 JSON 预览未保留限定长度的原文。");
            string notice = actual.Substring(0, actual.Length - expected.Length);
            RequireJsonCheck(notice.Contains("JSON")
                && notice.Contains(length.ToString("N0", CultureInfo.CurrentCulture)), "大型 JSON 缺少长度提示。");
            RequireJsonCheck(!char.IsHighSurrogate(actual[actual.Length - 1]), "预览截断了代理对。");
        }

        private static long CheckSavedJson(string path, string expected)
        {
            byte[] bytes = File.ReadAllBytes(path);
            RequireJsonCheck(bytes.Length < 3 || bytes[0] != 0xef || bytes[1] != 0xbb || bytes[2] != 0xbf, "JSON 文件包含 UTF-8 BOM。");
            var encoding = new UTF8Encoding(false, true);
            string saved = encoding.GetString(bytes);
            RequireJsonCheck(saved.Length == expected.Length && saved == expected
                && bytes.LongLength == encoding.GetByteCount(expected), "JSON 文件未保存完整原文。");
            RequireJsonCheck(JToken.DeepEquals(JToken.Parse(saved), JToken.Parse(expected)), "保存的 JSON 与推理缓存不一致。");
            return bytes.LongLength;
        }

        private async Task RequireJsonSaveFailureAsync(string path, params Type[] exceptionTypes)
        {
            try
            {
                await SaveJsonToPathAsync(path);
            }
            catch (Exception ex)
            {
                foreach (Type type in exceptionTypes)
                {
                    if (type.IsInstanceOfType(ex)) return;
                }
                throw;
            }
            throw new InvalidOperationException("保存未拒绝无效路径或空缓存。");
        }

        private int CheckSaveJsonArguments(string directory, string temporaryPrefix)
        {
            string screenshot = uiTestOptions.ScreenshotPath ?? temporaryPrefix + "-screen.png";
            var arguments = new List<string>
            {
                "ui-test", "--model", uiTestOptions.ModelPath, "--image", uiTestOptions.ImagePath,
                "--output", uiTestOptions.OutputPath, "--screenshot", screenshot,
                "--save-json", uiTestOptions.SaveJsonPath, "--test-mode", "infer"
            };
            RequireJsonCheck(UiTestOptions.TryParse(arguments.ToArray(), out UiTestOptions parsed, out string error)
                && parsed.SaveJsonPath == uiTestOptions.SaveJsonPath, "有效 JSON 保存参数未通过: " + error);
            arguments[12] = "infer-json";
            RequireJsonCheck(UiTestOptions.TryParse(arguments.ToArray(), out parsed, out error)
                && parsed.TestMode == "infer-json", "JSON 推理模式保存参数未通过: " + error);
            arguments[12] = "infer";
            int rejected = 0;
            foreach (string path in new[]
            {
                uiTestOptions.ModelPath, uiTestOptions.ImagePath, uiTestOptions.OutputPath,
                Path.GetFullPath(uiTestOptions.OutputPath) + ".tmp", screenshot, directory,
                Path.Combine(temporaryPrefix + "-missing", "result.json"), string.Empty
            })
            {
                arguments[10] = path;
                RequireJsonCheck(!UiTestOptions.TryParse(arguments.ToArray(), out parsed, out error)
                    && !string.IsNullOrEmpty(error), "JSON 保存参数未拒绝无效路径。");
                rejected++;
            }
            arguments[10] = uiTestOptions.SaveJsonPath;
            arguments[12] = "pressure";
            RequireJsonCheck(!UiTestOptions.TryParse(arguments.ToArray(), out parsed, out error)
                && !string.IsNullOrEmpty(error), "压力测试允许 JSON 保存参数。");
            return rejected + 1;
        }

        private static string NormalizeJsonCheckText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n");
        }

        private static void RequireJsonCheck(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
