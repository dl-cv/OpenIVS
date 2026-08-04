using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace OpenIVS2
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            var acceptanceMode = e.Args.Any(x => string.Equals(x, "--acceptance", StringComparison.OrdinalIgnoreCase));
            var window = new MainWindow(acceptanceMode);
            MainWindow = window;
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("发生未处理错误：" + e.Exception.Message, "OpenIVS 2026", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
