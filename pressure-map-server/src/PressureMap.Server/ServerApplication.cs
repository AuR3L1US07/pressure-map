using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;

namespace PressureMap.Server;

public static class ServerApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var options = builder.Configuration.GetSection("Cloud").Get<CloudOptions>() ?? new(); options.Validate();
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = options.MaxSnapshotBytes + 16384L);
        builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower; o.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping; o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; o.SerializerOptions.MaxDepth = 32; });
        builder.Services.AddSingleton(options); builder.Services.AddSingleton<ServerRepository>();
        builder.Services.AddSingleton<AiPlanningService>();
        builder.Services.AddHttpClient("ai-planning", c => c.Timeout = TimeSpan.FromSeconds(75))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.Configure<ForwardedHeadersOptions>(o => {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            foreach (var address in options.KnownProxies) o.KnownProxies.Add(IPAddress.Parse(address));
        });
        builder.Services.AddRateLimiter(o => {
            o.RejectionStatusCode = 429;
            o.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy("data", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        _ = app.Services.GetRequiredService<ServerRepository>();
        app.UseForwardedHeaders();
        app.Use(async (context, next) => {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.XContentTypeOptions = "nosniff";
            if (context.Request.Path != "/health" && !context.Request.IsHttps && !(options.AllowInsecureLoopback && context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip))) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "https_required" }); return; }
            try { await next(context); }
            catch (ApiValidationException e) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "invalid_data", message = e.Message }); }
            catch (SnapshotConflictException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "revision_conflict", message = "云端数据已更新，请下载后处理冲突。" }); }
            catch (BadHttpRequestException e) { context.Response.StatusCode = e.StatusCode; await context.Response.WriteAsJsonAsync(new { error = "invalid_request" }); }
            catch (JsonException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "invalid_json" }); }
            catch (SqliteException) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "storage_unavailable" }); }
        });
        app.Use(async (context, next) => {
            if (context.Request.Path.StartsWithSegments("/v1") && context.Request.Path != "/v1/auth/login" && context.Request.Path != "/v1/auth/register") {
                var auth = context.Request.Headers.Authorization.ToString();
                var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..] : "";
                var user = context.RequestServices.GetRequiredService<ServerRepository>().Authenticate(token);
                if (user is null) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "unauthorized" }); return; }
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Value.Id), new Claim(ClaimTypes.Name, user.Value.Name)], "Bearer"));
            }
            await next(context);
        });
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/ai/plan", (AiPlanRequest request, AiPlanningService service, CancellationToken cancellation) => service.Generate(request, cancellation)).RequireRateLimiting("ai");
        app.MapPost("/v1/auth/register", (RegisterRequest request, ServerRepository repository) => {
            repository.CreateUser(request.Account, request.Name, request.Password);
            return Results.Json(new { ok = true }, statusCode: 201);
        }).RequireRateLimiting("login");
        app.MapPost("/v1/auth/login", (LoginRequest request, ServerRepository repository) => {
            var session = repository.Login(request.Account, request.Password);
            return session is null ? Results.Json(new { error = "invalid_credentials" }, statusCode: 401) : Results.Ok(new { access_token = session.Token, token_type = "Bearer", expires_at = session.ExpiresAt, user = new { id = session.UserId, name = session.Name } });
        }).RequireRateLimiting("login");
        app.MapGet("/v1/auth/me", (HttpContext c) => Results.Ok(new { id = c.User.FindFirstValue(ClaimTypes.NameIdentifier), name = c.User.Identity!.Name })).RequireRateLimiting("data");
        app.MapPost("/v1/auth/logout", (HttpContext c, ServerRepository r) => { r.Logout(c.Request.Headers.Authorization.ToString()[7..]); return Results.Ok(new { ok = true }); }).RequireRateLimiting("data");
        app.MapPost("/v1/auth/password", (PasswordRequest request, HttpContext c, ServerRepository r) => {
            if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 128 || request.NewPassword is null) return Results.BadRequest(new { error = "invalid_password" });
            return r.ChangePassword(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.CurrentPassword, request.NewPassword) ? Results.Ok(new { ok = true, login_required = true }) : Results.Json(new { error = "invalid_credentials" }, statusCode: 401);
        }).RequireRateLimiting("login");
        app.MapGet("/v1/data", (HttpContext c, ServerRepository r) => Results.Ok(r.Download(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!))).RequireRateLimiting("data");
        app.MapPut("/v1/data", (UploadRequest request, HttpContext c, ServerRepository r) => Results.Ok(new { revision = r.Upload(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.BaseRevision, request.Snapshot) })).RequireRateLimiting("data");
        return app;
    }
}
