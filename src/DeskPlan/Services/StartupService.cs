using Microsoft.Win32;

namespace DeskPlan.Services;

public static class StartupService
{
    private const string ValueName = "DeskPlan";

    public static void Apply(bool enabled)
    {
        if (AppPaths.IsIsolated) return;
#if DEBUG
        return;
#else
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executable)) key.SetValue(ValueName, $"\"{executable}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // Startup registration is optional; the application itself remains usable.
        }
#endif
    }
}
