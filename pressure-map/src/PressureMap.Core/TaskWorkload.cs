namespace PressureMap.Core;

public sealed record PressureSource(string Label, double EquivalentHours);
public sealed record DailyPressure(int Score, double CourseHours, double TaskHours, IReadOnlyList<PressureSource> Sources)
{
    public string Level => Localization.Loc.T(Score >= 85 ? "负荷很高" : Score >= 65 ? "有些紧张" : Score >= 35 ? "节奏适中" : "比较轻松");
}
public static class TaskWorkload
{
    public static readonly string[] Kinds = ["作业", "考试", "复习", "实验", "报告", "其他"];
    public static bool IsToday(Todo task, DateTime now) => !task.IsFinished &&
        (task.Completed && task.RequiresSubmission || task.PlannedDate is { } plan && string.CompareOrdinal(plan, now.ToString("yyyy-MM-dd")) <= 0 || task.DueDate is { } due && string.CompareOrdinal(due, now.ToString("yyyy-MM-dd")) <= 0);
    public static List<Todo> Ordered(IEnumerable<Todo> tasks, DateTime now) => tasks.OrderBy(t => t.IsFinished).ThenByDescending(t => t.Deadline < now).ThenByDescending(t => t.Completed && t.RequiresSubmission && !t.Submitted).ThenByDescending(t => t.Important).ThenBy(t => t.Deadline ?? DateTime.MaxValue).ThenBy(t => t.CreatedAt).ToList();
    public static DailyPressure Compute(IEnumerable<Todo> tasks, IEnumerable<CourseOccurrence> courses, DateTime day, double capacity = 6)
    {
        if (!double.IsFinite(capacity) || capacity is < 1 or > 16) throw new InvalidDataException("每日等效负荷容量需要为 1～16 小时。");
        var date = day.Date; var sources = new List<PressureSource>();
        var courseHours = courses.Where(c => c.Start < date.AddDays(1) && c.End > date).Sum(c => (Min(c.End, date.AddDays(1)) - Max(c.Start, date)).TotalHours);
        // Concurrent classes count once toward the occupied time.
        var intervals = courses.Where(c => c.Start < date.AddDays(1) && c.End > date).Select(c => (Start: Max(c.Start, date), End: Min(c.End, date.AddDays(1)))).OrderBy(c => c.Start).ToList();
        double occupied = 0; DateTime? end = null;
        foreach (var interval in intervals) { var start = end is { } previous && previous > interval.Start ? previous : interval.Start; if (interval.End > start) occupied += (interval.End - start).TotalHours; if (end is null || interval.End > end) end = interval.End; }
        courseHours = occupied;
        if (courseHours > 0) sources.Add(new("当天课程", courseHours * .35));
        double taskHours = 0;
        foreach (var task in tasks.Where(t => !t.IsFinished))
        {
            var planned = DateOnly.TryParseExact(task.PlannedDate, "yyyy-MM-dd", out var p) ? p.ToDateTime(TimeOnly.MinValue) : (DateTime?)null;
            var due = task.Deadline?.Date;
            var assigned = planned is { } plannedDay && plannedDay <= date;
            if (!assigned && (due is null || due > date.AddDays(6))) continue;
            if (planned > date && due > date) continue;
            var remaining = due is { } deadline ? Math.Clamp((deadline - date).Days + 1, 1, 7) : 1;
            if (!double.IsFinite(task.EstimatedHours) || task.EstimatedHours is < .1 or > 100) throw new InvalidDataException("任务预计耗时无效。");
            var progress = task.Steps.Count == 0 ? 1 : Math.Max(.1, 1 - task.Steps.Count(s => s.Completed) / (double)task.Steps.Count);
            var hours = task.Completed && task.RequiresSubmission ? .25 : task.EstimatedHours * progress;
            var daily = assigned ? hours : hours / remaining;
            var urgency = due is null ? 1 : due < date ? 1.8 : due == date ? 1.6 : due <= date.AddDays(2) ? 1.25 : 1;
            var kind = task.TaskKind == "考试" ? 1.6 : task.TaskKind is "实验" or "报告" ? 1.15 : 1;
            var equivalent = daily * urgency * kind * (task.Important ? 1.1 : 1);
            taskHours += equivalent; sources.Add(new(task.CourseName.Length > 0 ? task.CourseName + " · " + task.Title : task.Title, equivalent));
        }
        var score = (int)Math.Clamp(Math.Round((courseHours * .35 + taskHours) / capacity * 100, MidpointRounding.AwayFromZero), 0, 100);
        return new(score, Math.Round(courseHours, 2), Math.Round(taskHours, 2), sources.OrderByDescending(s => s.EquivalentHours).ToList());
    }
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
