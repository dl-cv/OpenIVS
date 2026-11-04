using System;
using System.Threading.Tasks;

namespace DlcvDemo
{
    public partial class MainWindow
    {
        private void RunModelDialogTest()
        {
            string originalPath = Properties.Settings.Default.LastModelPath;
            try
            {
                Properties.Settings.Default.LastModelPath = uiTestOptions.ModelPath;
                WriteUiTestResult("opening-model-dialog", null);
                // 原生文件窗口交给隔离采集器截图，超时后结束本次测试进程。
                Task.Delay(30000).ContinueWith(task => Environment.Exit(0));
                button_loadmodel_Click(this, EventArgs.Empty);
            }
            finally
            {
                Properties.Settings.Default.LastModelPath = originalPath;
                Close();
            }
        }
    }
}
