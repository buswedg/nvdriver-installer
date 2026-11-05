using System.Collections.ObjectModel;
using System.IO;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;
using NVDriverInstaller.Services;

namespace NVDriverInstaller.ViewModels;

public class BuildViewModel : BaseViewModel
{
    private PackagingProfile? _selectedProfile;
    private DriverPackage? _selectedPackage;
    private bool _isBuilding;
    private bool _hasStarted;
    private string _status = string.Empty;
    private double _progress;
    private string _log = string.Empty;
    private bool _isLoadingTooling;

    public ObservableCollection<PackagingProfile> Profiles { get; } = new();
    public ObservableCollection<DriverPackage> CatalogPackages { get; } = new();
    public ObservableCollection<ConfigAlert> ActiveAlerts { get; } = new();

    public bool HasAlerts => ActiveAlerts.Count > 0;

    public bool IsLoadingTooling
    {
        get => _isLoadingTooling;
        set => SetProperty(ref _isLoadingTooling, value);
    }

    public PackagingProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            var oldType = _selectedProfile?.DriverType;
            if (SetProperty(ref _selectedProfile, value))
            {
                OnPropertyChanged(nameof(CanBuild));
                if (oldType != null && value != null && !oldType.Equals(value.DriverType, StringComparison.OrdinalIgnoreCase) && CatalogPackages.Count > 0)
                {
                    _ = RefreshCatalogAsync(forceRefresh: true);
                }
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
                OnPropertyChanged(nameof(CanBuild));
            }
        }
    }

    public bool IsBuilding
    {
        get => _isBuilding;
        set
        {
            if (SetProperty(ref _isBuilding, value))
            {
                OnPropertyChanged(nameof(CanBuild));
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public string ActionButtonText => IsBuilding ? "Building in PowerShell..." : "Build Package";

    public bool HasStarted
    {
        get => _hasStarted;
        set => SetProperty(ref _hasStarted, value);
    }

    public bool CanBuild => SelectedPackage != null && SelectedProfile != null && !IsBuilding && IsSfxToolingReady;

    public bool HasNoProfiles => Profiles.Count == 0;

    public bool IsSevenZipInstalled => SevenZipManager.IsInstalled();
    public bool HasSfxStub => SevenZipManager.HasSfxStub();
    public bool IsSfxToolingReady => IsSevenZipInstalled && HasSfxStub;

    public void RefreshToolingStatus()
    {
        OnPropertyChanged(nameof(IsSevenZipInstalled));
        OnPropertyChanged(nameof(HasSfxStub));
        OnPropertyChanged(nameof(IsSfxToolingReady));
        RefreshAlerts();
    }

    public void RefreshAlerts()
    {
        ActiveAlerts.Clear();

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
        OnPropertyChanged(nameof(CanBuild));
    }

    public async Task DownloadSevenZipAsync()
    {
        IsLoadingTooling = true;
        try
        {
            var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z"));
            await Task.Run(() => SevenZipManager.InstallSevenZipViaPowerShellPrompt(targetDir));
            RefreshToolingStatus();
        }
        finally
        {
            IsLoadingTooling = false;
        }
    }

    public bool IsLoadingCatalog
    {
        get => _isLoadingCatalog;
        set
        {
            if (SetProperty(ref _isLoadingCatalog, value))
            {
                OnPropertyChanged(nameof(CanBuild));
            }
        }
    }

    private bool _isLoadingCatalog;
    private string _catalogStatusText = string.Empty;
    private bool _catalogHasError;

    public string CatalogStatusText => _catalogStatusText;
    public bool CatalogHasError => _catalogHasError;
    public bool HasCatalogStatus => !string.IsNullOrEmpty(_catalogStatusText);

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

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string Log
    {
        get => _log;
        set => SetProperty(ref _log, value);
    }

    public BuildViewModel()
    {
        LoadProfiles();
    }

    public void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in ProfileManager.PackagingProfiles)
            Profiles.Add(p);

        var host = SystemInspector.InspectHost();
        var isNotebook = host.FormFactor.Equals("Notebook", StringComparison.OrdinalIgnoreCase);
        var targetDefaultId = isNotebook
            ? SettingsManager.Current.DefaultNotebookPackagingProfile
            : SettingsManager.Current.DefaultDesktopPackagingProfile;

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id.Equals(targetDefaultId, StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();

        RefreshToolingStatus();
    }

    public async Task EnsureCatalogLoadedAsync()
    {
        if (CatalogPackages.Count == 0 && !_isLoadingCatalog && !IsBuilding)
        {
            await RefreshCatalogAsync(forceRefresh: false);
        }
    }

    public async Task RefreshCatalogAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && CatalogPackages.Count > 0)
            return;

        if (_isLoadingCatalog || IsBuilding)
            return;

        IsLoadingCatalog = true;
        _catalogHasError = false;
        _catalogStatusText = "Querying official catalog...";
        Status = "Querying official catalog...";
        OnPropertyChanged(nameof(CatalogStatusText));
        OnPropertyChanged(nameof(CatalogHasError));
        OnPropertyChanged(nameof(HasCatalogStatus));

        try
        {
            var host = SystemInspector.InspectHost();
            var detectedType = host.FormFactor.Equals("Notebook", StringComparison.OrdinalIgnoreCase) ? "notebook" : "desktop";
            var driverType = !string.IsNullOrEmpty(SelectedProfile?.DriverType) && !SelectedProfile.DriverType.Equals("desktop", StringComparison.OrdinalIgnoreCase)
                ? SelectedProfile.DriverType
                : detectedType;
            var packages = await NvidiaCatalogClient.FetchAvailablePackagesAsync(driverType);

            CatalogPackages.Clear();
            foreach (var pkg in packages)
                CatalogPackages.Add(pkg);

            SelectedPackage = CatalogPackages.FirstOrDefault();
            _catalogStatusText = $"Loaded {CatalogPackages.Count} releases from upstream";
            _catalogHasError = false;
            Status = $"Loaded {CatalogPackages.Count} releases from upstream.";
        }
        catch (Exception ex)
        {
            _catalogStatusText = $"Error: {ex.Message}";
            _catalogHasError = true;
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingCatalog = false;
            OnPropertyChanged(nameof(CatalogStatusText));
            OnPropertyChanged(nameof(CatalogHasError));
            OnPropertyChanged(nameof(HasCatalogStatus));
            RefreshAlerts();
            OnPropertyChanged(nameof(CanBuild));
        }
    }

    public async Task BuildAsync()
    {
        if (SelectedPackage == null || SelectedProfile == null)
            return;

        if (IsBuilding)
            return;

        HasStarted = true;
        IsBuilding = true;
        Status = "Package build routine running in PowerShell...";

        try
        {
            var success = await PackageBuilder.BuildPackageViaPowerShellPromptAsync(SelectedProfile, SelectedPackage);
            if (success)
            {
                Status = "Package generated successfully in packages/clean.";
            }
            else
            {
                Status = "Package compilation was cancelled or encountered an error.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogBuildError("BuildViewModel.BuildAsync", ex);
            Status = $"Build error: {ex.Message}";
        }
        finally
        {
            IsBuilding = false;
        }
    }

    public void OpenBuildLog()
    {
        AppLogger.OpenLogFile(AppLogger.BuildLogPath);
    }

    public void OpenLogsFolder()
    {
        AppLogger.OpenLogsFolder();
    }
}
