using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DlcvDemo
{
    public partial class MainWindow
    {
        private async void RunDogInfoUiTest()
        {
            try
            {
                var devices = new JArray();
                var features = new JArray();
                for (int index = 1; index <= 10; index++)
                {
                    devices.Add("TEST-" + index.ToString("D4"));
                    features.Add("0");
                }
                for (int index = 0; index < 10; index++)
                {
                    foreach (string feature in new[] { "10120", "3", "2", "1" })
                        features.Add(feature);
                }
                var info = new JObject
                {
                    ["sentinel"] = new JObject { ["devices"] = devices, ["features"] = features },
                    ["virbox"] = new JObject { ["devices"] = new JArray(), ["features"] = new JArray() }
                };
                richTextBox1.Text = FormatDogInfoText(info, prependNoDogHint: false);
                richTextBox1.Select(0, 0);
                richTextBox1.ScrollToCaret();
                WriteUiTestResult("displayed", null);
                Refresh();
                await Task.Delay(uiTestOptions.CaptureDelayMs);
                UiTestExitCode = 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                WriteUiTestResult("failed", error);
            }
            finally
            {
                Close();
            }
        }
    }
}
