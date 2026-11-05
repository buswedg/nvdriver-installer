using System.IO;
using System.Collections.ObjectModel;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;
using NVDriverInstaller.Services;
using Wpf.Ui.Controls;

namespace NVDriverInstaller.ViewModels;

public enum InstallPathway
{
    StandardInstall,
    CleanInstall
}

public class InstallViewModel : BaseViewModel
{
    private InstallPathway _selectedPathway = InstallPathway.StandardInstall;
    private DduProfile? _selectedDduProfile;
    private DriverPackage? _selectedPackage;
    private bool _isBusy;
    private bool _hasStarted;
    private double _progressPercentage;
    private string _logOutput = string.Empty;
    private bool _quarantineNetwork;
    private bool _autoRebootSafeMode;
    private bool _autoRebootAfterInstall;
    private bool _dryRunMode;
    private bool _unattendedInstall;
    private bool _installNvcplAppx;
    private string _packageStatusText = string.Empty;
    private bool _packageHasError;

    public ObservableCollection<DduProfile> DduProfiles { get; } = new();
    public ObservableCollection<DriverPackage> AvailablePackages { get; } = new();
    public ObservableCollection<ConfigAlert> ActiveAlerts { get; } = new();

    public bool HasAlerts => ActiveAlerts.Count > 0;

    private bool _isLoadingTooling;
    public bool IsLoadingTooling
    {
        get => _isLoadingTooling;
        set => SetProperty(ref _isLoadingTooling, value);
    }

    public string PackageStatusText
    {
        get => _packageStatusText;
        set => SetProperty(ref _packageStatusText, value);
    }

    public bool PackageHasError
    {
        get => _packageHasError;
        set => SetProperty(ref _packageHasError, value);
    }

    public bool HasPackageStatus => !string.IsNullOrWhiteSpace(_packageStatusText);

    public bool QuarantineNetwork
    {
        get => _quarantineNetwork;
        set
        {
            if (SetProperty(ref _quarantineNetwork, value))
            {
                SettingsManager.Current.QuarantineNetwork = value;
                SettingsManager.Save();
            }
        }
    }

    public bool AutoRebootSafeMode
    {
        get => _autoRebootSafeMode;
        set
        {
            if (SetProperty(ref _autoRebootSafeMode, value))
            {
                SettingsManager.Current.AutoRebootToSafeMode = value;
                SettingsManager.Save();
                OnPropertyChanged(nameof(AutoReboot));
            }
        }
    }

    public bool AutoRebootAfterInstall
    {
        get => _autoRebootAfterInstall;
        set
        {
            if (SetProperty(ref _autoRebootAfterInstall, value))
            {
                SettingsManager.Current.AutoRebootAfterInstall = value;
                SettingsManager.Save();
            }
        }
    }

    public bool AutoReboot
    {
        get => AutoRebootSafeMode;
        set => AutoRebootSafeMode = value;
    }

    public bool UnattendedInstall
    {
        get => _unattendedInstall;
        set
        {
            if (SetProperty(ref _unattendedInstall, value))
            {
                SettingsManager.Current.UnattendedInstall = value;
                SettingsManager.Save();
            }
        }
    }

    public bool InstallNvcplAppx
    {
        get => _installNvcplAppx;
        set
        {
            if (SetProperty(ref _installNvcplAppx, value))
            {
                SettingsManager.Current.InstallNvcplAppx = value;
                SettingsManager.Save();
            }
        }
    }

    public bool DryRunMode
    {
        get => _dryRunMode;
        set
        {
            if (SetProperty(ref _dryRunMode, value))
            {
                SettingsManager.Current.DryRunMode = value;
                SettingsManager.Save();
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ActionButtonSymbol));
                OnPropertyChanged(nameof(IsSimulationMode));
                RefreshAlerts();
            }
        }
    }

    public bool IsSimulationMode => IsCleanInstall && _dryRunMode;

    public string ActionButtonText => IsBusy
        ? "Running in PowerShell..."
        : ((IsCleanInstall && DryRunMode) ? "Run Simulation" : "Install Package");

    public SymbolRegular ActionButtonSymbol => SymbolRegular.ArrowSyncCircle24;

    public InstallPathway SelectedPathway
    {
        get => _selectedPathway;
        set
        {
            if (SetProperty(ref _selectedPathway, value))
            {
                SettingsManager.Current.PreferredInstallPathway = value == InstallPathway.CleanInstall ? "clean" : "standard";
                SettingsManager.Save();
                OnPropertyChanged(nameof(IsCleanInstall));
                OnPropertyChanged(nameof(IsStandardInstall));
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ActionButtonSymbol));
                OnPropertyChanged(nameof(IsSimulationMode));
                RefreshAlerts();
            }
        }
    }

    public bool IsCleanInstall => SelectedPathway == InstallPathway.CleanInstall;
    public bool IsStandardInstall => SelectedPathway == InstallPathway.StandardInstall;

    public DduProfile? SelectedDduProfile
    {
        get => _selectedDduProfile;
        set
        {
            if (SetProperty(ref _selectedDduProfile, value))
            {
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public DriverPackage? SelectedPackage
    {
        get => _selectedPackage;
        set
        {
            if (SetProperty(ref _selectedPackage, value))
            {
                var pkgProfile = value?.ProfileName != null ? ProfileManager.GetPackagingProfile(value.ProfileName) : null;
                _unattendedInstall = pkgProfile?.Unattended ?? false;
                OnPropertyChanged(nameof(UnattendedInstall));
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(HasNoPackages));
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public bool HasStarted
    {
        get => _hasStarted;
        set => SetProperty(ref _hasStarted, value);
    }

    public bool IsDduInstalled => DduManager.IsInstalled();

    public bool CanInstall =>
        SelectedPackage != null &&
        (!IsCleanInstall || (SelectedDduProfile != null && DduManager.IsInstalled())) &&
        SevenZipManager.IsInstalled() &&
        !IsBusy;

    public bool HasNoPackages => AvailablePackages.Count == 0 && !IsBusy;

    public bool HasNoDduProfiles => DduProfiles.Count == 0;

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => SetProperty(ref _progressPercentage, value);
    }

    public string LogOutput
    {
        get => _logOutput;
        set => SetProperty(ref _logOutput, value);
    }

    public InstallViewModel()
    {
        LoadDduProfiles();
    }

    public void LoadDduProfiles()
    {
        DduProfiles.Clear();
        foreach (var d in ProfileManager.DduProfiles)
            DduProfiles.Add(d);

        var settings = SettingsManager.Current;
        var preferredPathway = settings.PreferredInstallPathway;
        _selectedPathway = preferredPathway.Equals("clean", StringComparison.OrdinalIgnoreCase)
            ? InstallPathway.CleanInstall
            : InstallPathway.StandardInstall;

        SelectedDduProfile = ProfileManager.GetDduProfile(settings.DefaultDduProfile);
        _quarantineNetwork = settings.QuarantineNetwork;
        _autoRebootSafeMode = settings.AutoRebootToSafeMode;
        _autoRebootAfterInstall = settings.AutoRebootAfterInstall;
        _dryRunMode = settings.DryRunMode;
        _unattendedInstall = settings.UnattendedInstall;
        _installNvcplAppx = settings.InstallNvcplAppx;
        OnPropertyChanged(nameof(SelectedPathway));
        OnPropertyChanged(nameof(IsCleanInstall));
        OnPropertyChanged(nameof(IsStandardInstall));
        OnPropertyChanged(nameof(QuarantineNetwork));
        OnPropertyChanged(nameof(AutoRebootSafeMode));
        OnPropertyChanged(nameof(AutoRebootAfterInstall));
        OnPropertyChanged(nameof(AutoReboot));
        OnPropertyChanged(nameof(DryRunMode));
        OnPropertyChanged(nameof(UnattendedInstall));
        OnPropertyChanged(nameof(InstallNvcplAppx));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(IsSimulationMode));
        OnPropertyChanged(nameof(HasNoDduProfiles));
        OnPropertyChanged(nameof(IsDduInstalled));
        OnPropertyChanged(nameof(CanInstall));
        RefreshAlerts();
    }

    public void SetPathway(InstallPathway pathway)
    {
        SelectedPathway = pathway;
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(IsDduInstalled));
        RefreshAlerts();
    }

    public void RefreshAlerts()
    {
        ActiveAlerts.Clear();

        if (IsCleanInstall)
        {
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
        }

        if (!SevenZipManager.IsInstalled())
        {
            var sevenZipPath = SevenZipManager.GetExecutablePath();
            ActiveAlerts.Add(new ConfigAlert
            {
                Title = "7-Zip Tooling Missing",
                Message = $"7z executable was not found at '{sevenZipPath}'. Extracting standalone (.exe) packages requires 7-Zip.",
                Severity = "Warning",
                ActionText = "Download 7-Zip",
                ActionKey = "download_7z"
            });
        }

        OnPropertyChanged(nameof(HasAlerts));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(IsDduInstalled));
    }

    public async Task DownloadDduAsync()
    {
        IsLoadingTooling = true;
        try
        {
            var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "ddu"));
            await Task.Run(() => DduManager.InstallDduViaPowerShellPrompt(targetDir));
            RefreshAlerts();
        }
        finally
        {
            IsLoadingTooling = false;
        }
    }

    public async Task DownloadSevenZipAsync()
    {
        IsLoadingTooling = true;
        try
        {
            var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z"));
            await Task.Run(() => SevenZipManager.InstallSevenZipViaPowerShellPrompt(targetDir));
            RefreshAlerts();
        }
        finally
        {
            IsLoadingTooling = false;
        }
    }

    public void LoadPackages()
    {
        IsBusy = true;
        AvailablePackages.Clear();

        try
        {
            // Only scan local built packages from packages/ (or custom directory)
            var local = PackageBuilder.ScanLocalPackages();
            foreach (var l in local)
                AvailablePackages.Add(l);

            SelectedPackage = AvailablePackages.FirstOrDefault();
            if (AvailablePackages.Count > 0)
            {
                PackageStatusText = $"Loaded {AvailablePackages.Count} built package(s) from packages/clean";
                PackageHasError = false;
            }
            else
            {
                PackageStatusText = "No built driver packages found in packages/clean";
                PackageHasError = true;
            }
        }
        catch (Exception ex)
        {
            PackageStatusText = $"Package lookup failed: {ex.Message}";
            PackageHasError = true;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoPackages));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(PackageStatusText));
            OnPropertyChanged(nameof(PackageHasError));
            OnPropertyChanged(nameof(HasPackageStatus));
        }
    }

    public async Task ExecuteInstallPipelineAsync()
    {
        if (SelectedPackage == null || string.IsNullOrEmpty(SelectedPackage.LocalFilePath))
            return;

        if (IsBusy)
            return;

        if (IsCleanInstall && (SelectedDduProfile == null || !IsDduInstalled))
            return;

        HasStarted = true;
        IsBusy = true;
        PackageStatusText = IsSimulationMode
            ? "Simulation running in PowerShell..."
            : "Driver installation running in PowerShell...";
        PackageHasError = false;

        try
        {
            var pkgProfile = SelectedPackage.ProfileName != null
                ? ProfileManager.GetPackagingProfile(SelectedPackage.ProfileName)
                : null;
            // Only unattended if the build profile used for the package explicitly specifies it
            var isUnattended = pkgProfile?.Unattended ?? false;
            var shouldInstallNvcpl = pkgProfile?.HasControlPanel ?? true;

            var success = await DriverInstaller.InstallDriverViaPowerShellPromptAsync(
                packagePath: SelectedPackage.LocalFilePath,
                isCleanInstall: IsCleanInstall,
                dduProfileId: SelectedDduProfile?.Id,
                packagingProfileId: pkgProfile?.Id,
                quarantineNetwork: QuarantineNetwork,
                autoRebootSafeMode: AutoRebootSafeMode,
                autoRebootAfterInstall: AutoRebootAfterInstall,
                unattended: isUnattended,
                installNvcpl: shouldInstallNvcpl,
                isDryRun: IsSimulationMode);

            if (success)
            {
                PackageStatusText = IsSimulationMode
                    ? "Pre-flight simulation completed successfully."
                    : "Installation pipeline completed successfully.";
                PackageHasError = false;
            }
            else
            {
                PackageStatusText = "Installation was cancelled or encountered an error.";
                PackageHasError = true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogInstallError("InstallViewModel.ExecuteInstallPipelineAsync", ex);
            PackageStatusText = $"Installation error: {ex.Message}";
            PackageHasError = true;
        }
        finally
        {
            IsBusy = false;
            LoadPackages();
        }
    }

    public void OpenInstallLog()
    {
        AppLogger.OpenLogFile(AppLogger.InstallLogPath);
    }

    public void OpenLogsFolder()
    {
        AppLogger.OpenLogsFolder();
    }
}
