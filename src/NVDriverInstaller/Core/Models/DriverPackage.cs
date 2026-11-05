using System.IO;

namespace NVDriverInstaller.Core.Models;

public class DriverPackage
{
    public string Version { get; set; } = string.Empty;
    public string DriverType { get; set; } = "desktop";
    public bool Dch { get; set; } = true;
    public bool IsStudio { get; set; } = false;
    public string? ProfileName { get; set; }
    public bool IsBuilt { get; set; } = false;
    public string? DownloadUrl { get; set; }
    public string? LocalFilePath { get; set; }
    public long FileSizeBytes { get; set; }
    public string ReleaseDate { get; set; } = string.Empty;

    public bool IsLocal => !string.IsNullOrEmpty(LocalFilePath) && (File.Exists(LocalFilePath) || Directory.Exists(LocalFilePath));

    public string BranchName => IsStudio ? "Studio" : "Game Ready";

    public string FormattedType =>
        string.IsNullOrEmpty(DriverType)
            ? "Desktop"
            : char.ToUpper(DriverType[0]) + DriverType[1..].ToLowerInvariant();

    public string DisplaySize =>
        FileSizeBytes > 0
            ? $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
            : "Prepared";

    public string DisplayName
    {
        get
        {
            if (IsBuilt)
            {
                var profileTag = !string.IsNullOrEmpty(ProfileName) ? $" [{ProfileName}]" : string.Empty;
                return $"v{Version} · {FormattedType}{profileTag} · {DisplaySize}";
            }

            return $"v{Version} · {FormattedType} · {BranchName} Driver (DCH)";
        }
    }

    public override string ToString() => DisplayName;
}
