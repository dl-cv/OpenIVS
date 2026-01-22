using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using dlcv_infer_csharp;
using OpenCvSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DlcvTest
{
    public partial class MainWindow
    {
        private bool IsLoadedModelDvp()
        {
            if (string.IsNullOrWhiteSpace(_loadedModelPath)) return false;
            try
            {
                return string.Equals(Path.GetExtension(_loadedModelPath), ".dvp", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void EnsureDvpParamsMirror(JObject inferenceParams)
        {
            if (inferenceParams == null) return;
            if (!IsLoadedModelDvp()) return;
            if (inferenceParams.ContainsKey("params_json")) return;

            var mirror = new JObject();
            foreach (var prop in inferenceParams.Properties().ToList())
            {
                if (string.Equals(prop.Name, "params_json", StringComparison.OrdinalIgnoreCase)) continue;
                mirror[prop.Name] = prop.Value;
            }
            inferenceParams["params_json"] = mirror;
        }

        private static bool IsInferDiagEnabled()
        {
            try
            {
                var v = Environment.GetEnvironmentVariable("DLCV_INFER_DIAG");
                if (string.IsNullOrWhiteSpace(v)) return false;
                v = v.Trim();
                return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetDvpServerUrl()
        {
            try
            {
                var v = Environment.GetEnvironmentVariable("DLCV_DVP_SERVER_URL");
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            catch
            {
                // ignore
            }
            return "http://127.0.0.1:9890";
        }

        private static string GetDiagLogPath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                string logDir = Path.Combine(baseDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                return Path.Combine(logDir, "infer_debug.log");
            }
            catch
            {
                return null;
            }
        }

        private static void AppendDiagLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            try
            {
                string path = GetDiagLogPath();
                if (string.IsNullOrWhiteSpace(path)) return;
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (_diagLogLock)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static string FormatParamsForLog(JObject p)
        {
            try
            {
                return p != null ? p.ToString(Formatting.None) : "<null>";
            }
            catch
            {
                return "<error>";
            }
        }

        private static int GetResultCount(Utils.CSharpResult result)
        {
            try
            {
                var samples = result.SampleResults;
                if (samples != null && samples.Count > 0)
                {
                    var r = samples[0].Results;
                    return r != null ? r.Count : 0;
                }
                return 0;
            }
            catch
            {
                return -1;
            }
        }

        private void LogInferStart(string tag, string imagePath, JObject inferenceParams, Mat image)
        {
            if (!IsInferDiagEnabled()) return;
            try
            {
                string imgName = string.IsNullOrEmpty(imagePath) ? "" : Path.GetFileName(imagePath);
                string size = image != null && !image.Empty() ? $"{image.Width}x{image.Height}" : "unknown";
                string modelPath = _loadedModelPath ?? "";
                AppendDiagLog($"[{tag}] start model='{modelPath}' image='{imgName}' size={size} params={FormatParamsForLog(inferenceParams)}");
            }
            catch
            {
                // ignore
            }
        }

        private void LogInferEnd(string tag, string imagePath, Utils.CSharpResult result, double elapsedMs)
        {
            if (!IsInferDiagEnabled()) return;
            try
            {
                string imgName = string.IsNullOrEmpty(imagePath) ? "" : Path.GetFileName(imagePath);
                int count = GetResultCount(result);
                AppendDiagLog($"[{tag}] end image='{imgName}' results={count} elapsedMs={elapsedMs:F2}");
            }
            catch
            {
                // ignore
            }
        }

        private void RunDvpHttpDiagnostic(Mat rgb, JObject inferenceParams, string imagePath, string tag)
        {
            if (!IsInferDiagEnabled()) return;
            if (!IsLoadedModelDvp()) return;
            if (rgb == null || rgb.Empty()) return;

            try
            {
                string serverUrl = GetDvpServerUrl();
                string modelPath = _loadedModelPath ?? "";
                string imgName = string.IsNullOrEmpty(imagePath) ? "" : Path.GetFileName(imagePath);

                string responseJson = null;
                int resultCount = -1;
                string code = null;
                string message = null;
                var sw = Stopwatch.StartNew();

                using (var bgr = new Mat())
                {
                    try
                    {
                        Cv2.CvtColor(rgb, bgr, ColorConversionCodes.RGB2BGR);
                    }
                    catch
                    {
                        rgb.CopyTo(bgr);
                    }

                    byte[] imageBytes = bgr.ToBytes(".png");
                    string base64Image = Convert.ToBase64String(imageBytes);

                    var request = new JObject
                    {
                        ["img"] = base64Image,
                        ["model_path"] = modelPath,
                        ["return_polygon"] = true
                    };

                    if (inferenceParams != null)
                    {
                        foreach (var param in inferenceParams.Properties())
                        {
                            request[param.Name] = param.Value;
                        }
                    }

                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);
                        var content = new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json");
                        var response = client.PostAsync($"{serverUrl}/api/inference", content).Result;
                        responseJson = response.Content.ReadAsStringAsync().Result;
                    }
                }

                sw.Stop();

                if (!string.IsNullOrWhiteSpace(responseJson))
                {
                    try
                    {
                        var obj = JObject.Parse(responseJson);
                        code = obj["code"]?.ToString();
                        message = obj["message"]?.ToString();
                        if (obj["results"] is JArray r1)
                        {
                            resultCount = r1.Count;
                        }
                        else if (obj["sample_results"] is JArray sr && sr.Count > 0)
                        {
                            var r2 = sr[0]?["results"] as JArray;
                            if (r2 != null) resultCount = r2.Count;
                        }
                        else if (obj["data"]?["results"] is JArray r3)
                        {
                            resultCount = r3.Count;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                string snippet = responseJson;
                if (!string.IsNullOrEmpty(snippet) && snippet.Length > 512)
                {
                    snippet = snippet.Substring(0, 512) + "...";
                }

                AppendDiagLog($"[{tag}|dvp_http] image='{imgName}' url='{serverUrl}' results={resultCount} elapsedMs={sw.Elapsed.TotalMilliseconds:F2} code='{code}' msg='{message}' resp='{snippet}'");
            }
            catch (Exception ex)
            {
                try
                {
                    string imgName = string.IsNullOrEmpty(imagePath) ? "" : Path.GetFileName(imagePath);
                    AppendDiagLog($"[{tag}|dvp_http] image='{imgName}' error='{ex.Message}'");
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}

