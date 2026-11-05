using System.Windows;
using System.Windows.Controls;

namespace NVDriverInstaller.Views.Pages;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            Focus();
            ViewModel.RefreshAlerts();
            await ViewModel.CheckConnectivityAsync();
        };
    }

    private void OnInstallPackageCardClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
        {
            main.NavigateToInstall();
        }
    }

    private void OnBuildPackageCardClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
        {
            main.NavigateToBuild();
        }
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
            else if (actionKey == "check_catalog")
            {
                await ViewModel.CheckConnectivityAsync();
            }
            else if (actionKey == "open_settings")
            {
                if (Window.GetWindow(this) is MainWindow main)
                    main.NavigateToSettings();
            }
            else if (actionKey == "open_utilities")
            {
                if (Window.GetWindow(this) is MainWindow main)
                    main.NavigateToUtilities();
            }
        }
    }
}
