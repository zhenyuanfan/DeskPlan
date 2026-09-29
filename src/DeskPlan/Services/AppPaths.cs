namespace DeskPlan.Services;

public static class AppPaths
{
    // Allows isolated UI verification without writing sample tasks to the personal database.
    public static bool IsIsolated => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DESKPLAN_DATA_DIR"));
    public static string DataDirectory { get; } = IsIsolated
        ? Path.GetFullPath(Environment.GetEnvironmentVariable("DESKPLAN_DATA_DIR")!)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPlan");

    public static string DatabasePath => Path.Combine(DataDirectory, "deskplan.db");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }
}
