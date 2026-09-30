using System.Drawing;

namespace DeskPlan.Services;

public static class DayPreviewLayout
{
    // Coordinates use the overlay's logical units, not physical screen pixels.
    public static RectangleF Place(RectangleF day, SizeF viewport, SizeF desired)
    {
        const float gap = 12;
        var inset = Math.Max(0, Math.Min(gap, (Math.Min(viewport.Width, viewport.Height) - 1) / 2));
        var width = Math.Clamp(desired.Width, 1, Math.Max(1, viewport.Width - 2 * inset));
        var height = Math.Clamp(desired.Height, 1, Math.Max(1, viewport.Height - 2 * inset));
        var right = viewport.Width - inset - width;
        var bottom = viewport.Height - inset - height;
        var x = day.Right + gap <= right ? day.Right + gap
            : day.Left - gap - width >= inset ? day.Left - gap - width : day.Left;
        return new RectangleF(Math.Clamp(x, inset, Math.Max(inset, right)),
            Math.Clamp(day.Top, inset, Math.Max(inset, bottom)), width, height);
    }
}
