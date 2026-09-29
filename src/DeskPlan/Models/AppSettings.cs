namespace DeskPlan.Models;

public sealed class AppSettings
{
    public int PositionSchema { get; set; }
    public string PreferredScreen { get; set; } = string.Empty;
    public int PixelLeft { get; set; } = int.MinValue;
    public int PixelTop { get; set; } = int.MinValue;
    public int? CompactLeft { get; set; }
    public int? CompactTop { get; set; }
    public int? ExpandedLeft { get; set; }
    public int? ExpandedTop { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double ExpandedWidth { get; set; } = 1080;
    public double ExpandedHeight { get; set; } = 700;
    public int WorkStartHour { get; set; } = 8;
    public int WorkEndHour { get; set; } = 22;
    public bool StartWithWindows { get; set; } = true;
    public bool StartExpanded { get; set; }
}
