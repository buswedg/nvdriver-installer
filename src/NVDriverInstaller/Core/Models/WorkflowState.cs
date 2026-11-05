using System.Text.Json.Serialization;

namespace NVDriverInstaller.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowPhase
{
    Idle,
    Prepared,
    SafeModeDdu,
    Completed,
    Failed
}

public class WorkflowState
{
    [JsonPropertyName("phase")]
    public WorkflowPhase Phase { get; set; } = WorkflowPhase.Idle;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("package_path")]
    public string PackagePath { get; set; } = string.Empty;

    [JsonPropertyName("network_disabled")]
    public bool NetworkDisabled { get; set; }

    [JsonPropertyName("ddu_profile")]
    public string DduProfile { get; set; } = "standard";

    [JsonPropertyName("packaging_profile")]
    public string PackagingProfile { get; set; } = "standard";

    [JsonPropertyName("auto_reboot_to_safe_mode")]
    public bool AutoRebootToSafeMode { get; set; } = true;

    [JsonPropertyName("auto_reboot_after_install")]
    public bool AutoRebootAfterInstall { get; set; } = true;

    [JsonPropertyName("auto_reboot")]
    public bool AutoReboot
    {
        get => AutoRebootToSafeMode;
        set => AutoRebootToSafeMode = value;
    }

    [JsonPropertyName("unattended_install")]
    public bool UnattendedInstall { get; set; } = false;

    [JsonPropertyName("install_nvcpl_appx")]
    public bool InstallNvcplAppx { get; set; } = true;

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
