using System.Windows;
using System.Windows.Controls;

namespace NVDriverInstaller.Views.Pages;

public partial class UtilitiesPage : Page
{
    public UtilitiesPage()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            Focus();
            ViewModel.RefreshStatus();
        };
    }

    private async void OnDisableAllNetworkClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.DisableAllNetworkAsync();
    }

    private async void OnEnableAllNetworkClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.EnableAllNetworkAsync();
    }

    private async void OnToggleAdapterClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string adapterName)
        {
            await ViewModel.ToggleAdapterAsync(adapterName);
        }
    }

    private async void OnDownloadDduClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.DownloadDduAsync();
    }

    private async void OnDduUpdateOrCheckClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.HandleDduUpdateOrCheckAsync();
    }

    private void OnOpenDduClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenDdu();
    }

    private async void OnDownloadSevenZipClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.DownloadSevenZipAsync();
    }

    private async void OnSevenZipUpdateOrCheckClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.HandleSevenZipUpdateOrCheckAsync();
    }

    private void OnOpenSevenZipClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSevenZip();
    }

    private void OnOpenSevenZipFolderClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSevenZipFolder();
    }

    private void OnToggleSafeModeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSafeMode();
    }
}
