using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace PressureMap.Core;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(property); return true; }
    public void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

public sealed class Course : Observable
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    private string name = "新课程", rawText = "";
    public string Name { get => name; set => Set(ref name, value); }
    public string RawText { get => rawText; set => Set(ref rawText, value); }
}

public sealed class StudyTask : Observable
{
    private string courseKey = "", name = "新任务", kind = "其他", source = "";
    private int? week;
    private double weight = 2;
    private double? estHours;
    public string CourseKey { get => courseKey; set => Set(ref courseKey, value); }
    public string Name { get => name; set => Set(ref name, value); }
    [JsonPropertyName("type")] public string Kind { get => kind; set => Set(ref kind, value); }
    public int? Week { get => week; set => Set(ref week, value); }
    public double Weight { get => weight; set => Set(ref weight, value); }
    public double? EstHours { get => estHours; set => Set(ref estHours, value); }
    public string Source { get => source; set => Set(ref source, value); }
    [JsonIgnore] public string CourseName { get; set; } = "";
    [JsonIgnore] public string KindLabel => Localization.Loc.Kind(Kind);
}

public sealed class Semester : Observable
{
    public long Id { get; set; }
    private string name = "新学期";
    public string Name { get => name; set => Set(ref name, value); }
    public int TotalWeeks { get; set; } = 18;
    public string? Week1Monday { get; set; }
    public double Threshold { get; set; } = 7;
    public double HoursPerWeek { get; set; } = 20;
    public Dictionary<string, double> TypeHours { get; set; } = new(Rules.Hours);
    public int CurrentStep { get; set; } = 1;
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public ObservableCollection<Course> Courses { get; set; } = [];
    public ObservableCollection<StudyTask> Tasks { get; set; } = [];
    [JsonIgnore] public string Summary => $"{Courses.Count} 门课程 · {Tasks.Count} 个任务";
}

public sealed class Todo : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string? DueDate { get; set; }
    public bool Important { get; set; }
    public bool Completed { get; set; }
    public string? SourceKey { get; set; }
    public string SourceLabel { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string CourseName { get; set; } = "";
    public string? PlannedDate { get; set; }
    public string TaskKind { get; set; } = "作业";
    public double EstimatedHours { get; set; } = 1;
    public string? DueTime { get; set; }
    public bool RequiresSubmission { get; set; }
    public bool Submitted { get; set; }
    public bool RemindersEnabled { get; set; } = true;
    public int RepeatDays { get; set; }
    public List<TaskStep> Steps { get; set; } = [];
    [JsonIgnore] public bool IsFinished => Completed && (!RequiresSubmission || Submitted);
    [JsonIgnore] public DateTime? Deadline => DateOnly.TryParseExact(DueDate, "yyyy-MM-dd", out var date) ? date.ToDateTime(TimeOnly.TryParseExact(DueTime, "HH:mm", out var time) ? time : new TimeOnly(23, 59)) : null;
    [JsonIgnore] public string Status => Submitted && RequiresSubmission ? "已提交" : Completed && RequiresSubmission ? "已完成 · 待提交" : Completed ? "已完成" : "待完成";
    [JsonIgnore] public bool IsOverdue => !IsFinished && Deadline is { } deadline && deadline < DateTime.Now;
    [JsonIgnore] public string DeadlineLabel => Deadline is { } deadline ? (deadline.Date == DateTime.Today ? "今天" : deadline.Date == DateTime.Today.AddDays(1) ? "明天" : deadline.ToString("M月d日")) + " " + deadline.ToString("HH:mm") : "";
    [JsonIgnore] public string DisplaySourceLabel => SourceLabel is "虚构演示数据" or "虚拟演示数据" or "演示数据" ? "" : SourceLabel;
    [JsonIgnore] public string Detail => string.Join(" · ", new[] { CourseName, Important ? "重要" : "", DueDate is null ? "" : (IsOverdue ? "已逾期 " : "截止 ") + DeadlineLabel, RequiresSubmission ? Status : "", Steps.Count > 0 ? $"子任务 {Steps.Count(x => x.Completed)}/{Steps.Count}" : "", RepeatDays > 0 ? $"每 {RepeatDays} 天" : "", DisplaySourceLabel }.Where(x => x.Length > 0));
}

public sealed class TaskStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}
public sealed class TodoDetails
{
    public string CourseName { get; set; } = "";
    public string? PlannedDate { get; set; }
    public string TaskKind { get; set; } = "作业";
    public double EstimatedHours { get; set; } = 1;
    public string? DueTime { get; set; }
    public bool RequiresSubmission { get; set; }
    public bool Submitted { get; set; }
    public bool RemindersEnabled { get; set; } = true;
    public int RepeatDays { get; set; }
    public List<TaskStep> Steps { get; set; } = [];
    public static TodoDetails From(Todo task) => new() { CourseName = task.CourseName, PlannedDate = task.PlannedDate, TaskKind = task.TaskKind, EstimatedHours = task.EstimatedHours, DueTime = task.DueTime, RequiresSubmission = task.RequiresSubmission, Submitted = task.Submitted, RemindersEnabled = task.RemindersEnabled, RepeatDays = task.RepeatDays, Steps = task.Steps };
    public void Apply(Todo task) { task.CourseName = CourseName; task.PlannedDate = PlannedDate; task.TaskKind = TaskKind; task.EstimatedHours = EstimatedHours; task.DueTime = DueTime; task.RequiresSubmission = RequiresSubmission; task.Submitted = Submitted; task.RemindersEnabled = RemindersEnabled; task.RepeatDays = RepeatDays; task.Steps = Steps; }
}

public static class DataJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };
    public static string Stamp() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
