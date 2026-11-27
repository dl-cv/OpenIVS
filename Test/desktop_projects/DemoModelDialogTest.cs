using System;
using System.Threading.Tasks;

namespace DlcvDemo
{
    public partial class MainWindow
    {
        private async void RunModelDialogTest()
        {
            string originalPath = Properties.Settings.Default.LastModelPath;
            try
            {
                Properties.Settings.Default.LastModelPath = uiTestOptions.ModelPath;
                WriteUiTestResult("opening-model-dialog", null);
                // 原生文件窗口交给隔离采集器截图，超时后结束本次测试进程。
                Task.Delay(30000).ContinueWith(task => Environment.Exit(0));
                await RunOperationAsync(SelectModelAsync, "加载模型失败");
            }
            finally
            {
                Properties.Settings.Default.LastModelPath = originalPath;
                Close();
            }
        }
    }
}
