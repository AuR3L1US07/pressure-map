using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PressureMap.Core;
using PressureMap.Localization;
using PressureMap.Services;

namespace PressureMap.Views;

public partial class MainWindow
{
    private Control AiPlannerPage()
    {
        string T(string zh, string en) => Loc.IsEnglish ? en : zh;
        var data = Store;
        var summary = Ui.Text("", "caption");
        var courses = Ui.Stack(); var tasks = Ui.Stack();
        var notes = Ui.Input("", T("例如：晚上 8 点后有空，优先完成实验报告", "For example: free after 8 pm; prioritize the lab report"));
        notes.AcceptsReturn = true; notes.MinHeight = 80; notes.MaxLength = 2000;
        Avalonia.Automation.AutomationProperties.SetName(notes, T("补充要求（可选）", "Additional preferences (optional)"));
        var status = Ui.Text("", "caption");
        var advice = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 220,
            PlaceholderText = T("生成后，这里会显示今天的安排建议。", "Your daily suggestions will appear here.") };
        Avalonia.Automation.AutomationProperties.SetName(advice, T("AI 安排建议", "AI planning suggestions"));
        CancellationTokenSource? pending = null;
        bool active = true;
        (DateTime Now, List<CourseOccurrence> Courses, List<Todo> Tasks) Snapshot()
        {
            var now = DateTime.Now;
            var dayCourses = CourseCalendar.Day(data.ListCalendars(), now.Date).ToList();
            var dayTasks = data.ListTodos().Where(t => !t.IsFinished && (TaskWorkload.IsToday(t, now)
                || t.Deadline is { } deadline && deadline < now.Date.AddDays(8))).OrderByDescending(t => t.Important).ThenBy(t => t.Deadline ?? DateTime.MaxValue).ToList();
            courses.Children.Clear(); tasks.Children.Clear();
            foreach (var c in dayCourses) courses.Children.Add(Ui.Text($"{c.Start:HH:mm}–{c.End:HH:mm}  {c.Title}"));
            foreach (var t in dayTasks) tasks.Children.Add(Ui.Text(t.Title + (t.Detail.Length > 0 ? " · " + t.Detail : "")));
            if (dayCourses.Count == 0) courses.Children.Add(Ui.Text(T("今天没有课程", "No classes today"), "caption"));
            if (dayTasks.Count == 0) tasks.Children.Add(Ui.Text(T("暂无今日或临近截止的待办", "No tasks scheduled today or due soon"), "caption"));
            summary.Text = T($"{now:M月d日} · {dayCourses.Count} 门课程 · {dayTasks.Count} 项相关待办", $"{now:MMM d} · {dayCourses.Count} classes · {dayTasks.Count} relevant tasks");
            return (now, dayCourses, dayTasks);
        }
        Snapshot();
        var generate = new Button { Content = T("生成今日建议", "Generate today's advice"), Classes = { "accent" } };
        var cancel = Ui.Button(T("取消", "Cancel"), () => pending?.Cancel(), "ghost"); cancel.IsVisible = false;
        generate.Click += async (_, _) =>
        {
            if (pending is not null) return;
            using var cancellation = new CancellationTokenSource(); pending = cancellation;
            generate.IsEnabled = notes.IsEnabled = false; cancel.IsVisible = true;
            status.Text = T("正在生成建议…", "Generating suggestions…");
            try
            {
                AiApiConfiguration config;
                try { config = AiApiConfiguration.Load(LocalStore.DirectoryPath); }
                catch { config = new(); }
                var snapshot = Snapshot();
                var context = JsonSerializer.Serialize(new { currentTime = snapshot.Now.ToString("yyyy-MM-dd HH:mm"), dailyCapacityHours = Preferences.DailyCapacityHours,
                    courses = snapshot.Courses.Select(c => new { c.Title, c.Start, c.End }),
                    tasks = snapshot.Tasks.Select(t => new { t.Title, t.CourseName, t.PlannedDate, t.Deadline, t.EstimatedHours, t.Important, t.Completed, t.RequiresSubmission, t.Submitted, steps = t.Steps.Select(s => new { s.Title, s.Completed }) }),
                    preferences = notes.Text });
                var result = config.IsValid
                    ? await DailyAiPlanner.GenerateAsync(config, context, Loc.IsEnglish, cancellation.Token)
                    : await cloud.GenerateAiPlanAsync(context, Loc.IsEnglish, cancellation.Token);
                if (!active || data != Store) return;
                advice.Text = result;
                status.Text = T($"更新于 {DateTime.Now:HH:mm} · 建议仅供参考，任务未自动修改。", $"Updated {DateTime.Now:HH:mm} · Suggestions only; tasks remain unchanged.");
            }
            catch (OperationCanceledException) { if (active) status.Text = T("已取消或请求超时，可以重试。", "Cancelled or timed out. You can retry."); }
            catch (Exception e) { if (active) status.Text = e is InvalidOperationException ? e.Message : T("生成失败，请检查接口配置和网络后重试。", "Generation failed. Check the API configuration and connection, then retry."); }
            finally { pending = null; generate.IsEnabled = notes.IsEnabled = true; cancel.IsVisible = false; }
        };
        var page = SectionPage(T("AI助手", "AI Assistant"),
            Ui.Card(Ui.Stack(Ui.Field(T("补充要求（可选）", "Additional preferences (optional)"), notes),
                Ui.Actions(generate, cancel), status)),
            Ui.Card(Ui.Stack(Ui.Text(T("安排建议", "Planning suggestions"), "h2"), advice)),
            Ui.Card(Ui.Stack(summary, Ui.Text(T("今日课程", "Today's classes"), "h2"), courses, Ui.Divider(), Ui.Text(T("今日与临近截止待办", "Today's tasks and upcoming deadlines"), "h2"), tasks)));
        page.DetachedFromVisualTree += (_, _) => { active = false; pending?.Cancel(); };
        return page;
    }
}
