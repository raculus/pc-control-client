using System;
using System.IO;
using Windows.Storage;

namespace pc_control_client
{
    public static class SimpleLogger
    {
        private static readonly object _lock = new object();

        // MSIX 전용 안전한 AppData 로컬 저장소 경로 사용
        private static readonly string LogFolderPath = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "Logs"
        );

        public static void Log(string message)
        {
            WriteToFile("INFO", message);
        }

        public static void LogError(string message, Exception? ex = null)
        {
            string logMessage = ex != null ? $"{message} | Exception: {ex.Message}\n{ex.StackTrace}" : message;
            WriteToFile("ERROR", logMessage);
        }

        private static void WriteToFile(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(LogFolderPath))
                    {
                        Directory.CreateDirectory(LogFolderPath);
                    }

                    string fileName = $"log_{DateTime.Now:yyyy-MM-DD}.txt";
                    string filePath = Path.Combine(LogFolderPath, fileName);
                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

                    File.AppendAllText(filePath, logLine);
                }
            }
            catch
            {
                // 로깅 예외 무시
            }
        }
    }
}