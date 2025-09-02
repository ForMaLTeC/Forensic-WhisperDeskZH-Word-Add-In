using System;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ForensicWhisperDeskZH.Common
{
    /// <summary>
    /// Provides centralized logging functionality
    /// </summary>
    public class LoggingService
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FennecTranscriptionSystem",
            "Logs");

        private static readonly string LogPath = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");
        private static readonly string ErrorPath = Path.Combine(LogDirectory, $"errors_{DateTime.Now:yyyyMMdd}.txt");

        private static readonly SemaphoreSlim LogLock = new SemaphoreSlim(1, 1);

        static LoggingService()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
            }
            catch
            {
                // Fallback to local directory if unable to create log directory
            }
        }

        /// <summary>
        /// Logs an informational message
        /// </summary>
        public static async Task LogMessageAsync(string message, string source = "Application")
        {
            await LogToFileAsync(LogPath, $"[INFO] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message}");
        }

        /// <summary>
        /// Logs an error message and optional exception
        /// </summary>
        public static async Task LogErrorAsync(string message, Exception ex = null, string source = "Application")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[ERROR] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]");
            sb.AppendLine($"Message: {message}");

            if (ex != null)
            {
                sb.AppendLine("Exception Details:");
                AppendExceptionDetails(sb, ex);
            }

            await LogToFileAsync(ErrorPath, sb.ToString());
            await LogToFileAsync(LogPath, $"[ERROR] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message}");
        }

        /// <summary>
        /// Logs a warning message
        /// </summary>
        public static async Task LogWarningAsync(string message, string source = "Application")
        {
            await LogToFileAsync(LogPath, $"[WARN] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message}");
        }

        private static void AppendExceptionDetails(StringBuilder sb, Exception ex, int level = 0)
        {
            if (ex == null) return;

            string indent = new string(' ', level * 2);
            sb.AppendLine($"{indent}Type: {ex.GetType().FullName}");
            sb.AppendLine($"{indent}Message: {ex.Message}");
            sb.AppendLine($"{indent}StackTrace:");
            sb.AppendLine($"{indent}{ex.StackTrace}");

            if (ex.InnerException != null)
            {
                sb.AppendLine($"{indent}--- Inner Exception ---");
                AppendExceptionDetails(sb, ex.InnerException, level + 1);
            }
        }

        private static async Task LogToFileAsync(string path, string content)
        {
            try
            {
                await LogLock.WaitAsync();
                try
                {
                    using (var writer = new StreamWriter(path, true))
                    {
                        await writer.WriteLineAsync(content);
                    }
                }
                finally
                {
                    LogLock.Release();
                }
            }
            catch
            {
                // Unable to log, but we don't want to crash the application
            }
        }

        // Synchronous versions for compatibility with existing code
        public static void LogMessage(string message, string source = "Application")
        {
            Task.Run(() => LogMessageAsync(message, source)).Wait();
        }

        public static void LogError(string message, Exception ex = null, string source = "Application")
        {
            Task.Run(() => LogErrorAsync(message, ex, source)).Wait();
        }

        public static void LogWarning(string message, string source = "Application")
        {
            Task.Run(() => LogWarningAsync(message, source)).Wait();
        }

        /// <summary>
        /// Plays a system warning sound to indicate transcription start
        /// </summary>
        public static void PlayTranscriptionStateChangeSound()
        {
            try
            {
                // Play system warning sound (async to avoid blocking)
                Task.Run(() =>
                {
                    try
                    {
                        SystemSounds.Asterisk.Play();
                    }
                    catch (Exception ex)
                    {
                        // If SystemSounds fails, try Console.Beep as fallback
                        try
                        {
                            Console.Beep(800, 200); // 800Hz for 200ms
                        }
                        catch
                        {
                            // Log but don't throw - sound is not critical for functionality
                            LoggingService.LogMessage($"TranscriptionService: Failed to play start sound: {ex.Message}", "TranscriptionService_PlayTranscriptionStartSound");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Log but don't throw - sound is not critical for functionality
                LoggingService.LogMessage($"TranscriptionService: Error initiating start sound: {ex.Message}", "TranscriptionService_PlayTranscriptionStartSound");
            }
        }

        /// <summary>
        /// Plays a system warning sound to indicate transcription start
        /// </summary>
        public static void PlayDictationModeChangeSound()
        {
            try
            {
                // Play system warning sound (async to avoid blocking)
                Task.Run(() =>
                {
                    try
                    {
                        SystemSounds.Exclamation.Play();
                    }
                    catch (Exception ex)
                    {
                        // If SystemSounds fails, try Console.Beep as fallback
                        try
                        {
                            Console.Beep(800, 200); // 800Hz for 200ms
                        }
                        catch
                        {
                            // Log but don't throw - sound is not critical for functionality
                            LoggingService.LogMessage($"TranscriptionService: Failed to play start sound: {ex.Message}", "TranscriptionService_PlayTranscriptionStartSound");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Log but don't throw - sound is not critical for functionality
                LoggingService.LogMessage($"TranscriptionService: Error initiating start sound: {ex.Message}", "TranscriptionService_PlayTranscriptionStartSound");
            }
        }
    }
}