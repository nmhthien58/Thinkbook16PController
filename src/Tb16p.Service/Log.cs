using Tb16p.Core.Config;

namespace Tb16p.Service;

/// <summary>Small append-only log in the settings folder, reset when it grows past 1 MB.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(AppSettings.Directory, "log.txt");

    public static void Info(string message) => Write("INFO ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
