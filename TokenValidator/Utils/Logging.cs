using System.IO;

namespace TokenValidator.Utils
{
    public static class Logging
    {
        private readonly static string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenValidator", "Class Logs");

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
                LogException(ex);
            }
        }
    }
}
