using System.Windows;
using System.Windows.Controls;

namespace NVDriverInstaller.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (s, e) => Focus();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveSettings();
    }

    private void OnViewLogClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLogFile();
    }

    private void OnViewBuildLogClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenBuildLog();
    }

    private void OnViewInstallLogClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenInstallLog();
    }

    private void OnOpenLogsFolderClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLogsFolder();
    }
}
