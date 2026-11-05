using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using NVDriverInstaller.Core.Models;
using NVDriverInstaller.Services;

namespace NVDriverInstaller.ViewModels;

public class UtilitiesViewModel : BaseViewModel
{
    private bool _isBusy;
    private string _status = string.Empty;
    private bool _isDduInstalled;
    private string _dduPath = string.Empty;
    private bool _isSafeModeArmed;

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(_status);

    public bool IsDduInstalled
    {
        get => _isDduInstalled;
        set => SetProperty(ref _isDduInstalled, value);
    }

    public string DduPath
    {
        get => _dduPath;
        set => SetProperty(ref _dduPath, value);
    }

    private string _dduStatusBadgeText = "Not Installed";
    private string _dduStatusBadgeBackground = "#2D1518";
    private string _dduStatusBadgeBorderBrush = "#5C2229";
    private string _dduStatusBadgeForeground = "#EF4444";
    private bool _isDduUpdateAvailable;
    private string? _availableRemoteVersion;

    public string DduStatusBadgeText
    {
        get => _dduStatusBadgeText;
        set => SetProperty(ref _dduStatusBadgeText, value);
    }

    public string DduStatusBadgeBackground
    {
        get => _dduStatusBadgeBackground;
        set => SetProperty(ref _dduStatusBadgeBackground, value);
    }

    public string DduStatusBadgeBorderBrush
    {
        get => _dduStatusBadgeBorderBrush;
        set => SetProperty(ref _dduStatusBadgeBorderBrush, value);
    }

    public string DduStatusBadgeForeground
    {
        get => _dduStatusBadgeForeground;
        set => SetProperty(ref _dduStatusBadgeForeground, value);
    }

    public bool IsDduUpdateAvailable
    {
        get => _isDduUpdateAvailable;
        set
        {
            if (SetProperty(ref _isDduUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(DduUpdateButtonText));
            }
        }
    }

    public string DduUpdateButtonText => IsDduUpdateAvailable ? "Update DDU" : "Check for Updates";

    public string DduStatusDescription => IsDduInstalled
        ? $"Portable Display Driver Uninstaller v{DduManager.GetLocalVersion() ?? "unknown"} ready for clean safe-mode driver removal."
        : "Display Driver Uninstaller is not installed. Download required for clean driver upgrade.";

    public void UpdateDduStatus()
    {
        var status = DduManager.GetStatusInfo(IsDduUpdateAvailable, _availableRemoteVersion);
        DduStatusBadgeText = status.BadgeText;
        DduStatusBadgeBackground = status.BadgeBackground;
        DduStatusBadgeBorderBrush = status.BadgeBorderBrush;
        DduStatusBadgeForeground = status.BadgeForeground;
        OnPropertyChanged(nameof(DduUpdateButtonText));
        OnPropertyChanged(nameof(DduStatusDescription));
    }

    private bool _isSevenZipInstalled;
    private string _sevenZipPath = string.Empty;
    private string _sevenZipStatusBadgeText = "Not Installed";
    private string _sevenZipStatusBadgeBackground = "#2D1518";
    private string _sevenZipStatusBadgeBorderBrush = "#5C2229";
    private string _sevenZipStatusBadgeForeground = "#EF4444";
    private bool _isSevenZipUpdateAvailable;
    private string? _availableSevenZipRemoteVersion;

    public bool IsSevenZipInstalled
    {
        get => _isSevenZipInstalled;
        set => SetProperty(ref _isSevenZipInstalled, value);
    }

    public string SevenZipPath
    {
        get => _sevenZipPath;
        set => SetProperty(ref _sevenZipPath, value);
    }

    public string SevenZipStatusBadgeText
    {
        get => _sevenZipStatusBadgeText;
        set => SetProperty(ref _sevenZipStatusBadgeText, value);
    }

    public string SevenZipStatusBadgeBackground
    {
        get => _sevenZipStatusBadgeBackground;
        set => SetProperty(ref _sevenZipStatusBadgeBackground, value);
    }

    public string SevenZipStatusBadgeBorderBrush
    {
        get => _sevenZipStatusBadgeBorderBrush;
        set => SetProperty(ref _sevenZipStatusBadgeBorderBrush, value);
    }

    public string SevenZipStatusBadgeForeground
    {
        get => _sevenZipStatusBadgeForeground;
        set => SetProperty(ref _sevenZipStatusBadgeForeground, value);
    }

    public bool IsSevenZipUpdateAvailable
    {
        get => _isSevenZipUpdateAvailable;
        set
        {
            if (SetProperty(ref _isSevenZipUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(SevenZipUpdateButtonText));
            }
        }
    }

    public string SevenZipUpdateButtonText => IsSevenZipUpdateAvailable ? "Update 7-Zip" : "Check for Updates";

    public string SevenZipStatusDescription
    {
        get
        {
            if (!IsSevenZipInstalled)
                return "7-Zip packaging tooling is not installed. Download required for archive extraction and standalone .exe builds.";
            if (!SevenZipManager.HasSfxStub())
                return "7-Zip CLI is present, but 7z.sfx stub is missing. Standalone .exe package compilation requires SFX stub.";
            return $"7-Zip CLI tooling v{SevenZipManager.GetLocalVersion() ?? "unknown"} and SFX modules ready for standalone packaging.";
        }
    }

    public void UpdateSevenZipStatus()
    {
        var status = SevenZipManager.GetStatusInfo(IsSevenZipUpdateAvailable, _availableSevenZipRemoteVersion);
        SevenZipStatusBadgeText = status.BadgeText;
        SevenZipStatusBadgeBackground = status.BadgeBackground;
        SevenZipStatusBadgeBorderBrush = status.BadgeBorderBrush;
        SevenZipStatusBadgeForeground = status.BadgeForeground;
        OnPropertyChanged(nameof(SevenZipUpdateButtonText));
        OnPropertyChanged(nameof(SevenZipStatusDescription));
    }

    public bool IsSafeModeArmed
    {
        get => _isSafeModeArmed;
        set
        {
            if (SetProperty(ref _isSafeModeArmed, value))
            {
                OnPropertyChanged(nameof(SafeModeStatusBadge));
                OnPropertyChanged(nameof(SafeModeStatusDescription));
                OnPropertyChanged(nameof(SafeModeButtonText));
            }
        }
    }

    public string SafeModeStatusBadge => IsSafeModeArmed ? "SafeBoot Active" : "Normal Boot";

    public string SafeModeStatusDescription => IsSafeModeArmed
        ? "Windows is configured to restart directly into Safe Mode (safeboot minimal)."
        : "Windows is configured for standard boot. Click below to schedule Safe Mode for next restart.";

    public string SafeModeButtonText => IsSafeModeArmed ? "Disable Safe Mode" : "Enable Safe Mode";

    public UtilitiesViewModel()
    {
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        DduPath = DduManager.GetDduExecutablePath();
        IsDduInstalled = DduManager.IsInstalled();
        SevenZipPath = SevenZipManager.GetExecutablePath();
        IsSevenZipInstalled = SevenZipManager.IsInstalled();
        IsSafeModeArmed = BcdBootManager.IsSafeBootConfigured();

        UpdateDduStatus();
        UpdateSevenZipStatus();

        if (_status.StartsWith("Required tooling missing", StringComparison.OrdinalIgnoreCase) ||
            _status.StartsWith("Display Driver Uninstaller", StringComparison.OrdinalIgnoreCase) ||
            _status.StartsWith("7-Zip", StringComparison.OrdinalIgnoreCase))
        {
            Status = string.Empty;
        }

        var physical = NetworkQuarantineService.GetPhysicalAdapters();

        for (var i = Adapters.Count - 1; i >= 0; i--)
        {
            if (!physical.Any(p => p.Name.Equals(Adapters[i].Name, StringComparison.OrdinalIgnoreCase)))
                Adapters.RemoveAt(i);
        }

        foreach (var p in physical)
        {
            var existing = Adapters.FirstOrDefault(a => a.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.InterfaceDescription = p.InterfaceDescription;
                existing.MacAddress = p.MacAddress;
                existing.Status = p.Status;
                existing.IsPhysical = p.IsPhysical;
                existing.AdminStatus = p.AdminStatus;
            }
            else
            {
                Adapters.Add(p);
            }
        }
    }

    public async Task DisableAllNetworkAsync()
    {
        IsBusy = true;
        try
        {
            await Task.Run(() => NetworkQuarantineService.DisableAllPhysical());
            RefreshStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void DisableAllNetwork() => _ = DisableAllNetworkAsync();

    public async Task EnableAllNetworkAsync()
    {
        IsBusy = true;
        try
        {
            await Task.Run(() => NetworkQuarantineService.EnableAllPhysical());
            RefreshStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void EnableAllNetwork() => _ = EnableAllNetworkAsync();

    public async Task ToggleAdapterAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        IsBusy = true;
        try
        {
            var adapter = Adapters.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (adapter == null) return;

            var isCurrentlyUp = adapter.IsUp;
            await Task.Run(() =>
            {
                if (isCurrentlyUp)
                    NetworkQuarantineService.DisableAdapter(name);
                else
                    NetworkQuarantineService.EnableAdapter(name);
            });

            RefreshStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ToggleAdapter(string name) => _ = ToggleAdapterAsync(name);

    public async Task HandleDduUpdateOrCheckAsync()
    {
        if (IsDduUpdateAvailable)
        {
            await DownloadDduAsync();
        }
        else
        {
            await CheckDduUpdatesAsync();
        }
    }

    public async Task HandleSevenZipUpdateOrCheckAsync()
    {
        if (IsSevenZipUpdateAvailable)
        {
            await DownloadSevenZipAsync();
        }
        else
        {
            await CheckSevenZipUpdatesAsync();
        }
    }

    public async Task CheckDduUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => DduManager.CheckForUpdatesViaPowerShellPrompt());
            if (result.success && result.isUpdateAvailable && !string.IsNullOrEmpty(result.remoteVersion))
            {
                _availableRemoteVersion = result.remoteVersion;
                IsDduUpdateAvailable = true;
            }
            else
            {
                IsDduUpdateAvailable = false;
                _availableRemoteVersion = null;
            }
            UpdateDduStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadDduAsync()
    {
        IsBusy = true;
        try
        {
            var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "ddu"));
            var success = await Task.Run(() => DduManager.InstallDduViaPowerShellPrompt(targetDir));
            if (success)
            {
                IsDduUpdateAvailable = false;
                _availableRemoteVersion = null;
            }
            RefreshStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenDdu()
    {
        var exe = DduManager.GetDduExecutablePath();
        if (File.Exists(exe))
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                Status = string.Empty;
            }
            catch (Exception ex)
            {
                Status = $"Failed to launch DDU: {ex.Message}";
            }
        }
        else
        {
            RefreshStatus();
            Status = "Display Driver Uninstaller executable was not found. Please click 'Download DDU' to install.";
        }
    }

    public async Task CheckSevenZipUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => SevenZipManager.CheckForUpdatesViaPowerShellPrompt());
            if (result.success && result.isUpdateAvailable && !string.IsNullOrEmpty(result.remoteVersion))
            {
                _availableSevenZipRemoteVersion = result.remoteVersion;
                IsSevenZipUpdateAvailable = true;
            }
            else
            {
                IsSevenZipUpdateAvailable = false;
                _availableSevenZipRemoteVersion = null;
            }
            UpdateSevenZipStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadSevenZipAsync()
    {
        IsBusy = true;
        try
        {
            var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z"));
            var success = await Task.Run(() => SevenZipManager.InstallSevenZipViaPowerShellPrompt(targetDir));
            if (success)
            {
                IsSevenZipUpdateAvailable = false;
                _availableSevenZipRemoteVersion = null;
            }
            RefreshStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenSevenZip()
    {
        var exe = SevenZipManager.GetExecutablePath();
        if (File.Exists(exe))
        {
            try
            {
                SevenZipManager.OpenSevenZip();
                Status = string.Empty;
            }
            catch (Exception ex)
            {
                Status = $"Failed to launch 7-Zip: {ex.Message}";
            }
        }
        else
        {
            RefreshStatus();
            Status = "7-Zip executable was not found. Please click 'Download 7-Zip' to install.";
        }
    }

    public void OpenSevenZipFolder()
    {
        var exe = SevenZipManager.GetExecutablePath();
        var dir = File.Exists(exe) ? Path.GetDirectoryName(exe) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z"));
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                SevenZipManager.OpenToolsFolder();
            }
            catch (Exception ex)
            {
                Status = $"Failed to open directory: {ex.Message}";
            }
        }
        else
        {
            RefreshStatus();
            Status = "7-Zip directory not found on disk.";
        }
    }

    public void ToggleSafeMode()
    {
        IsBusy = true;
        try
        {
            if (IsSafeModeArmed)
            {
                BcdBootManager.DisableSafeMode();
            }
            else
            {
                BcdBootManager.EnableSafeMode();
            }
            RefreshStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
