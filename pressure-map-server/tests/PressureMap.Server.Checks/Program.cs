using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PressureMap.Server;

#if CLIENT_AVAILABLE
if (args.Length == 2 && args[0] == "--live") { await LiveChecks.Run(args[1]); return; }
#endif

var root = Path.Combine(Path.GetFullPath(".test-temp"), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var count = 0;
void Check(bool pass, string title) { if (!pass) throw new Exception(title); count++; Console.WriteLine("PASS " + title); }
using var rsa = RSA.Create(2048);
var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); san.AddDnsName("localhost"); certificateRequest.CertificateExtensions.Add(san.Build());
using var generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
using var cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
var app = ServerApplication.Build([], b => {
    b.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["Cloud:DataDirectory"] = root, ["Logging:LogLevel:Default"] = "Warning", ["Ai:ApiKey"] = "test-server-only-key", ["Ai:DailyLimit"] = "2" });
    b.Services.AddSingleton<IHttpClientFactory>(new FakeAiFactory());
    b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(cert)));
});
await app.StartAsync();
try {
    var repository = app.Services.GetRequiredService<ServerRepository>();
    var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    var id = repository.CreateUser("student1", "学生甲", password); var otherId = repository.CreateUser("student2", "学生乙", password);
    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.Thumbprint == cert.Thumbprint };
    using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    async Task<JsonObject> Body(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonObject>())!;
    Check((await client.GetAsync("/health")).IsSuccessStatusCode, "HTTPS health");
    Check((await client.PostAsJsonAsync("/ai/plan", new { context = "" })).StatusCode == HttpStatusCode.BadRequest, "AI rejects empty input");
    Check((await client.PostAsJsonAsync("/ai/plan", new { context = new string('x', 24001) })).StatusCode == HttpStatusCode.BadRequest, "AI bounds input");
    var aiResponse = await client.PostAsJsonAsync("/ai/plan", new { context = "{\"courses\":[],\"tasks\":[]}", english = false });
    var aiText = await aiResponse.Content.ReadAsStringAsync();
    Check(aiResponse.IsSuccessStatusCode && aiText.Contains("advice") && !aiText.Contains("test-server-only-key"), "guests receive advice without exposing provider key");
    Check((await client.PostAsJsonAsync("/ai/plan", new { context = "test" })).IsSuccessStatusCode, "AI available for a second request");
    Check((await client.PostAsJsonAsync("/ai/plan", new { context = "test" })).StatusCode == HttpStatusCode.TooManyRequests, "AI enforces global daily budget");
    await client.PostAsJsonAsync("/ai/plan", new { context = "" });
    Check((await client.PostAsJsonAsync("/ai/plan", new { context = "test" })).StatusCode == HttpStatusCode.TooManyRequests, "AI enforces IP request limit");
    Check((await client.PostAsJsonAsync("/v1/auth/register", new { account = "new-student", name = "新同学", password = "short" })).StatusCode == HttpStatusCode.BadRequest, "registration validates password");
    Check((await client.PostAsJsonAsync("/v1/auth/register", new { account = "new-student", name = "新同学", password })).StatusCode == HttpStatusCode.Created, "anonymous registration persists account");
    Check((await client.PostAsJsonAsync("/v1/auth/register", new { account = "NEW-STUDENT", name = "重复", password })).StatusCode == HttpStatusCode.BadRequest, "duplicate normalized account rejected");
    Check(repository.Login("new-student", password)?.Name == "新同学", "registered account can log in");

    Check((await client.GetAsync("/v1/data")).StatusCode == HttpStatusCode.Unauthorized, "anonymous data denied");
    Check((await client.PostAsJsonAsync("/v1/auth/login", new { account = "student1", password = "wrong" })).StatusCode == HttpStatusCode.Unauthorized, "incorrect password denied");
    var login = await client.PostAsJsonAsync("/v1/auth/login", new { account = "student1", password }); var auth = await Body(login);
    Check(login.IsSuccessStatusCode && auth["user"]!["id"]!.GetValue<string>() == id, "login matches desktop contract");
    var token = auth["access_token"]!.GetValue<string>(); client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    var empty = await Body(await client.GetAsync("/v1/data")); Check(empty["revision"] is null, "new account empty revision");
    var snapshot = JsonNode.Parse("""{"schema_version":1,"semesters":[],"desktop_todos":[{"id":"todo1","title":"数学作业","completed":false,"important":true,"task_kind":"作业","estimated_hours":2,"due_date":"2026-10-20","due_time":"18:00","steps":[{"id":"step1","title":"第一题","completed":false}]}],"course_calendars":[]}""")!;
    var uploaded = await client.PutAsJsonAsync("/v1/data", new { base_revision = (string?)null, snapshot }); var revision = (await Body(uploaded))["revision"]!.GetValue<string>();
    Check(uploaded.IsSuccessStatusCode, "upload snapshot");
    var downloaded = await Body(await client.GetAsync("/v1/data")); Check(JsonNode.DeepEquals(snapshot, downloaded["snapshot"]), "download preserves extended task fields");
    Check(repository.Download(otherId).Revision is null, "account isolation");
    var races = await Task.WhenAll(Enumerable.Range(0,2).Select(_ => client.PutAsJsonAsync("/v1/data", new { base_revision = revision, snapshot })));
    Check(races.Count(r => r.IsSuccessStatusCode) == 1 && races.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1, "concurrent uploads have one winner");
    var current = repository.Download(id).Revision;
    Check((await client.PutAsJsonAsync("/v1/data", new { base_revision = current, snapshot = new { schema_version = "bad", semesters = new object[0] } })).StatusCode == HttpStatusCode.BadRequest, "malformed snapshot rejected");
    Check(repository.Download(id).Revision == current, "invalid upload preserves stored version");
    Check((await client.PutAsync("/v1/data", new StringContent("{bad", System.Text.Encoding.UTF8, "application/json"))).StatusCode == HttpStatusCode.BadRequest, "malformed JSON rejected");
    Check((await client.PostAsJsonAsync("/v1/auth/logout", new { })).IsSuccessStatusCode, "logout");
    Check((await client.GetAsync("/v1/data")).StatusCode == HttpStatusCode.Unauthorized, "logout revokes token");
#if CLIENT_AVAILABLE
    using var firstDevice = new PressureMap.Core.CloudClient(client.BaseAddress!.ToString().TrimEnd('/'), new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.Thumbprint == cert.Thumbprint });
    using var secondDevice = new PressureMap.Core.CloudClient(client.BaseAddress.ToString().TrimEnd('/'), new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.Thumbprint == cert.Thumbprint });
    await firstDevice.RegisterAsync("desktop-register", "桌面注册", password); await secondDevice.LoginAsync("desktop-register", password);
    var firstData = await firstDevice.DownloadAsync();
    var localStore = new PressureMap.Core.Store(Path.Combine(root,"client-data"));
    var semester = localStore.CreateSemester("测试学期");
    var course = new PressureMap.Core.Course { Name = "高等数学" }; semester.Courses.Add(course);
    semester.Tasks.Add(new PressureMap.Core.StudyTask { CourseKey = course.Key, Name = "期末考试", Kind = "考试", Week = 18, Weight = 8, EstHours = 6 }); localStore.Save(semester);
    localStore.CreateTodo(new PressureMap.Core.Todo { Title = "提交报告", Completed = true, RequiresSubmission = true, Submitted = false, Steps = [new() { Title = "复核", Completed = true }] });
    localStore.SaveCalendar(new PressureMap.Core.CalendarSource("课程.ics", "ics", "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Tests//EN\r\nBEGIN:VEVENT\r\nUID:test-course\r\nDTSTART:20261020T080000\r\nDTEND:20261020T094000\r\nSUMMARY:高等数学\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n", ""));
    using var fixture = JsonDocument.Parse(localStore.Snapshot());
    SnapshotValidator.Validate(fixture.RootElement, 64*1024*1024);
    var fixtureNode = JsonNode.Parse(fixture.RootElement.GetRawText())!.AsObject();
    var next = await firstDevice.UploadAsync(fixtureNode, firstData["revision"]?.GetValue<string>());
    var secondData = await secondDevice.DownloadAsync();
    Check(secondData["revision"]!.GetValue<string>() == next["revision"]!.GetValue<string>() && JsonNode.DeepEquals(fixtureNode, secondData["snapshot"]), "two real desktop API clients share snapshots");
    repository.CreateUser("sync-student", "同步学生", password);
    using var switchA = new NetworkSwitch(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.Thumbprint == cert.Thumbprint });
    using var switchB = new NetworkSwitch(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.Thumbprint == cert.Thumbprint });
    using var cloudA = new PressureMap.Core.CloudClient(client.BaseAddress.ToString().TrimEnd('/'),switchA);
    using var cloudB = new PressureMap.Core.CloudClient(client.BaseAddress.ToString().TrimEnd('/'),switchB);
    await cloudA.LoginAsync("sync-student",password); await cloudB.LoginAsync("sync-student",password);
    var dataA = new PressureMap.Core.Store(Path.Combine(root,"device-a")); var dataB = new PressureMap.Core.Store(Path.Combine(root,"device-b"));
    dataA.CreateSemester("同步学期"); dataA.CreateTodo(new PressureMap.Core.Todo { Title="同步任务一", RepeatDays=7 });
    var syncA = new PressureMap.Core.CloudSyncEngine(cloudA,dataA); var syncB = new PressureMap.Core.CloudSyncEngine(cloudB,dataB);
    Check(await syncA.SyncAsync() == PressureMap.Core.SyncOutcome.Uploaded,"sync engine uploads first local snapshot");
    Check(await syncB.SyncAsync() == PressureMap.Core.SyncOutcome.Downloaded && dataB.ListTodos().Single().Title == "同步任务一","second device restores cloud snapshot");
    Check(await syncB.SyncAsync() == PressureMap.Core.SyncOutcome.Unchanged,"restored SQL identifiers do not cause false sync changes");
    dataB.AddTodo("离线编辑"); switchB.Offline = true; var offlineHash = PressureMap.Core.SnapshotData.Hash(dataB.Snapshot());
    try { await syncB.SyncAsync(); throw new Exception("offline should fail"); } catch (IOException) { }
    Check(PressureMap.Core.SnapshotData.Hash(dataB.Snapshot()) == offlineHash,"offline failure retains edits and baseline");
    switchB.Offline = false; Check(await syncB.SyncAsync() == PressureMap.Core.SyncOutcome.Uploaded,"offline edits upload after reconnect");
    await syncA.SyncAsync(); Check(dataA.ListTodos().Any(t => t.Title == "离线编辑"),"other device receives reconnected edits");
    dataA.AddTodo("A 的修改"); dataB.AddTodo("B 的修改"); await syncA.SyncAsync();
    Check(await syncB.SyncAsync() == PressureMap.Core.SyncOutcome.Conflict && dataB.ListTodos().Any(t => t.Title == "B 的修改"),"both changed produces conflict without overwrite");
    await syncB.ResolveAsync(false); Check(dataB.ListTodos().Any(t => t.Title == "A 的修改") && !dataB.ListTodos().Any(t => t.Title == "B 的修改") && dataB.ListBackups().Any(),"choose remote creates backup and restores remote");
    dataA.AddTodo("第二轮 A"); dataB.AddTodo("第二轮 B"); await syncA.SyncAsync(); await syncB.SyncAsync(); await syncB.ResolveAsync(true); await syncA.SyncAsync();
    Check(dataA.ListTodos().Any(t => t.Title == "第二轮 B") && dataB.ListBackups().Any(f => f.Contains("cloud-conflict")),"choose local preserves remote backup and propagates chosen version");
    var restartedSync = new PressureMap.Core.CloudSyncEngine(cloudB,new PressureMap.Core.Store(dataB.DirectoryPath));
    Check(await restartedSync.SyncAsync() == PressureMap.Core.SyncOutcome.Unchanged,"sync baseline survives restart");
    dataA.AddTodo("网络请求期间的云端修改"); await syncA.SyncAsync(); switchB.AfterDownload = () => dataB.AddTodo("下载期间的本地编辑");
    try { await syncB.SyncAsync(); throw new Exception("stale local should fail"); } catch (InvalidOperationException) { }
    Check(dataB.ListTodos().Any(t => t.Title == "下载期间的本地编辑"),"download cannot overwrite edits made during request");
    await syncB.SyncAsync(); await syncB.ResolveAsync(false);
    var beforeBroken = PressureMap.Core.SnapshotData.Hash(dataB.Snapshot()); switchB.OverrideDownload = "{\"revision\":\"bad\",\"snapshot\":{\"schema_version\":1,\"semesters\":null}}";
    try { await syncB.SyncAsync(); throw new Exception("bad snapshot should fail"); } catch (InvalidDataException) { }
    Check(PressureMap.Core.SnapshotData.Hash(dataB.Snapshot()) == beforeBroken,"invalid remote cannot erase local data"); switchB.OverrideDownload = null;
    dataB.AddTodo("响应丢失后的任务"); switchB.DropUploadReply = true;
    try { await syncB.SyncAsync(); throw new Exception("lost response should fail"); } catch (IOException) { }
    Check(await syncB.SyncAsync() == PressureMap.Core.SyncOutcome.Unchanged,"lost upload response reconciles without duplicate upload");
    await syncA.SyncAsync(); dataA.AddTodo("上传竞争"); switchA.BeforeUpload = () => { var remote = repository.Download(cloudA.User!.Id); repository.Upload(cloudA.User.Id,remote.Revision,remote.Snapshot); };
    Check(await syncA.SyncAsync() == PressureMap.Core.SyncOutcome.Conflict,"server CAS race becomes a recoverable client conflict");
    await syncA.ResolveAsync(true);


#endif
    var second = repository.Login("student1", password)!; var replacement = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    repository.ResetPassword("student1", replacement); Check(repository.Authenticate(second.Token) is null && repository.Login("student1", password) is null, "password reset revokes sessions and old password");
    var third = repository.Login("student1", replacement)!; Check(third is not null, "new password works");
    var restarted = new ServerRepository(new CloudOptions { DataDirectory = root }); Check(restarted.Download(id).Revision == current, "database survives service recreation");
    var backup = Path.Combine(root,"backup.db"); repository.Backup(backup); Check(File.Exists(backup) && new FileInfo(backup).Length > 0, "online SQLite backup");
    repository.DisableUser("student1"); Check(repository.Authenticate(third!.Token) is null && repository.Login("student1", replacement) is null, "disabled account blocked");
    for (var i=0;i<5;i++) repository.Login("student2", "wrong"); Check(repository.Login("student2", password) is null, "persistent password lockout");
    var fakeClock = new TestClock(); var expiryRepo = new ServerRepository(new CloudOptions { DataDirectory = Path.Combine(root,"expiry"), SessionHours = 1 }, fakeClock);
    expiryRepo.CreateUser("expiry-test", "过期测试", password); var expiring = expiryRepo.Login("expiry-test", password)!;
    fakeClock.Current = fakeClock.Current.AddHours(2); Check(expiryRepo.Authenticate(expiring.Token) is null, "expired session rejected");
    Console.WriteLine($"{count} checks passed. Temporary database: {root}");
} finally { await app.StopAsync(); await app.DisposeAsync(); }

sealed class TestClock : TimeProvider { public DateTimeOffset Current = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Current; }

sealed class NetworkSwitch(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public bool Offline, DropUploadReply;
    public Action? BeforeUpload;
    public Action? AfterDownload;
    public string? OverrideDownload;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("simulated offline");
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/v1/data") && OverrideDownload is not null) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OverrideDownload,System.Text.Encoding.UTF8,"application/json") };
        if (request.Method == HttpMethod.Put) { var action=BeforeUpload; BeforeUpload=null; action?.Invoke(); }
        var response = await base.SendAsync(request,cancellationToken);
        if (request.Method == HttpMethod.Put && DropUploadReply) { DropUploadReply=false; response.Dispose(); throw new HttpRequestException("simulated lost response"); }
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/v1/data")) { var action=AfterDownload; AfterDownload=null; action?.Invoke(); }
        return response;
    }
}

sealed class FakeAiFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new FakeAiHandler());
}

sealed class FakeAiHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellation);
        if (request.RequestUri!.Host != "ark.cn-beijing.volces.com" || request.Headers.Authorization?.Parameter != "test-server-only-key" || !body.Contains("deepseek-v4-1-flash-261017") || !body.Contains("max_tokens"))
            throw new Exception("AI provider request contract is invalid");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { choices = new[] { new { message = new { content = "Study advice" } } } }) };
    }
}
