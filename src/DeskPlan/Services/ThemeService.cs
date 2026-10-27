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

        ApplyTheme(resources, isLight);
    }

    // Also used by offline visual checks; does not read or change Windows settings.
    public static void ApplyTheme(ResourceDictionary resources, bool isLight)
    {
        Set(resources, "WindowBackgroundBrush", isLight ? "#F4F6FA" : "#191D25");
        Set(resources, "CardBrush", isLight ? "#FFFFFF" : "#222833");
        Set(resources, "SurfaceBrush", isLight ? "#F1F4F9" : "#2C3441");
        Set(resources, "HeaderBrush", isLight ? "#F8FAFD" : "#252D39");
        Set(resources, "SoftBorderBrush", isLight ? "#EAF0F6" : "#323C4B");
        Set(resources, "TextBrush", isLight ? "#202B3D" : "#EEF2F8");
        Set(resources, "MutedTextBrush", isLight ? "#68768A" : "#ACB8CB");
        Set(resources, "BorderBrush", isLight ? "#DCE3ED" : "#404C5E");
        Set(resources, "AccentBrush", isLight ? "#3478F6" : "#6BA4FF");
        Set(resources, "TodayBrush", "#3478F6");
        Set(resources, "AccentSoftBrush", isLight ? "#EDF4FF" : "#293C59");
        Set(resources, "SuccessBrush", isLight ? "#25856A" : "#6AC4A1");
        Set(resources, "WarningBrush", isLight ? "#B77722" : "#E7B66F");
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
