using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static System.Net.WebRequestMethods;

namespace AppBootstrapper
{
    class Program
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                if (!IsDotNet9Instaleld())
                {
                    var dialogResult = MessageBox.Show(
                       owner: new WindowWrapper(GetActiveWindow()),
                       text: "This application requires .NET 9.0 Runtime. Would you like to download and install it now?",
                       caption: "Prerequisite Required",
                       buttons: MessageBoxButtons.YesNo,
                       icon: MessageBoxIcon.Question);

                    if (dialogResult == DialogResult.Yes)
                    {
                        DownloadAndInstallDotNet9();
                    }
                    return;
                }

                LaunchWpfApp();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"An error occurred: {ex.Message}\n\nPlease contact support.",
                    "Application Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        static bool IsDotNet9Instaleld()
        {
            const string subkey = @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost";

            using (var npdKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subkey))
            {
                var version = npdKey?.GetValue("Version")?.ToString();
                return version?.StartsWith("9.") ?? false;
            }
        }

        static void DownloadAndInstallDotNet9()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "donet9-runtime.exe");
            string downloadUrl = "https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe";

            try
            {
                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        Console.Write($"\rDownloading .NET 9.0 Runtime: {e.ProgressPercentage}%");
                    };

                    Console.WriteLine("Downloading .NET 9 Runtime...");
                    client.DownloadFileTaskAsync(new Uri(downloadUrl), tempFile).Wait();
                }

                Console.WriteLine("Installing .NET 9 Runtime...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = tempFile,
                    Arguments = "/install /quiet /norestart",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                var process = Process.Start(startInfo);
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    MessageBox.Show(
                        "The .NET 9 installation failed. Please try installing manually.",
                        "Installation Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show(
                        ".NET 9 Runtime was successfully installed. Please restart the application.",
                        "Installation Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to install .NET 9: {ex.Message}",
                    "Installation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                {
                    try { System.IO.File.Delete(tempFile); } catch { }
                }
            }
        }

        static void LaunchWpfApp()
        {
            try
            {
                string wpfAppPath = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "TokenValidator.exe");

                if (!System.IO.File.Exists(wpfAppPath))
                {
                    MessageBox.Show(
                        "Main application not found. Please reinstall the software.",
                        "Application Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                Process.Start(wpfAppPath);
            }
            catch (Exception)
            {

                throw;
            }
        }

    }
    public class WindowWrapper : IWin32Window
    {
        public WindowWrapper(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
