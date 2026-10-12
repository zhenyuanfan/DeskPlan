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
        HolidayCalendarInfoText.Text = $"中国大陆官方日历：{ChineseHolidayCalendar.SupportedYears} 年。新年份需更新应用日历。";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var enabled = AutoStartCheck.IsChecked == true;
        if (!StartupService.TryApply(enabled, out var error))
        {
            System.Windows.MessageBox.Show(this, error, "设置未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.StartWithWindows = enabled;
        DialogResult = true;
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
