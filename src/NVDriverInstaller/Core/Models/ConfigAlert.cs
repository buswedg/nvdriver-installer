namespace NVDriverInstaller.Core.Models;

public class ConfigAlert
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning"; // "Warning" or "Error"
    public string? ActionText { get; set; }
    public string? ActionKey { get; set; }
}
