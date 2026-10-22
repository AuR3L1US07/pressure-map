using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PressureMap.Core;

public sealed partial class Store
{
    public string DirectoryPath { get; }
    public string DatabasePath => Path.Combine(DirectoryPath, "pressure_map.db");
    public static string UserDirectory => Environment.GetEnvironmentVariable("PRESSURE_MAP_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "学期压力地图");
    public Store(string? directory = null)
    {
        DirectoryPath = directory ?? UserDirectory;
        Directory.CreateDirectory(DirectoryPath);
        using var connection = Open();
        using var stream = typeof(Store).Assembly.GetManifestResourceStream("PressureMap.Schema.sql")!;
        using var reader = new StreamReader(stream);
        Execute(connection, null, reader.ReadToEnd());
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, Pooling = false, DefaultTimeout = 5 }.ToString());
        connection.Open(); return connection;
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params object?[] values)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        for (var index = 0; index < values.Length; index++) command.Parameters.AddWithValue("@p" + index, values[index] ?? DBNull.Value);
        return command;
    }
    private static int Execute(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] values)
    { using var command = Command(c, t, sql, values); return command.ExecuteNonQuery(); }
    private static long Scalar(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] values)
    { using var command = Command(c, t, sql, values); return Convert.ToInt64(command.ExecuteScalar()); }
    private static string? NullableText(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));
    private static List<long> Ids(SqliteConnection c, SqliteTransaction? t)
    {
        using var command = Command(c, t, "SELECT id FROM semesters ORDER BY updated_at DESC,id DESC");
        using var reader = command.ExecuteReader(); var ids = new List<long>(); while (reader.Read()) ids.Add(reader.GetInt64(0)); return ids;
    }
    public List<Semester> ListSemesters()
    { using var c = Open(); using var t = c.BeginTransaction(deferred: true); return Ids(c, t).Select(id => Load(c, t, id)).ToList(); }
    public Semester Load(long id) { using var c = Open(); using var t = c.BeginTransaction(deferred: true); return Load(c, t, id); }
    private static Semester Load(SqliteConnection c, SqliteTransaction? t, long id)
    {
        Semester semester;
        using (var command = Command(c, t, "SELECT * FROM semesters WHERE id=@p0", id))
        using (var r = command.ExecuteReader())
        {
            if (!r.Read()) throw new InvalidDataException("学期不存在，可能已被另一个窗口删除。");
            semester = new() { Id = id, Name = r.GetString(r.GetOrdinal("name")), TotalWeeks = r.GetInt32(r.GetOrdinal("total_weeks")),
                Week1Monday = NullableText(r, "week1_monday"), Threshold = r.GetDouble(r.GetOrdinal("threshold")),
                HoursPerWeek = r.GetDouble(r.GetOrdinal("hours_per_week")), CurrentStep = r.GetInt32(r.GetOrdinal("current_step")),
                CreatedAt = r.GetString(r.GetOrdinal("created_at")), UpdatedAt = r.GetString(r.GetOrdinal("updated_at")) };
            foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, double>>(r.GetString(r.GetOrdinal("type_hours"))) ?? []) semester.TypeHours[pair.Key] = pair.Value;
        }
        using (var command = Command(c, t, "SELECT id,name,raw_text FROM courses WHERE semester_id=@p0 ORDER BY position,id", id))
        using (var r = command.ExecuteReader()) while (r.Read()) semester.Courses.Add(new() { Key = "c" + r.GetInt64(0), Name = r.GetString(1), RawText = r.GetString(2) });
        using (var command = Command(c, t, "SELECT course_id,name,type,week,weight,est_hours,source FROM tasks WHERE semester_id=@p0 ORDER BY position,id", id))
        using (var r = command.ExecuteReader()) while (r.Read())
        {
            var key = "c" + r.GetInt64(0);
            semester.Tasks.Add(new() { CourseKey = key, CourseName = semester.Courses.FirstOrDefault(x => x.Key == key)?.Name ?? "", Name = r.GetString(1), Kind = r.GetString(2),
                Week = r.IsDBNull(3) ? null : r.GetInt32(3), Weight = r.GetDouble(4), EstHours = r.IsDBNull(5) ? null : r.GetDouble(5), Source = r.GetString(6) });
        }
        return semester;
    }
    public Semester CreateSemester(string name)
    {
        var semester = new Semester { Name = name, CreatedAt = DataJson.Stamp(), UpdatedAt = DataJson.Stamp() }; Rules.Validate(semester);
        using var c = Open(); using var t = c.BeginTransaction();
        if (Scalar(c, t, "SELECT count(*) FROM semesters") >= 50) throw new InvalidDataException("最多保存 50 个学期。");
        Execute(c, t, "INSERT INTO semesters(name,total_weeks,type_hours,created_at,updated_at) VALUES(@p0,@p1,@p2,@p3,@p4)", name.Trim(), semester.TotalWeeks, JsonSerializer.Serialize(semester.TypeHours), semester.CreatedAt, semester.UpdatedAt);
        semester.Id = Scalar(c, t, "SELECT last_insert_rowid()"); t.Commit(); return semester;
    }
    public void Save(Semester semester)
    {
        Rules.Validate(semester);
        using var c = Open(); using var t = c.BeginTransaction(); var stamp = DataJson.Stamp();
        if (Execute(c, t, "UPDATE semesters SET name=@p0,total_weeks=@p1,week1_monday=@p2,threshold=@p3,hours_per_week=@p4,type_hours=@p5,current_step=@p6,updated_at=@p7 WHERE id=@p8",
            semester.Name.Trim(), semester.TotalWeeks, semester.Week1Monday, semester.Threshold, semester.HoursPerWeek, JsonSerializer.Serialize(semester.TypeHours), semester.CurrentStep, stamp, semester.Id) != 1)
            throw new InvalidDataException("学期已不存在，修改仍保留在界面中。");
        Execute(c, t, "DELETE FROM courses WHERE semester_id=@p0", semester.Id);
        var keys = new Dictionary<string, long>(); var position = 0;
        foreach (var course in semester.Courses)
        {
            Execute(c, t, "INSERT INTO courses(semester_id,name,raw_text,position) VALUES(@p0,@p1,@p2,@p3)", semester.Id, course.Name.Trim(), course.RawText, position++);
            keys[course.Key] = Scalar(c, t, "SELECT last_insert_rowid()");
        }
        position = 0;
        foreach (var task in semester.Tasks) Execute(c, t, "INSERT INTO tasks(semester_id,course_id,name,type,week,weight,est_hours,source,position) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
            semester.Id, keys[task.CourseKey], task.Name.Trim(), task.Kind, task.Week, task.Weight, task.EstHours, task.Source, position++);
        t.Commit(); semester.UpdatedAt = stamp;
    }
    public void DeleteSemester(long id) { using var c = Open(); using var t = c.BeginTransaction(deferred:false); WriteBackup(Snapshot(c,t), "before-delete-semester"); Execute(c,t,"DELETE FROM semesters WHERE id=@p0",id); t.Commit(); }
    public List<Todo> ListTodos() { using var c = Open(); return ListTodos(c, null); }
    private static List<Todo> ListTodos(SqliteConnection c, SqliteTransaction? t)
    {
        using var command = Command(c, t, "SELECT d.id,d.title,d.due_date,d.important,d.completed,d.source_key,d.source_label,d.created_at,d.updated_at,m.payload FROM desktop_todos d LEFT JOIN todo_details m ON m.todo_id=d.id ORDER BY d.completed,d.important DESC,d.due_date IS NULL,d.due_date,d.created_at,d.id");
        using var r = command.ExecuteReader(); var result = new List<Todo>();
        while (r.Read())
        {
            var item = new Todo { Id = r.GetString(0), Title = r.GetString(1), DueDate = r.IsDBNull(2) ? null : r.GetString(2), Important = r.GetBoolean(3), Completed = r.GetBoolean(4), SourceKey = r.IsDBNull(5) ? null : r.GetString(5), SourceLabel = r.GetString(6), CreatedAt = r.GetString(7), UpdatedAt = r.GetString(8) };
            if (!r.IsDBNull(9)) (JsonSerializer.Deserialize<TodoDetails>(r.GetString(9), DataJson.Options) ?? new()).Apply(item);
            result.Add(item);
        }
        return result;
    }
    public static void ValidateTodo(Todo todo)
    {
        if (string.IsNullOrWhiteSpace(todo.Title) || todo.Title.Length > 300 || todo.Title.Contains('\0')) throw new InvalidDataException("待办内容需要是 1 到 300 字。");
        if (!double.IsFinite(todo.EstimatedHours) || todo.EstimatedHours is < .1 or > 100 || !TaskWorkload.Kinds.Contains(todo.TaskKind)) throw new InvalidDataException("预计耗时需要为 0.1～100 小时，请选择有效任务类型。");
        if (todo.RepeatDays is < 0 or > 365) throw new InvalidDataException("重复间隔需要为 0～365 天。");
        if (todo.PlannedDate is not null && !DateOnly.TryParseExact(todo.PlannedDate, "yyyy-MM-dd", out _)) throw new InvalidDataException("安排日期无效。");
        if (todo.CourseName.Length > 200 || todo.Steps.Count > 20 || todo.Steps.Any(s => string.IsNullOrWhiteSpace(s.Title) || s.Title.Length > 200) || todo.Steps.Select(s => s.Id).Distinct().Count() != todo.Steps.Count) throw new InvalidDataException("关联课程最多 200 字，子任务最多 20 条，每条 1～200 字。");
        if (todo.DueTime is not null && (todo.DueDate is null || !TimeOnly.TryParseExact(todo.DueTime, "HH:mm", out _))) throw new InvalidDataException("请先填写截止日期，时间格式为 HH:mm。");
        if (todo.Submitted && (!todo.Completed || !todo.RequiresSubmission)) throw new InvalidDataException("已提交任务必须先完成，并勾选需要提交。");
        if (todo.DueDate is not null && !DateOnly.TryParseExact(todo.DueDate, "yyyy-MM-dd", out _)) throw new InvalidDataException("截止日期需要是有效的 YYYY-MM-DD 日期。");
    }
    public Todo CreateTodo(Todo todo)
    {
        todo.Id = Guid.NewGuid().ToString("N"); todo.CreatedAt = todo.UpdatedAt = DataJson.Stamp(); ValidateTodo(todo);
        using var c = Open(); using var t = c.BeginTransaction();
        if (Scalar(c, t, "SELECT count(*) FROM desktop_todos") >= 1000) throw new InvalidDataException("最多保存 1000 条待办。");
        InsertTodo(c, t, todo); SaveDetails(c, t, todo); t.Commit(); return todo;
    }
    public Todo AddTodo(string title, string? due = null, bool important = false)
    {
        var todo = new Todo { Title = title.Trim(), DueDate = due, Important = important, CreatedAt = DataJson.Stamp(), UpdatedAt = DataJson.Stamp() }; ValidateTodo(todo);
        using var c = Open(); using var t = c.BeginTransaction();
        if (Scalar(c, t, "SELECT count(*) FROM desktop_todos") >= 1000) throw new InvalidDataException("最多保存 1000 条待办，请先清理已完成的事项。");
        InsertTodo(c, t, todo); t.Commit(); return todo;
    }
    private static void InsertTodo(SqliteConnection c, SqliteTransaction t, Todo todo) => Execute(c, t,
        "INSERT INTO desktop_todos(id,title,due_date,important,completed,source_key,source_label,created_at,updated_at) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
        todo.Id, todo.Title, todo.DueDate, todo.Important, todo.Completed, todo.SourceKey, todo.SourceLabel, todo.CreatedAt, todo.UpdatedAt);
    public void UpdateTodo(Todo todo)
    {
        ValidateTodo(todo); using var c = Open(); using var t = c.BeginTransaction(); UpdateTodo(c, t, todo); t.Commit();
    }
    private static void UpdateTodo(SqliteConnection c, SqliteTransaction t, Todo todo)
    {
        ValidateTodo(todo);
        if (Execute(c, t, "UPDATE desktop_todos SET title=@p0,due_date=@p1,important=@p2,completed=@p3,updated_at=@p4 WHERE id=@p5", todo.Title.Trim(), todo.DueDate, todo.Important, todo.Completed, DataJson.Stamp(), todo.Id) != 1) throw new InvalidDataException("这条待办已不存在，请刷新后重试。");
        SaveDetails(c, t, todo); CreateNextRepeat(c, t, todo);
    }
    public void DeleteTodo(string id) { using var c = Open(); using var t = c.BeginTransaction(deferred: false); var before = Snapshot(c,t); WriteBackup(before, "before-delete"); Execute(c, t, "DELETE FROM desktop_todos WHERE id=@p0", id); undoDeletion = (before, SnapshotData.Hash(Snapshot(c,t))); t.Commit(); }
    public int ImportTodos(Semester semester)
    {
        Rules.Validate(semester); using var c = Open(); using var t = c.BeginTransaction();
        var count = Scalar(c, t, "SELECT count(*) FROM desktop_todos"); var added = 0;
        var occurrences = new Dictionary<string, int>();
        foreach (var task in semester.Tasks)
        {
            var course = semester.Courses.First(x => x.Key == task.CourseKey).Name;
            // Python json.dumps(ensure_ascii=False) 的分隔符，兼容原有来源判重哈希。
            var identity = $"[{semester.Id}, {JsonSerializer.Serialize(course, DataJson.Compact)}, {JsonSerializer.Serialize(task.Name, DataJson.Compact)}]";
            var occurrence = occurrences.GetValueOrDefault(identity); occurrences[identity] = occurrence + 1;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + occurrence))).ToLowerInvariant();
            if (Scalar(c, t, "SELECT count(*) FROM desktop_todos WHERE source_key=@p0", key) > 0) continue;
            if (count + added >= 1000) throw new InvalidDataException("导入后将超过 1000 条待办，此次导入未保存。");
            var due = task.Week is { } week && DateOnly.TryParseExact(semester.Week1Monday, "yyyy-MM-dd", out var start) ? start.AddDays(week * 7 - 1).ToString("yyyy-MM-dd") : null;
            InsertTodo(c, t, new() { Title = task.Name, DueDate = due, Important = task.Weight >= 4, SourceKey = key,
                CourseName = course, SourceLabel = $"{semester.Name} / {course} / " + (task.Week is { } w ? $"第 {w} 周" : "截止周待确认"), CreatedAt = DataJson.Stamp(), UpdatedAt = DataJson.Stamp() });
            var importedId = Command(c, t, "SELECT id FROM desktop_todos WHERE source_key=@p0", key);
            using (importedId) { var id = (string)importedId.ExecuteScalar()!; SaveDetails(c, t, new Todo { Id = id, CourseName = course, TaskKind = task.Kind == "考试" ? "考试" : task.Kind == "报告" ? "报告" : "作业", EstimatedHours = Math.Clamp(task.EstHours ?? Rules.Hours.GetValueOrDefault(task.Kind, 1), .1, 100), RequiresSubmission = task.Kind is "大作业" or "报告" }); }
            added++;
        }
        t.Commit(); return added;
    }
    public string Snapshot()
    {
        using var c = Open(); using var t = c.BeginTransaction(deferred: true);
        return Snapshot(c, t);
    }
    private static string Snapshot(SqliteConnection c, SqliteTransaction t) => JsonSerializer.Serialize(new { exported_at = DataJson.Stamp(), schema_version = 1,
            semesters = Ids(c, t).Select(id => Load(c, t, id)).ToList(), desktop_todos = ListTodos(c, t), course_calendars = ListCalendars(c, t) }, DataJson.Options);
    public void Export(string file)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, Snapshot(), new UTF8Encoding(false)); File.Move(temporary, file, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

