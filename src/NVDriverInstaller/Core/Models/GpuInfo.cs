namespace NVDriverInstaller.Core.Models;

public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public string RawDriverVersion { get; set; } = string.Empty;
    public string FormattedNvidiaVersion { get; set; } = string.Empty;
    public string PnpDeviceId { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public bool IsNvidia => Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        IsNvidia
            ? $"{Name} (v{FormattedNvidiaVersion})"
            : $"{Name} [{Status}]";
}

public class SystemHostInfo
{
    public string Hostname { get; set; } = Environment.MachineName;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string FormFactor { get; set; } = "Desktop"; // "Desktop" or "Notebook"
    public bool IsElevated { get; set; }
    public bool IsSafeMode { get; set; }
    public List<GpuInfo> Gpus { get; set; } = new();

    public GpuInfo? PrimaryNvidiaGpu =>
        Gpus.FirstOrDefault(g => g.IsNvidia);
}
