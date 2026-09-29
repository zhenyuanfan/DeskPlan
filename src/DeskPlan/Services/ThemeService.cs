using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DeskPlan.Services;

public static class ThemeService
{
    public static void ApplySystemTheme(ResourceDictionary resources)
    {
        var isLight = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            isLight = Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) != 0;
        }
        catch { }

        Set(resources, "WindowBackgroundBrush", isLight ? "#F5F5F7" : "#1C1C1E");
        Set(resources, "CardBrush", isLight ? "#FFFFFF" : "#242426");
        Set(resources, "SurfaceBrush", isLight ? "#F2F2F7" : "#303033");
        Set(resources, "TextBrush", isLight ? "#1D1D1F" : "#F5F5F7");
        Set(resources, "MutedTextBrush", isLight ? "#86868B" : "#A1A1A6");
        Set(resources, "BorderBrush", isLight ? "#E5E5EA" : "#3A3A3C");
        Set(resources, "AccentSoftBrush", isLight ? "#EAF3FF" : "#20324B");
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
