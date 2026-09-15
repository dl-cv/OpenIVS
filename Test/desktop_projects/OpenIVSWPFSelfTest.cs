using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dlcv_infer_csharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenIVSWPF.Managers;

namespace OpenIVSWPF
{
    internal static class DesktopSelfTest
    {
        private const double ScoreTolerance = 1e-6;
        internal static string ArtifactDirectory { get; private set; }

        internal static bool IsRequested(string[] args)
        {
            return args != null && args.Length > 0 && string.Equals(args[0], "selftest", StringComparison.OrdinalIgnoreCase);
        }

        internal static Task<int> RunAsync(string[] args)
        {
            string outputPath = args != null && args.Length >= 5 ? args[4] : null;
            if (args == null || args.Length != 5 ||
                !string.Equals(args[0], "selftest", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(args[1], "--model-root", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(args[3], "--output", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(args[2]) || string.IsNullOrWhiteSpace(outputPath))
            {
                return Task.FromResult(2);
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
                    MainWindow.RunDesktopSelfTestCase(
                        "classification", modelRoot, "猫狗-分类_120_50_s.dvt", "猫狗-狗.jpg", "狗", 0.9951171875),
                    MainWindow.RunDesktopSelfTestCase(
                        "segmentation", modelRoot, "气球-实例分割_120_50_s.dvt", "气球.jpg", "气球", 0.9892578125)
                };

                bool passed = cases.Cast<JObject>().All(item => item.Value<bool>("passed"));
                return Task.FromResult(WriteResult(outputPath, passed, cases) ? (passed ? 0 : 4) : 3);
            }
            catch (Exception ex)
            {
                var cases = new JArray(CreateCase("selftest", false, 0, new string[0], new double[0], false, false, ex.Message));
                WriteResult(outputPath, false, cases);
                return Task.FromResult(3);
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
        private Utils.CSharpResult? _selfTestResult;
        private bool _selfTestRenderCompleted;

        private MainWindow(SelfTestWindowMarker marker)
        {
            InitializeComponent();
            Closing -= Window_Closing;
            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;
            _modelManager = new ModelManager(
                UpdateStatus,
                ViewModel.UpdateModelStatus,
                (image, result) =>
                {
                    UpdateDisplayImage(image, result);
                    _selfTestResult = (Utils.CSharpResult)result;
                    _selfTestRenderCompleted = _lastCapturedImage != null;
                });
        }

        internal static JObject RunDesktopSelfTestCase(string name, string modelRoot,
            string modelFile, string imageFile, string expectedCategory, double expectedScore)
        {
            MainWindow window = null;
            Model loadedModel = null;
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
                var settings = new Settings { ModelPath = modelPath, ModelType = "DVT" };
                window._modelManager.InitializeModel(settings);
                loadedModel = window._modelManager.ModelForSelfTest;
                if (!window._modelManager.IsLoaded) throw new InvalidOperationException("实际程序模型加载失败");

                using (var image = new Bitmap(imagePath))
                {
                    window._modelManager.PerformInference(image);
                }

                using (var rendered = window.imageViewer1.CreateVisualizationBitmap())
                {
                    if (rendered != null)
                    {
                        rendered.Save(Path.Combine(DesktopSelfTest.ArtifactDirectory, name + ".png"),
                            System.Drawing.Imaging.ImageFormat.Png);
                        renderPassed = window._selfTestRenderCompleted && window._selfTestResult.HasValue &&
                            rendered.Width > 0 && rendered.Height > 0;
                    }
                }
                if (window._selfTestResult.HasValue && window._selfTestResult.Value.SampleResults != null &&
                    window._selfTestResult.Value.SampleResults.Count == 1)
                {
                    var objects = window._selfTestResult.Value.SampleResults[0].Results ?? new List<Utils.CSharpObjectResult>();
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
                        window.imageViewer1.ClearResults();
                        DisposeMasks(window._selfTestResult);
                        window._selfTestResult = null;
                        window._lastCapturedImage?.Dispose();
                        window._lastCapturedImage = null;
                        window._modelManager.Dispose();
                        window._modelManager.Dispose();
                        releasePassed = !window._modelManager.IsLoaded && loadedModel != null && loadedModel.modelIndex == -1;
                        window.imageViewer1.Dispose();
                        window.Close();
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

        private static void DisposeMasks(Utils.CSharpResult? result)
        {
            if (!result.HasValue || result.Value.SampleResults == null) return;
            foreach (var sample in result.Value.SampleResults)
            {
                if (sample.Results == null) continue;
                foreach (var item in sample.Results)
                {
                    item.Mask?.Dispose();
                }
            }
        }
    }
}

namespace OpenIVSWPF.Managers
{
    public partial class ModelManager
    {
        // 保留原模型引用以检查释放后的编号，防止仅清空管理器字段被计为通过。
        internal Model ModelForSelfTest => _model;
    }
}
