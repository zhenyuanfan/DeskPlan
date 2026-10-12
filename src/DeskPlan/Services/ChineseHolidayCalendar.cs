using System.Collections.Frozen;

namespace DeskPlan.Services;

public sealed record HolidayAdjustment(string Name, bool IsWorkingDay);

public static class ChineseHolidayCalendar
{
    // State Council notice, 国办发明电〔2025〕7号. Keep official compensatory
    // days off and makeup workdays together; lunar holiday dates must not be guessed.
    public const string SourceUrl = "https://www.beijing.gov.cn/zhengce/zhengcefagui/202511/t20251104_4258873.html";
    public const string SupportedYears = "2026";
    private static readonly FrozenDictionary<DateOnly, HolidayAdjustment> Adjustments = Build();

    public static bool HasYear(int year) => year == 2026;

    public static HolidayAdjustment? GetAdjustment(DateOnly date) => Adjustments.GetValueOrDefault(date);

    public static bool IsHoliday(DateOnly date) => GetAdjustment(date) is { IsWorkingDay: false };

    public static bool IsWorkingDay(DateOnly date) => GetAdjustment(date)?.IsWorkingDay
        ?? (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);

    public static string CoverageText(int year) => HasYear(year)
        ? $"{year} 放假 / 调休日历已内置"
        : $"{year} 节假日待更新，暂按原重复规则";

    private static FrozenDictionary<DateOnly, HolidayAdjustment> Build()
    {
        var days = new Dictionary<DateOnly, HolidayAdjustment>();
        AddHoliday("元旦", new(2026, 1, 1), new(2026, 1, 3));
        AddHoliday("春节", new(2026, 2, 15), new(2026, 2, 23));
        AddHoliday("清明节", new(2026, 4, 4), new(2026, 4, 6));
        AddHoliday("劳动节", new(2026, 5, 1), new(2026, 5, 5));
        AddHoliday("端午节", new(2026, 6, 19), new(2026, 6, 21));
        AddHoliday("中秋节", new(2026, 9, 25), new(2026, 9, 27));
        AddHoliday("国庆节", new(2026, 10, 1), new(2026, 10, 7));
        AddWorkday("元旦", new(2026, 1, 4));
        AddWorkday("春节", new(2026, 2, 14));
        AddWorkday("春节", new(2026, 2, 28));
        AddWorkday("劳动节", new(2026, 5, 9));
        AddWorkday("国庆节", new(2026, 9, 20));
        AddWorkday("国庆节", new(2026, 10, 10));
        return days.ToFrozenDictionary();

        void AddHoliday(string name, DateOnly first, DateOnly last)
        {
            for (var date = first; date <= last; date = date.AddDays(1))
                days.Add(date, new HolidayAdjustment(name, false));
        }

        void AddWorkday(string name, DateOnly date) => days.Add(date, new HolidayAdjustment(name, true));
    }
}
