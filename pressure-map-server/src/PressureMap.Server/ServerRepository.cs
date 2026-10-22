using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PressureMap.Server;

public sealed class ServerRepository
{
    private readonly CloudOptions options;
    private readonly TimeProvider clock;
    private readonly PasswordHasher<string> passwords = new(Options.Create(new PasswordHasherOptions { IterationCount = 210000 }));
    private readonly string dummyHash;
    public string DatabasePath => Path.Combine(options.DataDirectory, "pressure-cloud.db");
    public ServerRepository(CloudOptions options, TimeProvider? clock = null)
    {
        this.options = options; this.clock = clock ?? TimeProvider.System; options.Validate(); Directory.CreateDirectory(options.DataDirectory);
        dummyHash = passwords.HashPassword("dummy", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        using var c = Open(); Exec(c, null, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
        Exec(c, null, """
        CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY,account TEXT NOT NULL UNIQUE,name TEXT NOT NULL,password_hash TEXT NOT NULL,disabled INTEGER NOT NULL DEFAULT 0,failures INTEGER NOT NULL DEFAULT 0,locked_until TEXT,created_at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS sessions(token_hash TEXT PRIMARY KEY,user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,expires_at TEXT NOT NULL,created_at TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_sessions_user ON sessions(user_id);
        CREATE TABLE IF NOT EXISTS cloud_data(user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,revision TEXT NOT NULL,snapshot TEXT NOT NULL,updated_at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS cloud_history(sequence INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,revision TEXT NOT NULL,snapshot TEXT NOT NULL,updated_at TEXT NOT NULL,UNIQUE(user_id,revision));
        """);
    }
    private string Now => clock.GetUtcNow().ToString("O");
    private SqliteConnection Open() { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, Pooling = false, DefaultTimeout = 10 }.ToString()); c.Open(); return c; }
    private static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] values) { var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = sql; for (var i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value); return cmd; }
    private static void Exec(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] values) { using var cmd = Cmd(c, t, sql, values); cmd.ExecuteNonQuery(); }
    public static string NormalizeAccount(string account)
    {
        account = account.Trim().ToUpperInvariant();
        if (account.Length is < 3 or > 128 || account.Any(c => !(char.IsAsciiLetterOrDigit(c) || "@._+-".Contains(c)))) throw new ApiValidationException("账号需为 3～128 位字母、数字或 @._+-。"); return account;
    }
    public static void ValidatePassword(string password) { if (password.Length is < 12 or > 128 || password.Contains('\0')) throw new ApiValidationException("密码需为 12～128 字符。"); }
    public string CreateUser(string account, string name, string password)
    {
        account = NormalizeAccount(account ?? ""); password ??= ""; ValidatePassword(password); if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Contains('\0')) throw new ApiValidationException("显示名需为 1～100 字。");
        var id = Guid.NewGuid().ToString("N"); var hash = passwords.HashPassword(id, password); using var c = Open();
        try { Exec(c, null, "INSERT INTO users(id,account,name,password_hash,created_at) VALUES(@p0,@p1,@p2,@p3,@p4)", id, account, name.Trim(), hash, Now); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { throw new ApiValidationException("该账号已存在。"); }
        return id;
    }
    private static UserRecord? User(SqliteConnection c, SqliteTransaction? t, string account)
    {
        using var cmd = Cmd(c, t, "SELECT id,account,name,password_hash,disabled,failures,locked_until FROM users WHERE account=@p0", account); using var r = cmd.ExecuteReader();
        return r.Read() ? new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6)) : null;
    }
    public AuthSession? Login(string? account, string? password)
    {
        string normalized; try { normalized = NormalizeAccount(account ?? ""); } catch (ApiValidationException) { normalized = "INVALID"; }
        password ??= ""; if (password.Length > 128) return null;
        using var c = Open(); var initial = User(c, null, normalized);
        var result = passwords.VerifyHashedPassword(initial?.Id ?? "dummy", initial?.PasswordHash ?? dummyHash, password);
        if (initial is null) return null;
        using var t = c.BeginTransaction(deferred: false); var current = User(c, t, normalized)!;
        var locked = current.LockedUntil is { } until && DateTimeOffset.Parse(until) > clock.GetUtcNow();
        if (current.Disabled || locked || current.PasswordHash != initial.PasswordHash) return null;
        if (result == PasswordVerificationResult.Failed)
        {
            var failures = current.LockedUntil is not null ? 1 : current.Failures + 1;
            Exec(c, t, "UPDATE users SET failures=@p0,locked_until=@p1 WHERE id=@p2", failures, failures >= 5 ? clock.GetUtcNow().AddMinutes(15).ToString("O") : null, current.Id); t.Commit(); return null;
        }
        var hash = result == PasswordVerificationResult.SuccessRehashNeeded ? passwords.HashPassword(current.Id, password) : current.PasswordHash;
        Exec(c, t, "UPDATE users SET failures=0,locked_until=NULL,password_hash=@p0 WHERE id=@p1", hash, current.Id);
        Exec(c, t, "DELETE FROM sessions WHERE expires_at<=@p0", Now);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = clock.GetUtcNow().AddHours(options.SessionHours);
        Exec(c, t, "INSERT INTO sessions(token_hash,user_id,expires_at,created_at) VALUES(@p0,@p1,@p2,@p3)", TokenHash(token), current.Id, expires.ToString("O"), Now); t.Commit();
        return new(token, current.Id, current.Name, expires);
    }
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public (string Id, string Name)? Authenticate(string token)
    {
        if (token.Length != 43 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
        using var c = Open(); using var cmd = Cmd(c, null, "SELECT u.id,u.name FROM sessions s JOIN users u ON u.id=s.user_id WHERE token_hash=@p0 AND expires_at>@p1 AND u.disabled=0", TokenHash(token), Now); using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1)) : null;
    }
    public void Logout(string token) { using var c = Open(); Exec(c, null, "DELETE FROM sessions WHERE token_hash=@p0", TokenHash(token)); }
    public void ResetPassword(string account, string password)
    {
        var normalized = NormalizeAccount(account); ValidatePassword(password); using var c = Open(); using var t = c.BeginTransaction(deferred: false); var user = User(c, t, normalized) ?? throw new ApiValidationException("账号不存在。");
        Exec(c, t, "UPDATE users SET password_hash=@p0,failures=0,locked_until=NULL WHERE id=@p1", passwords.HashPassword(user.Id, password), user.Id); Exec(c, t, "DELETE FROM sessions WHERE user_id=@p0", user.Id); t.Commit();
    }
    public bool ChangePassword(string id, string oldPassword, string newPassword)
    {
        ValidatePassword(newPassword); using var c = Open(); using var t = c.BeginTransaction(deferred: false);
        using var cmd = Cmd(c, t, "SELECT account FROM users WHERE id=@p0 AND disabled=0", id); var account = cmd.ExecuteScalar() as string; if (account is null) return false;
        var user = User(c, t, account)!;
        if (passwords.VerifyHashedPassword(user.Id, user.PasswordHash, oldPassword) == PasswordVerificationResult.Failed) return false;
        Exec(c, t, "UPDATE users SET password_hash=@p0,failures=0,locked_until=NULL WHERE id=@p1", passwords.HashPassword(id, newPassword), id); Exec(c, t, "DELETE FROM sessions WHERE user_id=@p0", id); t.Commit(); return true;
    }
    public void DisableUser(string account)
    {
        using var c = Open(); using var t = c.BeginTransaction(deferred: false); var user = User(c, t, NormalizeAccount(account)) ?? throw new ApiValidationException("账号不存在。"); Exec(c, t, "UPDATE users SET disabled=1 WHERE id=@p0", user.Id); Exec(c, t, "DELETE FROM sessions WHERE user_id=@p0", user.Id); t.Commit();
    }
    private static JsonElement Parse(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    public CloudData Download(string id)
    {
        using var c = Open(); using var cmd = Cmd(c, null, "SELECT revision,snapshot FROM cloud_data WHERE user_id=@p0", id); using var r = cmd.ExecuteReader();
        return r.Read() ? new(r.GetString(0), Parse(r.GetString(1))) : new(null, Parse("{\"schema_version\":1,\"semesters\":[],\"desktop_todos\":[],\"course_calendars\":[]}"));
    }
    public string Upload(string id, string? expected, JsonElement snapshot)
    {
        SnapshotValidator.Validate(snapshot, options.MaxSnapshotBytes); using var c = Open(); using var t = c.BeginTransaction(deferred: false);
        string? revision = null, prior = null, updated = null;
        using (var cmd = Cmd(c, t, "SELECT revision,snapshot,updated_at FROM cloud_data WHERE user_id=@p0", id)) using (var r = cmd.ExecuteReader()) if (r.Read()) { revision = r.GetString(0); prior = r.GetString(1); updated = r.GetString(2); }
        if (revision != expected) throw new SnapshotConflictException();
        if (revision is not null) Exec(c, t, "INSERT INTO cloud_history(user_id,revision,snapshot,updated_at) VALUES(@p0,@p1,@p2,@p3)", id, revision, prior, updated);
        var next = Guid.NewGuid().ToString("N");
        Exec(c, t, "INSERT INTO cloud_data(user_id,revision,snapshot,updated_at) VALUES(@p0,@p1,@p2,@p3) ON CONFLICT(user_id) DO UPDATE SET revision=excluded.revision,snapshot=excluded.snapshot,updated_at=excluded.updated_at", id, next, snapshot.GetRawText(), Now);
        Exec(c, t, "DELETE FROM cloud_history WHERE user_id=@p0 AND sequence NOT IN (SELECT sequence FROM cloud_history WHERE user_id=@p0 ORDER BY sequence DESC LIMIT @p1)", id, options.HistoryLimit); t.Commit(); return next;
    }
    public void Backup(string destination)
    {
        destination = Path.GetFullPath(destination); if (File.Exists(destination)) throw new ApiValidationException("备份文件已存在，不会覆盖。"); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var source = Open(); using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()); target.Open(); source.BackupDatabase(target);
    }
}
