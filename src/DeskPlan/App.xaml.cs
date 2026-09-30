using System.Windows;
using DeskPlan.Services;

namespace DeskPlan;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private SingleInstanceService? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new SingleInstanceService(AppPaths.IsIsolated
            ? "QA:" + Path.GetFullPath(AppPaths.DataDirectory).ToUpperInvariant() : "Personal");
        if (!_instance.IsPrimary)
        {
            _instance.ActivateExisting();
            Shutdown();
            return;
        }
        if (!AppPaths.IsIsolated && LaunchEnvironment.IsPackaged)
        {
            System.Windows.MessageBox.Show("当前启动环境会改变数据位置。为保护已有任务，程序尚未打开数据库。\n\n请从文件资源管理器直接双击 DeskPlan.exe 启动。",
                "请直接打开桌面计划", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        ThemeService.ApplySystemTheme(Resources);

        var repository = new TaskRepository();
        repository.Initialize();
        repository.BackupIfNeeded();

        var settings = SettingsService.Load();
        var forceExpanded = Environment.GetCommandLineArgs()
            .Any(x => string.Equals(x, "--expanded", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(repository, settings, forceExpanded);
        // Expose the isolated preview to accessibility tools; the real widget stays tray-only.
        if (AppPaths.IsIsolated || e.Args.Contains("--inspect")) window.ShowInTaskbar = true;
        MainWindow = window;

        _tray = new TrayService(
            () => window.ShowAndLocate(),
            () => window.OpenSettings(),
            () =>
            {
                window.AllowRealClose = true;
                _tray?.Dispose();
                Shutdown();
            });

        window.Tray = _tray;
        window.Show();
        _instance.Listen(() => Dispatcher.BeginInvoke(() =>
        {
            // Do not hide an unsaved task editor behind the main widget.
            var dialog = Windows.OfType<Window>().FirstOrDefault(x => x != window && x.IsVisible);
            if (dialog is not null) dialog.Activate();
            else window.ShowAndLocate();
        }));
        if (!StartupService.TryApply(settings.StartWithWindows, out var startupError))
            System.Windows.MessageBox.Show(window, startupError, "开机自启未生效", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
