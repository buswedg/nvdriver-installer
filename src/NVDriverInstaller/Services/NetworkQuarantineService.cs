using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using NVDriverInstaller.Core.Models;

namespace NVDriverInstaller.Services;

/// <summary>
/// Manages network adapter discovery, physical interface isolation, and restoration.
/// Prevents Windows Update from downloading generic display drivers during DDU cleanups.
/// </summary>
public static class NetworkQuarantineService
{
    private static readonly string StateFilePath = Path.Combine(AppContext.BaseDirectory, "network_state.json");

    public static List<NetworkAdapterInfo> GetPhysicalAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        try
        {
            var scope = new ManagementScope(@"\\.\root\StandardCimv2");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(
                "SELECT Name, InterfaceDescription, PermanentAddress, HardwareInterface, InterfaceAdminStatus, InterfaceOperationalStatus FROM MSFT_NetAdapter WHERE HardwareInterface = True"));

            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                var desc = item["InterfaceDescription"]?.ToString() ?? string.Empty;
                var mac = item["PermanentAddress"]?.ToString() ?? string.Empty;
                var hw = Convert.ToBoolean(item["HardwareInterface"] ?? true);
                var adminVal = Convert.ToInt32(item["InterfaceAdminStatus"] ?? 1);
                var adminStatus = adminVal == 1 ? "Up" : "Down";
                var opStatusVal = Convert.ToInt32(item["InterfaceOperationalStatus"] ?? 1);
                var opStatus = opStatusVal == 1 ? "Up" : "Disconnected";

                adapters.Add(new NetworkAdapterInfo
                {
                    Name = name,
                    InterfaceDescription = desc,
                    MacAddress = mac,
                    AdminStatus = adminStatus,
                    Status = opStatus,
                    IsPhysical = hw
                });
            }
        }
        catch
        {
            adapters.AddRange(GetAdaptersViaPowerShell());
        }

        if (adapters.Count == 0)
        {
            adapters.AddRange(GetAdaptersViaNetsh());
        }

        return adapters;
    }

    private static List<NetworkAdapterInfo> GetAdaptersViaPowerShell()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Select-Object Name, InterfaceDescription, Status, InterfaceAdminStatus | ConvertTo-Json -Compress\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    using var doc = JsonDocument.Parse(output);
                    var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                        ? doc.RootElement.EnumerateArray().ToList()
                        : new List<JsonElement> { doc.RootElement };

                    foreach (var elem in elements)
                    {
                        var name = elem.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                        var desc = elem.TryGetProperty("InterfaceDescription", out var d) ? d.GetString() ?? "" : "";
                        var status = elem.TryGetProperty("Status", out var s) ? s.GetString() ?? "" : "";
                        var adminStatusVal = elem.TryGetProperty("InterfaceAdminStatus", out var a) ? a.GetInt32() : 1;
                        var adminStatus = adminStatusVal == 1 ? "Up" : "Down";

                        list.Add(new NetworkAdapterInfo
                        {
                            Name = name,
                            InterfaceDescription = desc,
                            AdminStatus = adminStatus,
                            Status = status,
                            IsPhysical = true
                        });
                    }
                }
            }
        }
        catch
        {
            // Ignore fallback errors
        }
        return list;
    }

    private static List<NetworkAdapterInfo> GetAdaptersViaNetsh()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            var psi = new ProcessStartInfo("netsh.exe", "interface show interface")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);

                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 && (parts[0].Equals("Enabled", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("Disabled", StringComparison.OrdinalIgnoreCase)))
                    {
                        var adminState = parts[0];
                        var name = string.Join(" ", parts.Skip(3)).Trim();
                        list.Add(new NetworkAdapterInfo
                        {
                            Name = name,
                            InterfaceDescription = name,
                            AdminStatus = adminState.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down",
                            Status = parts[1],
                            IsPhysical = true
                        });
                    }
                }
            }
        }
        catch
        {
            // Ignore fallback errors
        }
        return list;
    }

    public static bool DisableAdapter(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var isElevated = SystemInspector.IsElevated();

        if (isElevated)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\StandardCimv2");
                scope.Connect();
                var safeName = name.Replace("\"", "\\\"");
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM MSFT_NetAdapter WHERE Name=\"{safeName}\""));
                foreach (ManagementObject mo in searcher.Get())
                {
                    var outParams = mo.InvokeMethod("Disable", null, null);
                    var returnCode = Convert.ToUInt32(outParams?["ReturnValue"] ?? 0);
                    if (returnCode == 0)
                        return true;
                }
            }
            catch
            {
                // Fallback to PowerShell
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Disable-NetAdapter -Name '{name.Replace("'", "''")}' -Confirm:$false\"",
                UseShellExecute = !isElevated,
                Verb = isElevated ? "" : "runas",
                CreateNoWindow = isElevated,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(10000);
                if (p.ExitCode == 0)
                    return true;
            }
        }
        catch
        {
            // Fallback to netsh
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"interface set interface \"{name}\" disable",
                UseShellExecute = !isElevated,
                Verb = isElevated ? "" : "runas",
                CreateNoWindow = isElevated,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(8000);
                return p.ExitCode == 0;
            }
        }
        catch
        {
            // Ignore fallback errors
        }

        return false;
    }

    public static bool EnableAdapter(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var isElevated = SystemInspector.IsElevated();

        if (isElevated)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\StandardCimv2");
                scope.Connect();
                var safeName = name.Replace("\"", "\\\"");
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM MSFT_NetAdapter WHERE Name=\"{safeName}\""));
                foreach (ManagementObject mo in searcher.Get())
                {
                    var outParams = mo.InvokeMethod("Enable", null, null);
                    var returnCode = Convert.ToUInt32(outParams?["ReturnValue"] ?? 0);
                    if (returnCode == 0)
                        return true;
                }
            }
            catch
            {
                // Fallback to PowerShell
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Enable-NetAdapter -Name '{name.Replace("'", "''")}' -Confirm:$false\"",
                UseShellExecute = !isElevated,
                Verb = isElevated ? "" : "runas",
                CreateNoWindow = isElevated,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(10000);
                if (p.ExitCode == 0)
                    return true;
            }
        }
        catch
        {
            // Fallback to netsh
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"interface set interface \"{name}\" enable",
                UseShellExecute = !isElevated,
                Verb = isElevated ? "" : "runas",
                CreateNoWindow = isElevated,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(8000);
                return p.ExitCode == 0;
            }
        }
        catch
        {
            // Ignore fallback errors
        }

        return false;
    }

    public static List<string> DisableAllPhysical()
    {
        var adapters = GetPhysicalAdapters();
        var disabled = new List<string>();

        if (adapters.Count == 0)
            return disabled;

        SaveSnapshot(adapters);

        var active = adapters.Where(a => a.IsUp).ToList();
        if (active.Count == 0)
            return disabled;

        if (SystemInspector.IsElevated())
        {
            foreach (var a in active)
            {
                if (DisableAdapter(a.Name))
                    disabled.Add(a.Name);
            }
        }
        else
        {
            var commands = active.Select(a => $"Disable-NetAdapter -Name '{a.Name.Replace("'", "''")}' -Confirm:$false -ErrorAction SilentlyContinue");
            ExecuteElevatedScript(commands);

            var after = GetPhysicalAdapters();
            foreach (var a in active)
            {
                var match = after.FirstOrDefault(x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
                if (match != null && !match.IsUp)
                    disabled.Add(a.Name);
            }
        }

        return disabled;
    }

    public static List<string> EnableAllPhysical()
    {
        var adapters = GetPhysicalAdapters();
        var enabled = new List<string>();

        var inactive = adapters.Where(a => !a.IsUp).ToList();
        if (inactive.Count == 0)
            return enabled;

        if (SystemInspector.IsElevated())
        {
            foreach (var a in inactive)
            {
                if (EnableAdapter(a.Name))
                    enabled.Add(a.Name);
            }
        }
        else
        {
            var commands = inactive.Select(a => $"Enable-NetAdapter -Name '{a.Name.Replace("'", "''")}' -Confirm:$false -ErrorAction SilentlyContinue");
            ExecuteElevatedScript(commands);

            var after = GetPhysicalAdapters();
            foreach (var a in inactive)
            {
                var match = after.FirstOrDefault(x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
                if (match != null && match.IsUp)
                    enabled.Add(a.Name);
            }
        }

        return enabled;
    }

    public static List<string> RestoreAllAdapters()
    {
        var restored = new List<string>();
        var snapshot = LoadSnapshot();

        var targets = snapshot != null && snapshot.Count > 0
            ? snapshot.Where(a => a.IsUp).Select(a => a.Name).ToList()
            : GetPhysicalAdapters().Select(a => a.Name).ToList();

        if (targets.Count == 0)
        {
            ClearSnapshot();
            return restored;
        }

        if (SystemInspector.IsElevated())
        {
            foreach (var name in targets)
            {
                if (EnableAdapter(name))
                    restored.Add(name);
            }
        }
        else
        {
            var commands = targets.Select(n => $"Enable-NetAdapter -Name '{n.Replace("'", "''")}' -Confirm:$false -ErrorAction SilentlyContinue");
            ExecuteElevatedScript(commands);

            var after = GetPhysicalAdapters();
            foreach (var name in targets)
            {
                var match = after.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null && match.IsUp)
                    restored.Add(name);
            }
        }

        ClearSnapshot();
        return restored;
    }

    private static void ExecuteElevatedScript(IEnumerable<string> commands, int timeoutMs = 15000)
    {
        try
        {
            var cmdList = string.Join("; ", commands);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmdList}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(timeoutMs);
        }
        catch
        {
            // Non-fatal if UAC cancelled or execution timed out
        }
    }

    private static void SaveSnapshot(List<NetworkAdapterInfo> adapters)
    {
        try
        {
            var json = JsonSerializer.Serialize(adapters, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch
        {
            // Non-fatal snapshot error
        }
    }

    private static List<NetworkAdapterInfo>? LoadSnapshot()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                var json = File.ReadAllText(StateFilePath);
                return JsonSerializer.Deserialize<List<NetworkAdapterInfo>>(json);
            }
        }
        catch
        {
            // Non-fatal snapshot read error
        }
        return null;
    }

    private static void ClearSnapshot()
    {
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
        }
        catch
        {
            // Non-fatal snapshot delete error
        }
    }
}
