namespace NVDriverInstaller.Core.Models;

public class DduProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> CliFlags { get; set; } = new()
    {
        "-Silent",
        "-NoRestorePoint",
        "-PreventWinUpdate",
        "-CleanNvidia"
    };
    public Dictionary<string, object> Settings { get; set; } = new();

    public string DisplayName => string.IsNullOrEmpty(FileName) ? Name : $"{Name} ({FileName})";

    public override string ToString() => DisplayName;
}
