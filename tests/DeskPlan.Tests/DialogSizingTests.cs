using System.Drawing;
using DeskPlan.Services;

namespace DeskPlan.Tests;

public class DialogSizingTests
{
    [Fact]
    public void NormalScreen_PreservesPreferredSizeAndCentersOnOwner()
    {
        var result = DialogSizing.FitCentered(new Rectangle(300, 100, 1080, 700),
            new Rectangle(0, 0, 1920, 1040), 500, 650, 1, 1);
        Assert.Equal(new Rectangle(590, 125, 500, 650), result);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SmallScreenAtHighDpi_KeepsDialogAndButtonsWithinWorkingArea(double scale)
    {
        var area = new Rectangle(0, 0, 1366, 728);
        var result = DialogSizing.FitCentered(new Rectangle(950, 8, 380, 360), area, 500, 650, scale, scale);
        Assert.True(area.Contains(result));
        Assert.Equal(712, result.Height);
        Assert.Equal(8, result.Top);
        Assert.True(result.Height / scale < 650);
    }

    [Fact]
    public void NegativeCoordinateMonitor_StaysOnOwnersScreen()
    {
        var area = new Rectangle(-1600, -200, 1600, 860);
        var result = DialogSizing.FitCentered(new Rectangle(-200, -150, 190, 380), area, 500, 650, 1.5, 1.5);
        Assert.True(area.Contains(result));
        Assert.Equal(new Rectangle(-758, -192, 750, 844), result);
    }

    [Theory]
    [InlineData(460, 520)]
    [InlineData(500, 650)]
    [InlineData(640, 460)]
    public void SideTaskbar_UsesWorkingAreaNotFullScreen(double width, double height)
    {
        var area = new Rectangle(64, 0, 1302, 728);
        var result = DialogSizing.FitCentered(new Rectangle(64, 20, 380, 360), area, width, height, 1, 1);
        Assert.True(area.Contains(result));
        Assert.True(result.Left >= 72);
        Assert.True(result.Top >= 8);
        Assert.Equal((int)width, result.Width);
        Assert.Equal((int)height, result.Height);
    }

    [Fact]
    public void ExtremelySmallWorkArea_StillReturnsPositiveContainedBounds()
    {
        var area = new Rectangle(100, 100, 9, 7);
        var result = DialogSizing.FitCentered(new Rectangle(100, 100, 1, 1), area, 500, 650, 2, 2);
        Assert.True(area.Contains(result));
        Assert.True(result.Width > 0 && result.Height > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidDpi(double dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DialogSizing.FitCentered(new Rectangle(0, 0, 380, 360),
            new Rectangle(0, 0, 1920, 1040), 500, 650, dpi, 1));
    }
}
