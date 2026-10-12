using DeskPlan.Models;
using DeskPlan.Services;

namespace DeskPlan.Tests;

public sealed class TaskRepositoryTests : IDisposable
{
    private readonly string _directory;
    private readonly TaskRepository _repository;
    private static readonly DateOnly Monday = new(2026, 9, 21);

    public TaskRepositoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DeskPlanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _repository = new TaskRepository(Path.Combine(_directory, "test.db"));
        _repository.Initialize();
    }

    [Fact]
    public void QuickAdd_PersistsOnSelectedDate()
    {
        _repository.AddQuick("整理部署文档", Monday);

        var task = Assert.Single(_repository.GetTasksForDate(Monday));
        Assert.Equal("整理部署文档", task.Title);
        Assert.Equal(WorkStatus.Pending, task.Status);
        Assert.Empty(_repository.GetTasksForDate(Monday.AddDays(1)));
    }

    [Fact]
    public void Scheduling_PersistsStartAndEndTime()
    {
        _repository.AddQuick("回复邮件", Monday);
        var task = Assert.Single(_repository.GetTasksForDate(Monday));

        _repository.Schedule(task, Monday, 10 * 60 + 30, 60);

        var scheduled = Assert.Single(_repository.GetTasksForDate(Monday));
        Assert.Equal(630, scheduled.StartMinutes);
        Assert.Equal(690, scheduled.EndMinutes);
    }

    [Fact]
    public void DailyOccurrence_StatusIsIndependentPerDay()
    {
        _repository.Add(new TaskItem
        {
            Title = "每日复盘",
            RepeatKind = RepeatKind.Daily,
            RepeatStartDate = Monday,
            StartMinutes = 18 * 60,
            EndMinutes = 18 * 60 + 30
        });
        var mondayTask = Assert.Single(_repository.GetTasksForDate(Monday));

        _repository.UpdateStatus(mondayTask, WorkStatus.Completed);

        Assert.Equal(WorkStatus.Completed, Assert.Single(_repository.GetTasksForDate(Monday)).Status);
        Assert.Equal(WorkStatus.Pending, Assert.Single(_repository.GetTasksForDate(Monday.AddDays(1))).Status);
    }

    [Fact]
    public void SelectedWeekdays_OnlyAppearOnChosenDays()
    {
        _repository.Add(new TaskItem
        {
            Title = "周一周三事项",
            RepeatKind = RepeatKind.SelectedDays,
            RepeatDays = "1,3",
            RepeatStartDate = Monday
        });

        Assert.Single(_repository.GetTasksForDate(Monday));
        Assert.Empty(_repository.GetTasksForDate(Monday.AddDays(1)));
        Assert.Single(_repository.GetTasksForDate(Monday.AddDays(2)));
    }

    [Fact]
    public void ReviewedOverdueTask_StopsAppearingInReview()
    {
        _repository.AddQuick("昨天没做完", Monday.AddDays(-1));
        var overdue = Assert.Single(_repository.GetOverdue(Monday));

        _repository.MarkReviewed(overdue);

        Assert.Empty(_repository.GetOverdue(Monday));
        Assert.Single(_repository.GetTasksForDate(Monday.AddDays(-1)));
    }

    [Fact]
    public void LaterTask_IsKeptOutsideDailyList()
    {
        _repository.Add(new TaskItem { Title = "以后再处理", ScheduledDate = null });

        Assert.Single(_repository.GetLaterTasks());
        Assert.Empty(_repository.GetTasksForDate(Monday));
    }

    [Theory]
    [InlineData(2026, 9, 21)]
    [InlineData(2026, 9, 30)]
    [InlineData(2026, 12, 31)]
    public void ThreeDayTask_AppearsOnEveryDayIncludingEnd(int year, int month, int day)
    {
        var start = new DateOnly(year, month, day);
        var id = _repository.Add(new TaskItem { Title = "三天工作", ScheduledDate = start, DueDate = start.AddDays(2) });
        for (var i = 0; i < 3; i++)
        {
            var task = Assert.Single(_repository.GetTasksForDate(start.AddDays(i)));
            Assert.Equal(id, task.Id);
            Assert.Equal(start.AddDays(i), task.DisplayDate);
            Assert.True(task.IsMultiDay);
        }
        Assert.Empty(_repository.GetTasksForDate(start.AddDays(-1)));
        Assert.Empty(_repository.GetTasksForDate(start.AddDays(3)));
        Assert.Empty(_repository.GetOverdue(start.AddDays(2)));
        Assert.Single(_repository.GetOverdue(start.AddDays(3)));
    }

    [Fact]
    public void MultiDayTask_CompletionEditingAndDeletionApplyToOneSharedTask()
    {
        _repository.Add(new TaskItem { Title = "跨日任务", ScheduledDate = Monday, DueDate = Monday.AddDays(2) });
        var middle = Assert.Single(_repository.GetTasksForDate(Monday.AddDays(1)));
        _repository.UpdateStatus(middle, WorkStatus.Completed);
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(WorkStatus.Completed,
            Assert.Single(_repository.GetTasksForDate(Monday.AddDays(i))).Status));
        middle.Title = "修改后的任务";
        middle.DueDate = Monday.AddDays(3);
        _repository.UpdateTask(middle);
        Assert.Equal("修改后的任务", Assert.Single(_repository.GetTasksForDate(Monday.AddDays(3))).Title);
        _repository.Delete(middle);
        Assert.Empty(_repository.GetTasksForDate(Monday));
        Assert.Empty(_repository.GetTasksForDate(Monday.AddDays(3)));
        _repository.Restore(middle);
        Assert.Single(_repository.GetTasksForDate(Monday.AddDays(3)));
    }

    [Fact]
    public void MovingMultiDayTask_PreservesDuration()
    {
        _repository.Add(new TaskItem { Title = "延期三天工作", ScheduledDate = Monday, DueDate = Monday.AddDays(2) });
        var item = Assert.Single(_repository.GetTasksForDate(Monday));
        var newStart = Monday.AddDays(7);
        _repository.MoveToToday(item, newStart);
        Assert.Empty(_repository.GetTasksForDate(Monday));
        Assert.Equal(newStart.AddDays(2), Assert.Single(_repository.GetTasksForDate(newStart.AddDays(2))).DueDate);
        Assert.Empty(_repository.GetTasksForDate(newStart.AddDays(3)));
    }

    [Theory]
    [InlineData(RepeatKind.Daily)]
    [InlineData(RepeatKind.Weekdays)]
    [InlineData(RepeatKind.SelectedDays)]
    public void AutomaticRecurringTasksSkipHolidayAndResumeAfterwards(RepeatKind repeat)
    {
        _repository.Add(new TaskItem
        {
            Title = "自动工作安排", RepeatKind = repeat, RepeatDays = "0,1,2,3,4,5,6",
            RepeatStartDate = new DateOnly(2026, 9, 28), StartMinutes = 9 * 60
        });
        for (var day = 1; day <= 7; day++) Assert.Empty(_repository.GetTasksForDate(new DateOnly(2026, 10, day)));
        Assert.Single(_repository.GetTasksForDate(new DateOnly(2026, 9, 30)));
        Assert.Single(_repository.GetTasksForDate(new DateOnly(2026, 10, 8)));
        Assert.Empty(_repository.GetOverdue(new DateOnly(2026, 10, 8)));
    }

    [Fact]
    public void WorkingDayRepeatIncludesMakeupSaturdayButNotOrdinaryWeekend()
    {
        _repository.Add(new TaskItem
        {
            Title = "工作日安排", RepeatKind = RepeatKind.Weekdays, RepeatStartDate = new DateOnly(2026, 10, 1)
        });
        Assert.Single(_repository.GetTasksForDate(new DateOnly(2026, 10, 10)));
        Assert.Empty(_repository.GetTasksForDate(new DateOnly(2026, 10, 11)));
        Assert.Empty(_repository.GetTasksForDate(new DateOnly(2026, 10, 17)));
    }

    [Fact]
    public void DailyStillIncludesOrdinaryWeekendsAndSelectedDaysKeepTheirWeekdayRule()
    {
        _repository.Add(new TaskItem { Title = "每天", RepeatKind = RepeatKind.Daily, RepeatStartDate = Monday });
        _repository.Add(new TaskItem { Title = "周一", RepeatKind = RepeatKind.SelectedDays, RepeatDays = "1", RepeatStartDate = Monday });
        Assert.Equal("每天", Assert.Single(_repository.GetTasksForDate(new DateOnly(2026, 10, 10))).Title);
        Assert.Equal("每天", Assert.Single(_repository.GetTasksForDate(new DateOnly(2026, 10, 11))).Title);
        Assert.Equal(2, _repository.GetTasksForDate(new DateOnly(2026, 10, 12)).Count);
    }

    [Fact]
    public void ManualHolidayTasksRemainVisibleAndDoNotReenableTheRecurringSeries()
    {
        var holiday = new DateOnly(2026, 10, 1);
        _repository.Add(new TaskItem { Title = "例行工作", RepeatKind = RepeatKind.Daily, RepeatStartDate = Monday });
        _repository.AddQuick("手动假期安排", holiday);
        Assert.Equal("手动假期安排", Assert.Single(_repository.GetTasksForDate(holiday)).Title);
        Assert.Empty(_repository.GetTasksForDate(holiday.AddDays(1)));
        Assert.Single(_repository.GetOverdue(holiday.AddDays(1)));
        Assert.Equal("例行工作", Assert.Single(_repository.GetTasksForDate(new DateOnly(2026, 10, 8))).Title);
    }

    [Theory]
    [InlineData(WorkStatus.Completed)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.Skipped)]
    public void ExistingRecordedHolidayStatusSurvivesCalendarChange(WorkStatus status)
    {
        var holiday = new DateOnly(2026, 10, 1);
        _repository.Add(new TaskItem { Title = "已有记录", RepeatKind = RepeatKind.Daily, RepeatStartDate = Monday });
        var recorded = Assert.Single(_repository.GetTasksForDate(Monday));
        recorded.DisplayDate = holiday;
        _repository.UpdateStatus(recorded, status);
        var reopened = new TaskRepository(Path.Combine(_directory, "test.db"));
        reopened.Initialize();
        Assert.Equal(status, Assert.Single(reopened.GetTasksForDate(holiday)).Status);
        Assert.Empty(reopened.GetTasksForDate(holiday.AddDays(1)));
        reopened.Delete(recorded);
        Assert.Empty(reopened.GetTasksForDate(holiday));
    }

    [Fact]
    public void ExplicitHolidayEditsRemainButSortOnlyRecordsDoNotCreateExceptions()
    {
        var holiday = new DateOnly(2026, 10, 1);
        _repository.Add(new TaskItem { Title = "已有安排", RepeatKind = RepeatKind.Daily, RepeatStartDate = Monday });
        var item = Assert.Single(_repository.GetTasksForDate(Monday));
        item.DisplayDate = holiday;
        _repository.Reorder([item]);
        Assert.Empty(_repository.GetTasksForDate(holiday));
        item.Title = "假期手动调整";
        _repository.UpdateTask(item);
        Assert.Equal(item.Title, Assert.Single(_repository.GetTasksForDate(holiday)).Title);
    }

    [Fact]
    public void UnavailableCalendarYearFallsBackToOriginalRepeatRule()
    {
        _repository.Add(new TaskItem { Title = "工作日", RepeatKind = RepeatKind.Weekdays, RepeatStartDate = Monday });
        Assert.Single(_repository.GetTasksForDate(new DateOnly(2027, 1, 1)));
        Assert.Empty(_repository.GetTasksForDate(new DateOnly(2027, 1, 2)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
