using System.Diagnostics;
using System.IO;
using NVDriverInstaller.Core.Config;

namespace NVDriverInstaller.Services;

/// <summary>
/// Thread-safe file and console logging service for NVDriver Installer.
/// Writes persistent logs into the logs/ directory (e.g. logs/app.log, logs/build.log).
/// </summary>
public static class AppLogger
{
    private static readonly object LogLock = new();
    private const long MaxLogSizeBytes = 10 * 1024 * 1024; // 10 MB rotation

    public static string LogsDirectory => SettingsManager.ResolveLogsDir();
    public static string MainLogPath => Path.Combine(LogsDirectory, "app.log");
    public static string BuildLogPath => Path.Combine(LogsDirectory, "build.log");
    public static string InstallLogPath => Path.Combine(LogsDirectory, "install.log");

    public static void Log(string message, string level = "INFO")
    {
        WriteEntry(MainLogPath, message, level);
    }

    public static void LogBuild(string message, string level = "INFO")
    {
        WriteEntry(BuildLogPath, message, level);
        WriteEntry(MainLogPath, $"[BUILD] {message}", level);
    }

    public static void LogInstall(string message, string level = "INFO")
    {
        WriteEntry(InstallLogPath, message, level);
        WriteEntry(MainLogPath, $"[INSTALL] {message}", level);
    }

    public static void LogError(string context, Exception ex)
    {
        var details = $"{context} ERROR: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        if (ex.InnerException != null)
        {
            details += $"{Environment.NewLine}Inner: {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}";
        }
        WriteEntry(MainLogPath, details, "ERROR");
    }

    public static void LogBuildError(string context, Exception ex)
    {
        var details = $"{context} ERROR: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        if (ex.InnerException != null)
        {
            details += $"{Environment.NewLine}Inner: {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}";
        }
        WriteEntry(BuildLogPath, details, "ERROR");
        WriteEntry(MainLogPath, $"[BUILD] {details}", "ERROR");
    }

    public static void LogInstallError(string context, Exception ex)
    {
        var details = $"{context} ERROR: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        if (ex.InnerException != null)
        {
            details += $"{Environment.NewLine}Inner: {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}";
        }
        WriteEntry(InstallLogPath, details, "ERROR");
        WriteEntry(MainLogPath, $"[INSTALL] {details}", "ERROR");
    }

    private static void WriteEntry(string filePath, string message, string level)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var formatted = $"[{timestamp}] [{level}] {message}";

        Debug.WriteLine(formatted);

        try
        {
            lock (LogLock)
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                RotateIfOversized(filePath);

                File.AppendAllText(filePath, formatted + Environment.NewLine);
            }
        }
        catch
        {
            // Non-fatal if filesystem is restricted
        }
    }

    private static void RotateIfOversized(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var fi = new FileInfo(filePath);
                if (fi.Length > MaxLogSizeBytes)
                {
                    var backup = filePath + ".old";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(filePath, backup);
                }
            }
        }
        catch { }
    }

    public static void OpenLogsFolder()
    {
        try
        {
            var dir = LogsDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    public static void OpenLogFile(string? logPath = null)
    {
        try
        {
            var target = logPath ?? MainLogPath;
            if (!File.Exists(target))
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(target, $"# NVDriver Installer Log initialized at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
