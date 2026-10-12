using DeskPlan.Services;

namespace DeskPlan.Tests;

public class ChineseHolidayCalendarTests
{
    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(2, 15, 23)]
    [InlineData(4, 4, 6)]
    [InlineData(5, 1, 5)]
    [InlineData(6, 19, 21)]
    [InlineData(9, 25, 27)]
    [InlineData(10, 1, 7)]
    public void OfficialHolidayRangesIncludeBothEnds(int month, int first, int last)
    {
        for (var day = first; day <= last; day++)
        {
            var date = new DateOnly(2026, month, day);
            Assert.True(ChineseHolidayCalendar.IsHoliday(date));
            Assert.False(ChineseHolidayCalendar.IsWorkingDay(date));
        }
        Assert.False(ChineseHolidayCalendar.IsHoliday(new DateOnly(2026, month, first).AddDays(-1)));
        Assert.False(ChineseHolidayCalendar.IsHoliday(new DateOnly(2026, month, last).AddDays(1)));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 14)]
    [InlineData(2, 28)]
    [InlineData(5, 9)]
    [InlineData(9, 20)]
    [InlineData(10, 10)]
    public void OfficialMakeupWeekendsAreWorkingDays(int month, int day)
    {
        var date = new DateOnly(2026, month, day);
        Assert.True(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.True(ChineseHolidayCalendar.IsWorkingDay(date));
        Assert.False(ChineseHolidayCalendar.IsHoliday(date));
    }

    [Fact]
    public void UnknownYearIsNotInventedOrCopiedFromPreviousYear()
    {
        Assert.False(ChineseHolidayCalendar.HasYear(2027));
        Assert.Null(ChineseHolidayCalendar.GetAdjustment(new DateOnly(2027, 10, 1)));
        Assert.Contains("待更新", ChineseHolidayCalendar.CoverageText(2027));
    }
}
