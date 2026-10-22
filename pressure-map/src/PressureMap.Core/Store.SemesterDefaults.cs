using System.Text.Json;

namespace PressureMap.Core;

public sealed partial class Store
{
    public static string AcademicSemesterName(DateTime date, int offset = 0)
    {
        var year = date.Month >= 9 ? date.Year : date.Year - 1;
        var term = date.Month is >= 2 and <= 8 ? 1 : 0;
        var index = term + offset;
        year += index / 2;
        return $"{year}—{year + 1}学年第{(index % 2 == 0 ? "一" : "二")}学期";
    }

    public void EnsureDefaultSemesters(DateTime date)
    {
        using var c = Open();
        using var t = c.BeginTransaction();
        Execute(c, t, "CREATE TABLE IF NOT EXISTS app_initializations (key TEXT PRIMARY KEY)");
        if (Scalar(c, t, "SELECT count(*) FROM app_initializations WHERE key='academic-semesters-v1'") > 0) return;
        var stamp = DataJson.Stamp();
        // Insert backwards so the current semester appears first in the existing list order.
        for (var offset = 3; offset >= 0; offset--)
        {
            var name = AcademicSemesterName(date, offset);
            if (Scalar(c, t, "SELECT count(*) FROM semesters WHERE name=@p0", name) > 0) continue;
            if (Scalar(c, t, "SELECT count(*) FROM semesters") >= 50) break;
            var semester = new Semester();
            Execute(c, t, "INSERT INTO semesters(name,total_weeks,type_hours,created_at,updated_at) VALUES(@p0,@p1,@p2,@p3,@p3)",
                name, semester.TotalWeeks, JsonSerializer.Serialize(semester.TypeHours), stamp);
        }
        Execute(c, t, "INSERT INTO app_initializations(key) VALUES('academic-semesters-v1')");
        t.Commit();
    }
}
