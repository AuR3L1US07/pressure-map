using System.Text.Json;
using PressureMap.Core;

namespace PressureMap.Services;

public static class CloudSessionCache
{
    private sealed record Session(string BaseUrl, string Token, Account User);
    private static string FilePath(string directory) => Path.Combine(directory, "cloud-session.protected");
    public static bool Restore(string directory, CloudClient client)
    {
        var path = FilePath(directory);
        if (!File.Exists(path)) return false;
        var session = JsonSerializer.Deserialize<Session>(WindowsSecret.Transform(File.ReadAllBytes(path), true));
        if (session is null || session.User is null || !string.Equals(session.BaseUrl, client.BaseUrl, StringComparison.Ordinal)) return false;
        client.RestoreSession(session.Token, session.User);
        return true;
    }
    public static void Save(string directory, CloudClient client)
    {
        var path = FilePath(directory);
        if (client.Token is null || client.User is null) { File.Delete(path); return; }
        var bytes = WindowsSecret.Transform(JsonSerializer.SerializeToUtf8Bytes(new Session(client.BaseUrl, client.Token, client.User)), false);
        var temporary = path + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
