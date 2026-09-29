using DeskPlan.Models;
using Microsoft.Data.Sqlite;

namespace DeskPlan.Services;

public sealed class TaskRepository
{
    private readonly string _connectionString;

    public TaskRepository()
        : this(AppPaths.DatabasePath)
    {
    }

    public TaskRepository(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS tasks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                scheduled_date TEXT NULL,
                start_minutes INTEGER NULL,
                end_minutes INTEGER NULL,
                due_date TEXT NULL,
                notes TEXT NOT NULL DEFAULT '',
                status INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NOT NULL DEFAULT 0,
                repeat_kind INTEGER NOT NULL DEFAULT 0,
                repeat_days TEXT NOT NULL DEFAULT '',
                repeat_start_date TEXT NULL,
                reviewed INTEGER NOT NULL DEFAULT 0,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                source TEXT NOT NULL DEFAULT 'local',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS occurrences (
                task_id INTEGER NOT NULL,
                occurrence_date TEXT NOT NULL,
                status INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NULL,
                override_title TEXT NULL,
                override_start_minutes INTEGER NULL,
                override_end_minutes INTEGER NULL,
                override_notes TEXT NULL,
                reviewed INTEGER NOT NULL DEFAULT 0,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (task_id, occurrence_date),
                FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS notifications (
                task_id INTEGER NOT NULL,
                occurrence_date TEXT NOT NULL,
                notified_at TEXT NOT NULL,
                PRIMARY KEY (task_id, occurrence_date)
            );

            CREATE INDEX IF NOT EXISTS ix_tasks_scheduled_date ON tasks(scheduled_date);
            CREATE INDEX IF NOT EXISTS ix_tasks_repeat ON tasks(repeat_kind, repeat_start_date);
            """;
        command.ExecuteNonQuery();
    }

    public long AddQuick(string title, DateOnly date)
    {
        var order = NextSortOrder(date);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tasks(title, scheduled_date, sort_order, created_at, updated_at)
            VALUES (@title, @date, @sort, @now, @now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("@title", title.Trim());
        command.Parameters.AddWithValue("@date", Iso(date));
        command.Parameters.AddWithValue("@sort", order);
        command.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("O"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public long Add(TaskItem item)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tasks(
                title, scheduled_date, start_minutes, end_minutes, due_date, notes, status,
                sort_order, repeat_kind, repeat_days, repeat_start_date, created_at, updated_at)
            VALUES(
                @title, @scheduled, @start, @end, @due, @notes, @status,
                @sort, @repeat, @days, @repeatStart, @now, @now);
            SELECT last_insert_rowid();
            """;
        BindTask(command, item);
        command.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("O"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public List<TaskItem> GetTasksForDate(DateOnly date)
    {
        var result = new List<TaskItem>();
        using var connection = Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, title, scheduled_date, start_minutes, end_minutes, due_date, notes,
                       status, sort_order, repeat_kind, repeat_days, repeat_start_date
                FROM tasks
                WHERE is_deleted = 0 AND repeat_kind = 0
                  AND scheduled_date <= @date
                  AND MAX(scheduled_date, COALESCE(due_date, scheduled_date)) >= @date
                ORDER BY sort_order, id;
                """;
            command.Parameters.AddWithValue("@date", Iso(date));
            using var reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadTask(reader, date, false));
        }

        var recurring = new List<TaskItem>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, title, scheduled_date, start_minutes, end_minutes, due_date, notes,
                       status, sort_order, repeat_kind, repeat_days, repeat_start_date
                FROM tasks
                WHERE is_deleted = 0 AND repeat_kind <> 0 AND repeat_start_date <= @date
                ORDER BY sort_order, id;
                """;
            command.Parameters.AddWithValue("@date", Iso(date));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var item = ReadTask(reader, date, true);
                if (OccursOn(item, date)) recurring.Add(item);
            }
        }

        foreach (var item in recurring)
        {
            ApplyOccurrence(connection, item, date);
            if (item.Status != (WorkStatus)(-1)) result.Add(item);
        }

        return result
            .OrderBy(x => x.IsDone)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.StartMinutes ?? int.MaxValue)
            .ThenBy(x => x.Id)
            .ToList();
    }

    public List<TaskItem> GetLaterTasks()
    {
        var result = new List<TaskItem>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, scheduled_date, start_minutes, end_minutes, due_date, notes,
                   status, sort_order, repeat_kind, repeat_days, repeat_start_date
            FROM tasks
            WHERE is_deleted = 0 AND repeat_kind = 0 AND scheduled_date IS NULL
              AND status NOT IN (2, 3)
            ORDER BY sort_order, id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(ReadTask(reader, DateOnly.FromDateTime(DateTime.Today), false));
        return result;
    }

    public List<TaskItem> GetOverdue(DateOnly today)
    {
        var result = new List<TaskItem>();
        using var connection = Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, title, scheduled_date, start_minutes, end_minutes, due_date, notes,
                       status, sort_order, repeat_kind, repeat_days, repeat_start_date
                FROM tasks
                WHERE is_deleted = 0 AND repeat_kind = 0 AND scheduled_date IS NOT NULL
                  AND MAX(scheduled_date, COALESCE(due_date, scheduled_date)) < @today
                  AND status IN (0, 1) AND reviewed = 0
                ORDER BY scheduled_date, sort_order;
                """;
            command.Parameters.AddWithValue("@today", Iso(today));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var scheduled = ParseDate(reader, "scheduled_date") ?? today.AddDays(-1);
                result.Add(ReadTask(reader, scheduled, false));
            }
        }

        var yesterday = today.AddDays(-1);
        foreach (var item in GetTasksForDate(yesterday).Where(x => x.IsRecurring && !x.IsDone))
        {
            if (!OccurrenceReviewed(connection, item.Id, yesterday)) result.Add(item);
        }

        return result;
    }

    public void UpdateStatus(TaskItem item, WorkStatus status)
    {
        item.Status = status;
        if (item.IsRecurring)
        {
            UpsertOccurrence(item, status: status);
            return;
        }

        Execute("UPDATE tasks SET status=@status, updated_at=@now WHERE id=@id;",
            ("@status", (int)status), ("@now", DateTimeOffset.Now.ToString("O")), ("@id", item.Id));
    }

    public void UpdateTask(TaskItem item)
    {
        if (item.IsRecurring && item.IsOccurrence)
        {
            UpsertOccurrence(item, status: item.Status, title: item.Title,
                start: item.StartMinutes, end: item.EndMinutes, notes: item.Notes);
            return;
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks SET title=@title, scheduled_date=@scheduled, start_minutes=@start,
                end_minutes=@end, due_date=@due, notes=@notes, status=@status,
                sort_order=@sort, repeat_kind=@repeat, repeat_days=@days,
                repeat_start_date=@repeatStart, updated_at=@now
            WHERE id=@id;
            """;
        BindTask(command, item);
        command.Parameters.AddWithValue("@id", item.Id);
        command.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Schedule(TaskItem item, DateOnly date, int startMinutes, int durationMinutes = 30)
    {
        item.ScheduledDate = date;
        item.DisplayDate = date;
        item.StartMinutes = startMinutes;
        item.EndMinutes = Math.Min(startMinutes + durationMinutes, 24 * 60);

        if (item.IsRecurring)
        {
            UpsertOccurrence(item, start: item.StartMinutes, end: item.EndMinutes);
        }
        else
        {
            Execute("""
                UPDATE tasks SET scheduled_date=@date, start_minutes=@start, end_minutes=@end,
                    updated_at=@now WHERE id=@id;
                """,
                ("@date", Iso(date)), ("@start", startMinutes), ("@end", item.EndMinutes.Value),
                ("@now", DateTimeOffset.Now.ToString("O")), ("@id", item.Id));
        }
    }

    public void Reorder(IReadOnlyList<TaskItem> items)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            item.SortOrder = index;
            if (item.IsRecurring)
            {
                using var occurrence = connection.CreateCommand();
                occurrence.Transaction = transaction;
                occurrence.CommandText = """
                    INSERT INTO occurrences(task_id, occurrence_date, status, sort_order, updated_at)
                    VALUES(@id, @date, @status, @sort, @now)
                    ON CONFLICT(task_id, occurrence_date) DO UPDATE SET
                        sort_order=excluded.sort_order, updated_at=excluded.updated_at;
                    """;
                occurrence.Parameters.AddWithValue("@id", item.Id);
                occurrence.Parameters.AddWithValue("@date", Iso(item.DisplayDate));
                occurrence.Parameters.AddWithValue("@status", (int)item.Status);
                occurrence.Parameters.AddWithValue("@sort", index);
                occurrence.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("O"));
                occurrence.ExecuteNonQuery();
            }
            else
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE tasks SET sort_order=@sort, updated_at=@now WHERE id=@id;";
                command.Parameters.AddWithValue("@sort", index);
                command.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("O"));
                command.Parameters.AddWithValue("@id", item.Id);
                command.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public void Delete(TaskItem item)
    {
        if (item.IsRecurring)
        {
            UpsertOccurrence(item, isDeleted: true);
            return;
        }

        Execute("UPDATE tasks SET is_deleted=1, updated_at=@now WHERE id=@id;",
            ("@now", DateTimeOffset.Now.ToString("O")), ("@id", item.Id));
    }

    public void Restore(TaskItem item)
    {
        if (item.IsRecurring)
        {
            UpsertOccurrence(item, isDeleted: false);
            return;
        }

        Execute("UPDATE tasks SET is_deleted=0, updated_at=@now WHERE id=@id;",
            ("@now", DateTimeOffset.Now.ToString("O")), ("@id", item.Id));
    }

    public void MoveToToday(TaskItem item, DateOnly today)
    {
        if (item.IsRecurring)
        {
            UpdateStatus(item, WorkStatus.Skipped);
            return;
        }

        var duration = item.ScheduledDate.HasValue && item.DueDate.HasValue
            ? Math.Max(0, item.DueDate.Value.DayNumber - item.ScheduledDate.Value.DayNumber) : 0;
        Execute("""
            UPDATE tasks SET scheduled_date=@today, due_date=@due, reviewed=0, updated_at=@now WHERE id=@id;
            """,
            ("@today", Iso(today)), ("@due", item.DueDate.HasValue ? Iso(today.AddDays(duration)) : DBNull.Value),
            ("@now", DateTimeOffset.Now.ToString("O")), ("@id", item.Id));
    }

    public void MarkReviewed(TaskItem item)
    {
        if (item.IsRecurring)
        {
            UpsertOccurrence(item, reviewed: true);
            return;
        }

        Execute("UPDATE tasks SET reviewed=1, updated_at=@now WHERE id=@id;",
            ("@now", DateTimeOffset.Now.ToString("O")), ("@id", item.Id));
    }

    public bool WasNotified(long taskId, DateOnly date)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM notifications WHERE task_id=@id AND occurrence_date=@date LIMIT 1;";
        command.Parameters.AddWithValue("@id", taskId);
        command.Parameters.AddWithValue("@date", Iso(date));
        return command.ExecuteScalar() is not null;
    }

    public void MarkNotified(long taskId, DateOnly date)
    {
        Execute("""
            INSERT OR REPLACE INTO notifications(task_id, occurrence_date, notified_at)
            VALUES(@id, @date, @now);
            """,
            ("@id", taskId), ("@date", Iso(date)), ("@now", DateTimeOffset.Now.ToString("O")));
    }

    public void BackupIfNeeded()
    {
        try
        {
            AppPaths.EnsureDirectories();
            var target = Path.Combine(AppPaths.BackupDirectory, $"deskplan-{DateTime.Today:yyyy-MM-dd}.db");
            if (File.Exists(target) || !File.Exists(AppPaths.DatabasePath)) return;

            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                command.ExecuteNonQuery();
            }
            File.Copy(AppPaths.DatabasePath, target, true);

            foreach (var old in new DirectoryInfo(AppPaths.BackupDirectory)
                         .GetFiles("deskplan-*.db").OrderByDescending(x => x.CreationTimeUtc).Skip(14))
                old.Delete();
        }
        catch
        {
            // Backup failure should not make the planner unavailable.
        }
    }

    private int NextSortOrder(DateOnly date)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM tasks WHERE scheduled_date=@date;";
        command.Parameters.AddWithValue("@date", Iso(date));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private void UpsertOccurrence(TaskItem item, WorkStatus? status = null, string? title = null,
        int? start = null, int? end = null, string? notes = null, bool? reviewed = null, bool? isDeleted = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO occurrences(
                task_id, occurrence_date, status, sort_order, override_title,
                override_start_minutes, override_end_minutes, override_notes,
                reviewed, is_deleted, updated_at)
            VALUES(@id, @date, @status, @sort, @title, @start, @end, @notes, @reviewed, @deleted, @now)
            ON CONFLICT(task_id, occurrence_date) DO UPDATE SET
                status=CASE WHEN @hasStatus=1 THEN @status ELSE occurrences.status END,
                sort_order=COALESCE(@sort, occurrences.sort_order),
                override_title=COALESCE(@title, occurrences.override_title),
                override_start_minutes=CASE WHEN @hasStart=1 THEN @start ELSE occurrences.override_start_minutes END,
                override_end_minutes=CASE WHEN @hasEnd=1 THEN @end ELSE occurrences.override_end_minutes END,
                override_notes=COALESCE(@notes, occurrences.override_notes),
                reviewed=CASE WHEN @hasReviewed=1 THEN @reviewed ELSE occurrences.reviewed END,
                is_deleted=CASE WHEN @hasDeleted=1 THEN @deleted ELSE occurrences.is_deleted END,
                updated_at=@now;
            """;
        command.Parameters.AddWithValue("@id", item.Id);
        command.Parameters.AddWithValue("@date", Iso(item.DisplayDate));
        command.Parameters.AddWithValue("@status", (int)(status ?? item.Status));
        command.Parameters.AddWithValue("@hasStatus", status.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("@sort", item.SortOrder);
        command.Parameters.AddWithValue("@title", (object?)title ?? DBNull.Value);
        command.Parameters.AddWithValue("@start", (object?)start ?? DBNull.Value);
        command.Parameters.AddWithValue("@end", (object?)end ?? DBNull.Value);
        command.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@hasStart", start.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("@hasEnd", end.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("@reviewed", reviewed == true ? 1 : 0);
        command.Parameters.AddWithValue("@hasReviewed", reviewed.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("@deleted", isDeleted == true ? 1 : 0);
        command.Parameters.AddWithValue("@hasDeleted", isDeleted.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static bool OccursOn(TaskItem item, DateOnly date)
    {
        if (item.RepeatStartDate.HasValue && date < item.RepeatStartDate.Value) return false;
        return item.RepeatKind switch
        {
            RepeatKind.Daily => true,
            RepeatKind.Weekdays => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday,
            RepeatKind.SelectedDays => item.RepeatDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(x => int.TryParse(x, out var day) && day == (int)date.DayOfWeek),
            _ => false
        };
    }

    private static void ApplyOccurrence(SqliteConnection connection, TaskItem item, DateOnly date)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, sort_order, override_title, override_start_minutes,
                   override_end_minutes, override_notes, is_deleted
            FROM occurrences WHERE task_id=@id AND occurrence_date=@date;
            """;
        command.Parameters.AddWithValue("@id", item.Id);
        command.Parameters.AddWithValue("@date", Iso(date));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return;
        if (reader.GetInt32(6) != 0)
        {
            item.Status = (WorkStatus)(-1);
            return;
        }
        item.Status = (WorkStatus)reader.GetInt32(0);
        if (!reader.IsDBNull(1)) item.SortOrder = reader.GetInt32(1);
        if (!reader.IsDBNull(2)) item.Title = reader.GetString(2);
        if (!reader.IsDBNull(3)) item.StartMinutes = reader.GetInt32(3);
        if (!reader.IsDBNull(4)) item.EndMinutes = reader.GetInt32(4);
        if (!reader.IsDBNull(5)) item.Notes = reader.GetString(5);
    }

    private static bool OccurrenceReviewed(SqliteConnection connection, long id, DateOnly date)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT reviewed FROM occurrences WHERE task_id=@id AND occurrence_date=@date;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@date", Iso(date));
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) != 0;
    }

    private static TaskItem ReadTask(SqliteDataReader reader, DateOnly displayDate, bool occurrence)
    {
        return new TaskItem
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            ScheduledDate = ParseDate(reader, "scheduled_date"),
            DisplayDate = displayDate,
            StartMinutes = GetNullableInt(reader, "start_minutes"),
            EndMinutes = GetNullableInt(reader, "end_minutes"),
            DueDate = ParseDate(reader, "due_date"),
            Notes = reader.GetString(reader.GetOrdinal("notes")),
            Status = (WorkStatus)reader.GetInt32(reader.GetOrdinal("status")),
            SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
            RepeatKind = (RepeatKind)reader.GetInt32(reader.GetOrdinal("repeat_kind")),
            RepeatDays = reader.GetString(reader.GetOrdinal("repeat_days")),
            RepeatStartDate = ParseDate(reader, "repeat_start_date"),
            IsOccurrence = occurrence
        };
    }

    private static void BindTask(SqliteCommand command, TaskItem item)
    {
        command.Parameters.AddWithValue("@title", item.Title.Trim());
        command.Parameters.AddWithValue("@scheduled", item.ScheduledDate.HasValue ? Iso(item.ScheduledDate.Value) : DBNull.Value);
        command.Parameters.AddWithValue("@start", (object?)item.StartMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("@end", (object?)item.EndMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("@due", item.DueDate.HasValue ? Iso(item.DueDate.Value) : DBNull.Value);
        command.Parameters.AddWithValue("@notes", item.Notes ?? string.Empty);
        command.Parameters.AddWithValue("@status", (int)item.Status);
        command.Parameters.AddWithValue("@sort", item.SortOrder);
        command.Parameters.AddWithValue("@repeat", (int)item.RepeatKind);
        command.Parameters.AddWithValue("@days", item.RepeatDays ?? string.Empty);
        command.Parameters.AddWithValue("@repeatStart", item.RepeatStartDate.HasValue ? Iso(item.RepeatStartDate.Value) : DBNull.Value);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=3000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private void Execute(string sql, params (string Name, object Value)[] values)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        command.ExecuteNonQuery();
    }

    private static int? GetNullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateOnly? ParseDate(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : DateOnly.Parse(reader.GetString(ordinal));
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
