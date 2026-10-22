using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PressureMap.Core;

public static class CloudSnapshotValidator
{
    private static readonly string[] Types = ["考试", "大作业", "报告", "测验", "其他"];
    public static void Validate(JsonElement root, int maxBytes)
    {
        Object(root); Keys(root, "schema_version", "exported_at", "semesters", "desktop_todos", "course_calendars");
        if (!root.TryGetProperty("schema_version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1) Fail("不支持的数据版本。");
        if (Encoding.UTF8.GetByteCount(root.GetRawText()) > maxBytes) Fail("数据超过账户单次同步容量。");
        var ids = new HashSet<long>();
        foreach (var semester in Array(root, "semesters", 50, true))
        {
            Object(semester); Keys(semester, "id", "name", "total_weeks", "week1_monday", "threshold", "hours_per_week", "type_hours", "current_step", "created_at", "updated_at", "courses", "tasks");
            if (!semester.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number < 1 || !ids.Add(number)) Fail("学期标识无效或重复。");
            Text(semester, "name", 100, true); var weeks = Number(semester, "total_weeks", 1, 30, true, true);
            Number(semester, "threshold", 1, 30, true); Number(semester, "hours_per_week", 1, 100, true); Number(semester, "current_step", 1, 4, true, true);
            Date(semester, "week1_monday", true); var monday = Text(semester, "week1_monday", 10); if (monday.Length > 0 && DateOnly.ParseExact(monday, "yyyy-MM-dd").DayOfWeek != DayOfWeek.Monday) Fail("学期第一周必须从周一开始。");
            if (!semester.TryGetProperty("type_hours", out var hours) || hours.ValueKind != JsonValueKind.Object || hours.EnumerateObject().Count() > 20) Fail("默认任务耗时无效。");
            foreach (var kind in Types) Number(hours, kind, 0, 200, true);
            foreach (var property in hours.EnumerateObject()) Number(hours, property.Name, 0, 200, true);
            var courses = new HashSet<string>();
            foreach (var course in Array(semester, "courses", 10, true))
            {
                Object(course); Keys(course, "key", "name", "raw_text"); var key = Text(course, "key", 40, true); if (!courses.Add(key)) Fail("课程标识重复。"); Text(course, "name", 100, true); Text(course, "raw_text", 20000);
            }
            foreach (var task in Array(semester, "tasks", 300, true))
            {
                Object(task); Keys(task, "course_key", "name", "type", "week", "weight", "est_hours", "source");
                if (!courses.Contains(Text(task, "course_key", 40, true))) Fail("任务关联了不存在的课程。"); Text(task, "name", 100, true);
                if (!Types.Contains(Text(task, "type", 20, true))) Fail("任务类型无效。"); Number(task, "week", 1, weeks, false, true); Number(task, "weight", 0, 10, true); Number(task, "est_hours", 0, 200); Text(task, "source", 500);
            }
        }
        var todoIds = new HashSet<string>(); var sourceKeys = new HashSet<string>();
        foreach (var task in Array(root, "desktop_todos", 1000))
        {
            Object(task); Keys(task, "id", "title", "due_date", "important", "completed", "source_key", "source_label", "created_at", "updated_at", "course_name", "planned_date", "task_kind", "estimated_hours", "due_time", "requires_submission", "submitted", "reminders_enabled", "repeat_days", "steps");
            if (!todoIds.Add(Text(task, "id", 128, true))) Fail("待办标识重复。"); Text(task, "title", 300, true); var key = Text(task, "source_key", 512); if (key.Length > 0 && !sourceKeys.Add(key)) Fail("待办来源重复。");
            Text(task, "source_label", 500); Text(task, "course_name", 200); Date(task, "due_date"); Date(task, "planned_date");
            Bool(task, "important"); var completed = Bool(task, "completed"); var requires = Bool(task, "requires_submission"); var submitted = Bool(task, "submitted"); Bool(task, "reminders_enabled"); Number(task, "repeat_days", 0, 365, false, true);
            if (submitted && (!completed || !requires)) Fail("提交状态无效。");
            var dueTime = Text(task, "due_time", 5); if (dueTime.Length > 0 && (Text(task, "due_date", 10).Length == 0 || !TimeOnly.TryParseExact(dueTime, "HH:mm", out _))) Fail("截止时间无效。");
            var type = Text(task, "task_kind", 20); if (type.Length > 0 && !new[] { "作业", "考试", "复习", "实验", "报告", "其他" }.Contains(type)) Fail("待办类型无效。"); Number(task, "estimated_hours", .1, 100);
            var stepIds = new HashSet<string>(); foreach (var step in Array(task, "steps", 20)) { Object(step); Keys(step, "id", "title", "completed"); if (!stepIds.Add(Text(step, "id", 128, true))) Fail("子任务标识重复。"); Text(step, "title", 200, true); Bool(step, "completed"); }
        }
        var calendarNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var calendar in Array(root, "course_calendars", 20))
        {
            Object(calendar); Keys(calendar, "name", "format", "content", "imported_at"); var name = Text(calendar, "name", 200, true); if (name.Contains('/') || name.Contains('\\') || !calendarNames.Add(name)) Fail("课表名称无效或重复。");
            var format = Text(calendar, "format", 3, true); var content = Text(calendar, "content", 2 * 1024 * 1024, true);
            if (Encoding.UTF8.GetByteCount(content) > 2 * 1024 * 1024 || !new[] { "ics", "csv" }.Contains(format) || !name.EndsWith("." + format, StringComparison.OrdinalIgnoreCase)) Fail("课表内容或格式无效。");
            if (format == "ics" && (!content.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) || !content.Contains("END:VCALENDAR", StringComparison.OrdinalIgnoreCase))) Fail("日历文件不完整。");
        }
    }
    private static void Keys(JsonElement item, params string[] names) { var seen = new HashSet<string>(); foreach (var p in item.EnumerateObject()) if (!seen.Add(p.Name) || !names.Contains(p.Name)) Fail("数据包含当前版本不支持的字段：" + p.Name); }
    private static void Object(JsonElement value) { if (value.ValueKind != JsonValueKind.Object) Fail("数据需要为对象。"); }
    private static IEnumerable<JsonElement> Array(JsonElement parent, string name, int max, bool required = false)
    {
        if (!parent.TryGetProperty(name, out var value)) { if (required) Fail("缺少字段：" + name); return []; }
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > max) Fail("列表无效或过长：" + name); return value.EnumerateArray().ToArray();
    }
    private static string Text(JsonElement parent, string name, int max, bool required = false)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) { if (required) Fail("缺少字段：" + name); return ""; }
        if (value.ValueKind != JsonValueKind.String) Fail("文本字段无效：" + name); var text = value.GetString()!;
        if (text.Length > max || text.Contains('\0') || required && string.IsNullOrWhiteSpace(text)) Fail("文本长度无效：" + name); return text;
    }
    private static double Number(JsonElement parent, string name, double min, double max, bool required = false, bool integer = false)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) { if (required) Fail("缺少字段：" + name); return 0; }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n) || n < min || n > max || integer && n != Math.Truncate(n)) Fail("数值字段无效：" + name); return value.GetDouble();
    }
    private static bool Bool(JsonElement parent, string name) { if (!parent.TryGetProperty(name, out var value)) return false; if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail("布尔字段无效：" + name); return value.GetBoolean(); }
    private static void Date(JsonElement parent, string name, bool allowEmpty = false) { var text = Text(parent, name, 10); if (text.Length == 0 && allowEmpty) return; if (text.Length > 0 && !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) Fail("日期无效：" + name); }
    private static void Fail(string message) => throw new InvalidDataException(message);
}
