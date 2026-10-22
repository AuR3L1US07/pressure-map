using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace PressureMap.Services;

// Platform credentials and cookies stay device-local and are encrypted for the Windows user.
public sealed class OnlineCourseService(string directory) : IDisposable
{
    private readonly string root = Path.Combine(directory, "online-courses");
    private Process? worker;
    private readonly Dictionary<string, JsonNode> sessions = [];
    public bool Busy => worker is not null;
    public string LastMessage { get; private set; } = "";
    private bool stopRequested;
    internal static string? TimeoutReason(string action, double seconds, double idle) =>
        action == "authenticate" && seconds >= 65 ? "登录超时，请检查网络后重试。" :
        action != "authenticate" && ((action != "manual" && idle >= 90) || seconds >= 600) ?
        "平台长时间没有返回结果，读取已停止。请检查网络或重新登录后重试，已读取的数据保留。" : null;
    private string FileFor(string platform, string suffix)
    {
        if (platform is not ("chaoxing" or "mooc")) throw new ArgumentException("不支持的平台。");
        Directory.CreateDirectory(root);
        return Path.Combine(root, platform + suffix);
    }
    public JsonObject LoginOptions(string platform)
    {
        var file = FileFor(platform, ".options.json");
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : new JsonObject { ["saveAccount"] = true, ["autoLogin"] = true };
    }
    public bool AutoLogin(string platform) => LoginOptions(platform)["autoLogin"]?.GetValue<bool>() ?? true;
    public bool HasSavedLogin(string platform) => AutoLogin(platform) && (File.Exists(FileFor(platform, ".account")) || File.Exists(FileFor(platform, ".session")));
    public void ConfigureLogin(string platform, string username, string password, bool saveAccount, bool autoLogin)
    {
        if (saveAccount) SaveAccount(platform, username, password);
        else { Forget(platform); }
        SetLoginOptions(platform, saveAccount, autoLogin);
    }
    public void SetLoginOptions(string platform, bool saveAccount, bool autoLogin)
    {
        WriteAtomic(FileFor(platform, ".options.json"), System.Text.Encoding.UTF8.GetBytes(new JsonObject { ["saveAccount"] = saveAccount, ["autoLogin"] = autoLogin }.ToJsonString()));
        if (!autoLogin) File.Delete(FileFor(platform, ".session"));
    }
    public async Task StopAndWaitAsync()
    {
        if (!Busy) return;
        Stop();
        while (Busy) await Task.Delay(100);
    }
    public JsonObject Account(string platform)
    {
        var file = FileFor(platform, ".account");
        return File.Exists(file) ? JsonNode.Parse(WindowsSecret.Transform(File.ReadAllBytes(file), true))!.AsObject() : new();
    }
    public void SaveAccount(string platform, string username, string password)
    {
        if (Busy) throw new InvalidOperationException("请先停止当前操作。");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new ArgumentException("请输入账号和密码。");
        // A new account must never inherit another account's course observations or cookies.
        if (Account(platform)["username"]?.GetValue<string>() != username.Trim()) Forget(platform);
        WriteSecret(FileFor(platform, ".account"), new JsonObject { ["username"] = username.Trim(), ["password"] = password });
    }
    public void Forget(string platform)
    {
        if (Busy) throw new InvalidOperationException("请先停止当前操作。");
        sessions.Remove(platform);
        foreach (var suffix in new[] { ".account", ".session", ".courses.json", ".options.json" }) File.Delete(FileFor(platform, suffix));
    }
    public JsonObject Courses(string platform)
    {
        var file = FileFor(platform, ".courses.json");
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : new();
    }
    private static void WriteAtomic(string file, byte[] data)
    {
        File.WriteAllBytes(file + ".tmp", data); File.Move(file + ".tmp", file, true);
    }
    private static void WriteSecret(string file, JsonNode value) => WriteAtomic(file, WindowsSecret.Transform(System.Text.Encoding.UTF8.GetBytes(value.ToJsonString()), false));
    public async Task<string?> RunAsync(string platform, string action, Action<string> status, Action changed, JsonObject? loginCredentials = null)
    {
        if (Busy) throw new InvalidOperationException("已有操作正在进行。");
        if (action is not ("scan" or "login" or "manual" or "authenticate")) throw new ArgumentException("不支持的操作。");
        var account = loginCredentials ?? Account(platform);
        if (action is ("login" or "authenticate") && account["username"] is null) throw new InvalidOperationException("请先保存账号和密码，或选择网页登录。");
        var assets = Path.Combine(AppContext.BaseDirectory, "OnlineCourses");
        var start = new ProcessStartInfo(Path.Combine(assets, ".runtime", "node.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        start.ArgumentList.Add(Path.Combine(assets, "reader.cjs")); start.ArgumentList.Add(platform); start.ArgumentList.Add(action);
        var sessionFile = FileFor(platform, ".session");
        var state = sessions.TryGetValue(platform, out var liveState) ? liveState.DeepClone() : AutoLogin(platform) && File.Exists(sessionFile) ? JsonNode.Parse(WindowsSecret.Transform(File.ReadAllBytes(sessionFile), true)) : null;
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("无法启动浏览器组件。");
        var started = Stopwatch.StartNew();
        long lastProgress = Environment.TickCount64;
        string? timeoutReason = null;
        stopRequested = false;
        worker = process;
        LastMessage = action == "scan" ? "正在连接平台，检查登录状态…" : "正在登录…";
        status(LastMessage);
        using var loginDeadline = new System.Threading.Timer(_ =>
        {
            var seconds = started.Elapsed.TotalSeconds;
            var idle = (Environment.TickCount64 - Interlocked.Read(ref lastProgress)) / 1000d;
            var reason = TimeoutReason(action, seconds, idle);
            if (reason is null) return;
            try { if (!process.HasExited) { timeoutReason = reason; process.Kill(true); } }
            catch (InvalidOperationException) { }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        string? loginStatus = null;
        string? failure = null;
        var completed = false;
        try
        {
            var errors = process.StandardError.ReadToEndAsync(); // Drain without logging credentials or page contents.
            await process.StandardInput.WriteLineAsync(new JsonObject { ["type"] = "bootstrap", ["credentials"] = action == "manual" || action == "scan" && !AutoLogin(platform) ? null : account, ["state"] = action == "manual" ? null : state }.ToJsonString());
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var item = JsonNode.Parse(line)!.AsObject();
                Interlocked.Exchange(ref lastProgress, Environment.TickCount64);
                if (item["message"] is { } update) { LastMessage = update.GetValue<string>(); status(LastMessage); }
                switch (item["type"]?.GetValue<string>())
                {
                    case "error": failure = item["message"]?.GetValue<string>() ?? "平台读取失败，请重试。"; break;
                    case "done":
                        completed = true;
                        if (item["ok"]?.GetValue<bool>() == false) failure = item["message"]?.GetValue<string>();
                        break;
                    case "login_result":
                        loginStatus = item["status"]?.GetValue<string>();
                        if (action == "manual" && item["status"]?.GetValue<string>() == "success")
                        { File.Delete(FileFor(platform, ".account")); File.Delete(FileFor(platform, ".courses.json")); changed(); }
                        break;
                    case "session_state":
                        sessions[platform] = item["state"]!.DeepClone();
                        if (AutoLogin(platform)) WriteSecret(sessionFile, item["state"]!);
                        break;
                    case "catalog":
                    {
                        var cache = Courses(platform);
                        foreach (var course in item["courses"]!.AsArray())
                        {
                            var url = course!["url"]!.GetValue<string>();
                            if (cache[url] is JsonObject old) old["name"] = course["name"]!.DeepClone();
                            else cache[url] = course.DeepClone();
                        }
                        SaveCourses(platform, cache); changed(); break;
                    }
                    case "course":
                    {
                        var cache = Courses(platform); var url = item["url"]!.GetValue<string>();
                        var course = cache[url] as JsonObject ?? new JsonObject { ["name"] = url, ["url"] = url };
                        cache[url] = null; cache[url] = course;
                        foreach (var key in new[] { "counts", "metrics", "visits", "error" }) course[key] = item[key]?.DeepClone();
                        course["updated"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");
                        SaveCourses(platform, cache); changed(); goto default;
                    }
                    default:
                        break;
                }
            }
            await process.WaitForExitAsync(); await errors;
            if (stopRequested) { LastMessage = "已取消读取，已读取的数据保留。"; status(LastMessage); return loginStatus; }
            if (timeoutReason is not null) throw new TimeoutException(timeoutReason);
            if (failure is not null) throw new InvalidOperationException(failure);
            if (!completed && loginStatus is null) throw new InvalidOperationException("读取进程提前结束，未返回完整结果。请重试，已有记录保留。");
            if (process.ExitCode != 0) throw new InvalidOperationException("操作未完成，已读取的课程记录已保留。请检查网络和登录状态。");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); }
            finally { worker = null; }
        }
        return loginStatus;
    }
    private void SaveCourses(string platform, JsonObject cache) => WriteAtomic(FileFor(platform, ".courses.json"), System.Text.Encoding.UTF8.GetBytes(cache.ToJsonString()));
    public void Stop()
    {
        stopRequested = true;
        var pending = worker;
        try { if (pending is { HasExited: false }) pending.StandardInput.WriteLine("{\"type\":\"stop\"}"); } catch (IOException) { }
        _ = StopAfterGraceAsync(pending);
    }
    private async Task StopAfterGraceAsync(Process? pending)
    {
        await Task.Delay(8000);
        try { if (pending is not null && ReferenceEquals(worker, pending) && !pending.HasExited) pending.Kill(true); }
        catch (InvalidOperationException) { }
    }
    public void Dispose() { try { if (worker is { HasExited: false } p) p.Kill(true); } catch (InvalidOperationException) { } }
}

