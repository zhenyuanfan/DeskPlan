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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
