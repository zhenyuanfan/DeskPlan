using DeskPlan.Models;

namespace DeskPlan.Services;

public sealed class ReminderService(TaskRepository repository)
{
    /// <summary>
    /// Checks the whole current day rather than only the current minute, so the first
    /// timer tick after startup or sleep also catches missed reminders. The repository
    /// remains responsible for holiday rules and persistent per-day deduplication.
    /// </summary>
    public int CheckAndNotify(DateTime now, Action<string, string> notify)
    {
        ArgumentNullException.ThrowIfNull(notify);
        var date = DateOnly.FromDateTime(now);
        var minute = now.Hour * 60 + now.Minute;
        var due = repository.GetTasksForDate(date)
            .Where(task => !task.IsDone && task.StartMinutes is >= 0 && task.StartMinutes <= minute)
            .Where(task => !repository.WasNotified(task.Id, date))
            .OrderBy(task => task.StartMinutes)
            .ThenBy(task => task.Id)
            .ToList();
        if (due.Count == 0) return 0;

        var title = due.Count > 1 ? $"今天有 {due.Count} 项安排已到时间"
            : due[0].StartMinutes < minute ? "补个提醒" : "该开始了";
        var lines = due.Take(3)
            .Select(task => $"{TaskItem.FormatMinutes(task.StartMinutes!.Value)}  {PreviewTitle(task.Title)}")
            .ToList();
        lines.Add(due.Count > 3 ? $"还有 {due.Count - 3} 项，打开桌面计划查看。" : "打开桌面计划查看。");

        // One system notification per check prevents a burst after waking. Do not
        // consume reminders if handing the notification to the system fails.
        notify(title, string.Join(Environment.NewLine, lines));
        foreach (var task in due) repository.MarkNotified(task.Id, date);
        return due.Count;
    }

    private static string PreviewTitle(string title)
    {
        var singleLine = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int limit = 54;
        if (singleLine.Length <= limit) return singleLine;
        var end = char.IsHighSurrogate(singleLine[limit - 1]) ? limit - 1 : limit;
        return singleLine[..end] + "…";
    }
}
