using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NVDriverInstaller.ViewModels;

namespace NVDriverInstaller.Views.Pages;

public partial class InstallPage : Page
{
    public InstallPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            ViewModel.LoadDduProfiles();
            ViewModel.LoadPackages();
            ViewModel.RefreshAlerts();
        };
    }

    private async void OnAlertActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string actionKey)
        {
            if (actionKey == "download_ddu")
            {
                await ViewModel.DownloadDduAsync();
            }
            else if (actionKey == "download_7z")
            {
                await ViewModel.DownloadSevenZipAsync();
            }
        }
    }

    private void OnStandardInstallPathwaySelected(object sender, MouseButtonEventArgs e)
    {
        ViewModel.SetPathway(InstallPathway.StandardInstall);
    }

    private void OnCleanInstallPathwaySelected(object sender, MouseButtonEventArgs e)
    {
        ViewModel.SetPathway(InstallPathway.CleanInstall);
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadPackages();
    }

    private async void OnExecuteInstallClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || !ViewModel.CanInstall)
            return;

        if (ViewModel.DryRunMode)
        {
            await ViewModel.ExecuteInstallPipelineAsync();
            return;
        }

        var pkgName = System.IO.Path.GetFileName(ViewModel.SelectedPackage?.LocalFilePath) ?? "driver package";
        var pathwayName = ViewModel.IsCleanInstall ? "Clean Upgrade (DDU)" : "Standard In-Place";

        var result = MessageBox.Show(
            $"Are you sure you want to install the target package '{pkgName}' using the selected {pathwayName} pathway?",
            "Confirm Driver Installation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await ViewModel.ExecuteInstallPipelineAsync();
        }
    }
}
