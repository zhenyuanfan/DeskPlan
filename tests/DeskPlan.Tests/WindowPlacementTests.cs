using System.Drawing;
using DeskPlan.Services;

namespace DeskPlan.Tests;

public class WindowPlacementTests
{
    [Theory]
    [InlineData(1532, 8, 1160, 720, 0, 0, 1920, 1040, 752, 8, 1160, 720)]
    [InlineData(1532, 672, 1160, 720, 0, 0, 1920, 1040, 752, 312, 1160, 720)]
    [InlineData(-388, 8, 1160, 720, -1920, 0, 1920, 1040, -1168, 8, 1160, 720)]
    [InlineData(1928, -1000, 1160, 720, 1920, -1080, 1920, 1040, 1928, -1000, 1160, 720)]
    [InlineData(200, 200, 2320, 1440, 0, 0, 1366, 728, 8, 8, 1350, 712)]
    [InlineData(9999, -9999, 380, 360, 0, 0, 1920, 1040, 1532, 8, 380, 360)]
    [InlineData(300, 200, 1080, 700, 0, 0, 1920, 1040, 300, 200, 1080, 700)]
    [InlineData(0, 0, 380, 360, 48, 0, 1872, 1080, 56, 8, 380, 360)]
    public void FitsWithoutCrossingWorkingArea(int x, int y, int width, int height,
        int areaX, int areaY, int areaWidth, int areaHeight, int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        var area = new Rectangle(areaX, areaY, areaWidth, areaHeight);
        var actual = WindowPlacement.Fit(new Rectangle(x, y, width, height), area);
        Assert.Equal(new Rectangle(expectedX, expectedY, expectedWidth, expectedHeight), actual);
        Assert.True(area.Contains(actual));
    }

    [Fact]
    public void ExpandingDoesNotChangeSavedCompactAnchor()
    {
        var area = new Rectangle(0, 0, 1920, 1040);
        var compact = new Rectangle(1532, 8, 380, 360);
        var expanded = WindowPlacement.Fit(new Rectangle(compact.Location, new Size(1160, 720)), area);
        Assert.Equal(752, expanded.Left);
        Assert.Equal(compact, WindowPlacement.Fit(compact, area));
    }

    [Fact]
    public void FitIsIdempotent()
    {
        var area = new Rectangle(-1600, -200, 1600, 860);
        var once = WindowPlacement.Fit(new Rectangle(-100, 700, 2000, 1400), area);
        Assert.Equal(once, WindowPlacement.Fit(once, area));
    }
}
