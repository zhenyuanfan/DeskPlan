using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeskPlan.Models;

public enum WorkStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Skipped = 3
}

public enum RepeatKind
{
    None = 0,
    Daily = 1,
    Weekdays = 2,
    SelectedDays = 3
}

public sealed class TaskItem : INotifyPropertyChanged
{
    private WorkStatus _status;

    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? ScheduledDate { get; set; }
    public DateOnly DisplayDate { get; set; }
    public int? StartMinutes { get; set; }
    public int? EndMinutes { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public RepeatKind RepeatKind { get; set; }
    public string RepeatDays { get; set; } = string.Empty;
    public DateOnly? RepeatStartDate { get; set; }
    public bool IsOccurrence { get; set; }

    public WorkStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsDone => Status is WorkStatus.Completed or WorkStatus.Skipped;
    public bool HasTime => StartMinutes.HasValue;
    public bool IsRecurring => RepeatKind != RepeatKind.None;
    public bool IsMultiDay => !IsRecurring && ScheduledDate.HasValue && DueDate > ScheduledDate;
    public string DateRangeText => IsMultiDay ? $"{ScheduledDate:MM/dd} – {DueDate:MM/dd}" : string.Empty;
    public string CalendarTimeText => IsMultiDay
        ? (DisplayDate == ScheduledDate ? "开始" : DisplayDate == DueDate ? "截止" : "持续")
        : StartMinutes.HasValue ? FormatMinutes(StartMinutes.Value) : string.Empty;

    public string TimeText
    {
        get
        {
            if (!StartMinutes.HasValue) return string.Empty;
            var start = FormatMinutes(StartMinutes.Value);
            return EndMinutes.HasValue ? $"{start}–{FormatMinutes(EndMinutes.Value)}" : start;
        }
    }

    public string SecondaryText
    {
        get
        {
            var parts = new List<string>();
            if (HasTime) parts.Add(TimeText);
            if (DueDate.HasValue) parts.Add($"截止 {DueDate:MM-dd}");
            if (IsRecurring) parts.Add(RepeatText);
            return string.Join("  ·  ", parts);
        }
    }

    public string RepeatText => RepeatKind switch
    {
        RepeatKind.Daily => "每天",
        RepeatKind.Weekdays => "工作日",
        RepeatKind.SelectedDays => "每周指定日期",
        _ => string.Empty
    };

    public string StatusText => Status switch
    {
        WorkStatus.InProgress => "进行中",
        WorkStatus.Completed => "已完成",
        WorkStatus.Skipped => "已跳过",
        _ => "待办"
    };

    public static string FormatMinutes(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    public TaskItem Clone() => (TaskItem)MemberwiseClone();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
