using System.Diagnostics;
using Microsoft.Win32;

namespace NVDriverInstaller.Services;

public static class BcdBootManager
{
    private const string RunOnceRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    public static bool IsSafeBootConfigured()
    {
        try
        {
            var psi = new ProcessStartInfo("bcdedit.exe", "/enum {current}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("safeboot", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    public static bool EnableSafeMode()
    {
        try
        {
            var isElevated = SystemInspector.IsElevated();
            var psi = new ProcessStartInfo("bcdedit.exe", "/set {current} safeboot Minimal")
            {
                RedirectStandardOutput = isElevated,
                RedirectStandardError = isElevated,
                UseShellExecute = !isElevated,
                Verb = isElevated ? "" : "runas",
                CreateNoWindow = isElevated,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(10000);

            // Verification check
            return IsSafeBootConfigured();
        }
        catch
        {
            return false;
        }
    }

    public static bool DisableSafeMode()
    {
        try
        {
            var isElevated = SystemInspector.IsElevated();
            foreach (var target in new[] { "{current}", "{default}" })
            {
                var psi = new ProcessStartInfo("bcdedit.exe", $"/deletevalue {target} safeboot")
                {
                    RedirectStandardOutput = isElevated,
                    RedirectStandardError = isElevated,
                    UseShellExecute = !isElevated,
                    Verb = isElevated ? "" : "runas",
                    CreateNoWindow = isElevated,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }

            var fallback = new ProcessStartInfo("bcdedit.exe", "/deletevalue safeboot")
            {
                RedirectStandardOutput = isElevated,
                RedirectStandardError = isElevated,
                UseShellExecute = !isElevated,
                Verb = isElevated ? "" : "runas",
                CreateNoWindow = isElevated,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var fb = Process.Start(fallback);
            fb?.WaitForExit(5000);

            // Confirm safeboot is no longer configured
            return !IsSafeBootConfigured();
        }
        catch
        {
            return false;
        }
    }

    public static bool RegisterRunOnce(string name, string command, bool safeMode = false)
    {
        // Safe Mode Winlogon execution requires leading asterisk '*'
        var cleanName = name.TrimStart('*');
        var regName = safeMode ? $"*{cleanName}" : cleanName;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunOnceRegistryKey, writable: true);
            if (key == null) return false;

            key.SetValue(regName, command, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool UnregisterRunOnce(string name)
    {
        var cleanName = name.TrimStart('*');
        var starName = $"*{cleanName}";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunOnceRegistryKey, writable: true);
            if (key == null) return true;

            try { key.DeleteValue(cleanName, throwOnMissingValue: false); } catch { }
            try { key.DeleteValue(starName, throwOnMissingValue: false); } catch { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ScheduleReboot(int delaySeconds = 5, string reason = "NVDriver Installer: Safe Mode Transition")
    {
        try
        {
            var psi = new ProcessStartInfo("shutdown.exe", $"/r /t {delaySeconds} /c \"{reason}\" /f")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool AbortReboot()
    {
        try
        {
            var psi = new ProcessStartInfo("shutdown.exe", "/a")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
