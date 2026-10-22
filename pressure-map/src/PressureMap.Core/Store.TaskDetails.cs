using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PressureMap.Core;

public sealed record ReminderState(string? DeliveredKey, DateTime? SnoozedUntil);
public sealed record TaskReminder(Todo Task, string Key, string Message);
public static class ReminderRules
{
    public static TaskReminder? Due(Todo task, ReminderState? state, DateTime now)
    {
        if (task.IsFinished || !task.RemindersEnabled || task.Deadline is not { } due || now < due.AddDays(-1)) return null;
        var stage = now >= due ? 0 : now >= due.AddHours(-1) ? 60 : 1440;
        var key = $"{due:yyyy-MM-ddTHH:mm}|{stage}|{(task.Completed ? "submit" : "work")}";
        if (state?.SnoozedUntil is { } snooze && now < snooze) return null;
        if (state?.SnoozedUntil is null && state?.DeliveredKey == key) return null;
        var label = task.Completed && task.RequiresSubmission ? "已完成，记得提交" : stage == 0 ? "已到截止时间" : stage == 60 ? "将在 1 小时内截止" : "将在 24 小时内截止";
        return new(task, key, $"{task.Title}\n{label} · {due:M月d日 HH:mm}");
    }
}
public sealed partial class Store
{
    private static void SaveDetails(SqliteConnection c, SqliteTransaction t, Todo task) => Execute(c, t, "INSERT INTO todo_details(todo_id,payload) VALUES(@p0,@p1) ON CONFLICT(todo_id) DO UPDATE SET payload=excluded.payload", task.Id, JsonSerializer.Serialize(TodoDetails.From(task), DataJson.Compact));
    public Dictionary<string, ReminderState> ReminderStates()
    {
        using var c = Open(); using var command = Command(c, null, "SELECT todo_id,delivered_key,snoozed_until FROM todo_reminders"); using var reader = command.ExecuteReader();
        var result = new Dictionary<string, ReminderState>();
        while (reader.Read()) result[reader.GetString(0)] = new(reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture));
        return result;
    }
    public ReminderState? ReminderFor(string id)
    {
        using var c = Open(); using var cmd = Command(c, null, "SELECT delivered_key,snoozed_until FROM todo_reminders WHERE todo_id=@p0", id); using var r = cmd.ExecuteReader();
        return r.Read() ? new(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : DateTime.Parse(r.GetString(1), System.Globalization.CultureInfo.InvariantCulture)) : null;
    }
    public void RecordReminder(string id, string key, DateTime? snooze = null)
    {
        using var c = Open(); Execute(c, null, "INSERT INTO todo_reminders(todo_id,delivered_key,snoozed_until) VALUES(@p0,@p1,@p2) ON CONFLICT(todo_id) DO UPDATE SET delivered_key=excluded.delivered_key,snoozed_until=excluded.snoozed_until", id, key, snooze?.ToString("yyyy-MM-ddTHH:mm:ss"));
    }
    public List<string> CourseNames()
    {
        var names = ListSemesters().SelectMany(s => s.Courses).Select(c => c.Name).ToList();
        foreach (var source in ListCalendars()) names.AddRange(CourseCalendar.Names(source));
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Order().ToList();
    }
}
