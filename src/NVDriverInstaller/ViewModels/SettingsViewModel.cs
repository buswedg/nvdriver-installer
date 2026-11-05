using System.Collections.ObjectModel;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;
using NVDriverInstaller.Services;

namespace NVDriverInstaller.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private string _dduPath = string.Empty;
    private string _sevenZipPath = string.Empty;
    private string _downloadCachePath = string.Empty;
    private int _rebootDelaySeconds;
    private string _selectedDefaultDesktopProfile = "standard";
    private string _selectedDefaultNotebookProfile = "notebook";
    private string _selectedDefaultDduProfile = "standard";
    private string _status = string.Empty;

    public ObservableCollection<PackagingProfile> PackagingProfiles { get; } = new();
    public ObservableCollection<DduProfile> DduProfiles { get; } = new();

    public string DduPath
    {
        get => _dduPath;
        set => SetProperty(ref _dduPath, value);
    }

    public string SevenZipPath
    {
        get => _sevenZipPath;
        set => SetProperty(ref _sevenZipPath, value);
    }

    public string DownloadCachePath
    {
        get => _downloadCachePath;
        set => SetProperty(ref _downloadCachePath, value);
    }

    public int RebootDelaySeconds
    {
        get => _rebootDelaySeconds;
        set => SetProperty(ref _rebootDelaySeconds, value);
    }

    public string SelectedDefaultDesktopProfile
    {
        get => _selectedDefaultDesktopProfile;
        set => SetProperty(ref _selectedDefaultDesktopProfile, value);
    }

    public string SelectedDefaultNotebookProfile
    {
        get => _selectedDefaultNotebookProfile;
        set => SetProperty(ref _selectedDefaultNotebookProfile, value);
    }

    public string SelectedDefaultDduProfile
    {
        get => _selectedDefaultDduProfile;
        set => SetProperty(ref _selectedDefaultDduProfile, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public SettingsViewModel()
    {
        LoadSettings();
    }

    public void LoadSettings()
    {
        var current = SettingsManager.Current;
        DduPath = current.DduPath;
        SevenZipPath = current.SevenZipPath;
        DownloadCachePath = current.DownloadCachePath;
        RebootDelaySeconds = current.RebootDelaySeconds;
        SelectedDefaultDesktopProfile = current.DefaultDesktopPackagingProfile;
        SelectedDefaultNotebookProfile = current.DefaultNotebookPackagingProfile;
        SelectedDefaultDduProfile = current.DefaultDduProfile;

        PackagingProfiles.Clear();
        foreach (var p in ProfileManager.PackagingProfiles)
            PackagingProfiles.Add(p);

        DduProfiles.Clear();
        foreach (var d in ProfileManager.DduProfiles)
            DduProfiles.Add(d);
    }

    public void SaveSettings()
    {
        var settings = SettingsManager.Current;
        settings.DduPath = DduPath?.Trim().Trim('\"') ?? string.Empty;
        settings.SevenZipPath = SevenZipPath?.Trim().Trim('\"') ?? string.Empty;
        settings.DownloadCachePath = DownloadCachePath?.Trim().Trim('\"') ?? string.Empty;
        settings.RebootDelaySeconds = RebootDelaySeconds;
        settings.DefaultDesktopPackagingProfile = SelectedDefaultDesktopProfile;
        settings.DefaultNotebookPackagingProfile = SelectedDefaultNotebookProfile;
        settings.DefaultDduProfile = SelectedDefaultDduProfile;

        // Keep local VM properties clean of quotes
        DduPath = settings.DduPath;
        SevenZipPath = settings.SevenZipPath;
        DownloadCachePath = settings.DownloadCachePath;

        SettingsManager.Save(settings);
        Status = "Preferences saved to settings.json";
    }

    public void OpenLogsFolder()
    {
        AppLogger.OpenLogsFolder();
    }

    public void OpenLogFile()
    {
        AppLogger.OpenLogFile(AppLogger.MainLogPath);
    }

    public void OpenBuildLog()
    {
        AppLogger.OpenLogFile(AppLogger.BuildLogPath);
    }

    public void OpenInstallLog()
    {
        AppLogger.OpenLogFile(AppLogger.InstallLogPath);
    }
}
