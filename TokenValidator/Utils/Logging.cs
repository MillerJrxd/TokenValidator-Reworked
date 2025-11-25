using System.IO;
using System.Reflection.Emit;
using System.Text;

namespace TokenValidator.Utils
{
    public static class Logging
    {
        private readonly static string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenValidator", "Class Logs");

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error,
            Critical
        }

        public static void LogException(Exception ex, string? context = null)
        {
            LogError($"Exception occurred{(context != null ? $" in {context}" : "")}", ex);
        }

        public static void LogDebug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public static void LogInfo(string message)
        {
            Log(LogLevel.Info, message);
        }

        public static void LogWarning(string message)
        {
            Log(LogLevel.Warning, message);
        }

        public static void LogError (string message, Exception? ex = null)
        {
            Log(LogLevel.Error, message, ex);
        }

        public static void LogCritical(string message, Exception? ex = null)
        {
            Log(LogLevel.Critical, message, ex);
        }

        private static void Log(LogLevel level, string message, Exception? ex = null)
        {
            try
            {
                if (!Directory.Exists(LogFilePath))
                {
                    Directory.CreateDirectory(LogFilePath);
                }

                string logFileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
                string logFile = Path.Combine(LogFilePath, logFileName);

                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");

                if (ex != null)
                {
                    sb.AppendLine($"  Exception Type: {ex.GetType().FullName}");
                    sb.AppendLine($"  Message: {ex.Message}");
                    sb.AppendLine($"  StackTrace:");

                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        foreach (var line in ex.StackTrace.Split('\n'))
                        {
                            sb.AppendLine($"    {line.TrimEnd()}");
                        }
                    }

                    if (ex.InnerException != null)
                    {
                        sb.AppendLine($"  Inner Exception Type: {ex.InnerException.GetType().FullName}");
                        sb.AppendLine($"  Inner Message: {ex.InnerException.Message}");

                        if (!string.IsNullOrEmpty(ex.InnerException.StackTrace))
                        {
                            sb.AppendLine($"  Inner StackTrace:");
                            foreach (var line in ex.InnerException.StackTrace.Split('\n'))
                            {
                                sb.AppendLine($"    {line.TrimEnd()}");
                            }
                        }
                    }
                }

                sb.AppendLine();

                lock (typeof(Logging))
                {
                    File.AppendAllText(logFile, sb.ToString());
                }
            }
            catch
            {

            }
        }

        public static void ClearLogs()
        {
            try
            {
                if (Directory.Exists(LogFilePath))
                {
                    foreach (var logFile in Directory.GetFiles(LogFilePath, "log_*.txt"))
                    {
                        var creationTime = File.GetCreationTime(logFile);
                        if (DateTime.Now - creationTime > TimeSpan.FromDays(14))
                            File.Delete(logFile);
                    }   
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "ClearLogs");
            }
        }
    }
}
