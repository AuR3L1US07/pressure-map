using System.Text.Json;

namespace PressureMap.Services;

/// <summary>Local configuration only; this type never issues network requests.</summary>
public sealed record AiApiConfiguration(string BaseUrl = "", string Model = "", string ApiKey = "")
{
    private static string FilePath(string directory) => Path.Combine(directory, "ai-api.protected");

    public static AiApiConfiguration Load(string directory)
    {
        var path = FilePath(directory);
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<AiApiConfiguration>(WindowsSecret.Transform(File.ReadAllBytes(path), true))
            ?? throw new InvalidDataException("AI API configuration is empty.");
    }

    public void Save(string directory)
    {
        var path = FilePath(directory);
        var encrypted = WindowsSecret.Transform(JsonSerializer.SerializeToUtf8Bytes(this), false);
        var temporary = path + ".tmp";
        try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Clear(string directory) => File.Delete(FilePath(directory));

    public bool IsValid => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(ApiKey);
}
