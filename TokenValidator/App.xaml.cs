using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace TokenValidator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var services = new ServiceCollection();
            services.AddHttpClient("SCPClient", client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "SCP SL Token Validation Tool");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            var provider = services.BuildServiceProvider();
            Current.Properties["ServiceProvider"] = provider;

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = (Exception)args.ExceptionObject;
                if (!IsClipboardException(ex))
                    LogUnhandledException(ex, "AppDomain.CurrentDomain.UnhandledException");
            };

            Current.DispatcherUnhandledException += (s, args) =>
            {
                if (!IsClipboardException(args.Exception))
                {
                    LogUnhandledException(args.Exception, "Application.Current.DispatcherUnhandledException");
                }
                args.Handled = true;
            };

            base.OnStartup(e);
        }

        private static bool IsClipboardException(Exception ex)
        {
            return ex is System.Runtime.InteropServices.COMException comEx &&
                   comEx.ErrorCode == unchecked((int)0x800401D0);
        }

        private static void LogUnhandledException(Exception exception, string source)
        {
            string errorMessage = $"An unhandled exception occurred: {exception.Message}";
            System.Windows.MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            try
            {
                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TokenValidator",
                    "error.log");

                string errorLog = $"[{DateTime.Now}] {source}: {exception.Message}\r\n{exception.GetType().FullName}\r\n{exception.StackTrace}\r\n\r\n";
                System.IO.File.AppendAllText(logPath, errorLog);
            }
            catch
            {

            }
        }
    }
}

