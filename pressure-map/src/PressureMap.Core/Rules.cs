using System.Text.Json;

namespace PressureMap.Core;

public sealed record Pressure(Dictionary<string, double[]> Per, double[] Total);
public sealed record WorkPlan(StudyTask Task, int Start, double Missing, List<(int Week, double Hours)> Allocations, List<int> Overlap)
{
    public bool Late => Missing > 1e-8;
    public string Summary => $"第 {Start} 周开始 → 第 {Task.Week} 周截止" + (Late ? $" · 缺少 {Missing:0.##} 小时" : "");
    public string Reason => Overlap.Count > 0 ? $"第 {string.Join("、", Overlap)} 周有压力预警，可用时间减半" : "按每周可用时间分配";
}

public static class Rules
{
    public static readonly string[] Types = ["考试", "大作业", "报告", "测验", "其他"];
    public static readonly Dictionary<string, double> Weights = new() { ["考试"] = 5, ["大作业"] = 4, ["报告"] = 3, ["测验"] = 2, ["其他"] = 2 };
    public static readonly Dictionary<string, double> Hours = new() { ["考试"] = 15, ["大作业"] = 20, ["报告"] = 8, ["测验"] = 4, ["其他"] = 4 };
    public static bool ValidWeek(int? week, int weeks) => week is >= 1 && week <= weeks;
    public static Pressure Compute(IEnumerable<StudyTask> tasks, int weeks)
    {
        var per = new Dictionary<string, double[]>(); var total = new double[weeks + 1];
        foreach (var task in tasks.Where(t => ValidWeek(t.Week, weeks)))
        {
            if (!per.TryGetValue(task.CourseKey, out var values)) per[task.CourseKey] = values = new double[weeks + 1];
            foreach (var (offset, factor) in new[] { (0, 1.0), (1, .6), (2, .3) })
            {
                var week = task.Week!.Value - offset;
                if (week > 0) { values[week] += task.Weight * factor; total[week] += task.Weight * factor; }
            }
        }
        foreach (var values in per.Values.Append(total)) for (var i = 0; i < values.Length; i++) values[i] = Math.Round(values[i], 2);
        return new(per, total);
    }
    public static string Warning(double value, double threshold) => Localization.Loc.T(value >= threshold * 1.4 ? "红色预警" : value >= threshold ? "橙色预警" : "");
    public static List<WorkPlan> Plan(Semester semester)
    {
        var computed = Compute(semester.Tasks, semester.TotalWeeks);
        var capacity = Enumerable.Range(0, semester.TotalWeeks + 1).Select(w => w == 0 ? 0 : semester.HoursPerWeek * .4 * (computed.Total[w] >= semester.Threshold ? .5 : 1)).ToArray();
        var plans = new List<WorkPlan>();
        foreach (var task in semester.Tasks.Where(t => ValidWeek(t.Week, semester.TotalWeeks)).OrderBy(t => t.Week))
        {
            var need = task.EstHours ?? semester.TypeHours.GetValueOrDefault(task.Kind, Hours[task.Kind]);
            var start = task.Week!.Value;
            var allocations = new List<(int, double)>();
            for (var week = task.Week.Value; week > 0 && need > 1e-8; week--)
            {
                var give = Math.Min(capacity[week], need);
                if (give <= 1e-8) continue;
                capacity[week] -= give; need = Math.Round(need - give, 8); start = week;
                allocations.Add((week, Math.Round(give, 2)));
            }
            if (need > 1e-8) start = 1;
            plans.Add(new(task, start, Math.Max(0, Math.Round(need, 2)), allocations,
                Enumerable.Range(start, task.Week.Value - start + 1).Where(w => computed.Total[w] >= semester.Threshold).ToList()));
        }
        return plans.OrderBy(p => p.Start).ThenBy(p => p.Task.Week).ToList();
    }
    public static int? DateToWeek(string? value, string? monday, int weeks)
    {
        if (!DateOnly.TryParseExact(monday, "yyyy-MM-dd", out var start) || value is null) return null;
        foreach (var year in new[] { start.Year, start.Year + 1 })
            if (DateOnly.TryParseExact($"{year}-{value}", "yyyy-MM-dd", out var day))
            {
                var week = (int)Math.Floor((day.DayNumber - start.DayNumber) / 7.0) + 1;
                if (ValidWeek(week, weeks)) return week;
            }
        return null;
    }
    public static (List<StudyTask> Tasks, List<Course> Added) ParseAi(string text, Semester semester)
    {
        var first = text.IndexOf('['); var last = text.LastIndexOf(']');
        if (first < 0 || last < first) throw new InvalidDataException("请粘贴 AI 返回的完整 JSON 数组。");
        JsonDocument document;
        try { document = JsonDocument.Parse(text[first..(last + 1)]); }
        catch (JsonException) { throw new InvalidDataException("JSON 格式不正确，请复制完整数组后重试。"); }
        using (document)
        {
            var items = document.RootElement;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 300) throw new InvalidDataException("任务数组需要包含 1 到 300 条任务。");
            if (semester.Courses.Select(c => c.Name.Trim()).Distinct().Count() != semester.Courses.Count) throw new InvalidDataException("课程名有重复，请先修改。");
            var lookup = semester.Courses.ToDictionary(c => c.Name.Trim(), c => c.Key);
            var tasks = new List<StudyTask>(); var added = new List<Course>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("每条任务需要是 JSON 对象。");
                string Read(string key, string fallback = "") => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
                static string Limit(string value, int length) => value.Length > length ? value[..length] : value;
                var course = Limit(Read("course", "未分类课程").Trim(), 100);
                if (string.IsNullOrWhiteSpace(course)) course = "未分类课程";
                if (!lookup.TryGetValue(course, out var key))
                {
                    if (lookup.Count >= 10) throw new InvalidDataException("解析后的课程超过 10 门，请核对课程名。");
                    var created = new Course { Name = course }; added.Add(created); lookup[course] = key = created.Key;
                }
                var kind = Read("type", "其他"); if (!Types.Contains(kind)) kind = "其他";
                int? week = item.TryGetProperty("week", out var w) && w.TryGetInt32Safe(out var number) && ValidWeek(number, semester.TotalWeeks) ? number : DateToWeek(Read("date"), semester.Week1Monday, semester.TotalWeeks);
                tasks.Add(new() { CourseKey = key, CourseName = course, Name = Limit(Read("name", "未命名任务").Trim(), 100),
                    Kind = kind, Week = week, Weight = Weights[kind], Source = Limit(Read("source"), 500) });
            }
            return (tasks, added);
        }
    }
    private static bool TryGetInt32Safe(this JsonElement value, out int number) { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number); }
    public static string BuildPrompt(IEnumerable<Course> courses) => "你是课程任务提取助手。提取课程中所有有时间要求的任务。只输出 JSON 数组。每项包含 course（原课程名）、name、type（考试/大作业/报告/测验/其他）、week（明确截止周，否则 null）、date（明确日期 MM-DD，否则 null）、source（原文摘录）。一句多个任务要拆开，不编造，不猜学期末周次。\n\n" + string.Join("\n\n", courses.Select(c => $"【{c.Name}】\n{c.RawText}"));
    public static void Validate(Semester semester)
    {
        static void Text(string? value, int limit, string label, bool empty = false)
        { if (value is null || (!empty && string.IsNullOrWhiteSpace(value)) || value.Length > limit || value.Contains('\0')) throw new InvalidDataException($"{label}需要是 {(empty ? 0 : 1)} 到 {limit} 字。"); }
        static void Number(double value, double min, double max, string label)
        { if (!double.IsFinite(value) || value < min || value > max) throw new InvalidDataException($"{label}需要在 {min} 到 {max} 之间。"); }
        Text(semester.Name, 100, "学期名"); Number(semester.TotalWeeks, 1, 30, "总周数");
        Number(semester.Threshold, 1, 30, "高压阈值"); Number(semester.HoursPerWeek, 1, 100, "每周可用小时"); Number(semester.CurrentStep, 1, 4, "当前步骤");
        if (!string.IsNullOrEmpty(semester.Week1Monday) && (!DateOnly.TryParseExact(semester.Week1Monday, "yyyy-MM-dd", out var start) || start.DayOfWeek != DayOfWeek.Monday)) throw new InvalidDataException("第 1 周日期必须是有效的周一，格式为 YYYY-MM-DD。");
        foreach (var kind in Types) { if (!semester.TypeHours.ContainsKey(kind)) throw new InvalidDataException("缺少任务默认估时。"); Number(semester.TypeHours[kind], 0, 200, kind + "估时"); }
        if (semester.Courses.Count > 10 || semester.Tasks.Count > 300) throw new InvalidDataException("每学期最多 10 门课和 300 个任务。");
        var keys = new HashSet<string>();
        foreach (var course in semester.Courses) { Text(course.Key, 40, "课程标识"); if (!keys.Add(course.Key)) throw new InvalidDataException("课程标识重复。"); Text(course.Name, 100, "课程名"); Text(course.RawText, 20000, "课程原文", true); }
        foreach (var task in semester.Tasks)
        {
            if (!keys.Contains(task.CourseKey)) throw new InvalidDataException("任务缺少对应课程。"); Text(task.Name, 100, "任务名");
            if (!Types.Contains(task.Kind)) throw new InvalidDataException("任务类型不正确。");
            if (task.Week is not null && !ValidWeek(task.Week, semester.TotalWeeks)) throw new InvalidDataException("截止周超出学期范围。");
            Number(task.Weight, 0, 10, "权重"); if (task.EstHours is { } hours) Number(hours, 0, 200, "任务估时"); Text(task.Source, 500, "原文出处", true);
        }
    }
    public static Semester Sample()
    {
        using var stream = typeof(Rules).Assembly.GetManifestResourceStream("PressureMap.Sample.json")!;
        using var json = JsonDocument.Parse(stream);
        var semester = new Semester { Name = "示例学期", TotalWeeks = json.RootElement.GetProperty("total_weeks").GetInt32() };
        foreach (var c in json.RootElement.GetProperty("courses").EnumerateArray()) semester.Courses.Add(new() { Name = c.GetProperty("name").GetString()!, RawText = c.GetProperty("raw_text").GetString()! });
        var (tasks, _) = ParseAi(json.RootElement.GetProperty("tasks").GetRawText(), semester);
        foreach (var task in tasks) semester.Tasks.Add(task);
        return semester;
    }
}
