using Microsoft.Win32;

namespace DeskPlan.Services;

public static class StartupService
{
    private const string ValueName = "DeskPlan";

    public static bool TryApply(bool enabled, out string? error)
    {
        error = null;
        if (AppPaths.IsIsolated) return true;
#if DEBUG
        return true;
#else
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    throw new InvalidOperationException("无法确定程序所在位置。");
                var command = BuildCommand(executable);
                key.SetValue(ValueName, command);
                if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal))
                    throw new IOException("启动项未能保存。");
            }
            else
            {
                key.DeleteValue(ValueName, false);
                if (key.GetValue(ValueName) is not null) throw new IOException("启动项未能移除。");
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"无法{(enabled ? "开启" : "关闭")}开机自启，请稍后在设置中重试。\n{ex.Message}";
            return false;
        }
#endif
    }

    public static string BuildCommand(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)
            || executable.Contains('"') || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("需要完整的 EXE 路径。", nameof(executable));
        return $"\"{executable}\"";
    }
}
