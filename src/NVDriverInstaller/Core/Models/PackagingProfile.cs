namespace NVDriverInstaller.Core.Models;

public class PackagingProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DriverType { get; set; } = "desktop"; // "desktop" or "notebook"
    public string DriverBranch { get; set; } = "latest_dch";
    public List<string> Components { get; set; } = new();

    // Tweaks
    public bool DisableTelemetry { get; set; } = true;
    public bool CleanInstall { get; set; } = true;
    public bool Unattended { get; set; } = false;

    public bool HasPhysX => Components.Any(c => c.Contains("physx", StringComparison.OrdinalIgnoreCase));
    public bool HasAudio => Components.Any(c => c.Contains("audio", StringComparison.OrdinalIgnoreCase));
    public bool HasPlatform => Components.Any(c => c.Contains("platform", StringComparison.OrdinalIgnoreCase));
    public bool HasControlPanel => Components.Any(c => c.Contains("control panel", StringComparison.OrdinalIgnoreCase) || c.Contains("nvcpl", StringComparison.OrdinalIgnoreCase));

    public string DisplayName => string.IsNullOrEmpty(FileName) ? Name : $"{Name} ({FileName})";

    public override string ToString() => DisplayName;
}
