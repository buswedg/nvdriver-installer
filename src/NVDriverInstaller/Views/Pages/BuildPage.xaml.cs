using System.Windows;
using System.Windows.Controls;

namespace NVDriverInstaller.Views.Pages;

public partial class BuildPage : Page
{
    public BuildPage()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            Focus();
            ViewModel.RefreshToolingStatus();
            await ViewModel.EnsureCatalogLoadedAsync();
        };
    }

    private async void OnAlertActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string actionKey)
        {
            if (actionKey == "download_7z")
            {
                await ViewModel.DownloadSevenZipAsync();
            }
            else if (actionKey == "check_catalog")
            {
                await ViewModel.RefreshCatalogAsync(forceRefresh: true);
            }
        }
    }

    private async void OnQueryCatalogClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshCatalogAsync(forceRefresh: true);
    }

    private async void OnBuildClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBuilding || !ViewModel.CanBuild) return;
        await ViewModel.BuildAsync();
    }
}
