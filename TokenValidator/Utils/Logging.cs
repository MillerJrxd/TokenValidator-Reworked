using System.IO;

namespace TokenValidator.Utils
{
    public static class Logging
    {
        private static string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenValidator", "Class Logs");

        public static void LogException(Exception ex)
        {
            try
            {
                if (!Directory.Exists(LogFilePath))
                {
                    Directory.CreateDirectory(LogFilePath);
                }

                string logFileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string logFile = Path.Combine(LogFilePath, logFileName);

                using (StreamWriter writer = new StreamWriter(logFile, true))
                {
                    writer.WriteLine($"{DateTime.Now}: Exception occurred.");
                    writer.WriteLine($"Type: {ex.GetType().FullName}");
                    writer.WriteLine($"Message: {ex.Message}");
                    writer.WriteLine($"StackTrace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        writer.WriteLine($"InnerException Type: {ex.InnerException.GetType().FullName}");
                        writer.WriteLine($"InnerException: {ex.InnerException.Message}");
                        writer.WriteLine($"InnerException StackTrace: {ex.InnerException.StackTrace}");
                    }
                }
            }
            catch
            {
                // If logging fails, there's nothing we can do.  
            }
        }

        public static void ClearLogs()
        {
            try
            {
                if (Directory.Exists(LogFilePath))
                {
                    string[] logFiles = Directory.GetFiles(LogFilePath, "log_*.txt");
                    foreach (string logFile in logFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(logFile);
                        if (fileName.StartsWith("log_") && DateTime.TryParseExact(fileName.Substring(4), "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out DateTime logDate))
                        {
                            if ((DateTime.Now - logDate).TotalDays > 14)
                            {
                                File.Delete(logFile);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }
    }
}
