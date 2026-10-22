using System.Text.Json;
using PressureMap.Core;

namespace PressureMap.Services;

public sealed class Preferences
{
    public string ApiUrl { get; set; } = "";
    /// <summary>界面语言：zh（默认）或 en。</summary>
    public string Language { get; set; } = "zh";
    public bool ScanOnlineCoursesOnStartup { get; set; } = false;
    public bool DesktopSnap { get; set; } = true;
    public bool TodoClickThrough { get; set; }
    public bool ScheduleClickThrough { get; set; }
    public bool PressureClickThrough { get; set; }
    public double DailyCapacityHours { get; set; } = 6;
    public bool TaskRemindersEnabled { get; set; } = true;
    public bool PressureEnabled { get; set; }
    public bool PressureCollapsed { get; set; }
    public bool PressureLocked { get; set; }
    public int? PressureX { get; set; }
    public int? PressureY { get; set; }
    public double PressureWidth { get; set; } = 220;
    public double PressureHeight { get; set; } = 236;
    public bool ScheduleEnabled { get; set; }
    public bool ScheduleCollapsed { get; set; }
    public bool ScheduleLocked { get; set; }
    public int? ScheduleX { get; set; }
    public int? ScheduleY { get; set; }
    public double ScheduleWidth { get; set; } = 410;
    public double ScheduleHeight { get; set; } = 620;
    public bool DesktopEnabled { get; set; }
    public bool DesktopCollapsed { get; set; }
    public bool DesktopLocked { get; set; }
    public bool DesktopGlass { get; set; } = true;
    public double DesktopGlassTint { get; set; } = .48;
    public bool ReduceMotion { get; set; }
    public bool SemesterListCollapsed { get; set; }
    /// <summary>首次启动时的登录提示是否已经出现过；只提示一次。</summary>
    public bool LoginPrompted { get; set; }
    public string Theme { get; set; } = "Light";
    public int? DesktopX { get; set; }
    public int? DesktopY { get; set; }
    public double DesktopWidth { get; set; } = 410;
    public double DesktopHeight { get; set; } = 560;
    public static Preferences Load(string directory)
    {
        var path = Path.Combine(directory, "settings-v2.json");
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), DataJson.Options) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }
    public void Save(string directory)
    {
        var path = Path.Combine(directory, "settings-v2.json"); var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, DataJson.Options)); File.Move(temporary, path, true);
    }
}

