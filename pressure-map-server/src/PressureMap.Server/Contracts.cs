using System.Text.Json;

namespace PressureMap.Server;

public sealed class CloudOptions
{
    public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");
    public int SessionHours { get; set; } = 168;
    public int HistoryLimit { get; set; } = 5;
    public int MaxSnapshotBytes { get; set; } = 64 * 1024 * 1024;
    public bool AllowInsecureLoopback { get; set; }
    public string[] KnownProxies { get; set; } = [];
    public void Validate()
    {
        if (SessionHours is < 1 or > 720 || HistoryLimit is < 1 or > 20 || MaxSnapshotBytes is < 1024 or > 64 * 1024 * 1024) throw new InvalidOperationException("Invalid Cloud configuration.");
        DataDirectory = Path.GetFullPath(DataDirectory);
    }
}
public sealed record UserRecord(string Id, string Account, string Name, string PasswordHash, bool Disabled, int Failures, string? LockedUntil);
public sealed record AuthSession(string Token, string UserId, string Name, DateTimeOffset ExpiresAt);
public sealed record CloudData(string? Revision, JsonElement Snapshot);
public sealed record RegisterRequest(string Account, string Name, string Password);
public sealed record LoginRequest(string Account, string Password);
public sealed record UploadRequest(string? BaseRevision, JsonElement Snapshot);
public sealed record PasswordRequest(string CurrentPassword, string NewPassword);
public sealed class SnapshotConflictException : Exception { }
public sealed class ApiValidationException(string message) : Exception(message) { }
