using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;

namespace PressureMap.Core;

public sealed record Account(string Id, string Name);
public sealed class CloudConflictException : InvalidOperationException { public CloudConflictException() : base("云端数据已更新，请处理冲突后重试。") { } }
public sealed class CloudClient : IDisposable
{
    private readonly HttpClient client;
    public string BaseUrl { get; }
    public string? Token { get; private set; }
    public Account? User { get; private set; }
    public event Action? SessionChanged;
    public void RestoreSession(string token, Account user)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(user.Id))
            throw new InvalidDataException("保存的登录状态无效。");
        Token = token; User = user;
    }
    public CloudClient(string? url = null, HttpMessageHandler? handler = null)
    {
        BaseUrl = (url ?? Environment.GetEnvironmentVariable("PRESSURE_MAP_API_URL") ?? "https://121.43.32.85/api").TrimEnd('/');
        client = new(handler ?? CreateHandler()) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 70*1024*1024 };
    }
    private static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
            errors == SslPolicyErrors.None ||
            (request.RequestUri is { Scheme: "https", Host: "121.43.32.85", Port: 443 } &&
             certificate is not null && errors == SslPolicyErrors.RemoteCertificateChainErrors &&
             DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime() && DateTime.UtcNow <= certificate.NotAfter.ToUniversalTime() &&
             certificate.GetCertHashString(HashAlgorithmName.SHA256) == "ED1232C2FB02BC34F3E7C059E8222492CA86680A688469E99B5C60D8A782816F")
    };
    private async Task<JsonObject> Send(HttpMethod method, string path, object? payload, bool authenticated, CancellationToken cancellation)
    {
        if (BaseUrl.Length == 0) throw new InvalidOperationException("尚未填写云服务地址，请先使用本地模式。");
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https" || baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
            throw new InvalidOperationException("云服务地址必须是有效的 HTTPS 地址。");
        if (authenticated && Token is null) throw new InvalidOperationException("请先登录。");
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (payload is not null) request.Content = JsonContent.Create(payload, options: DataJson.Compact);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, cancellation); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new IOException("云服务请求超时，请稍后重试。"); }
        catch (HttpRequestException) { throw new IOException("无法连接云服务，请检查网络后重试。"); }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized) { Logout(); throw new InvalidOperationException("账号或密码不正确，或登录已过期。"); }
            if (response.StatusCode == HttpStatusCode.Conflict) throw new CloudConflictException();
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                JsonObject? detail = null;
                try { detail = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation); } catch (System.Text.Json.JsonException) { }
                throw new InvalidOperationException(detail?["message"]?.GetValue<string>() ?? "提交的信息无效，请检查后重试。");
            }
            if ((int)response.StatusCode == 429) throw new IOException("操作过于频繁，请一分钟后重试。");
            if (!response.IsSuccessStatusCode) throw new IOException("云服务暂时不可用，请稍后重试。");
            try { return await response.Content.ReadFromJsonAsync<JsonObject>(cancellation) ?? throw new IOException("云服务返回了空数据。"); }
            catch (System.Text.Json.JsonException) { throw new IOException("云服务返回了无效数据。"); }
        }
    }
    public async Task<Account> RegisterAsync(string account, string name, string password, CancellationToken cancellation = default)
    {
        await Send(HttpMethod.Post, "/v1/auth/register", new { account, name, password }, false, cancellation);
        return await LoginAsync(account, password, cancellation);
    }
    public async Task<Account> LoginAsync(string account, string password, CancellationToken cancellation = default)
    {
        var json = await Send(HttpMethod.Post, "/v1/auth/login", new { account, password }, false, cancellation);
        if (json["access_token"] is not JsonValue tokenNode || !tokenNode.TryGetValue<string>(out var token) || string.IsNullOrWhiteSpace(token) ||
            json["user"] is not JsonObject user || user["id"] is not JsonValue idNode || !idNode.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id))
            throw new IOException("登录响应缺少账户信息。");
        Token = token; User = new(id, user["name"] is JsonValue name && name.TryGetValue<string>(out var display) ? display : id); SessionChanged?.Invoke(); return User;
    }
    public Task<JsonObject> DownloadAsync(CancellationToken cancellation = default) => Send(HttpMethod.Get, "/v1/data", null, true, cancellation);
    public async Task<string> GenerateAiPlanAsync(string context, bool english, CancellationToken cancellation = default)
    {
        using var aiClient = new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromSeconds(90) };
        using var response = await aiClient.PostAsJsonAsync(BaseUrl + "/ai/plan", new { context, english }, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException((int)response.StatusCode == 429
                ? (english ? "AI service is busy or its daily limit has been reached. Try later." : "AI 请求较多或今日额度已用完，请稍后重试。")
                : (english ? "AI service is temporarily unavailable. Try again later." : "AI 服务暂时不可用，请稍后重试。"));
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation);
        var advice = json?["advice"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(advice)) throw new IOException("AI 未返回建议，请重试。");
        return advice;
    }
    public Task<JsonObject> UploadAsync(JsonObject snapshot, string? revision, CancellationToken cancellation = default) => Send(HttpMethod.Put, "/v1/data", new { base_revision = revision, snapshot }, true, cancellation);
    public string AccountDirectory(string localDirectory)
    {
        if (User is null) throw new InvalidOperationException("请先登录。");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BaseUrl + "\n" + User.Id))).ToLowerInvariant();
        return Path.Combine(localDirectory, "accounts", hash);
    }
    public async Task LogoutAsync(CancellationToken cancellation = default) { try { if (Token is not null) await Send(HttpMethod.Post, "/v1/auth/logout", new { }, true, cancellation); } finally { Logout(); } }
    public void Logout() { Token = null; User = null; SessionChanged?.Invoke(); }
    public void Dispose() => client.Dispose();
}
