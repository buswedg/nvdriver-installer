using System.Collections.ObjectModel;
using System.IO;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;
using NVDriverInstaller.Services;

namespace NVDriverInstaller.ViewModels;

public class DashboardViewModel : BaseViewModel
{
    private SystemHostInfo _hostInfo = new();
    private DriverPackage? _latestPackage;
    private bool _isLoading;
    private string _statusMessage = "Ready";

    public ObservableCollection<ConfigAlert> ActiveAlerts { get; } = new();

    public SystemHostInfo HostInfo
    {
        get => _hostInfo;
        set => SetProperty(ref _hostInfo, value);
    }

    public DriverPackage? LatestPackage
    {
        get => _latestPackage;
        set => SetProperty(ref _latestPackage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasAlerts => ActiveAlerts.Count > 0;

    public string GpuSummary =>
        HostInfo.PrimaryNvidiaGpu?.ToString() ?? "No NVIDIA GPU detected";

    public DashboardViewModel()
    {
        RefreshHost();
    }

    public void RefreshHost()
    {
        HostInfo = SystemInspector.InspectHost();
        RefreshAlerts();
        OnPropertyChanged(nameof(GpuSummary));
    }

    public void RefreshAlerts()
    {
        ActiveAlerts.Clear();

        // 1. Check DDU setup
        if (!DduManager.IsInstalled())
        {
            var dduPath = DduManager.GetDduExecutablePath();
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "Display Driver Uninstaller Missing",
                Message = $"DDU executable was not found at '{dduPath}'. Safe Mode clean upgrade requires DDU to purge previous drivers.",
                Severity = "Warning",
                ActionText = "Download DDU",
                ActionKey = "download_ddu"
            });
        }
        else if (DduManager.IsUpdateAvailable())
        {
            var localVer = DduManager.GetLocalVersion() ?? "unknown";
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "DDU Update Available",
                Message = $"A newer release of Display Driver Uninstaller (v{DduManager.CachedRemoteVersion}) is available (installed: v{localVer}).",
                Severity = "Info",
                ActionText = "Update DDU",
                ActionKey = "download_ddu"
            });
        }

        // 2. Check 7-Zip tooling setup
        if (!SevenZipManager.IsInstalled())
        {
            var sevenZipPath = SevenZipManager.GetExecutablePath();
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "7-Zip Tooling Missing",
                Message = $"7z executable was not found at '{sevenZipPath}'. Compiling standalone (.exe) packages and extracting upstream driver archives requires 7-Zip.",
                Severity = "Warning",
                ActionText = "Download 7-Zip",
                ActionKey = "download_7z"
            });
        }
        else if (!SevenZipManager.HasSfxStub())
        {
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "7-Zip SFX Module Missing",
                Message = "7z.sfx stub was not found in 7-Zip tooling directory. Standalone .exe package compilation requires 7z.sfx.",
                Severity = "Warning",
                ActionText = "Update 7-Zip",
                ActionKey = "download_7z"
            });
        }
        else if (SevenZipManager.IsUpdateAvailable())
        {
            var localVer = SevenZipManager.GetLocalVersion() ?? "unknown";
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "7-Zip Update Available",
                Message = $"A newer release of 7-Zip (v{SevenZipManager.CachedRemoteVersion}) is available (installed: v{localVer}).",
                Severity = "Info",
                ActionText = "Update 7-Zip",
                ActionKey = "download_7z"
            });
        }

        // 2. Check Profile parsing issues
        foreach (var err in ProfileManager.ParsingErrors)
        {
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "Configuration TOML Parse Error",
                Message = $"Failed to parse profile: {err}. Default fallbacks are active.",
                Severity = "Error",
                ActionText = "Settings",
                ActionKey = "open_settings"
            });
        }

        // 3. Check Settings parsing issues
        if (SettingsManager.ParsingError != null)
        {
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "settings.json Parse Warning",
                Message = SettingsManager.ParsingError,
                Severity = "Warning",
                ActionText = "Settings",
                ActionKey = "open_settings"
            });
        }

        // 4. Check if previous catalog fetch reported an error
        if (NvidiaCatalogClient.LastFetchFailed)
        {
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "NVIDIA Catalog Unreachable",
                Message = $"Cannot connect to NVIDIA catalog: {NvidiaCatalogClient.LastFetchError}. Online package lookup is currently unavailable.",
                Severity = "Warning",
                ActionText = "Retry",
                ActionKey = "check_catalog"
            });
        }

        OnPropertyChanged(nameof(HasAlerts));
    }

    public async Task CheckConnectivityAsync()
    {
        var ok = await NvidiaCatalogClient.CheckConnectivityAsync();
        if (!ok)
        {
            RefreshAlerts();
        }
    }

    public async Task DownloadDduAsync()
    {
        IsLoading = true;
        try
        {
            var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "ddu"));
            await Task.Run(() => DduManager.InstallDduViaPowerShellPrompt(targetDir));
            RefreshAlerts();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DownloadSevenZipAsync()
    {
        IsLoading = true;
        try
        {
            var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z"));
            await Task.Run(() => SevenZipManager.InstallSevenZipViaPowerShellPrompt(targetDir));
            RefreshAlerts();
        }
        finally
        {
            IsLoading = false;
        }
    }
}
