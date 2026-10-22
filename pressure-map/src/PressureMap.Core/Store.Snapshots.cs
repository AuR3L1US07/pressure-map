using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace PressureMap.Core;

public sealed class SnapshotData
{
    public int SchemaVersion { get; set; } = 1;
    public List<Semester> Semesters { get; set; } = [];
    public List<Todo> DesktopTodos { get; set; } = [];
    public List<CalendarSource> CourseCalendars { get; set; } = [];
    public string Summary => $"{Semesters.Count} 个学期 · {Semesters.Sum(s => s.Courses.Count)} 门课程 · {DesktopTodos.Count} 条待办 · {CourseCalendars.Count} 份课表";
    public bool IsEmpty => Semesters.Count == 0 && DesktopTodos.Count == 0 && CourseCalendars.Count == 0;
    public static SnapshotData Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        CloudSnapshotValidator.Validate(document.RootElement, 64*1024*1024);
        var data = JsonSerializer.Deserialize<SnapshotData>(json, DataJson.Options) ?? throw new InvalidDataException("数据快照为空。");
        foreach (var semester in data.Semesters) Rules.Validate(semester);
        foreach (var todo in data.DesktopTodos) Store.ValidateTodo(todo);
        foreach (var source in data.CourseCalendars) if (CourseCalendar.Preview(source.Name, source.Content, DateTime.Today).Source.Format != source.Format) throw new InvalidDataException("课表格式不匹配。");
        return data;
    }
    public static string Hash(string json)
    {
        var root = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<SnapshotData>(json, DataJson.Options)!, DataJson.Options)!.AsObject(); root.Remove("exported_at");
        var semesters = root["semesters"]!.AsArray();
        foreach (var semester in semesters.OfType<JsonObject>())
        {
            semester.Remove("updated_at");
            var keys = new Dictionary<string,string>(); var index = 0;
            foreach (var course in semester["courses"]!.AsArray().OfType<JsonObject>()) { var key = "c" + index++; keys[course["key"]!.GetValue<string>()] = key; course["key"] = key; }
            foreach (var task in semester["tasks"]!.AsArray().OfType<JsonObject>()) task["course_key"] = keys[task["course_key"]!.GetValue<string>()];
        }
        foreach (var todo in root["desktop_todos"]?.AsArray().OfType<JsonObject>() ?? []) todo.Remove("updated_at");
        void SortList(string name, Func<JsonNode,string> key) { if (root[name] is JsonArray array) { var items = array.Where(x => x is not null).OrderBy(x => key(x!), StringComparer.Ordinal).Select(x => x!.DeepClone()).ToArray(); root[name] = new JsonArray(items); } }
        SortList("semesters", n => n["id"]!.ToString()); SortList("desktop_todos", n => n["id"]!.ToString()); SortList("course_calendars", n => n["name"]!.ToString());
        JsonNode? Canonical(JsonNode? node) => node switch { JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string,JsonNode?>(p.Key, Canonical(p.Value)))), JsonArray a => new JsonArray(a.Select(Canonical).ToArray()), _ => node?.DeepClone() };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(root)!.ToJsonString(DataJson.Compact))));
    }
}

public sealed partial class Store
{
    private (string Before, string Expected)? undoDeletion;
    public bool CanUndoDeletion => undoDeletion is not null;
    public string BackupDirectory => Path.Combine(DirectoryPath,"backups");
    public string WriteBackup(string snapshot, string reason)
    {
        Directory.CreateDirectory(BackupDirectory);
        if (reason.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))) throw new ArgumentException("Invalid backup reason.");
        var path = Path.Combine(BackupDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + reason + "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var temporary = path + ".tmp";
        try { File.WriteAllText(temporary,snapshot,new UTF8Encoding(false)); File.Move(temporary,path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
    public string Backup() => WriteBackup(Snapshot(),"manual");
    public void AutoBackup()
    {
        Directory.CreateDirectory(BackupDirectory);
        if (Directory.EnumerateFiles(BackupDirectory, DateTime.Now.ToString("yyyyMMdd") + "-*-automatic-*.json").Any()) return;
        WriteBackup(Snapshot(),"automatic");
        foreach (var file in new DirectoryInfo(BackupDirectory).GetFiles("*-automatic-*.json").OrderByDescending(f => f.Name).Skip(14)) file.Delete();
    }
    public List<string> ListBackups() => Directory.Exists(BackupDirectory) ? Directory.GetFiles(BackupDirectory,"*.json").OrderDescending(StringComparer.Ordinal).ToList() : [];
    public static string ReadBackup(string path) { if (new FileInfo(path).Length > 64*1024*1024) throw new InvalidDataException("备份超过 64 MiB。"); return File.ReadAllText(path); }
    public void UndoDeletion()
    {
        var undo = undoDeletion ?? throw new InvalidOperationException("没有可撤销的删除。");
        ApplySnapshot(undo.Before,undo.Expected); undoDeletion = null;
    }
    public void ApplySnapshot(string json, string expectedHash)
    {
        var data = SnapshotData.Parse(json);
        using var c = Open(); using var t = c.BeginTransaction(deferred:false);
        var current = Snapshot(c,t);
        if (SnapshotData.Hash(current) != expectedHash) throw new InvalidOperationException("本地数据已有新修改，请刷新后重试；未覆盖任何数据。");
        WriteBackup(current,"before-restore");
        var reminders = new List<(string Id,string? Key,string? Snooze)>();
        using (var cmd = Command(c,t,"SELECT todo_id,delivered_key,snoozed_until FROM todo_reminders")) using (var r = cmd.ExecuteReader()) while(r.Read()) reminders.Add((r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2)));
        Execute(c,t,"DELETE FROM semesters; DELETE FROM desktop_todos; DELETE FROM course_calendars;");
        foreach (var s in data.Semesters)
        {
            Execute(c,t,"INSERT INTO semesters(id,name,total_weeks,week1_monday,threshold,hours_per_week,type_hours,current_step,created_at,updated_at) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",s.Id,s.Name,s.TotalWeeks,s.Week1Monday,s.Threshold,s.HoursPerWeek,JsonSerializer.Serialize(s.TypeHours),s.CurrentStep,s.CreatedAt,s.UpdatedAt);
            var keys = new Dictionary<string,long>(); var position = 0;
            foreach (var course in s.Courses) { Execute(c,t,"INSERT INTO courses(semester_id,name,raw_text,position) VALUES(@p0,@p1,@p2,@p3)",s.Id,course.Name,course.RawText,position++); keys[course.Key] = Scalar(c,t,"SELECT last_insert_rowid()"); }
            position = 0;
            foreach (var task in s.Tasks) Execute(c,t,"INSERT INTO tasks(semester_id,course_id,name,type,week,weight,est_hours,source,position) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",s.Id,keys[task.CourseKey],task.Name,task.Kind,task.Week,task.Weight,task.EstHours,task.Source,position++);
        }
        foreach (var todo in data.DesktopTodos) { InsertTodo(c,t,todo); SaveDetails(c,t,todo); }
        foreach (var source in data.CourseCalendars) Execute(c,t,"INSERT INTO course_calendars(name,format,content,imported_at) VALUES(@p0,@p1,@p2,@p3)",source.Name,source.Format,source.Content,source.ImportedAt);
        var ids = data.DesktopTodos.Select(x => x.Id).ToHashSet();
        foreach (var r in reminders.Where(r => ids.Contains(r.Id))) Execute(c,t,"INSERT INTO todo_reminders(todo_id,delivered_key,snoozed_until) VALUES(@p0,@p1,@p2)",r.Id,r.Key,r.Snooze);
        t.Commit();
    }
    public void CompleteTodos(IEnumerable<string> ids)
    {
        using var c = Open(); using var t = c.BeginTransaction(deferred:false); var selected = ids.ToHashSet();
        foreach (var todo in ListTodos(c,t).Where(x => selected.Contains(x.Id))) { todo.Completed = true; UpdateTodo(c,t,todo); }
        t.Commit();
    }
    private static void CreateNextRepeat(SqliteConnection c, SqliteTransaction t, Todo todo)
    {
        if (!todo.IsFinished || todo.RepeatDays == 0) return;
        var source = "repeat:" + todo.Id;
        if (Scalar(c,t,"SELECT count(*) FROM desktop_todos WHERE source_key=@p0",source) > 0) return;
        if (Scalar(c,t,"SELECT count(*) FROM desktop_todos") >= 1000) throw new InvalidDataException("清理已完成任务后再完成重复任务，待办已达 1000 条。");
        var next = DateOnly.TryParseExact(todo.DueDate ?? todo.PlannedDate,"yyyy-MM-dd",out var day) ? day : DateOnly.FromDateTime(DateTime.Today);
        do { next = next.AddDays(todo.RepeatDays); } while (next <= DateOnly.FromDateTime(DateTime.Today));
        var copy = JsonSerializer.Deserialize<Todo>(JsonSerializer.Serialize(todo,DataJson.Options),DataJson.Options)!;
        copy.Id = Guid.NewGuid().ToString("N"); copy.Completed = copy.Submitted = false; copy.PlannedDate = next.ToString("yyyy-MM-dd");
        if (copy.DueDate is not null) copy.DueDate = copy.PlannedDate;
        copy.SourceKey = source; copy.SourceLabel = "重复任务"; copy.CreatedAt = copy.UpdatedAt = DataJson.Stamp();
        foreach (var step in copy.Steps) { step.Id = Guid.NewGuid().ToString("N"); step.Completed = false; }
        InsertTodo(c,t,copy); SaveDetails(c,t,copy);
    }
}
