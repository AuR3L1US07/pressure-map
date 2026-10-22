using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace PressureMap.Core;

public sealed record HduSemester(string Id, string Name, DateTime WeekOne)
{
    /// <summary>教务 PDF 首页上的学期标识，格式为“2026-2027学年第1学期”。</summary>
    public string Token
    {
        get { var parts = Id.Split('-'); return parts.Length == 3 ? $"{parts[0]}-{parts[1]}学年第{parts[2]}学期" : Name; }
    }
    public override string ToString() => Name;
}
public sealed record HduPdfResult(string Name, string Ics, int LessonCount, IReadOnlyList<string> Warnings);
/// <summary>PDF 导入前的识别结果：是否杭电教务课表，以及对应的学期预设（识别不出学期时为 null）。</summary>
public sealed record HduPdfProbe(bool IsSchedule, HduSemester? Semester);

public static class HduPdfCalendar
{
    public const int MaxPdfBytes = 10 * 1024 * 1024;
    public static IReadOnlyList<HduSemester> Semesters { get; } = [new("2026-2027-1", "杭电 2026—2027 学年第一学期", new(2026, 9, 14))];
    public const string CalendarSummary = "第1周：2026年9月14日；新生9月21日（第2周）起上课。\n中秋9月25—27日、国庆10月1—7日、元旦1月1日停课。\n9月20日补10月6日课，10月10日补10月7日课。\n2027年1月10—20日停课考试，1月21日起寒假。\n10月24日为校庆，校历未注明停课，按原课表安排。";
    private static readonly string[] Starts = ["08:05", "08:55", "10:00", "10:50", "11:40", "13:30", "14:20", "15:15", "16:05", "18:30", "19:20", "20:10"];
    private static readonly string[] Ends = ["08:50", "09:40", "10:45", "11:35", "12:25", "14:15", "15:05", "16:00", "16:50", "19:15", "20:05", "20:55"];
    public static DateTime? AdjustDate(DateTime nominal)
    {
        var date = nominal.Date;
        if (date == new DateTime(2026, 10, 6)) return new(2026, 9, 20);
        if (date == new DateTime(2026, 10, 7)) return new(2026, 10, 10);
        if (date >= new DateTime(2026, 9, 25) && date <= new DateTime(2026, 9, 27) ||
            date >= new DateTime(2026, 10, 1) && date <= new DateTime(2026, 10, 7) ||
            date == new DateTime(2027, 1, 1) || date >= new DateTime(2027, 1, 10) ||
            date == new DateTime(2026, 9, 20) || date == new DateTime(2026, 10, 10)) return null;
        return date;
    }
    public static IReadOnlyList<int> ParseWeeks(string expression)
    {
        var normalized = Regex.Replace(expression, @"\s+", "").Replace('（', '(').Replace('）', ')').Replace('，', ',').Replace('、', ',').Replace("第", "");
        var weeks = new SortedSet<int>();
        foreach (var part in normalized.Split(','))
        {
            var m = Regex.Match(part, @"^(\d+)(?:-(\d+))?周(?:\(([单双])\))?$");
            if (!m.Success) throw new InvalidDataException($"无法识别周次：{expression}");
            var first = int.Parse(m.Groups[1].Value); var last = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : first;
            if (first < 1 || last > 20 || last < first) throw new InvalidDataException($"周次超出当前学期范围：{expression}");
            for (var w = first; w <= last; w++) if (!m.Groups[3].Success || w % 2 == (m.Groups[3].Value == "单" ? 1 : 0)) weeks.Add(w);
        }
        if (weeks.Count == 0) throw new InvalidDataException($"周次没有有效教学周：{expression}");
        return weeks.ToArray();
    }
    /// <summary>判断 PDF 是否为杭电教务课表，并尽量匹配到已有学期预设；无法识别时只返回是否可解析。</summary>
    public static HduPdfProbe Probe(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxPdfBytes) return new(false, null);
        try
        {
            using var document = PdfDocument.Open(bytes);
            if (document.NumberOfPages is < 1 or > 10) return new(false, null);
            var text = Regex.Replace(document.GetPage(1).Text, @"\s+", "");
            var semester = Semesters.FirstOrDefault(s => text.Contains(s.Token));
            var known = semester is not null || text.Contains("杭州电子科技大学") || text.Contains("学年第");
            return new(known, semester);
        }
        catch { return new(false, null); }
    }
    public static HduPdfResult Convert(string name, byte[] bytes, string semesterId)
    {
        var semester = Semesters.SingleOrDefault(s => s.Id == semesterId) ?? throw new InvalidDataException("请选择支持的学期。");
        if (bytes.Length > MaxPdfBytes) throw new InvalidDataException("PDF 不能超过 10 MB。");
        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages > 10) throw new InvalidDataException("课表 PDF 最多支持 10 页。");
        var first = document.GetPage(1);
        if (!Regex.Replace(first.Text, @"\s+", "").Contains(semester.Token)) throw new InvalidDataException("PDF 学期与所选学期不符，或不是可提取文字的杭电教务课表。");
        var header = first.Letters.GroupBy(l => Math.Round(l.StartBaseLine.Y / 3) * 3)
            .Select(g => g.OrderBy(l => l.StartBaseLine.X).ToArray()).FirstOrDefault(g => string.Concat(g.Select(l => l.Value)).Contains("星期一"))
            ?? throw new InvalidDataException("未找到星期表头，暂不支持此 PDF 版式。");
        var headerText = string.Concat(header.Select(l => l.Value));
        var centers = new double[7];
        for (var d = 0; d < 7; d++)
        {
            var index = headerText.IndexOf("星期" + "一二三四五六日"[d], StringComparison.Ordinal);
            if (index < 0 || header.Any(l => l.Value.Length != 1)) throw new InvalidDataException("无法识别课表星期列。");
            centers[d] = (header[index].BoundingBox.Left + header[index + 2].BoundingBox.Right) / 2;
        }
        var width = centers[1] - centers[0];
        var columns = Enumerable.Range(0, 7).Select(_ => new StringBuilder()).ToArray();
        foreach (var page in document.GetPages())
        {
            var footer = page.Letters.GroupBy(l => Math.Round(l.StartBaseLine.Y / 3) * 3)
                .Select(g => (Y: g.Key, Text: string.Concat(g.OrderBy(l => l.StartBaseLine.X).Select(l => l.Value))))
                .FirstOrDefault(l => l.Text.Contains("其他课程"));
            for (var d = 0; d < 7; d++)
            {
                var letters = page.Letters.Where(l => l.StartBaseLine.X >= centers[d] - width / 2 && l.StartBaseLine.X < centers[d] + width / 2 &&
                    (page.Number != 1 || l.StartBaseLine.Y < header[0].StartBaseLine.Y - 5) && (footer.Text is null || l.StartBaseLine.Y > footer.Y + 5));
                foreach (var line in letters.GroupBy(l => Math.Round(l.StartBaseLine.Y / 3) * 3).OrderByDescending(g => g.Key))
                    columns[d].AppendLine(string.Concat(line.OrderBy(l => l.StartBaseLine.X).Select(l => l.Value)));
            }
        }
        var events = new List<CourseOccurrence>(); var lessons = 0;
        var pattern = @"(?<title>[^\r\n]+)\r?\n\((?<start>\d+)(?:-(?<end>\d+))?节\)(?<weeks>[^/]+)/\s*校\s*区\s*:(?<campus>[^/]+)/\s*场\s*地\s*:(?<room>[^/]+)/\s*教\s*师\s*:(?<teacher>[^/]+)/\s*教\s*学\s*班\s*:";
        for (var d = 0; d < 7; d++)
        {
            var text = columns[d].ToString(); var matches = Regex.Matches(text, pattern);
            if (Regex.Matches(text, @"\(\d+(?:-\d+)?节\)").Count != matches.Count) throw new InvalidDataException("有课程信息未能完整解析，请检查 PDF 版式，未导入任何课程。");
            foreach (Match m in matches)
            {
                string Field(string key) => Regex.Replace(m.Groups[key].Value, @"\s+", "");
                var start = int.Parse(Field("start")); var end = m.Groups["end"].Success ? int.Parse(Field("end")) : start;
                if (start < 1 || end > 12 || end < start) throw new InvalidDataException("课程节次无效。");
                if (Field("campus") != "下沙") throw new InvalidDataException("当前预设仅支持下沙校区作息，请勿使用其他校区课表。");
                foreach (var week in ParseWeeks(Field("weeks")))
                {
                    var nominal = semester.WeekOne.AddDays((week - 1) * 7 + d); var date = AdjustDate(nominal);
                    if (date is null) continue;
                    var description = $"教师：{Field("teacher")}\n校区：{Field("campus")}\n第{week}周 · 第{start}—{end}节" + (date != nominal ? $"\n调课：补{nominal:yyyy-MM-dd}课程" : "");
                    events.Add(new(m.Groups["title"].Value.Trim(), date.Value.Add(TimeSpan.Parse(Starts[start - 1], CultureInfo.InvariantCulture)), date.Value.Add(TimeSpan.Parse(Ends[end - 1], CultureInfo.InvariantCulture)), Field("room"), description));
                }
                lessons++;
            }
        }
        if (lessons == 0 || events.Count == 0) throw new InvalidDataException("未识别到有具体上课时间的课程，请使用教务处导出的文字版 PDF。");
        var warnings = new List<string>();
        return new(Path.GetFileNameWithoutExtension(name) + ".ics", ToIcs(events), lessons, warnings);
    }
    public static string ToIcs(IEnumerable<CourseOccurrence> events)
    {
        var result = new StringBuilder();
        void Line(string value)
        {
            var size = 0;
            foreach (var rune in value.EnumerateRunes()) { var length = rune.Utf8SequenceLength; if (size + length > 75) { result.Append("\r\n "); size = 1; } result.Append(rune); size += length; }
            result.Append("\r\n");
        }
        string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
        Line("BEGIN:VCALENDAR"); Line("VERSION:2.0"); Line("PRODID:-//PressureMap//HDU Calendar//CN"); Line("CALSCALE:GREGORIAN");
        foreach (var e in events.DistinctBy(e => (e.Title, e.Start, e.End, e.Location)).OrderBy(e => e.Start))
        {
            var hash = System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{e.Title}|{e.Start:O}|{e.End:O}|{e.Location}")));
            Line("BEGIN:VEVENT"); Line($"UID:{hash}@hdu.pressuremap"); Line($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
            // UTC avoids reliance on a consumer's timezone database or floating-time defaults.
            Line($"DTSTART:{e.Start.AddHours(-8):yyyyMMdd'T'HHmmss'Z'}"); Line($"DTEND:{e.End.AddHours(-8):yyyyMMdd'T'HHmmss'Z'}");
            Line("SUMMARY:" + Escape(e.Title)); Line("LOCATION:" + Escape(e.Location)); Line("DESCRIPTION:" + Escape(e.Description)); Line("END:VEVENT");
        }
        Line("END:VCALENDAR"); return result.ToString();
    }
}
