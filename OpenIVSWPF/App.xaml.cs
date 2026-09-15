using System;
using System.Windows;

namespace OpenIVSWPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (DesktopSelfTest.IsRequested(e.Args))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    int exitCode = await DesktopSelfTest.RunAsync(e.Args);
                    Environment.ExitCode = exitCode;
                    Shutdown(exitCode);
                }));
                return;
            }

            StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
            base.OnStartup(e);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            Dispatcher.UnhandledException += Dispatcher_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"发生未处理的异常：{e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void Dispatcher_UnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"发生未处理的异常：{e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
