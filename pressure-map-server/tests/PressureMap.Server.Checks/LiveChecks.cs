#if CLIENT_AVAILABLE
using System.Security.Cryptography;
using System.Text.Json;
using PressureMap.Core;

static class LiveChecks
{
    public static async Task Run(string url)
    {
        var root = Path.Combine(Path.GetFullPath(".test-temp"), "live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var account = "sync-check-" + Guid.NewGuid().ToString("N")[..16];
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        using var a = new CloudClient(url); using var b = new CloudClient(url);
        await a.RegisterAsync(account, "公网同步验证", password);
        await b.LoginAsync(account, password);
        if (a.User?.Id != b.User?.Id) throw new Exception("Account identity mismatch");
        var first = new Store(Path.Combine(root, "device-a")); var second = new Store(Path.Combine(root, "device-b"));
        var semester = first.CreateSemester("公网验证学期");
        semester.Courses.Add(new Course { Name = "高等数学" }); first.Save(semester);
        first.AddTodo("公网验证作业");
        first.SaveCalendar(new CalendarSource("验证课表.ics", "ics", "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//PressureMap//EN\r\nBEGIN:VEVENT\r\nUID:live-course\r\nDTSTART:20261020T080000\r\nDTEND:20261020T094000\r\nSUMMARY:高等数学\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n", ""));
        var syncA = new CloudSyncEngine(a, first); var syncB = new CloudSyncEngine(b, second);
        if (await syncA.SyncAsync() != SyncOutcome.Uploaded || await syncB.SyncAsync() != SyncOutcome.Downloaded) throw new Exception("Initial sync failed");
        if (SnapshotData.Hash(first.Snapshot()) != SnapshotData.Hash(second.Snapshot())) throw new Exception("Course/calendar/todo mismatch");
        second.AddTodo("第二台设备新增任务");
        if (await syncB.SyncAsync() != SyncOutcome.Uploaded || await syncA.SyncAsync() != SyncOutcome.Downloaded) throw new Exception("Reverse sync failed");
        using var third = new CloudClient(url); await third.LoginAsync(account, password);
        var restored = new Store(Path.Combine(root, "device-c"));
        if (await new CloudSyncEngine(third, restored).SyncAsync() != SyncOutcome.Downloaded || SnapshotData.Hash(restored.Snapshot()) != SnapshotData.Hash(first.Snapshot())) throw new Exception("Fresh device restore failed");
        await a.LogoutAsync(); await b.LogoutAsync(); await third.LogoutAsync();
        var report = new { status = "passed", url, account, checked_at = DateTimeOffset.UtcNow, checks = new[] { "register", "login", "upload courses/calendar/todos", "second device restores", "bidirectional edits", "fresh third device restores", "logout" } };
        var path = Path.Combine(root, "verification.json"); File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS live registration and three-device sync. Report: " + path);
        Console.WriteLine("Verification account: " + account);
    }
}
#endif
