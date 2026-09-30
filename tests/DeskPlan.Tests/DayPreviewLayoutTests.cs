using System.Drawing;
using DeskPlan.Services;

namespace DeskPlan.Tests;

public class DayPreviewLayoutTests
{
    [Fact]
    public void LeftDayOpensOnRight()
    {
        var result = DayPreviewLayout.Place(new RectangleF(0, 120, 154, 100), new SizeF(1080, 700), new SizeF(420, 540));
        Assert.Equal(166, result.X);
        Assert.Equal(120, result.Y);
    }

    [Fact]
    public void RightDayOpensOnLeft()
    {
        var result = DayPreviewLayout.Place(new RectangleF(925, 120, 154, 100), new SizeF(1080, 700), new SizeF(420, 540));
        Assert.Equal(493, result.X);
    }

    [Theory]
    [InlineData(0, 0, 1080, 700)]
    [InlineData(925, 580, 1080, 700)]
    [InlineData(350, 300, 760, 480)]
    [InlineData(200, 200, 320, 240)]
    public void CardStaysInsideWindow(float x, float y, float width, float height)
    {
        var result = DayPreviewLayout.Place(new RectangleF(x, y, 150, 100), new SizeF(width, height), new SizeF(420, 540));
        Assert.True(new RectangleF(0, 0, width, height).Contains(result));
        Assert.True(result.Width > 0 && result.Height > 0);
    }

    [Fact]
    public void EmptyDayUsesContentHeightRatherThanFullHeight()
    {
        var result = DayPreviewLayout.Place(new RectangleF(900, 560, 150, 100), new SizeF(1080, 700), new SizeF(420, 180));
        Assert.Equal(180, result.Height);
        Assert.Equal(508, result.Top);
    }
}
