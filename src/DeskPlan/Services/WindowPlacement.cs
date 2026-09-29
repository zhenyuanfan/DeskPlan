using System.Drawing;

namespace DeskPlan.Services;

/// <summary>All values are physical pixels, including on monitors left of the primary display.</summary>
public static class WindowPlacement
{
    public static Rectangle Fit(Rectangle requested, Rectangle workingArea, int margin = 8)
    {
        var inset = Math.Max(0, Math.Min(margin, (Math.Min(workingArea.Width, workingArea.Height) - 1) / 2));
        var available = Rectangle.Inflate(workingArea, -inset, -inset);
        var width = Math.Clamp(requested.Width, 1, available.Width);
        var height = Math.Clamp(requested.Height, 1, available.Height);
        return new Rectangle(
            Math.Clamp(requested.X, available.Left, available.Right - width),
            Math.Clamp(requested.Y, available.Top, available.Bottom - height), width, height);
    }
}
