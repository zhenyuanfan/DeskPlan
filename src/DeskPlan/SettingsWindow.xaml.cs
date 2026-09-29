using System.Diagnostics;
using System.Windows;
using DeskPlan.Models;
using DeskPlan.Services;

namespace DeskPlan;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        AutoStartCheck.IsChecked = settings.StartWithWindows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = AutoStartCheck.IsChecked == true;
        DialogResult = true;
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
