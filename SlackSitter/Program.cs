using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SlackSitter
{
    internal static class StartupTrace
    {
        private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlackSitter");
        private static string FilePath => Path.Combine(DirectoryPath, "startup-trace.log");

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        public static void LogException(string source, Exception exception)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var filePath = Path.Combine(DirectoryPath, "startup-error.log");
                File.AppendAllText(filePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }

    public static class Program
    {
        [DllImport("Microsoft.ui.xaml.dll")]
        private static extern void XamlCheckProcessRequirements();

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                StartupTrace.Log("Program.Main entered");

                AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                {
                    StartupTrace.LogException("AppDomain.CurrentDomain.UnhandledException", eventArgs.ExceptionObject as Exception ?? new Exception("Unhandled non-exception error."));
                };

                TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
                {
                    StartupTrace.LogException("TaskScheduler.UnobservedTaskException", eventArgs.Exception);
                    eventArgs.SetObserved();
                };

                StartupTrace.Log("Calling XamlCheckProcessRequirements");
                XamlCheckProcessRequirements();
                StartupTrace.Log("XamlCheckProcessRequirements completed");
                WinRT.ComWrappersSupport.InitializeComWrappers();
                StartupTrace.Log("InitializeComWrappers completed");

                Application.Start(_ =>
                {
                    StartupTrace.Log("Application.Start callback entered");
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    var app = new App();
                    StartupTrace.Log("App instance created");
                });
            }
            catch (Exception ex)
            {
                StartupTrace.LogException("Program.Main", ex);
                throw;
            }
        }
    }
}
