using System.Text.Json.Serialization;

namespace NVDriverInstaller.Core.Models;

/// <summary>
/// General application settings managed exclusively via the in-app UI.
/// Serialized locally to settings.json in the application root directory.
/// </summary>
public class AppSettings
{
    [JsonPropertyName("ddu_path")]
    public string DduPath { get; set; } = "./tools/ddu/Display Driver Uninstaller.exe";

    [JsonPropertyName("seven_zip_path")]
    public string SevenZipPath { get; set; } = "./tools/7z/7z.exe";

    [JsonPropertyName("auto_reboot_to_safe_mode")]
    public bool AutoRebootToSafeMode { get; set; } = true;

    [JsonPropertyName("auto_reboot_after_install")]
    public bool AutoRebootAfterInstall { get; set; } = true;

    [JsonPropertyName("auto_reboot_after_safe_mode")]
    public bool AutoRebootAfterSafeMode
    {
        get => AutoRebootToSafeMode;
        set => AutoRebootToSafeMode = value;
    }

    [JsonPropertyName("quarantine_network")]
    public bool QuarantineNetwork { get; set; } = true;

    [JsonPropertyName("download_cache_path")]
    public string DownloadCachePath { get; set; } = "./packages";

    [JsonPropertyName("default_desktop_packaging_profile")]
    public string DefaultDesktopPackagingProfile { get; set; } = "standard";

    [JsonPropertyName("default_notebook_packaging_profile")]
    public string DefaultNotebookPackagingProfile { get; set; } = "notebook";

    [JsonPropertyName("default_packaging_profile")]
    public string DefaultPackagingProfile
    {
        get => DefaultDesktopPackagingProfile;
        set => DefaultDesktopPackagingProfile = value;
    }

    [JsonPropertyName("preferred_install_pathway")]
    public string PreferredInstallPathway { get; set; } = "standard";

    [JsonPropertyName("default_ddu_profile")]
    public string DefaultDduProfile { get; set; } = "standard";

    [JsonPropertyName("dry_run_mode")]
    public bool DryRunMode { get; set; } = false;

    [JsonPropertyName("unattended_install")]
    public bool UnattendedInstall { get; set; } = false;

    [JsonPropertyName("install_nvcpl_appx")]
    public bool InstallNvcplAppx { get; set; } = true;

    [JsonPropertyName("reboot_delay_seconds")]
    public int RebootDelaySeconds { get; set; } = 5;

    [JsonPropertyName("safe_mode_runonce_name")]
    public string SafeModeRunOnceName { get; set; } = "*NVDriverInstallerResume";

    [JsonPropertyName("normal_mode_runonce_name")]
    public string NormalModeRunOnceName { get; set; } = "NVDriverInstallerResume";
}
