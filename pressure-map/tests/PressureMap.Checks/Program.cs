using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using PressureMap.Core;

var total = 0;
void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); total++; Console.WriteLine("PASS: " + name); }
void Throws(Action action, string name) { try { action(); } catch { Assert(true, name); return; } throw new Exception("FAIL: " + name); }
async Task ThrowsAsync(Func<Task> action, string name) { try { await action(); } catch { Assert(true, name); return; } throw new Exception("FAIL: " + name); }
var root = Path.Combine(Environment.GetEnvironmentVariable("PRESSURE_MAP_TEST_ROOT") ?? Path.GetTempPath(), "csharp-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var defaultsRoot = Path.Combine(root, "semester-defaults");
    var defaultsStore = new Store(defaultsRoot);
    defaultsStore.EnsureDefaultSemesters(new DateTime(2026, 10, 20));
    Assert(defaultsStore.ListSemesters().Select(s => s.Name).SequenceEqual(new[] { "2026—2027学年第一学期", "2026—2027学年第二学期", "2027—2028学年第一学期", "2027—2028学年第二学期" }), "four academic semesters start with current term");
    defaultsStore.DeleteSemester(defaultsStore.ListSemesters()[0].Id);
    new Store(defaultsRoot).EnsureDefaultSemesters(new DateTime(2027, 3, 1));
    Assert(defaultsStore.ListSemesters().Count == 3, "semester initialization preserves deletion across restart");
    Assert(Store.AcademicSemesterName(new DateTime(2027, 1, 1)) == "2026—2027学年第一学期" && Store.AcademicSemesterName(new DateTime(2027, 2, 1)) == "2026—2027学年第二学期" && Store.AcademicSemesterName(new DateTime(2027, 9, 1)) == "2027—2028学年第一学期", "academic year boundaries");
    var accountDefaults = new Store(Path.Combine(root, "semester-account"));
    accountDefaults.CreateSemester("已有学期");
    accountDefaults.CreateSemester("2026—2027学年第一学期");
    accountDefaults.EnsureDefaultSemesters(new DateTime(2026, 10, 20));
    Assert(accountDefaults.ListSemesters().Count == 5 && defaultsStore.ListSemesters().Count == 3, "account defaults preserve existing terms and avoid duplicates");
    var store = new Store(root); var sample = Rules.Sample(); Rules.Validate(sample);
    var preferences = new PressureMap.Services.Preferences { DesktopEnabled = true, DesktopLocked = true, DesktopWidth = 460, Theme = "Dark" };
    preferences.Save(root); var restoredPreferences = PressureMap.Services.Preferences.Load(root);
    Assert(restoredPreferences.DesktopEnabled && restoredPreferences.DesktopLocked && restoredPreferences.DesktopWidth == 460 && restoredPreferences.Theme == "Dark", "preferences snake-case roundtrip");
    File.WriteAllText(Path.Combine(root, "settings-v2.json"), "bad json");
    Assert(PressureMap.Services.Preferences.Load(root).Theme == "Light", "corrupt preferences fallback preserves database");
    Assert(sample.Courses.Count == 4 && sample.Tasks.Count == 12, "sample parsing");
    var created = store.CreateSemester("秋季学期"); sample.Id = created.Id; sample.CreatedAt = created.CreatedAt; store.Save(sample);
    var loaded = store.Load(sample.Id);
    Assert(loaded.Tasks.Count == 12 && loaded.Courses.Count == 4, "SQLite roundtrip");
    Assert(loaded.Tasks.All(t => loaded.Courses.Any(c => c.Key == t.CourseKey)), "course-task associations");
    Assert(new Store(root).ListSemesters().Count == 1, "opening existing database preserves data");
    var pressure = Rules.Compute(loaded.Tasks, loaded.TotalWeeks);
    Assert(pressure.Total[16] == 11 && pressure.Total[17] == 10, "pressure sample totals");
    Assert(Rules.Warning(9.8, 7) == "红色预警" && Rules.Warning(7, 7) == "橙色预警", "warning thresholds");
    var simple = Rules.Compute([new StudyTask { CourseKey = "c", Week = 3, Weight = 5 }], 5);
    Assert(simple.Total[1] == 1.5 && simple.Total[2] == 3 && simple.Total[3] == 5, "pressure decay");
    Assert(Rules.Compute([new StudyTask { CourseKey = "c", Week = null, Weight = 10 }], 5).Total.All(x => x == 0), "pending tasks excluded");
    Assert(Rules.DateToWeek("01-04", "2026-12-28", 18) == 2, "cross-year dates");
    Assert(Rules.DateToWeek("02-30", "2026-09-07", 18) is null, "invalid dates excluded");
    var parsed = Rules.ParseAi("```json\n[{\"course\":\"新课程\",\"name\":\"报告\",\"type\":\"未知\",\"week\":null}]\n```", loaded);
    Assert(parsed.Added.Count == 1 && parsed.Tasks[0].Kind == "其他" && parsed.Tasks[0].Week is null, "AI JSON fences and unknown courses/types");
    Throws(() => Rules.ParseAi("[]", loaded), "empty extraction rejected");
    Throws(() => Rules.ParseAi("not json", loaded), "invalid extraction rejected");
    loaded.TotalWeeks = 0; Throws(() => store.Save(loaded), "invalid save rejected");
    Assert(store.Load(sample.Id).TotalWeeks == 18, "invalid save preserves old semester"); loaded.TotalWeeks = 18;
    loaded.Threshold = double.NaN; Throws(() => store.Save(loaded), "non-finite numbers rejected"); loaded.Threshold = 7;
    loaded.Week1Monday = "2026-09-08"; Throws(() => store.Save(loaded), "first day must be Monday"); loaded.Week1Monday = "2026-09-07"; store.Save(loaded);
    var plans = Rules.Plan(loaded); Assert(plans.Count == 11, "plan excludes pending task");
    var zero = new Semester { HoursPerWeek = 0 }; zero.Tasks.Add(new() { CourseKey = "c", Week = 2, Weight = 2, Kind = "其他", EstHours = 4 });
    Assert(Rules.Plan(zero)[0].Missing == 4, "no capacity reports missing hours");
    var key = store.AddTodo("原有待办", "2026-10-20", true); key.Completed = true; store.UpdateTodo(key);
    Assert(store.ListTodos()[0].Completed && store.ListTodos()[0].Important, "todo update persists");
    var invalid = new Todo { Id = key.Id, Title = "", DueDate = "2026-02-30" }; Throws(() => store.UpdateTodo(invalid), "invalid todo rejected");
    Assert(store.ListTodos()[0].Title == "原有待办", "failed todo update preserves data");
    var count = store.ImportTodos(loaded); Assert(count == 12, "semester tasks imported");
    Assert(store.ImportTodos(store.Load(loaded.Id)) == 0, "repeat import and changed SQL IDs do not duplicate");
    Assert(store.ListTodos().Any(t => t.DueDate == "2026-11-01"), "due date is end of due week");
    var export = Path.Combine(root, "export.json"); store.Export(export);
    using (var json = JsonDocument.Parse(File.ReadAllText(export))) Assert(json.RootElement.GetProperty("desktop_todos").GetArrayLength() == 13 && json.RootElement.GetProperty("semesters").GetArrayLength() == 1, "export contains semesters and todos");
    using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
    { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_task BEFORE INSERT ON tasks BEGIN SELECT RAISE(ABORT,'test'); END"; command.ExecuteNonQuery(); }
    loaded.Name = "不应该保存"; Throws(() => store.Save(loaded), "SQLite insert failure propagates");
    Assert(store.Load(loaded.Id).Name != "不应该保存" && store.Load(loaded.Id).Tasks.Count == 12, "transaction rollback restores semester and children");
    store.DeleteTodo(key.Id); Assert(store.ListTodos().Count == 12, "todo deletion");
    var other = new Store(Path.Combine(root, "account")); Assert(other.ListTodos().Count == 0 && other.ListSemesters().Count == 0, "account store isolation");
    using (var cloud = new CloudClient("")) await ThrowsAsync(() => cloud.LoginAsync("user", "password"), "unconfigured server fails clearly");
    using (var cloud = new CloudClient("http://example.com")) await ThrowsAsync(() => cloud.LoginAsync("user", "password"), "insecure API URL blocked");
    using (var cloud = new CloudClient("https://example.com", new FakeHttp([new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }])))
    { await ThrowsAsync(() => cloud.LoginAsync("user", "password"), "incomplete authentication response rejected"); Assert(cloud.Token is null, "invalid authentication never creates session"); }
    var responses = new FakeHttp([
        new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"token\",\"user\":{\"id\":\"a\",\"name\":\"账号\"}}", Encoding.UTF8, "application/json") },
        new(HttpStatusCode.OK) { Content = new StringContent("{\"revision\":\"r1\"}", Encoding.UTF8, "application/json") },
        new(HttpStatusCode.Conflict), new(HttpStatusCode.Unauthorized)]);
    using (var cloud = new CloudClient("https://example.com/api", responses))
    {
        var account = await cloud.LoginAsync("user", "password"); Assert(account.Id == "a" && cloud.Token == "token", "real login response validated");
        await cloud.DownloadAsync(); Assert(responses.LastAuthorization == "Bearer token" && responses.LastPath == "/api/v1/data", "authenticated cloud data endpoint");
        await ThrowsAsync(() => cloud.UploadAsync(new JsonObject(), "r0"), "cloud conflict reported");
        await ThrowsAsync(() => cloud.DownloadAsync(), "expired login reported"); Assert(cloud.Token is null && cloud.User is null, "401 clears credentials");
    }
    string Ics(string events) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//PressureMap Tests//CN\r\n" + events + "END:VCALENDAR\r\n";
    if (OperatingSystem.IsWindows())
    {
        var sessionRoot = Path.Combine(root, "saved-session"); Directory.CreateDirectory(sessionRoot);
        using var original = new CloudClient("https://example.com/api");
        original.RestoreSession("saved-token", new Account("saved-user", "Saved account"));
        PressureMap.Services.CloudSessionCache.Save(sessionRoot, original);
        Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(sessionRoot, "cloud-session.protected"))).Contains("saved-token"), "saved session is encrypted");
        var backend = new FakeHttp([new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }, new(HttpStatusCode.ServiceUnavailable), new(HttpStatusCode.Unauthorized)]);
        using var restored = new CloudClient("https://example.com/api", backend);
        Assert(PressureMap.Services.CloudSessionCache.Restore(sessionRoot, restored) && restored.User?.Id == "saved-user", "account session survives client restart");
        restored.SessionChanged += () => PressureMap.Services.CloudSessionCache.Save(sessionRoot, restored);
        await restored.DownloadAsync(); Assert(backend.LastAuthorization == "Bearer saved-token", "restored session authenticates requests");
        await ThrowsAsync(() => restored.DownloadAsync(), "temporary server failure reported");
        Assert(restored.Token is not null && File.Exists(Path.Combine(sessionRoot, "cloud-session.protected")), "temporary failure preserves saved login");
        using var different = new CloudClient("https://other.example.com/api");
        Assert(!PressureMap.Services.CloudSessionCache.Restore(sessionRoot, different) && different.Token is null, "saved credentials stay bound to API server");
        await ThrowsAsync(() => restored.DownloadAsync(), "expired restored session reported");
        Assert(!File.Exists(Path.Combine(sessionRoot, "cloud-session.protected")), "401 removes persisted credentials");
        PressureMap.Services.CloudSessionCache.Save(sessionRoot, original);
        original.SessionChanged += () => PressureMap.Services.CloudSessionCache.Save(sessionRoot, original);
        original.Logout(); Assert(!File.Exists(Path.Combine(sessionRoot, "cloud-session.protected")), "explicit logout removes persisted credentials");
    }
    string Event(string uid, string start, string end, string extra = "", string title = "高等数学") => $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTART:{start}\r\nDTEND:{end}\r\nSUMMARY:{title}\r\nLOCATION:6教北202\r\n{extra}END:VEVENT\r\n";
    var day = new DateTime(2026, 10, 19);
    var recurring = Ics(Event("math", "20261019T080500", "20261019T094000", "RRULE:FREQ=WEEKLY;COUNT=4\r\nEXDATE:20261026T080500\r\n"));
    Assert(HduPdfCalendar.ParseWeeks("2周,6-8周(双),12-16周(双)").SequenceEqual(new[] { 2, 6, 8, 12, 14, 16 }), "HDU mixed week ranges preserve per-range parity");
    Assert(HduPdfCalendar.ParseWeeks("第2周").Single() == 2 && HduPdfCalendar.ParseWeeks("2-6周,9-17周").Count == 14, "HDU single and discontinuous weeks");
    Throws(() => HduPdfCalendar.ParseWeeks("2-21周"), "HDU unsupported weeks rejected");
    Throws(() => HduPdfCalendar.ParseWeeks("2-17"), "HDU ambiguous weeks rejected");
    Assert(HduPdfCalendar.AdjustDate(new(2026, 10, 6)) == new DateTime(2026, 9, 20) && HduPdfCalendar.AdjustDate(new(2026, 10, 7)) == new DateTime(2026, 10, 10), "HDU makeup lessons use original teaching week");
    Assert(new[] { new DateTime(2026,9,25), new DateTime(2026,9,27), new DateTime(2026,10,1), new DateTime(2027,1,1), new DateTime(2027,1,10), new DateTime(2027,1,21) }.All(d => HduPdfCalendar.AdjustDate(d) is null), "HDU holidays exams and winter break excluded");
    Assert(HduPdfCalendar.AdjustDate(new(2026, 10, 24)) == new DateTime(2026, 10, 24), "HDU anniversary does not invent a suspension");
    var escapedIcs = HduPdfCalendar.ToIcs([new("长课程名" + new string('课', 60) + ",;", new(2026,10,19,8,5,0), new(2026,10,19,9,40,0), "教室;A", "教师：甲\n补课")]);
    var escapedPreview = CourseCalendar.Preview("escaped.ics", escapedIcs, day);
    Assert(escapedPreview.EventCount == 1 && escapedPreview.Upcoming.Single().Location == "教室;A" && escapedPreview.Upcoming.Single().Start == new DateTime(2026,10,19,0,5,0,DateTimeKind.Utc).ToLocalTime(), "HDU ICS UTF8 folding escaping and China timezone roundtrip");
    Assert(escapedIcs.Split("\r\n").All(l => Encoding.UTF8.GetByteCount(l) <= 75), "HDU ICS lines respect 75 octet limit");
    var hduPdfPath = Environment.GetEnvironmentVariable("HDU_TEST_PDF");
    if (!string.IsNullOrEmpty(hduPdfPath))
    {
        var pdfBytes = File.ReadAllBytes(hduPdfPath);
        var convertedPdf = HduPdfCalendar.Convert("教务课表.pdf", pdfBytes, "2026-2027-1");
        var sourcePdf = CourseCalendar.Preview(convertedPdf.Name, convertedPdf.Ics, new(2026,9,14));
        Assert(convertedPdf.LessonCount == 16 && sourcePdf.EventCount == 156, "HDU real two-page PDF extracts all 16 slots and 156 lessons");
        Assert(CourseCalendar.Day([sourcePdf.Source], new(2026,9,20)).Count == 4 && CourseCalendar.Day([sourcePdf.Source], new(2026,10,10)).Count == 2, "HDU real PDF makeup dates and even weeks");
        Assert(CourseCalendar.Day([sourcePdf.Source], new(2026,10,6)).Count == 0 && CourseCalendar.Day([sourcePdf.Source], new(2026,9,25)).Count == 0, "HDU real PDF original holiday dates empty");
        Assert(!CourseCalendar.Names(sourcePdf.Source).Any(n => n.Contains("国家安全") || n.Contains("人工智能")), "HDU other courses intentionally excluded");
        Throws(() => HduPdfCalendar.Convert("bad.pdf", pdfBytes, "unknown"), "HDU semester selection required");
        store.SaveCalendar(sourcePdf.Source); store.SaveCalendar(sourcePdf.Source);
        Assert(store.ListCalendars().Count == 1, "HDU converted PDF persists and reimports without duplication"); store.DeleteCalendar(sourcePdf.Source.Name);
    }
    var calendarPreview = CourseCalendar.Preview("课表.ics", recurring, day);
    Assert(calendarPreview.Upcoming.Count == 3, "weekly recurrence and excluded lesson");
    Assert(CourseCalendar.Day([calendarPreview.Source], day).Single().Location == "6教北202", "calendar room and local date");
    Assert(CourseCalendar.Day([calendarPreview.Source], day.AddDays(7)).Count == 0, "cancelled school date excluded");
    var fortnight = CourseCalendar.Preview("单双周.ics", Ics(Event("odd", "20261019T100000", "20261019T113500", "RRULE:FREQ=WEEKLY;INTERVAL=2;COUNT=3\r\n")), day).Source;
    Assert(CourseCalendar.Day([fortnight], day.AddDays(7)).Count == 0 && CourseCalendar.Day([fortnight], day.AddDays(14)).Count == 1, "alternating week schedule");
    var overrideIcs = Ics(Event("move", "20261019T080000", "20261019T090000", "RRULE:FREQ=WEEKLY;COUNT=2\r\n") + Event("move", "20261026T100000", "20261026T110000", "RECURRENCE-ID:20261026T080000\r\nSEQUENCE:2\r\n"));
    var moved = CourseCalendar.Preview("调课.ics", overrideIcs, day).Source;
    Assert(CourseCalendar.Day([moved], day.AddDays(7)).Single().Start.Hour == 10, "recurrence override replaces old course time");
    var simultaneous = CourseCalendar.Preview("冲突.ics", Ics(Event("a", "20261019T080000", "20261019T090000") + Event("b", "20261019T080000", "20261019T093000", title: "英语")), day).Source;
    Assert(CourseCalendar.Day([simultaneous], day).Count == 2, "simultaneous courses are retained");
    Assert(CourseCalendar.Day([calendarPreview.Source, calendarPreview.Source], day).Count == 1, "duplicate calendar occurrences merged");
    var utcSource = CourseCalendar.Preview("utc.ics", Ics(Event("utc", "20261019T000500Z", "20261019T014000Z")), day).Source;
    Assert(CourseCalendar.Day([utcSource], day).Single().Start == new DateTime(2026, 10, 19, 0, 5, 0, DateTimeKind.Utc).ToLocalTime(), "UTC calendar converts to device local time");
    var overnight = CourseCalendar.Preview("跨日.ics", Ics(Event("night", "20261018T233000", "20261019T003000")), day).Source;
    Assert(CourseCalendar.Day([overnight], day).Count == 1 && CourseCalendar.Day([overnight], day.AddDays(-1)).Count == 1, "overnight course appears on both dates");
    var classItem = CourseCalendar.Day([calendarPreview.Source], day).Single();
    Assert(classItem.State(classItem.Start) == "正在上课" && classItem.State(classItem.End) == "已结束", "course status has correct inclusive/exclusive boundaries");
    var allDay = CourseCalendar.Preview("假日.ics", Ics("BEGIN:VEVENT\r\nUID:holiday\r\nDTSTART;VALUE=DATE:20261019\r\nDTEND;VALUE=DATE:20261020\r\nSUMMARY:假日\r\nEND:VEVENT\r\n"), day);
    Assert(allDay.AllDayCount == 1 && allDay.Upcoming.Count == 0, "all-day calendar events are clearly excluded");
    Throws(() => CourseCalendar.Preview("bad.ics", "invalid", day), "malformed calendar rejected");
    Throws(() => CourseCalendar.Preview("bad.csv", "日期,课程,开始时间,结束时间\n2026-10-19,数学,10:00,09:00", day), "invalid CSV times rejected");
    var csv = "日期,课程,开始时间,结束时间,教室,教师\r\n2026-10-19,\"数学, A\",08:00,09:00,6教,王老师\r\n";
    var csvSource = CourseCalendar.Preview("csv课表.csv", csv, day).Source;
    Assert(CourseCalendar.Day([csvSource], day).Single().Title == "数学, A", "CSV quoted cells and Chinese headers");
    store.SaveCalendar(calendarPreview.Source); store.SaveCalendar(calendarPreview.Source);
    Assert(store.ListCalendars().Count == 1, "same file reimport updates instead of duplicating");
    Throws(() => store.SaveCalendar(calendarPreview.Source with { Content = "broken" }), "failed calendar import rejected before commit");
    Assert(store.ListCalendars().Single().Content == recurring, "failed calendar import preserves previous data");
    Assert(JsonDocument.Parse(store.Snapshot()).RootElement.GetProperty("course_calendars").GetArrayLength() == 1, "backup includes course calendar sources");
    var isolatedCalendarStore = new Store(Path.Combine(root, "calendar-isolation"));
    Assert(isolatedCalendarStore.ListCalendars().Count == 0, "course calendars respect account storage isolation");
    store.DeleteCalendar(calendarPreview.Source.Name); Assert(store.ListCalendars().Count == 0, "calendar removal persists");
    var workloadDay = DateTime.Today;
    var work = new Todo { Title = "数学作业", CourseName = "高等数学", PlannedDate = workloadDay.ToString("yyyy-MM-dd"), DueDate = workloadDay.ToString("yyyy-MM-dd"), DueTime = "23:59", EstimatedHours = 2, RequiresSubmission = true, TaskKind = "作业", Steps = [new() { Title = "解题" }, new() { Title = "检查" }] };
    var savedWork = store.CreateTodo(work); var reread = store.ListTodos().Single(t => t.Id == savedWork.Id);
    Assert(reread.CourseName == "高等数学" && reread.DueTime == "23:59" && reread.RequiresSubmission && reread.Steps.Count == 2 && reread.PlannedDate == workloadDay.ToString("yyyy-MM-dd"), "student task metadata persists");
    Assert(reread.Deadline == workloadDay.AddHours(23).AddMinutes(59), "precise task deadline");
    Assert(new Todo { DueDate = workloadDay.ToString("yyyy-MM-dd") }.Deadline == workloadDay.AddHours(23).AddMinutes(59), "date-only legacy task defaults to end of day");
    Throws(() => Store.ValidateTodo(new Todo { Title = "bad", DueTime = "09:00" }), "time without date rejected");
    Throws(() => Store.ValidateTodo(new Todo { Title = "bad", DueDate = workloadDay.ToString("yyyy-MM-dd"), DueTime = "24:00" }), "invalid clock time rejected");
    Throws(() => Store.ValidateTodo(new Todo { Title = "bad", RequiresSubmission = true, Submitted = true }), "submission requires completed work");
    Throws(() => Store.ValidateTodo(new Todo { Title = "bad", EstimatedHours = double.NaN }), "nonfinite task estimate rejected");
    Assert(TaskWorkload.IsToday(reread, workloadDay.AddHours(12)), "planned and due task appears in today");
    Assert(!TaskWorkload.IsToday(new Todo { Title = "未来", DueDate = workloadDay.AddDays(3).ToString("yyyy-MM-dd") }, workloadDay), "future task excluded from today");
    Assert(TaskWorkload.IsToday(new Todo { Title = "昨日未完", PlannedDate = workloadDay.AddDays(-1).ToString("yyyy-MM-dd") }, workloadDay), "unfinished planned tasks carry to today");
    Assert(TaskWorkload.Compute([], [], workloadDay).Score == 0, "empty day pressure is zero");
    var beforePressure = TaskWorkload.Compute([reread], [], workloadDay);
    Assert(beforePressure.Score is > 0 and <= 100 && beforePressure.Sources.Count == 1, "pressure score and explanation sources");
    reread.TaskKind = "考试"; Assert(TaskWorkload.Compute([reread], [], workloadDay).Score > beforePressure.Score, "exam increases equivalent pressure"); reread.TaskKind = "作业";
    reread.Steps[0].Completed = true; Assert(TaskWorkload.Compute([reread], [], workloadDay).Score < beforePressure.Score, "subtask progress lowers task pressure");
    reread.Completed = true;
    Assert(!reread.IsFinished && TaskWorkload.IsToday(reread, workloadDay), "done but unsubmitted remains actionable");
    Assert(TaskWorkload.Compute([reread], [], workloadDay).Score < beforePressure.Score && TaskWorkload.Compute([reread], [], workloadDay).Score > 0, "submission retains only small finishing workload");
    reread.Submitted = true; store.UpdateTodo(reread);
    Assert(store.ListTodos().Single(t => t.Id == reread.Id).IsFinished && TaskWorkload.Compute([reread], [], workloadDay).Score == 0, "submitted task removed from workload");
    Assert(TaskWorkload.Compute([new Todo { Title = "超负荷", PlannedDate = workloadDay.ToString("yyyy-MM-dd"), EstimatedHours = 100 }], [], workloadDay).Score == 100, "daily pressure capped at 100");
    Assert(TaskWorkload.Compute([new Todo { Title = "未安排", EstimatedHours = 10 }], [], workloadDay).Score == 0, "undated unplanned tasks excluded from daily score");
    var lessons = new[] { new CourseOccurrence("A", workloadDay.AddHours(8), workloadDay.AddHours(10), ""), new CourseOccurrence("B", workloadDay.AddHours(9), workloadDay.AddHours(11), "") };
    Assert(TaskWorkload.Compute([], lessons, workloadDay).CourseHours == 3, "overlapping class hours not double counted");
    Assert(TaskWorkload.Compute([], lessons, workloadDay, 8).Score < TaskWorkload.Compute([], lessons, workloadDay, 4).Score, "configurable capacity changes pressure");
    Throws(() => TaskWorkload.Compute([], [], workloadDay, 0), "invalid daily capacity rejected");
    var reminderTask = new Todo { Title = "交报告", DueDate = workloadDay.ToString("yyyy-MM-dd"), DueTime = "18:00", RequiresSubmission = true };
    var reminderNow = workloadDay.AddHours(17).AddMinutes(30); var reminder = ReminderRules.Due(reminderTask, null, reminderNow)!;
    Assert(reminder is not null && reminder.Key.Contains("|60|"), "one hour deadline reminder");
    Assert(ReminderRules.Due(reminderTask, new(reminder!.Key, null), reminderNow) is null, "same reminder stage deduplicates");
    Assert(ReminderRules.Due(reminderTask, new(reminder.Key, reminderNow.AddMinutes(15)), reminderNow) is null, "snoozed reminder suppressed");
    Assert(ReminderRules.Due(reminderTask, new(reminder.Key, reminderNow.AddMinutes(-1)), reminderNow) is not null, "snoozed reminder returns when elapsed");
    Assert(ReminderRules.Due(reminderTask, new(reminder.Key, null), workloadDay.AddHours(18))?.Key.Contains("|0|") == true, "deadline stage fires after earlier reminder");
    reminderTask.Completed = true; Assert(ReminderRules.Due(reminderTask, null, reminderNow)?.Message.Contains("记得提交") == true, "completed work still reminds to submit");
    reminderTask.Submitted = true; Assert(ReminderRules.Due(reminderTask, null, reminderNow) is null, "submitted task stops reminders");
    store.RecordReminder(savedWork.Id, "stage", reminderNow); Assert(store.ReminderFor(savedWork.Id)?.SnoozedUntil == reminderNow, "reminder state persists across reload");
    using (var guard = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
    {
        guard.Open(); using var command = guard.CreateCommand(); command.CommandText = "CREATE TRIGGER reject_metadata BEFORE UPDATE ON todo_details BEGIN SELECT RAISE(FAIL,'test metadata failure'); END;"; command.ExecuteNonQuery();
        var oldTitle = reread.Title; reread.Title = "不得部分保存"; Throws(() => store.UpdateTodo(reread), "metadata failure propagates");
        Assert(store.ListTodos().Single(t => t.Id == reread.Id).Title == oldTitle, "metadata and task update roll back together"); command.CommandText = "DROP TRIGGER reject_metadata"; command.ExecuteNonQuery();
    }
    store.DeleteTodo(savedWork.Id); Assert(store.ReminderFor(savedWork.Id) is null, "task removal cascades reminder state");
    var snapshotJson = store.Snapshot(); var sourceHash = SnapshotData.Hash(snapshotJson);
    var copyStore = new Store(Path.Combine(root,"restored"));
    copyStore.ApplySnapshot(snapshotJson,SnapshotData.Hash(copyStore.Snapshot()));
    Assert(SnapshotData.Hash(copyStore.Snapshot()) == sourceHash,"snapshot restore preserves data despite new SQL course IDs");
    Assert(copyStore.ListBackups().Count == 1,"restore backs up prior content");
    var deletion = copyStore.AddTodo("误删撤销"); copyStore.DeleteTodo(deletion.Id); copyStore.UndoDeletion();
    Assert(copyStore.ListTodos().Any(t => t.Id == deletion.Id),"deleted task undo preserves stable ID");
    var staleHash = SnapshotData.Hash(copyStore.Snapshot()); copyStore.AddTodo("并发本地编辑");
    Throws(() => copyStore.ApplySnapshot(snapshotJson,staleHash),"restore rejects local edits after preview");
    Assert(copyStore.ListTodos().Any(t => t.Title == "并发本地编辑"),"concurrent edit survives failed restore");
    var intactHash = SnapshotData.Hash(copyStore.Snapshot());
    Throws(() => copyStore.ApplySnapshot("{broken",intactHash),"corrupt backup rejected");
    Assert(SnapshotData.Hash(copyStore.Snapshot()) == intactHash,"corrupt backup cannot erase database");
    using (var guard = new SqliteConnection($"Data Source={copyStore.DatabasePath};Pooling=False"))
    {
        guard.Open(); using var command = guard.CreateCommand(); command.CommandText = "CREATE TRIGGER reject_restored_course BEFORE INSERT ON courses BEGIN SELECT RAISE(FAIL,'test restore failure'); END;"; command.ExecuteNonQuery();
        Throws(() => copyStore.ApplySnapshot(snapshotJson,intactHash),"restore write failure propagates");
        Assert(SnapshotData.Hash(copyStore.Snapshot()) == intactHash,"restore failure rolls back all deleted data");
        command.CommandText = "DROP TRIGGER reject_restored_course"; command.ExecuteNonQuery();
    }
    copyStore.AutoBackup(); copyStore.AutoBackup(); Assert(copyStore.ListBackups().Count(f => Path.GetFileName(f).Contains("-automatic-")) == 1,"automatic backup deduplicates within day");
    var repeat = copyStore.CreateTodo(new Todo { Title="每周报告", RepeatDays=7, RequiresSubmission=true, DueDate=DateTime.Today.ToString("yyyy-MM-dd"), Steps=[new() { Title="撰写", Completed=true }] });
    repeat.Completed=true; copyStore.UpdateTodo(repeat); Assert(!copyStore.ListTodos().Any(t => t.SourceKey == "repeat:"+repeat.Id),"repeating submission task waits for submitted state");
    repeat.Submitted=true; copyStore.UpdateTodo(repeat); copyStore.UpdateTodo(repeat);
    var nextRepeat = copyStore.ListTodos().Single(t => t.SourceKey == "repeat:"+repeat.Id);
    Assert(!nextRepeat.Completed && !nextRepeat.Submitted && nextRepeat.RepeatDays == 7 && nextRepeat.DueDate == DateTime.Today.AddDays(7).ToString("yyyy-MM-dd") && !nextRepeat.Steps[0].Completed,"repeat creates exactly one next task with reset progress");
    var batch1=copyStore.AddTodo("批量一"); var batch2=copyStore.AddTodo("批量二"); copyStore.CompleteTodos([batch1.Id,batch2.Id]);
    Assert(copyStore.ListTodos().Where(t => t.Id == batch1.Id || t.Id == batch2.Id).All(t => t.IsFinished),"batch completion persists both tasks");
    var legacyDirectory = args.Length > 0 ? args[0] : Path.Combine(root, "legacy-fixture");
    if (args.Length == 0)
    {
        Directory.CreateDirectory(legacyDirectory);
        using var connection = new SqliteConnection($"Data Source={Path.Combine(legacyDirectory, "pressure_map.db")};Pooling=False;Foreign Keys=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "python-v1.sql")); command.ExecuteNonQuery();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "python-v1-expected.json"), Path.Combine(legacyDirectory, "expected.json"));
    }
    if (Directory.Exists(legacyDirectory))
    {
        var legacy = new Store(legacyDirectory); var prior = legacy.ListSemesters().Single(); Rules.Validate(prior);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(legacyDirectory, "expected.json")));
        var expectedTotal = expected.RootElement.GetProperty("total").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert(Rules.Compute(prior.Tasks, prior.TotalWeeks).Total.SequenceEqual(expectedTotal), "Python/C# pressure parity on old database");
        var oldPlans = expected.RootElement.GetProperty("plans").EnumerateArray().ToList(); var migrated = Rules.Plan(prior);
        Assert(oldPlans.Count == migrated.Count && oldPlans.Zip(migrated).All(pair => pair.First.GetProperty("name").GetString() == pair.Second.Task.Name && pair.First.GetProperty("start").GetInt32() == pair.Second.Start && pair.First.GetProperty("missing").GetDouble() == pair.Second.Missing), "Python/C# plan parity");
        Assert(legacy.ImportTodos(prior) == 0, "Python import hashes remain compatible");
        prior.Name = "C# 已验证兼容"; legacy.Save(prior);
        Assert(legacy.ListTodos().Any(t => t.Completed), "old completed todo state retained");
    }
    // 本地缓存包含网课课程记录、账号登录状态、临时文件和日志；数据库、课表和备份必须保留。
    var cacheRoot = Path.Combine(root, "cache-check");
    var accountStore = Path.Combine(cacheRoot, "accounts", "acc1");
    foreach (var directory in new[] { Path.Combine(cacheRoot, "online-courses"), Path.Combine(accountStore, "online-courses"), Path.Combine(cacheRoot, "backups") }) Directory.CreateDirectory(directory);
    string CachePath(string file) => Path.Combine(cacheRoot, file.Replace('/', Path.DirectorySeparatorChar));
    var cached = new[] { "online-courses/chaoxing.courses.json", "online-courses/chaoxing.account", "online-courses/chaoxing.session", "online-courses/chaoxing.options.json", "online-courses/temp.tmp", "native.log", "accounts/acc1/online-courses/mooc.courses.json" };
    var kept = new[] { "pressure_map.db", "settings-v2.json", "backups/20261020-120000-000-manual-12345678.json", "accounts/acc1/pressure_map.db" };
    foreach (var file in cached.Concat(kept)) File.WriteAllText(CachePath(file), "1234");
    var cacheGroups = PressureMap.Services.LocalCache.Inspect(cacheRoot);
    Assert(cacheGroups.Count == 4 && cacheGroups.Sum(g => g.Files) == 7, "local cache inspection covers guest and account directories only");
    var cleared = PressureMap.Services.LocalCache.Clear(cacheRoot);
    Assert(cleared.Freed == 28 && cleared.Skipped == 0, "local cache clear reports freed bytes");
    Assert(cached.All(file => !File.Exists(CachePath(file))), "local cache clear removes records, sign-in state and logs");
    Assert(kept.All(file => File.Exists(CachePath(file))), "local cache clear keeps database, schedules and backups");
    Assert(PressureMap.Services.LocalCache.Inspect(cacheRoot).Count == 0, "local cache is empty after clearing");
    // 从账号目录进来时，游客目录的缓存也要一起清掉，避免退出登录后旧记录又出现。
    File.WriteAllText(CachePath("native.log"), "1234"); File.WriteAllText(Path.Combine(accountStore, "native.log"), "1234");
    Assert(PressureMap.Services.LocalCache.Clear(accountStore).Freed == 8 && !File.Exists(CachePath("native.log")) && !File.Exists(Path.Combine(accountStore, "native.log")), "local cache clear covers the guest directory from an account directory");
    Assert(PressureMap.Services.LocalCache.Size(1536) == "1.5 KB" && PressureMap.Services.LocalCache.Size(512) == "512 B", "local cache size formatting");
    PressureMap.Localization.Loc.Use("en");
    Assert(PressureMap.Localization.Loc.T("设置") == "Settings", "english dictionary covers settings");
    Assert(PressureMap.Localization.Loc.T("nav missing") == "nav missing", "unknown text falls back to source");
    Assert(PressureMap.Localization.Loc.F("第 {0} 周：{1:0.##} 小时", 3, 4.5) == "Week 3: 4.5 hours", "english template formatting");
    Assert(PressureMap.Localization.Loc.Date(new DateTime(2026, 10, 19), "M月d日", "MMM d") == "Oct 19", "date format follows language");
    PressureMap.Localization.Loc.Use("zh");
    Assert(PressureMap.Localization.Loc.T("设置") == "设置" && Rules.Warning(9.8, 7) == "红色预警", "chinese restored after english");
    Console.WriteLine($"All {total} checks passed.");
}
finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }

sealed class FakeHttp(List<HttpResponseMessage> responses) : HttpMessageHandler
{
    public string? LastAuthorization { get; private set; }
    public string? LastPath { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { LastAuthorization = request.Headers.Authorization?.ToString(); LastPath = request.RequestUri?.AbsolutePath; var response = responses[0]; responses.RemoveAt(0); return Task.FromResult(response); }
}

