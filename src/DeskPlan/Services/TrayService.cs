using System.Drawing;
using System.Windows;

namespace DeskPlan.Services;

public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly Icon _applicationIcon;
    private bool _disposed;

    public TrayService(Action show, Action settings, Action exit)
    {
        using var iconStream = typeof(TrayService).Assembly.GetManifestResourceStream("DeskPlan.TrayIcon")
            ?? throw new InvalidOperationException("Missing tray icon resource.");
        using var source = new Icon(iconStream, new System.Drawing.Size(32, 32));
        _applicationIcon = (Icon)source.Clone();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示或定位", null, (_, _) => Dispatch(show));
        menu.Items.Add("设置", null, (_, _) => Dispatch(settings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatch(exit));

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "桌面计划",
            Icon = _applicationIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => Dispatch(show);
    }

    public void Notify(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _applicationIcon.Dispose();
    }

    private static void Dispatch(Action action) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(action);
}
