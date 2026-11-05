using System.IO;
using System.Text.Json;
using NVDriverInstaller.Core.Models;

namespace NVDriverInstaller.Core.Config;

/// <summary>
/// Single source of truth for general application settings.
/// Saves and loads settings.json automatically from the application directory.
/// </summary>
public static class SettingsManager
{
    private static readonly string SettingsFilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static AppSettings? _current;
    private static readonly object LockObj = new();

    public static string? ParsingError { get; private set; }

    public static AppSettings Current
    {
        get
        {
            if (_current == null)
            {
                lock (LockObj)
                {
                    _current ??= LoadOrCreate();
                }
            }
            return _current;
        }
    }

    private static AppSettings LoadOrCreate()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    ParsingError = null;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            ParsingError = $"settings.json: {ex.Message}";
        }

        var defaults = new AppSettings();
        Save(defaults);
        return defaults;
    }

    public static void Save(AppSettings? settings = null)
    {
        lock (LockObj)
        {
            var target = settings ?? _current ?? new AppSettings();
            _current = target;

            try
            {
                var json = JsonSerializer.Serialize(target, JsonOptions);
                var tempFile = SettingsFilePath + ".tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, SettingsFilePath, overwrite: true);
            }
            catch
            {
                // Non-fatal if read-only filesystem
            }
        }
    }

    /// <summary>
    /// Resolves the absolute path to the Display Driver Uninstaller executable.
    /// Evaluates AppSettings.DduPath and portable tools/ddu candidates.
    /// </summary>
    public static string ResolveDduPath()
    {
        var configured = Current.DduPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var testPath = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));

            if (Directory.Exists(testPath))
            {
                var candidate = Path.Combine(testPath, "Display Driver Uninstaller.exe");
                if (File.Exists(candidate)) return candidate;
                var subfolderCandidate = Path.Combine(testPath, "DDU", "Display Driver Uninstaller.exe");
                if (File.Exists(subfolderCandidate)) return subfolderCandidate;
            }
            else if (File.Exists(testPath))
            {
                return testPath;
            }
        }

        // Common alternative portable candidates
        var subfolderExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "ddu", "DDU", "Display Driver Uninstaller.exe"));
        if (File.Exists(subfolderExe))
            return subfolderExe;

        var directExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "ddu", "Display Driver Uninstaller.exe"));
        if (File.Exists(directExe))
            return directExe;

        var devCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "ddu", "Display Driver Uninstaller.exe"));
        if (File.Exists(devCandidate))
            return devCandidate;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "ddu", "Display Driver Uninstaller.exe"));
    }

    /// <summary>
    /// Resolves the absolute path for downloads and driver staging.
    /// </summary>
    public static string ResolveDownloadsDir()
    {
        var dir = string.IsNullOrWhiteSpace(Current.DownloadCachePath) ? "./packages" : Current.DownloadCachePath;
        if (Path.IsPathRooted(dir))
            return dir;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dir));
    }

    /// <summary>
    /// Resolves the absolute path for raw official package downloads (./packages/raw).
    /// </summary>
    public static string ResolveRawPackagesDir()
    {
        var dir = Path.Combine(ResolveDownloadsDir(), "raw");
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }
        return dir;
    }

    /// <summary>
    /// Resolves the absolute path for debloated clean packages (./packages/clean).
    /// </summary>
    public static string ResolveCleanPackagesDir()
    {
        var dir = Path.Combine(ResolveDownloadsDir(), "clean");
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }
        return dir;
    }

    /// <summary>
    /// Resolves the absolute path for application and build logs.
    /// </summary>
    public static string ResolveLogsDir()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "logs"));
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }
        return dir;
    }

    /// <summary>
    /// Resolves the absolute path to the 7-Zip executable.
    /// Evaluates AppSettings.SevenZipPath and portable tools/7z candidates.
    /// </summary>
    public static string Resolve7ZipPath()
    {
        var configured = Current.SevenZipPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var testPath = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));

            if (Directory.Exists(testPath))
            {
                var candidate = Path.Combine(testPath, "7z.exe");
                if (File.Exists(candidate)) return candidate;
            }
            else if (File.Exists(testPath))
            {
                var name = Path.GetFileName(testPath);
                if (name.Equals("7zFM.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("7zG.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(testPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var sibling = Path.Combine(dir, "7z.exe");
                        if (File.Exists(sibling)) return sibling;
                    }
                }
                return testPath;
            }
            else
            {
                var dir = Path.GetDirectoryName(testPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var sibling = Path.Combine(dir, "7z.exe");
                    if (File.Exists(sibling)) return sibling;
                }
            }
        }

        var defaultRelative = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z", "7z.exe"));
        if (File.Exists(defaultRelative)) return defaultRelative;

        var devCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "7z", "7z.exe"));
        if (File.Exists(devCandidate)) return devCandidate;

        var subCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z.exe"));
        if (File.Exists(subCandidate)) return subCandidate;

        var appCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "7z.exe"));
        if (File.Exists(appCandidate)) return appCandidate;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        return defaultRelative;
    }

    /// <summary>
    /// Resolves the absolute path to the 7-Zip SFX module.
    /// Evaluates alongside resolved 7z.exe and portable tools/7z candidates.
    /// </summary>
    public static string? Resolve7zSfxPath()
    {
        // First check alongside resolved 7z.exe
        var sevenZip = Resolve7ZipPath();
        if (File.Exists(sevenZip))
        {
            var dir = Path.GetDirectoryName(sevenZip);
            if (!string.IsNullOrEmpty(dir))
            {
                var sfxBeside = Path.Combine(dir, "7z.sfx");
                if (File.Exists(sfxBeside)) return sfxBeside;
                var sfxConBeside = Path.Combine(dir, "7zCon.sfx");
                if (File.Exists(sfxConBeside)) return sfxConBeside;
            }
        }

        var defaultSfx = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z", "7z.sfx"));
        if (File.Exists(defaultSfx)) return defaultSfx;

        var devSfx = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "7z", "7z.sfx"));
        if (File.Exists(devSfx)) return devSfx;

        return null;
    }
}
