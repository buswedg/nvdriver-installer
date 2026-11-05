using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using NVDriverInstaller.Core.Models;

namespace NVDriverInstaller.Services;

public static class SystemInspector
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CLEANBOOT = 67;

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSafeMode()
    {
        try
        {
            // SM_CLEANBOOT: 0 = Normal, 1 = Fail-safe, 2 = Fail-safe with network
            return GetSystemMetrics(SM_CLEANBOOT) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static string WmiToNvidiaVersion(string? wmiVer)
    {
        if (string.IsNullOrWhiteSpace(wmiVer))
            return "Unknown";

        var cleaned = wmiVer.Replace(".", "").Trim();
        if (cleaned.Length >= 5)
        {
            var sub = cleaned[^5..];
            if (int.TryParse(sub[..3], out var major))
            {
                var minor = sub[3..];
                return $"{major}.{minor}";
            }
        }
        return wmiVer;
    }

    public static SystemHostInfo InspectHost()
    {
        var info = new SystemHostInfo
        {
            Hostname = Environment.MachineName,
            IsElevated = IsElevated(),
            IsSafeMode = IsSafeMode()
        };

        // Query Host Hardware
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem");
            foreach (var item in searcher.Get())
            {
                info.Manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                info.Model = item["Model"]?.ToString()?.Trim() ?? string.Empty;

                if (item["PCSystemType"] != null && int.TryParse(item["PCSystemType"].ToString(), out var typeInt))
                {
                    // 2 = Mobile (Laptop/Notebook)
                    info.FormFactor = typeInt == 2 ? "Notebook" : "Desktop";
                }
            }
        }
        catch
        {
            // Fallback default form factor
            info.FormFactor = "Desktop";
        }

        // Query Video Controllers
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, PNPDeviceID, Status FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString()?.Trim() ?? "Unknown Display Adapter";
                var rawVer = item["DriverVersion"]?.ToString()?.Trim() ?? string.Empty;
                var pnp = item["PNPDeviceID"]?.ToString()?.Trim() ?? string.Empty;
                var status = item["Status"]?.ToString()?.Trim() ?? "OK";

                info.Gpus.Add(new GpuInfo
                {
                    Name = name,
                    RawDriverVersion = rawVer,
                    FormattedNvidiaVersion = WmiToNvidiaVersion(rawVer),
                    PnpDeviceId = pnp,
                    Status = status
                });
            }
        }
        catch
        {
            // Management query failure fallback
        }

        return info;
    }
}
