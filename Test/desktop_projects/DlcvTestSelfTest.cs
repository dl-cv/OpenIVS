using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DlcvTest
{
    internal static class DesktopSelfTest
    {
        private const double ScoreTolerance = 1e-6;
        internal static string ArtifactDirectory { get; private set; }

        internal static bool IsRequested(string[] args)
        {
            return args != null && args.Length > 0 && string.Equals(args[0], "selftest", StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<int> RunAsync(string[] args)
        {
            string outputPath = args != null && args.Length >= 5 ? args[4] : null;
            if (args == null || args.Length != 5 ||
                !string.Equals(args[0], "selftest", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(args[1], "--model-root", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(args[3], "--output", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(args[2]) || string.IsNullOrWhiteSpace(outputPath))
            {
                return 2;
            }

            try
            {
                string modelRoot = Path.GetFullPath(args[2]);
                string fullOutput = Path.GetFullPath(outputPath);
                string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullOutput.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetExtension(fullOutput), ".json", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("自测输出必须是系统临时目录内的 JSON 文件");
                ArtifactDirectory = Path.GetDirectoryName(fullOutput);
                Directory.CreateDirectory(ArtifactDirectory);
                var cases = new JArray
                {
                    await MainWindow.RunDesktopSelfTestCaseAsync(
                        "classification", modelRoot, "猫狗-分类_PLUS_s.dvt", "猫狗-狗.jpg", "狗", 0.9951171875),
                    await MainWindow.RunDesktopSelfTestCaseAsync(
                        "segmentation", modelRoot, "气球-实例分割_PLUS_s.dvt", "气球.jpg", "气球", 0.9892578125)
                };

                bool passed = cases.Cast<JObject>().All(item => item.Value<bool>("passed"));
                return WriteResult(outputPath, passed, cases) ? (passed ? 0 : 4) : 3;
            }
            catch (Exception ex)
            {
                var cases = new JArray(CreateCase("selftest", false, 0, new string[0], new double[0], false, false, ex.Message));
                WriteResult(outputPath, false, cases);
                return 3;
            }
        }

        internal static JObject CreateCase(string name, bool passed, int count, IEnumerable<string> categories,
            IEnumerable<double> scores, bool releasePassed, bool renderPassed, string error)
        {
            return new JObject
            {
                ["name"] = name,
                ["passed"] = passed,
                ["count"] = count,
                ["categories"] = new JArray(categories),
                ["scores"] = new JArray(scores),
                ["release_check_passed"] = releasePassed,
                ["render_check_passed"] = renderPassed,
                ["error"] = error == null ? JValue.CreateNull() : new JValue(error)
            };
        }

        private static bool WriteResult(string outputPath, bool passed, JArray cases)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputPath) || ArtifactDirectory == null) return false;
                string fullPath = Path.GetFullPath(outputPath);
                if (!string.Equals(Path.GetDirectoryName(fullPath), ArtifactDirectory, StringComparison.OrdinalIgnoreCase)) return false;
                var root = new JObject { ["passed"] = passed, ["cases"] = cases };
                File.WriteAllText(fullPath, root.ToString(Formatting.None), new UTF8Encoding(false, true));
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static bool ScoreMatches(double actual, double expected)
        {
            return Math.Abs(actual - expected) <= ScoreTolerance;
        }
    }

    public partial class MainWindow
    {
        private sealed class SelfTestWindowMarker { }

        private MainWindow(SelfTestWindowMarker marker)
        {
            _selfTestMode = true;
            InitializeComponent();
            Closing -= Window_Closing;
            DataContext = this;
            InitializeWpfViewers();
            _isInitializing = true;
            ConfidenceVal.Text = "0.5";
            AutoLabelComplexityVal.Text = "0.001";
            TopKVal.Text = "1";
        }

        internal static async Task<JObject> RunDesktopSelfTestCaseAsync(string name, string modelRoot,
            string modelFile, string imageFile, string expectedCategory, double expectedScore)
        {
            MainWindow window = null;
            Utils.CSharpResult? result = null;
            var categories = new List<string>();
            var scores = new List<double>();
            int count = 0;
            bool renderPassed = false;
            bool releasePassed = false;
            string error = null;

            try
            {
                string modelPath = Path.Combine(modelRoot, modelFile);
                string imagePath = Path.Combine(modelRoot, imageFile);
                if (!File.Exists(modelPath)) throw new FileNotFoundException("模型文件不存在", modelPath);
                if (!File.Exists(imagePath)) throw new FileNotFoundException("图片文件不存在", imagePath);

                window = new MainWindow(new SelfTestWindowMarker());
                if (!await window.LoadModelAsync(modelPath, false)) throw new InvalidOperationException("实际程序模型加载失败");

                window._currentImagePath = imagePath;
                await window.ProcessSelectedImageAsync(imagePath);
                result = window.wpfViewer2.Result;
                var viewer = window.wpfViewer2;
                viewer.Width = 500;
                viewer.Height = 400;
                viewer.Measure(new System.Windows.Size(500, 400));
                viewer.Arrange(new System.Windows.Rect(0, 0, 500, 400));
                viewer.UpdateLayout();
                viewer.ResetViewToFit();
                await viewer.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                viewer.UpdateLayout();
                var rendered = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    500, 400, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rendered.Render(viewer);
                byte[] pixels = new byte[500 * 400 * 4];
                rendered.CopyPixels(pixels, 500 * 4, 0);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rendered));
                using (var stream = File.Create(Path.Combine(DesktopSelfTest.ArtifactDirectory, name + ".png")))
                    encoder.Save(stream);
                renderPassed = viewer.Source != null && result.HasValue && pixels.Distinct().Count() > 4;

                if (result.HasValue && result.Value.SampleResults != null && result.Value.SampleResults.Count == 1)
                {
                    var objects = result.Value.SampleResults[0].Results ?? new List<Utils.CSharpObjectResult>();
                    count = objects.Count;
                    categories.AddRange(objects.Select(item => item.CategoryName));
                    scores.AddRange(objects.Select(item => (double)item.Score));
                }

                bool baselinePassed = count == 1 && categories.Count == 1 && scores.Count == 1 &&
                    string.Equals(categories[0], expectedCategory, StringComparison.Ordinal) &&
                    DesktopSelfTest.ScoreMatches(scores[0], expectedScore);
                if (!baselinePassed) error = "推理结果与固定基准不符";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                if (window != null)
                {
                    try
                    {
                        window.wpfViewer2.ClearResults();
                        window.wpfViewer2.Source = null;
                        DisposeCSharpResultMasks(result);
                        var loadedModel = window.model as Model;
                        var disposable = window.model as IDisposable;
                        if (disposable != null)
                        {
                            disposable.Dispose();
                            disposable.Dispose();
                        }
                        window.model = null;
                        window.Close();
                        releasePassed = loadedModel != null && loadedModel.modelIndex == -1;
                    }
                    catch (Exception ex)
                    {
                        error = error ?? ("释放失败: " + ex.Message);
                    }
                }
            }

            bool passed = error == null && count == 1 && renderPassed && releasePassed;
            return DesktopSelfTest.CreateCase(name, passed, count, categories, scores, releasePassed, renderPassed, error);
        }
    }
}
