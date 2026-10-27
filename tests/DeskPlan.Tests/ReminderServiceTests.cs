using DeskPlan.Models;
using DeskPlan.Services;

namespace DeskPlan.Tests;

public sealed class ReminderServiceTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private readonly string _directory;
    private readonly string _databasePath;
    private readonly TaskRepository _repository;
    private readonly ReminderService _reminders;
    private readonly List<(string Title, string Message)> _notifications = [];

    public ReminderServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DeskPlanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "reminders.db");
        _repository = new TaskRepository(_databasePath);
        _repository.Initialize();
        _reminders = new ReminderService(_repository);
    }

    [Fact]
    public void WakeAfterSeveralStartTimes_SendsOneSummaryAndRecordsAllDueTasks()
    {
        var later = Add("准备午间材料", Monday, 11 * 60);
        var earlier = Add("整理早间安排", Monday, 9 * 60);
        var future = Add("下午例行安排", Monday, 15 * 60);

        Assert.Equal(2, Check(Monday, 12 * 60));

        var notification = Assert.Single(_notifications);
        Assert.Contains("2 项", notification.Title);
        Assert.StartsWith("09:00  整理早间安排", notification.Message);
        Assert.Contains("11:00  准备午间材料", notification.Message);
        Assert.DoesNotContain("下午例行安排", notification.Message);
        Assert.True(_repository.WasNotified(earlier, Monday));
        Assert.True(_repository.WasNotified(later, Monday));
        Assert.False(_repository.WasNotified(future, Monday));
    }

    [Fact]
    public void LaterPollingClockRollbackAndRestart_DoNotRepeatDeliveredReminders()
    {
        Add("上午安排", Monday, 9 * 60);
        Assert.Equal(1, Check(Monday, 10 * 60));
        Assert.Equal(0, Check(Monday, 10 * 60 + 1));
        Assert.Equal(0, Check(Monday, 9 * 60));

        var reopened = new ReminderService(new TaskRepository(_databasePath));
        Assert.Equal(0, reopened.CheckAndNotify(At(Monday, 12 * 60), Notify));
        Assert.Single(_notifications);
    }

    [Fact]
    public void NotYetDueTask_IsNotNotifiedEarlyAndIsNotifiedAtItsStartMinute()
    {
        var id = Add("准时安排", Monday, 10 * 60);
        Assert.Equal(0, Check(Monday, 10 * 60 - 1));
        Assert.False(_repository.WasNotified(id, Monday));
        Assert.Empty(_notifications);

        Assert.Equal(1, Check(Monday, 10 * 60));
        Assert.Equal("该开始了", Assert.Single(_notifications).Title);
        Assert.Equal(0, Check(Monday, 10 * 60));
    }

    [Fact]
    public void FirstCheckAfterStartup_CatchesEarlierUnnotifiedTask()
    {
        Add("启动前的安排", Monday, 9 * 60);

        Assert.Equal(1, Check(Monday, 10 * 60));

        Assert.Equal("补个提醒", Assert.Single(_notifications).Title);
    }

    [Fact]
    public void FinishedSkippedUntimedDeletedAndFutureTasks_AreNotIncluded()
    {
        Add("已完成", Monday, 9 * 60, WorkStatus.Completed);
        Add("已跳过", Monday, 9 * 60, WorkStatus.Skipped);
        Add("没有时间", Monday, null);
        Add("尚未到时间", Monday, 18 * 60);
        var deleted = Add("已删除", Monday, 9 * 60);
        _repository.Delete(_repository.GetTasksForDate(Monday).Single(task => task.Id == deleted));
        var active = Add("进行中的安排", Monday, 9 * 60, WorkStatus.InProgress);

        Assert.Equal(1, Check(Monday, 12 * 60));

        Assert.Contains("进行中的安排", Assert.Single(_notifications).Message);
        Assert.True(_repository.WasNotified(active, Monday));
        Assert.False(_repository.WasNotified(deleted, Monday));
    }

    [Fact]
    public void WakeOnAnotherDay_OnlyChecksThatDayAndDoesNotBackfillYesterday()
    {
        var yesterday = Add("昨天未提醒的安排", Monday.AddDays(-1), 9 * 60);
        var tomorrow = Add("明天的安排", Monday.AddDays(1), 9 * 60);
        var today = Add("今天的安排", Monday, 9 * 60);

        Assert.Equal(1, Check(Monday, 12 * 60));

        Assert.Contains("今天的安排", Assert.Single(_notifications).Message);
        Assert.False(_repository.WasNotified(yesterday, Monday.AddDays(-1)));
        Assert.False(_repository.WasNotified(tomorrow, Monday.AddDays(1)));
        Assert.True(_repository.WasNotified(today, Monday));
    }

    [Fact]
    public void RecurringTask_HasIndependentReminderRecordForEachDay()
    {
        var id = _repository.Add(new TaskItem
        {
            Title = "每日安排", RepeatKind = RepeatKind.Daily,
            RepeatStartDate = Monday.AddDays(-1), StartMinutes = 9 * 60
        });

        Assert.Equal(1, Check(Monday, 12 * 60));
        Assert.Equal(1, Check(Monday.AddDays(1), 12 * 60));

        Assert.Equal(2, _notifications.Count);
        Assert.True(_repository.WasNotified(id, Monday));
        Assert.True(_repository.WasNotified(id, Monday.AddDays(1)));
        Assert.False(_repository.WasNotified(id, Monday.AddDays(-1)));
    }

    [Fact]
    public void Holiday_StillSkipsAutomaticTasksButIncludesManualArrangements()
    {
        var holiday = new DateOnly(2026, 10, 1);
        var recurring = _repository.Add(new TaskItem
        {
            Title = "自动重复安排", RepeatKind = RepeatKind.Daily,
            RepeatStartDate = holiday.AddDays(-1), StartMinutes = 9 * 60
        });
        var manual = Add("假期手动安排", holiday, 9 * 60);

        Assert.Equal(1, Check(holiday, 12 * 60));

        Assert.Contains("假期手动安排", Assert.Single(_notifications).Message);
        Assert.False(_repository.WasNotified(recurring, holiday));
        Assert.True(_repository.WasNotified(manual, holiday));
    }

    [Fact]
    public void FailedNotification_DoesNotConsumeDueTasksAndCanBeRetried()
    {
        var first = Add("第一项安排", Monday, 9 * 60);
        var second = Add("第二项安排", Monday, 10 * 60);
        Assert.Throws<InvalidOperationException>(() =>
            _reminders.CheckAndNotify(At(Monday, 12 * 60), (_, _) => throw new InvalidOperationException()));

        Assert.False(_repository.WasNotified(first, Monday));
        Assert.False(_repository.WasNotified(second, Monday));
        Assert.Equal(2, Check(Monday, 12 * 60));
        Assert.Single(_notifications);
    }

    [Fact]
    public void NotificationRecords_AreWrittenOnlyAfterNotificationIsHandedOff()
    {
        var id = Add("稍后记录提醒状态", Monday, 9 * 60);

        _reminders.CheckAndNotify(At(Monday, 12 * 60), (_, _) =>
            Assert.False(_repository.WasNotified(id, Monday)));

        Assert.True(_repository.WasNotified(id, Monday));
    }

    [Fact]
    public void ManyLongTasks_ProduceOneShortSingleLinePerTaskPreview()
    {
        var ids = Enumerable.Range(0, 8)
            .Select(i => Add($"安排 {i}\r\n" + new string('长', 300), Monday, 9 * 60 + i))
            .ToList();

        Assert.Equal(8, Check(Monday, 12 * 60));

        var notification = Assert.Single(_notifications);
        Assert.Contains("8 项", notification.Title);
        Assert.Contains("还有 5 项", notification.Message);
        Assert.Equal(4, notification.Message.Split(Environment.NewLine).Length);
        Assert.True(notification.Message.Length < 255);
        Assert.All(ids, id => Assert.True(_repository.WasNotified(id, Monday)));
    }

    [Fact]
    public void InterruptedPolling_CatchesTheGapOnceAndStillWaitsForFutureTasks()
    {
        Add("暂停期间的第一项", Monday, 9 * 60);
        Add("暂停期间的第二项", Monday, 10 * 60);
        Add("恢复以后的安排", Monday, 13 * 60);

        Assert.Equal(0, Check(Monday, 8 * 60));
        // No checks occur while the machine is asleep; the first resumed tick uses
        // the new wall-clock time, without needing a previous-tick timestamp.
        Assert.Equal(2, Check(Monday, 12 * 60));
        Assert.Equal(0, Check(Monday, 12 * 60 + 1));
        Assert.Equal(1, Check(Monday, 13 * 60));

        Assert.Equal(2, _notifications.Count);
        Assert.Contains("2 项", _notifications[0].Title);
        Assert.DoesNotContain("恢复以后的安排", _notifications[0].Message);
        Assert.Contains("恢复以后的安排", _notifications[1].Message);
    }

    [Fact]
    public void TaskEditedBeforeCatchUp_UsesLatestTitleAndTime()
    {
        Add("原来的标题", Monday, 9 * 60);
        Assert.Equal(0, Check(Monday, 8 * 60));
        var item = Assert.Single(_repository.GetTasksForDate(Monday));
        item.Title = "修改后的标题";
        item.StartMinutes = 14 * 60;
        _repository.UpdateTask(item);

        Assert.Equal(0, Check(Monday, 12 * 60));
        Assert.Equal(1, Check(Monday, 14 * 60));

        var notification = Assert.Single(_notifications);
        Assert.Contains("14:00  修改后的标题", notification.Message);
        Assert.DoesNotContain("原来的标题", notification.Message);
    }

    [Fact]
    public void TaskMovedToTomorrowBeforeCatchUp_IsNotNotifiedOnItsOldDate()
    {
        var id = Add("改到明天的安排", Monday, 9 * 60);
        Assert.Equal(0, Check(Monday, 8 * 60));
        var item = Assert.Single(_repository.GetTasksForDate(Monday));
        item.ScheduledDate = Monday.AddDays(1);
        _repository.UpdateTask(item);

        Assert.Equal(0, Check(Monday, 12 * 60));
        Assert.False(_repository.WasNotified(id, Monday));
        Assert.Equal(1, Check(Monday.AddDays(1), 12 * 60));
        Assert.True(_repository.WasNotified(id, Monday.AddDays(1)));
        Assert.Single(_notifications);
    }

    [Fact]
    public void AlreadyNotifiedTask_EditedOnSameDayKeepsExistingDailyDeduplication()
    {
        Add("当天只提醒一次的安排", Monday, 9 * 60);
        Assert.Equal(1, Check(Monday, 9 * 60));
        var item = Assert.Single(_repository.GetTasksForDate(Monday));
        item.StartMinutes = 15 * 60;
        item.Title = "修改后的当天安排";
        _repository.UpdateTask(item);

        Assert.Equal(0, Check(Monday, 15 * 60));
        Assert.Single(_notifications);
    }

    [Fact]
    public void RecurringOccurrenceEditedBeforeCatchUp_UsesTodayOverrideOnly()
    {
        _repository.Add(new TaskItem
        {
            Title = "默认重复安排", RepeatKind = RepeatKind.Daily,
            RepeatStartDate = Monday, StartMinutes = 9 * 60
        });
        Assert.Equal(0, Check(Monday, 8 * 60));
        var occurrence = Assert.Single(_repository.GetTasksForDate(Monday));
        occurrence.StartMinutes = 15 * 60;
        occurrence.Title = "当天临时改期";
        _repository.UpdateTask(occurrence);

        Assert.Equal(0, Check(Monday, 12 * 60));
        Assert.Equal(1, Check(Monday, 15 * 60));
        Assert.Contains("15:00  当天临时改期", _notifications[0].Message);
        Assert.Equal(1, Check(Monday.AddDays(1), 9 * 60));
        Assert.Contains("09:00  默认重复安排", _notifications[1].Message);
    }

    [Fact]
    public void RecurringOccurrencesDeletedOrCompletedBetweenChecks_AreNotReminded()
    {
        var deleted = _repository.Add(new TaskItem
        {
            Title = "当天删除的重复安排", RepeatKind = RepeatKind.Daily,
            RepeatStartDate = Monday, StartMinutes = 9 * 60
        });
        var completed = _repository.Add(new TaskItem
        {
            Title = "当天完成的重复安排", RepeatKind = RepeatKind.Daily,
            RepeatStartDate = Monday, StartMinutes = 9 * 60
        });
        Assert.Equal(0, Check(Monday, 8 * 60));
        var items = _repository.GetTasksForDate(Monday);
        _repository.Delete(items.Single(item => item.Id == deleted));
        _repository.UpdateStatus(items.Single(item => item.Id == completed), WorkStatus.Completed);

        Assert.Equal(0, Check(Monday, 12 * 60));
        Assert.False(_repository.WasNotified(deleted, Monday));
        Assert.False(_repository.WasNotified(completed, Monday));
        Assert.Empty(_notifications);
        Assert.Equal(2, Check(Monday.AddDays(1), 12 * 60));
        Assert.Single(_notifications);
    }

    [Fact]
    public void MidnightResume_DoesNotReplayYesterdayAndIncludesMidnightTask()
    {
        var yesterday = Add("午夜前错过的安排", Monday, 23 * 60 + 59);
        var nextDay = Monday.AddDays(1);
        var midnight = Add("零点的安排", nextDay, 0);
        var later = Add("零点后一小时的安排", nextDay, 60);
        Assert.Equal(0, Check(Monday, 23 * 60 + 58));

        Assert.Equal(1, Check(nextDay, 0));

        Assert.False(_repository.WasNotified(yesterday, Monday));
        Assert.True(_repository.WasNotified(midnight, nextDay));
        Assert.False(_repository.WasNotified(later, nextDay));
        Assert.Contains("00:00  零点的安排", Assert.Single(_notifications).Message);
    }

    [Fact]
    public void LongEmojiTitle_TruncationDoesNotSplitASurrogatePair()
    {
        Add(new string('字', 53) + "😀后续内容", Monday, 9 * 60);

        Assert.Equal(1, Check(Monday, 12 * 60));

        var message = Assert.Single(_notifications).Message;
        Assert.Contains(new string('字', 53) + "…", message);
        Assert.DoesNotContain(message, char.IsSurrogate);
    }

    private long Add(string title, DateOnly date, int? start, WorkStatus status = WorkStatus.Pending) =>
        _repository.Add(new TaskItem { Title = title, ScheduledDate = date, StartMinutes = start, Status = status });

    private int Check(DateOnly date, int minute) => _reminders.CheckAndNotify(At(date, minute), Notify);

    private static DateTime At(DateOnly date, int minute) => date.ToDateTime(new TimeOnly(minute / 60, minute % 60));

    private void Notify(string title, string message) => _notifications.Add((title, message));

    public void Dispose() => Directory.Delete(_directory, true);
}
