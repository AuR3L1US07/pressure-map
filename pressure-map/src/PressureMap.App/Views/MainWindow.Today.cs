using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    private TodoWindow? desktopPressure;
    private TodoControl? todayTasks;
    private ScheduleControl? todayCourses;
    private PressureControl? settingsPressure;
    private Window? reminderWindow;
    private ToggleSwitch? pressureToggleControl;
    private bool updatingPressureToggle;
    private TextBlock? todayHeading;
    private readonly DispatcherTimer taskTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private TextBlock? todaySubtitle, courseCount, taskCount, submissionCount;
    private int overviewRevision;
    private PressureRing? homePressureRing;
    private TextBlock? homePressureScore, homePressureLevel;
    private Control TodayPage()
    {
        todayTasks = new TodoControl(Store, todayMode: true); todayTasks.Changed += RefreshTodoViews;
        todayCourses = new ScheduleControl(Store, compact: true); todayCourses.TaskRequested += async title => await NewTodoForCourseAsync(title);
        todayCourses.SettingsRequested += () => SelectPage(8);
        todayHeading = Ui.Text(Loc.T("今天") + " · " + Loc.Date(DateTime.Today, "M月d日", "MMM d"), "h1");
        todaySubtitle = Ui.Text("", "caption");
        var titles = Ui.Stack(todayHeading, todaySubtitle); titles.Spacing = 4;
        var heading = Ui.Columns("*,16,Auto", titles, new Border(), Ui.AsyncButton("新建任务", () => NewTodoForCourseAsync(""), "accent"));
        courseCount = Ui.Text("—"); taskCount = Ui.Text("—"); submissionCount = Ui.Text("—");
        Control Metric(string label, TextBlock value)
        {
            value.FontSize = 22; value.FontWeight = FontWeight.Bold;
            var unit = Ui.Text(label == "今日课程" ? "门" : "项", "caption"); unit.FontSize = 14; var number = Ui.Row(value, unit); number.Spacing = 6;
            var content = Ui.Row(Ui.Text(label, "caption"), number); content.Spacing = 10;
            var button = Ui.Button("", () => SelectPage(label == "今日课程" ? 8 : label == "今日待办" ? 2 : 9), "ghost"); button.Classes.Add("metric"); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Content = content; button.Padding = new Thickness(8, 4); button.CornerRadius = new CornerRadius(14); return button;
        }
        Border Divider() => new() { Width = 1, Classes = { "today-divider" }, Margin = new Thickness(0, 4) };
        homePressureRing = new PressureRing { Width = 48, Height = 48 };
        homePressureScore = Ui.Text("—"); homePressureScore.FontSize = 14; homePressureScore.FontWeight = FontWeight.SemiBold; homePressureScore.HorizontalAlignment = HorizontalAlignment.Center; homePressureScore.VerticalAlignment = VerticalAlignment.Center;
        homePressureLevel = Ui.Text("正在计算", "caption");
        var ring = new Grid { Width = 48, Height = 48 }; ring.Children.Add(homePressureRing); ring.Children.Add(homePressureScore);
        var pressure = Ui.Columns("48,10,*", ring, new Border(), Ui.Stack(2, Ui.Text("每日压力", "label"), homePressureLevel));
        ToolTip.SetTip(pressure, "每日压力，满分 100。计算说明与桌面开关位于常规设置。");
        var pressureButton = Ui.AsyncButton("",ShowPressureDetailsAsync,"icon"); pressureButton.Content = pressure; pressureButton.Padding = new Thickness(0); pressureButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(pressureButton,"查看每日压力来源");
        var overview = Ui.Card(Ui.Columns("*,16,1,16,*,16,1,16,*,16,1,16,190", Metric("今日课程", courseCount), new Border(), Divider(), new Border(), Metric("今日待办", taskCount), new Border(), Divider(), new Border(), Metric("待提交作业", submissionCount), new Border(), Divider(), new Border(), pressureButton));
        overview.Padding = new Thickness(16, 8);
        var courses = new Grid { RowDefinitions = new RowDefinitions("Auto,8,*") };
        courses.Children.Add(Ui.Columns("*,Auto", Ui.Text("课程日程", "h2"), Ui.Button("管理课表", () => SelectPage(8), "pill")));
        Grid.SetRow(todayCourses, 2); courses.Children.Add(todayCourses);
        var tasks = new Grid { RowDefinitions = new RowDefinitions("Auto,6,*") };
        tasks.Children.Add(Ui.Columns("*,Auto", Ui.Text("今天要完成", "h2"), Ui.Button("全部待办", () => SelectPage(2), "pill")));
        Grid.SetRow(todayTasks, 2); tasks.Children.Add(todayTasks);
        var courseCard = Ui.Card(courses); courseCard.Padding = new Thickness(20, 16); courseCard.VerticalAlignment = VerticalAlignment.Stretch;
        var taskCard = Ui.Card(tasks); taskCard.Padding = new Thickness(20, 16); taskCard.VerticalAlignment = VerticalAlignment.Stretch;
        todayCourses.MinHeight = 200;
        var columns = Ui.Columns("*,16,*", courseCard, new Border(), taskCard);
        columns.VerticalAlignment = VerticalAlignment.Stretch;
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,18,Auto,18,*") };
        page.Children.Add(heading); Grid.SetRow(overview, 2); page.Children.Add(overview);
        var scroll = Ui.Scroll(columns);
        bool? narrowLayout = null;
        void Reflow() { var narrow = Bounds.Width < 1130; if (narrowLayout == narrow) return; narrowLayout = narrow; scroll.VerticalScrollBarVisibility = narrow ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled; todayCourses.Height = narrow ? 420 : double.NaN; todayTasks.Height = narrow ? 480 : double.NaN; columns.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,16,*"); columns.RowDefinitions = new RowDefinitions(narrow ? "Auto,16,Auto" : "*"); Grid.SetColumn(taskCard, narrow ? 0 : 2); Grid.SetRow(taskCard, narrow ? 2 : 0); }
        columns.AttachedToVisualTree += (_, _) => Reflow();
        EventHandler<SizeChangedEventArgs> resize = (_, _) => Reflow(); SizeChanged += resize; columns.DetachedFromVisualTree += (_, _) => SizeChanged -= resize;
        Grid.SetRow(scroll, 4); page.Children.Add(scroll);
        page.Children.Remove(heading); page.RowDefinitions = new RowDefinitions("Auto,12,*"); Grid.SetRow(overview, 0); Grid.SetRow(scroll, 2);
        _ = RefreshTodayOverviewAsync(); var frame = new PageFrame(heading, page, false); frame.RowDefinitions = new RowDefinitions("Auto,12,*"); return frame;
    }
    private async Task RefreshTodayOverviewAsync()
    {
        if (todaySubtitle is null) return;
        var own = ++overviewRevision; var data = Store; var now = DateTime.Now;
        try
        {
            var result = await Task.Run(() => (Tasks: data.ListTodos(), Courses: CourseCalendar.Day(data.ListCalendars(), now.Date)));
            if (own != overviewRevision || data != Store) return;
            var pending = result.Tasks.Where(t => !t.IsFinished).ToList();
            courseCount!.Text = result.Courses.Count.ToString();
            var report = TaskWorkload.Compute(result.Tasks, result.Courses, now.Date, Preferences.DailyCapacityHours);
            homePressureScore!.Text = report.Score.ToString(); homePressureLevel!.Text = report.Level;
            homePressureRing!.SetScore(report.Score, ((ISolidColorBrush)Ui.Brush(report.Score >= 85 ? "DangerBrush" : report.Score >= 65 ? "WarningBrush" : report.Score < 35 ? "SuccessBrush" : "AccentBrush")).Color,MotionReduced);
            Avalonia.Automation.AutomationProperties.SetName(homePressureRing, Loc.F("每日压力 {0} 分，满分 100，{1}", report.Score, report.Level));
            taskCount!.Text = pending.Count(t => TaskWorkload.IsToday(t, now)).ToString();
            submissionCount!.Text = pending.Count(t => t.Completed && t.RequiresSubmission && !t.Submitted).ToString();
            todayHeading!.Text = Loc.T("今天") + " · " + Loc.Date(now, "M月d日", "MMM d");
            var overdue = pending.Count(t => t.Deadline < now);
            var next = pending.Where(t => t.Deadline >= now).OrderBy(t => t.Deadline).FirstOrDefault();
            todaySubtitle.Text = Loc.DayOfWeek(now) +
                (overdue > 0 ? Loc.F(" · {0} 项已逾期，请优先处理", overdue)
                 : next?.Deadline is { } deadline ? Loc.F(" · 最近截止 {0} · {1}", Loc.Date(deadline, "M月d日 HH:mm", "MMM d, HH:mm"), next.Title)
                 : Loc.T(" · 暂无临近截止事项"));
            todaySubtitle.MaxLines = 2; todaySubtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        catch (Exception e) { if (own == overviewRevision) { courseCount!.Text = taskCount!.Text = submissionCount!.Text = "—"; todaySubtitle.Text = Loc.T("今日概览读取失败：") + e.Message; } }
    }
    private async Task ShowPressureDetailsAsync()
    {
        try
        {
            var data = Store; var report = await Task.Run(() => TaskWorkload.Compute(data.ListTodos(),CourseCalendar.Day(data.ListCalendars(),DateTime.Today),DateTime.Today,Preferences.DailyCapacityHours));
            await Dialogs.PressureDetails(this,report,Preferences.DailyCapacityHours);
        }
        catch (Exception e) { Error(e); }
    }
    private async Task NewTodoForCourseAsync(string course)
    {
        try
        {
            var draft = new Todo { CourseName = course, PlannedDate = DateTime.Today.ToString("yyyy-MM-dd"), RequiresSubmission = course.Length > 0 };
            if (await Dialogs.EditTodo(this, draft, Store.CourseNames()) is not { } task) return;
            Store.CreateTodo(task); RefreshTodoViews();
        }
        catch (Exception e) { Error(e); }
    }
    public void ShowPressureDesktop()
    {
        try
        {
            if (desktopPressure?.NativeAvailable == false) { desktopPressure.Dispose(); desktopPressure = null; }
            if (desktopPressure is null)
            {
                desktopPressure = new TodoWindow(Store, Preferences, LocalStore.DirectoryPath, pressurePanel: true);
                desktopPressure.ManageRequested += Restore;
                desktopPressure.HiddenByUser += () => { Preferences.PressureEnabled = false; UpdatePressureToggle(false); SavePreferences(); };
            }
            desktopPressure.Present(); EnsureTray(); Preferences.PressureEnabled = true; UpdatePressureToggle(true); SavePreferences();
        }
        catch (Exception e) { Preferences.PressureEnabled = false; UpdatePressureToggle(false); Error(e); }
    }
    private void HidePressureDesktop() { desktopPressure?.HideFromDesktop(); Preferences.PressureEnabled = false; UpdatePressureToggle(false); SavePreferences(); }
    private void UpdatePressureToggle(bool enabled) { updatingPressureToggle = true; if (pressureToggleControl is not null) pressureToggleControl.IsChecked = enabled; updatingPressureToggle = false; }
    public void StartTaskServices()
    {
        if (taskTimer.IsEnabled) return;
        taskTimer.Tick += (_, _) =>
        {
            try { if (currentPage == 0) { todayTasks?.Refresh(); _ = RefreshTodayOverviewAsync(); } Store.AutoBackup(); CheckTaskReminders(); }
            catch (Exception e) { Error("提醒检查失败：" + e.Message); }
        };
        taskTimer.Start(); try { Store.AutoBackup(); CheckTaskReminders(); } catch (Exception e) { Error("提醒检查失败：" + e.Message); }
    }
    private void CheckTaskReminders()
    {
        if (!Preferences.TaskRemindersEnabled || reminderWindow is not null) return;
        var now = DateTime.Now;
        var states = Store.ReminderStates();
        var due = Store.ListTodos().Select(t => ReminderRules.Due(t, states.GetValueOrDefault(t.Id), now)).Where(r => r is not null).Cast<TaskReminder>().ToList();
        if (due.Count > 0) ShowReminder(due);
    }
    private void ShowReminder(IReadOnlyList<TaskReminder> reminders)
    {
        var reminderStore = Store; var panel = Ui.Stack(Ui.Text("学习提醒", "h2"));
        foreach (var item in reminders.Take(5)) panel.Children.Add(Ui.Text(item.Message));
        if (reminders.Count > 5) panel.Children.Add(Ui.Text(Loc.F("另有 {0} 项任务，请在今天视图查看。", reminders.Count - 5), "caption"));
        var popup = new Window { Title = Loc.T("学习提醒 · 学期地图"), Width = 360, MaxHeight = 620, SizeToContent = SizeToContent.Height, CanResize = false, WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Border { Background = Brush.Parse(Preferences.Theme == "Dark" ? "#262624" : "#FFFFFF"), Padding = new Thickness(20), Child = Ui.Scroll(panel), BorderBrush = Brush.Parse("#7F77DD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) } };
        var screen = Screens.Primary; if (screen is not null) popup.Position = new PixelPoint(screen.WorkingArea.Right - (int)(380 * screen.Scaling), screen.WorkingArea.Bottom - (int)(340 * screen.Scaling));
        var recorded = false;
        void Record(DateTime? snooze = null) { foreach (var item in reminders) reminderStore.RecordReminder(item.Task.Id, item.Key, snooze); recorded = true; }
        panel.Children.Add(Ui.Row(Ui.Button("延后 15 分钟", () => { try { Record(DateTime.Now.AddMinutes(15)); popup.Close(); } catch (Exception e) { Error(e); } }, "pill"),
            Ui.Button("查看任务", () => { popup.Close(); Restore(); SelectPage(0); }, "accent"), Ui.Button("关闭", () => popup.Close(), "pill")));
        popup.Closed += (_, _) => { try { if (!recorded) Record(); } catch (Exception e) { Error(e); } reminderWindow = null; };
        popup.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (screen is null) return;
            popup.Position = new PixelPoint(Math.Max(screen.WorkingArea.X, screen.WorkingArea.Right - (int)(popup.Width * screen.Scaling) - 20), Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - (int)(popup.Bounds.Height * screen.Scaling) - 20));
        }, DispatcherPriority.Loaded);
        reminderWindow = popup; popup.Show();
    }
}
