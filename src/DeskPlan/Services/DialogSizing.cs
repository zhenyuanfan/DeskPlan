using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeskPlan.Services;

/// <summary>Keeps dialog chrome and its fixed action row inside the owner's monitor.</summary>
public static class DialogSizing
{
    public static void Attach(Window window)
    {
        var preferredWidth = window.Width;
        var preferredHeight = window.Height;
        var minimumWidth = window.MinWidth;
        var minimumHeight = window.MinHeight;
        var fitting = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        // SourceInitialized runs before the first visible frame. Loaded repeats the
        // calculation after WPF has settled a possible per-monitor DPI transition.
        window.SourceInitialized += (_, _) => Fit(centerOnOwner: true);
        window.Loaded += (_, _) => Fit(centerOnOwner: true);
        window.DpiChanged += (_, _) =>
        {
            if (window.IsLoaded && !fitting)
                window.Dispatcher.BeginInvoke(() => Fit(centerOnOwner: false));
        };

        void Fit(bool centerOnOwner)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || fitting) return;
            fitting = true;
            try
            {
                var ownerHandle = window.Owner is { } owner ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                var screen = System.Windows.Forms.Screen.FromHandle(centerOnOwner && ownerHandle != IntPtr.Zero ? ownerHandle : handle);
                var area = screen.WorkingArea;
                var anchor = GetWindowRect(centerOnOwner && ownerHandle != IntPtr.Zero ? ownerHandle : handle, out var rectangle)
                    ? rectangle.ToRectangle() : area;

                if (centerOnOwner)
                {
                    // Move while still hidden so GetDpi below describes the target
                    // monitor, not the monitor where Windows first created the HWND.
                    var ownerDpi = window.Owner is { } dpiOwner ? VisualTreeHelper.GetDpi(dpiOwner) : VisualTreeHelper.GetDpi(window);
                    var initial = FitCentered(anchor, area, preferredWidth, preferredHeight, ownerDpi.DpiScaleX, ownerDpi.DpiScaleY);
                    SetWindowPos(handle, IntPtr.Zero, initial.Left, initial.Top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                }

                var dpi = VisualTreeHelper.GetDpi(window);
                var available = WindowPlacement.Fit(area, area);
                var target = centerOnOwner
                    ? FitCentered(anchor, area, preferredWidth, preferredHeight, dpi.DpiScaleX, dpi.DpiScaleY)
                    : WindowPlacement.Fit(anchor, area);
                window.MinWidth = window.MinHeight = 0;
                window.MaxWidth = available.Width / dpi.DpiScaleX;
                window.MaxHeight = available.Height / dpi.DpiScaleY;
                window.MinWidth = Math.Min(minimumWidth, window.MaxWidth);
                window.MinHeight = Math.Min(minimumHeight, window.MaxHeight);
                window.Width = target.Width / dpi.DpiScaleX;
                window.Height = target.Height / dpi.DpiScaleY;
                SetWindowPos(handle, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, SwpNoZOrder | SwpNoActivate);
            }
            finally { fitting = false; }
        }
    }

    /// <summary>Pure calculation: owner/work-area rectangles are physical pixels; desired size is WPF DIPs.</summary>
    public static Rectangle FitCentered(Rectangle owner, Rectangle workingArea,
        double width, double height, double dpiScaleX, double dpiScaleY)
    {
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0
            || !double.IsFinite(dpiScaleX) || dpiScaleX <= 0 || !double.IsFinite(dpiScaleY) || dpiScaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Dialog dimensions and DPI scales must be finite and positive.");
        var pixelWidth = (int)Math.Clamp(Math.Ceiling(width * dpiScaleX), 1, int.MaxValue);
        var pixelHeight = (int)Math.Clamp(Math.Ceiling(height * dpiScaleY), 1, int.MaxValue);
        return WindowPlacement.Fit(new Rectangle(owner.Left + (owner.Width - pixelWidth) / 2,
            owner.Top + (owner.Height - pixelHeight) / 2, pixelWidth, pixelHeight), workingArea);
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
}
