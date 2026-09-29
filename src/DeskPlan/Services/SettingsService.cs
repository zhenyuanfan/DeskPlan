using System.Text.Json;
using DeskPlan.Models;

namespace DeskPlan.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static AppSettings Load()
    {
        AppPaths.EnsureDirectories();
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // A broken preference file must never prevent the user's tasks from opening.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        var temporary = AppPaths.SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, AppPaths.SettingsPath, true);
    }
}
