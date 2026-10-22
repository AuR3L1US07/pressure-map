using Microsoft.Data.Sqlite;

namespace PressureMap.Core;

public sealed partial class Store
{
    public List<CalendarSource> ListCalendars() { using var c = Open(); return ListCalendars(c, null); }
    private static List<CalendarSource> ListCalendars(SqliteConnection c, SqliteTransaction? t)
    {
        using var command = Command(c, t, "SELECT name,format,content,imported_at FROM course_calendars ORDER BY name");
        using var reader = command.ExecuteReader(); var result = new List<CalendarSource>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))); return result;
    }
    public void SaveCalendar(CalendarSource source)
    {
        var validated = CourseCalendar.Preview(source.Name, source.Content, DateTime.Today);
        if (source.Format != validated.Source.Format) throw new InvalidDataException("课表格式与文件名不一致。");
        if (source.Name != Path.GetFileName(source.Name) || source.Name.Length is < 1 or > 200) throw new InvalidDataException("课表名称无效。");
        using var c = Open(); using var t = c.BeginTransaction();
        if (Scalar(c, t, "SELECT count(*) FROM course_calendars") >= 20 && Scalar(c, t, "SELECT count(*) FROM course_calendars WHERE name=@p0", source.Name) == 0) throw new InvalidDataException("最多保存 20 份课表，请先移除旧课表。");
        Execute(c, t, "INSERT INTO course_calendars(name,format,content,imported_at) VALUES(@p0,@p1,@p2,@p3) ON CONFLICT(name) DO UPDATE SET format=excluded.format,content=excluded.content,imported_at=excluded.imported_at", source.Name, source.Format, source.Content, DataJson.Stamp()); t.Commit();
    }
    public void DeleteCalendar(string name) { using var c = Open(); using var t = c.BeginTransaction(deferred:false); WriteBackup(Snapshot(c,t),"before-delete-calendar"); Execute(c,t,"DELETE FROM course_calendars WHERE name=@p0",name); t.Commit(); }
}
