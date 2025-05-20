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
                LogUnhandledException((Exception)args.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");

            Current.DispatcherUnhandledException += (s, args) =>
            {
                LogUnhandledException(args.Exception, "Application.Current.DispatcherUnhandledException");
                args.Handled = true;
            };

            base.OnStartup(e);

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

