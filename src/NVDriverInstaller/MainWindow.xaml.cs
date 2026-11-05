using System.Windows;
using System.Windows.Media.Imaging;
using NVDriverInstaller.Services;
using NVDriverInstaller.Views.Pages;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace NVDriverInstaller;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ApplicationThemeManager.Apply(this);

        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute);
            Icon = BitmapFrame.Create(iconUri);
        }
        catch { }

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        NavigateToDashboard();
    }

    public void NavigateToDashboard()
    {
        RootNavigation.Navigate(typeof(DashboardPage));
    }

    public void NavigateToInstall()
    {
        RootNavigation.Navigate(typeof(InstallPage));
    }

    public void NavigateToBuild()
    {
        RootNavigation.Navigate(typeof(BuildPage));
    }

    public void NavigateToUtilities()
    {
        RootNavigation.Navigate(typeof(UtilitiesPage));
    }

    public void NavigateToSettings()
    {
        RootNavigation.Navigate(typeof(SettingsPage));
    }
}
