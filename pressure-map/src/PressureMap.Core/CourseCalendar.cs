using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ical.Net;
using Calendar = Ical.Net.Calendar;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;

namespace PressureMap.Core;

public sealed record CalendarSource(string Name, string Format, string Content, string ImportedAt = "");
public sealed record CourseOccurrence(string Title, DateTime Start, DateTime End, string Location, string Description = "", string Source = "")
{
    public string TimeLabel => $"{Start:HH:mm}–{End:HH:mm}" + (Start.Date != End.Date ? "（跨日）" : "");
    public string State(DateTime now) => now >= End ? "已结束" : now >= Start ? "正在上课" : "未开始";
}
public sealed record CalendarPreview(CalendarSource Source, int EventCount, int AllDayCount, IReadOnlyList<CourseOccurrence> Upcoming);

public static class CourseCalendar
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static CalendarPreview Preview(string name, string content, DateTime today)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaxBytes) throw new InvalidDataException("课表文件不能超过 2 MB。");
        var format = Path.GetExtension(name).ToLowerInvariant() switch { ".ics" => "ics", ".csv" => "csv", _ => throw new InvalidDataException("请选择 .ics 日历或 .csv 课表。") };
        var source = new CalendarSource(Path.GetFileName(name), format, content, DataJson.Stamp());
        int count, allDay;
        if (format == "ics")
        {
            var calendar = ParseIcs(content); count = calendar.Events.Count; allDay = calendar.Events.Count(e => e.IsAllDay);
            if (count == 0) throw new InvalidDataException("日历中没有课程事件。");
            foreach (var item in calendar.Events.Where(e => !e.IsAllDay && e.Status != "CANCELLED"))
            {
                if (item.DtStart is null || item.DtEnd is null && item.Duration is null) throw new InvalidDataException($"课程“{item.Summary}”缺少开始或结束时间。");
                if (string.IsNullOrWhiteSpace(item.Summary)) throw new InvalidDataException("存在未命名课程，请补充课程名称。");
            }
        }
        else { count = ParseCsv(content).Count; allDay = 0; }
        var upcoming = Between([source], today.Date, today.Date.AddDays(366)).Take(200).ToList();
        return new(source, count, allDay, upcoming);
    }
    private static Calendar ParseIcs(string content)
    {
        if (!content.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) || !content.Contains("END:VCALENDAR", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不是完整的 ICS 日历文件。");
        var calendar = Calendar.Load(content.TrimStart('\uFEFF')) ?? throw new InvalidDataException("无法读取日历。");
        if (calendar.Events.Count > 5000) throw new InvalidDataException("单个课表最多包含 5000 个事件。");
        return calendar;
    }
    private static DateTime Local(CalDateTime value) => value.IsFloating ? DateTime.SpecifyKind(value.Value, DateTimeKind.Local) : value.AsUtc.ToLocalTime();
    public static List<CourseOccurrence> Between(IEnumerable<CalendarSource> sources, DateTime from, DateTime to)
    {
        if (to <= from || to - from > TimeSpan.FromDays(370)) throw new InvalidDataException("查询日期范围无效。");
        var results = new List<CourseOccurrence>();
        foreach (var source in sources)
        {
            if (source.Format == "csv") results.AddRange(ParseCsv(source.Content).Where(e => e.Start < to && e.End > from).Select(e => e with { Source = source.Name }));
            else
            {
                var calendar = ParseIcs(source.Content);
                var start = new CalDateTime(DateTime.SpecifyKind(from.AddDays(-2), DateTimeKind.Unspecified));
                var options = new EvaluationOptions { MaxUnmatchedIncrementsLimit = 2000 };
                var examined = 0;
                foreach (var occurrence in calendar.GetOccurrences<CalendarEvent>(start, options))
                {
                    if (++examined > 25000) throw new InvalidDataException("重复课程过于密集，无法导入此课表。");
                    var begin = Local(occurrence.Period.StartTime);
                    if (begin >= to.AddDays(2)) break;
                    if (occurrence.Source is not CalendarEvent item || item.IsAllDay || item.Status == "CANCELLED") continue;
                    var endTime = occurrence.Period.EffectiveEndTime ?? throw new InvalidDataException($"课程“{item.Summary}”缺少结束时间。");
                    var end = Local(endTime);
                    if (end <= begin || end - begin > TimeSpan.FromDays(2)) throw new InvalidDataException($"课程“{item.Summary}”的结束时间无效（单次最多两天）。");
                    if (begin < to && end > from) results.Add(new(item.Summary?.Trim() ?? "未命名课程", begin, end, item.Location ?? "", item.Description ?? "", source.Name));
                }
            }
            if (results.Count > 20000) throw new InvalidDataException("课程数量超过查询上限。");
        }
        return results.DistinctBy(e => (e.Title, e.Start, e.End, e.Location)).OrderBy(e => e.Start).ThenBy(e => e.End).ToList();
    }
    public static List<CourseOccurrence> Day(IEnumerable<CalendarSource> sources, DateTime date) => Between(sources, date.Date, date.Date.AddDays(1));
    public static IReadOnlyList<IReadOnlyList<string>> CsvRows(string content)
    {
        var rows = new List<IReadOnlyList<string>>(); var cells = new List<string>(); var word = new StringBuilder(); var quoted = false;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '"') { if (quoted && i + 1 < content.Length && content[i + 1] == '"') { word.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { cells.Add(word.ToString()); word.Clear(); }
            else if ((c == '\r' || c == '\n') && !quoted)
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                cells.Add(word.ToString()); word.Clear(); if (cells.Any(x => x.Length > 0)) rows.Add(cells.ToArray()); cells.Clear();
            }
            else word.Append(c);
        }
        if (quoted) throw new InvalidDataException("CSV 引号没有闭合。");
        cells.Add(word.ToString()); if (cells.Any(x => x.Length > 0)) rows.Add(cells.ToArray());
        return rows;
    }
    private static List<CourseOccurrence> ParseCsv(string content)
    {
        var rows = CsvRows(content.TrimStart('\uFEFF')); if (rows.Count < 2) throw new InvalidDataException("CSV 课表没有课程数据。");
        var headers = rows[0].Select(x => x.Trim().ToLowerInvariant()).ToList();
        int Column(params string[] names) => headers.FindIndex(h => names.Contains(h));
        var dateCol = Column("日期", "date"); var titleCol = Column("课程", "课程名称", "title"); var startCol = Column("开始时间", "start"); var endCol = Column("结束时间", "end"); var locationCol = Column("教室", "地点", "location"); var teacherCol = Column("教师", "teacher");
        if (new[] { dateCol, titleCol, startCol, endCol }.Any(x => x < 0)) throw new InvalidDataException("CSV 需要“日期、课程、开始时间、结束时间”列，可选“教室、教师”。");
        if (rows.Count > 5001) throw new InvalidDataException("CSV 最多导入 5000 条课程。");
        var result = new List<CourseOccurrence>();
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index]; string Read(int col) => col >= 0 && col < row.Count ? row[col].Trim() : "";
            if (!DateOnly.TryParseExact(Read(dateCol), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !TimeOnly.TryParseExact(Read(startCol), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(Read(endCol), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start || Read(titleCol).Length == 0)
                throw new InvalidDataException($"CSV 第 {index + 1} 行无效：日期用 yyyy-MM-dd，时间用 HH:mm，结束时间必须晚于开始时间。");
            result.Add(new(Read(titleCol), date.ToDateTime(start), date.ToDateTime(end), Read(locationCol), Read(teacherCol)));
        }
        return result;
    }
    public static IEnumerable<string> Names(CalendarSource source) => source.Format == "csv" ? ParseCsv(source.Content).Select(e => e.Title) : ParseIcs(source.Content).Events.Select(e => e.Summary ?? "");
}


