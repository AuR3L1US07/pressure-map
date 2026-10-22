using System.Globalization;
using System.Text.RegularExpressions;
using PressureMap.Localization;

namespace PressureMap.Views;

/// <summary>Filename-derived labels only; restore and storage stay in the existing workflow.</summary>
public sealed record BackupPresentation(string Path, string Title, string Detail, string Kind)
{
    public static BackupPresentation FromPath(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var match = Regex.Match(name, @"^(\d{8}-\d{6})-\d{3}-([^-]+)-.*\.json$");
        if (!match.Success || !DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new(path, name, "", "");
        var title = date.Date == DateTime.Today ? Loc.T("今天") + " " + date.ToString("HH:mm") : Loc.Date(date, "M月d日 HH:mm", "MMM d, HH:mm");
        var kind = Loc.T(match.Groups[2].Value switch { "automatic" => "自动", "manual" => "手动", _ => "备份" });
        long bytes = 0; try { bytes = new FileInfo(path).Length; } catch (IOException) { }
        return new(path, title, match.Groups[1].Value + " · " + (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB", kind);
    }
}
