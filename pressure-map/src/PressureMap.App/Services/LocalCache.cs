namespace PressureMap.Services;

/// <summary>
/// 本机缓存：网课课程记录、已保存的网课账号与登录状态、临时文件与运行日志。
/// 学期与任务数据、课表、备份都在数据库或 backups 里，清缓存不会碰它们。
/// 同时覆盖同一数据根下的游客目录和各账号目录，避免切换账号后旧缓存又冒出来。
/// </summary>
public static class LocalCache
{
    public sealed record Group(string Label, long Bytes, int Files);
    public readonly record struct ClearResult(long Freed, int Skipped);

    private static readonly string[] LogFiles = ["native.log", "startup-error.log"];

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString() + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.#") + " KB",
        _ => (bytes / 1024d / 1024d).ToString("0.#") + " MB"
    };

    public static List<Group> Inspect(string dataDirectory)
    {
        var groups = new List<Group>();
        foreach (var (label, files) in Candidates(dataDirectory))
        {
            long bytes = 0;
            var count = 0;
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;
                    bytes += info.Length; count++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (count > 0) groups.Add(new Group(label, bytes, count));
        }
        return groups;
    }

    public static ClearResult Clear(string dataDirectory)
    {
        long freed = 0;
        var skipped = 0;
        foreach (var (_, files) in Candidates(dataDirectory))
            foreach (var file in files)
            {
                try
                {
                    var length = new FileInfo(file).Length;
                    File.Delete(file);
                    freed += length;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
        return new ClearResult(freed, skipped);
    }

    private static IEnumerable<(string Label, List<string> Files)> Candidates(string dataDirectory)
    {
        var scopes = Scopes(dataDirectory);
        yield return ("网课课程记录", scopes.SelectMany(scope => Files(Path.Combine(scope, "online-courses"), "*.courses.json")).ToList());
        yield return ("网课账号与登录状态", scopes.SelectMany(scope => new[] { "*.account", "*.session", "*.options.json" }
            .SelectMany(pattern => Files(Path.Combine(scope, "online-courses"), pattern))).ToList());
        // 只清理网课目录里的残留；数据目录根部的 *.tmp 属于正在写入的设置文件，不碰。
        yield return ("临时文件", scopes.SelectMany(scope => Files(Path.Combine(scope, "online-courses"), "*.tmp")).ToList());
        yield return ("运行日志", scopes.SelectMany(scope => LogFiles.Select(name => Path.Combine(scope, name)).Where(File.Exists)).ToList());
    }

    /// <summary>当前数据目录，加上同一数据根下的游客目录和各账号目录。</summary>
    private static List<string> Scopes(string dataDirectory)
    {
        var scopes = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var full = Path.GetFullPath(path);
            if (!scopes.Contains(full, StringComparer.OrdinalIgnoreCase)) scopes.Add(full);
        }
        Add(dataDirectory);
        var accounts = Path.Combine(dataDirectory, "accounts");
        if (Directory.Exists(accounts))
        {
            foreach (var account in Directories(accounts)) Add(account);
            return scopes;
        }
        // 当前打开的就是账号目录（dataRoot/accounts/<hash>）：把游客目录和同级账号一起覆盖。
        var parent = Path.GetDirectoryName(Path.GetFullPath(dataDirectory));
        var root = parent is null ? null : Path.GetDirectoryName(parent);
        if (root is null || parent is null || !string.Equals(Path.GetFileName(parent), "accounts", StringComparison.OrdinalIgnoreCase)) return scopes;
        Add(root);
        foreach (var account in Directories(Path.Combine(root, "accounts"))) Add(account);
        return scopes;
    }

    private static List<string> Files(string directory, string pattern)
    {
        try { return Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory, pattern)] : []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static List<string> Directories(string directory)
    {
        try { return Directory.Exists(directory) ? [.. Directory.EnumerateDirectories(directory)] : []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
