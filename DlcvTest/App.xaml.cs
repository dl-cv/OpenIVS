using System;
using System.Windows;

namespace DlcvTest
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (!DesktopSelfTest.IsRequested(e.Args))
            {
                StartupUri = new Uri("Views/MainWindow/MainWindow.xaml", UriKind.Relative);
                base.OnStartup(e);
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                int exitCode = await DesktopSelfTest.RunAsync(e.Args);
                Environment.ExitCode = exitCode;
                Shutdown(exitCode);
            }));
        }
    }
}
