using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Layout;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public static class Dialogs
{
    public static async Task Info(Window owner, string title, string message)
    {
        var panel = Ui.Stack(Ui.Text(message)); var dialog = Window(title, Ui.Scroll(panel), 540);
        panel.Children.Add(Ui.Button("关闭", () => dialog.Close())); await dialog.ShowDialog(owner);
    }
    public static Window CreateWindow(string title, Control content, double width = 440)
    {
        var dialog = new Window {
            Title = Loc.T(title), Width = width, SizeToContent = SizeToContent.Height, MaxHeight = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false,
            WindowDecorations = WindowDecorations.BorderOnly, ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaTitleBarHeightHint = 40
        };
        var close = new Button { Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 5,5 L 17,17 M 17,5 L 5,17") } };
        close.Classes.Add("caption-button"); close.Classes.Add("caption-close");
        AutomationProperties.SetName(close, Loc.T("关闭")); ToolTip.SetTip(close, Loc.T("关闭"));
        WindowDecorationProperties.SetElementRole(close, WindowDecorationsElementRole.CloseButton);
        close.Click += (_, _) => dialog.Close();
        var caption = Ui.Text(title, "caption"); caption.Margin = new Thickness(24, 0, 0, 0);
        var header = new Border { Background = Brushes.Transparent, Child = Ui.Columns("*,Auto", caption, close), Padding = new Thickness(0, 0, 8, 0) };
        WindowDecorationProperties.SetElementRole(header, WindowDecorationsElementRole.TitleBar);
        var shell = new Grid { RowDefinitions = new RowDefinitions("40,*") };
        shell.Children.Add(header);
        var body = new Border { Padding = new Thickness(24, 12, 24, 24), Child = content };
        Grid.SetRow(body, 1); shell.Children.Add(body); dialog.Content = shell;
        dialog.Opened += (_, _) => MainWindow.ApplyRoundedCorners(dialog);
        dialog.SizeChanged += (_, _) => MainWindow.ApplyRoundedCorners(dialog);
        return dialog;
    }
    private static Window Window(string title, Control content, double width = 440) => CreateWindow(title, content, width);
    public static async Task PressureDetails(Window owner, DailyPressure report, double capacity)
    {
        var total = report.CourseHours * .35 + report.TaskHours;
        var panel = Ui.Stack(Ui.Text($"{report.Score} / 100 · {report.Level}","h1"),Ui.Text(Loc.F("当天等效负荷 {0:0.##} 小时 / 每日容量 {1:0.#} 小时", total, capacity),"caption"));
        if (report.Sources.Count == 0) panel.Children.Add(Ui.Text("今天还没有计入压力的课程或任务。","muted"));
        foreach (var source in report.Sources)
        {
            var value = total > 0 ? source.EquivalentHours/total : 0;
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = value, Height = 6 };
            panel.Children.Add(Ui.Stack(Ui.Columns("*,16,Auto",Ui.Text(source.Label),new Border(),Ui.Text(Loc.F("{0:0.##} 小时 · {1:P0}", source.EquivalentHours, value),"caption")),bar));
        }
        panel.Children.Add(Ui.Text("按等效负荷排序；完成任务或确认提交后会重新计算。","caption"));
        var scroll = Ui.Scroll(panel); scroll.MaxHeight = 520;
        var body = Ui.Stack(scroll); var dialog = Window("今日压力来源",body,580);
        body.Children.Add(Ui.Button("关闭",() => dialog.Close(),"accent")); await dialog.ShowDialog(owner);
    }
    public static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var panel = Ui.Stack(Ui.Text(message)); var dialog = Window(title, panel);
        panel.Children.Add(Ui.Row(Ui.Button("确认", () => dialog.Close(true), title.Contains("删除") || title.Contains("恢复") || title.Contains("清除") || title.Contains("移除") ? "danger" : "accent"), Ui.Button("取消", () => dialog.Close(false), "ghost")));
        return await dialog.ShowDialog<bool>(owner);
    }
    public static async Task<int?> Choose(Window owner, string title, string message, string first, string second)
    {
        var panel = Ui.Stack(Ui.Text(message)); var dialog = Window(title,panel,560);
        panel.Children.Add(Ui.Button(first,() => dialog.Close((int?)0),"accent"));
        panel.Children.Add(Ui.Button(second,() => dialog.Close((int?)1)));
        panel.Children.Add(Ui.Button("取消",() => dialog.Close()));
        return await dialog.ShowDialog<int?>(owner);
    }
    public static async Task<string?> Ask(Window owner, string title, string initial = "")
    {
        var field = Ui.Input(initial); field.MaxLength = 100;
        var panel = Ui.Stack(Ui.Field(title, field)); var dialog = Window(title, panel);
        panel.Children.Add(Ui.Row(Ui.Button("保存", () => { if (!string.IsNullOrWhiteSpace(field.Text)) dialog.Close(field.Text.Trim()); }, "accent"), Ui.Button("取消", () => dialog.Close())));
        dialog.Opened += (_, _) => field.Focus(); return await dialog.ShowDialog<string?>(owner);
    }
    public static async Task<Account?> Login(Window owner, CloudClient cloud)
    {
        var account = Ui.Input("", "账号"); account.MaxLength = 128;
        var name = Ui.Input("", "显示名"); name.MaxLength = 100;
        var password = Ui.Input(); password.PasswordChar = '●'; password.MaxLength = 128;
        var confirmation = Ui.Input(); confirmation.PasswordChar = '●'; confirmation.MaxLength = 128;
        Control Field(string label, Control input) {
            var text = Ui.Text(label, "label"); text.Margin = new Thickness(0);
            var field = Ui.Stack(text, input); field.Spacing = 6; return field;
        }
        var nameField = Field("显示名", name); nameField.IsVisible = false;
        var confirmationField = Field("确认密码", confirmation); confirmationField.IsVisible = false;
        var hint = Ui.Text("密码需为 12～128 字符。", "caption"); hint.IsVisible = false;
        var error = Ui.Text("", "danger"); error.IsVisible = false;
        void SetError(string message) { error.Text = Loc.T(message); error.IsVisible = message.Length > 0; }
        var title = Ui.Text("登录学期地图", "h1");
        var heading = Ui.Stack(title, Ui.Text("登录后同步课程、课表和待办。", "muted")); heading.Spacing = 6;
        var fields = Ui.Stack(Field("账号", account), nameField, Field("密码", password), hint, confirmationField); fields.Spacing = 14;
        var panel = Ui.Stack(heading, fields); panel.Spacing = 20;
        var dialog = Window("账号 · 学期地图", panel); var busy = false; var registering = false;
        var skip = Ui.Link("暂不登录，继续使用", () => dialog.Close());
        var submit = new Button { Content = Loc.T("登录"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 40, IsDefault = true }; submit.Classes.Add("accent"); Ui.CenterButtonText(submit);
        var toggle = Ui.Link("没有账号？注册", () => { });
        toggle.Click += (_, _) => {
            if (busy) return;
            registering = !registering;
            nameField.IsVisible = confirmationField.IsVisible = hint.IsVisible = registering;
            title.Text = Loc.T(registering ? "注册学期地图" : "登录学期地图");
            submit.Content = Loc.T(registering ? "注册并登录" : "登录");
            toggle.Content = Loc.T(registering ? "已有账号？登录" : "没有账号？注册");
            password.Text = confirmation.Text = ""; SetError("");
        };
        submit.Click += async (_, _) =>
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(account.Text) || string.IsNullOrEmpty(password.Text)) { SetError("请输入账号和密码。"); return; }
            if (registering && (string.IsNullOrWhiteSpace(name.Text) || password.Text.Length < 12)) { SetError("请填写显示名，并设置至少 12 位密码。"); return; }
            if (registering && password.Text != confirmation.Text) { SetError("两次输入的密码不一致。"); return; }
            busy = true; submit.IsEnabled = toggle.IsEnabled = skip.IsEnabled = false;
            account.IsEnabled = name.IsEnabled = password.IsEnabled = confirmation.IsEnabled = false;
            submit.Content = Loc.T(registering ? "正在注册…" : "正在登录…"); SetError("");
            try {
                var user = registering
                    ? await cloud.RegisterAsync(account.Text.Trim(), name.Text!.Trim(), password.Text)
                    : await cloud.LoginAsync(account.Text.Trim(), password.Text);
                password.Text = confirmation.Text = ""; busy = false; dialog.Close(user);
            }
            catch (Exception exception) { password.Text = confirmation.Text = ""; SetError(exception.Message); }
            finally {
                busy = false; submit.IsEnabled = toggle.IsEnabled = skip.IsEnabled = true;
                account.IsEnabled = name.IsEnabled = password.IsEnabled = confirmation.IsEnabled = true;
                submit.Content = Loc.T(registering ? "注册并登录" : "登录");
            }
        };
        dialog.Closing += (_, e) => { if (busy) { e.Cancel = true; return; } password.Text = confirmation.Text = ""; };
        var actions = Ui.Stack(error, submit); actions.Spacing = 8;
        var footer = Ui.Columns("*,Auto", toggle, skip);
        var bottom = Ui.Stack(actions, footer); bottom.Spacing = 8;
        panel.Children.Add(bottom);
        return await dialog.ShowDialog<Account?>(owner);
    }
    public static async Task<Todo?> EditTodo(Window owner, Todo todo, IEnumerable<string>? courseNames = null)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<Todo>(System.Text.Json.JsonSerializer.Serialize(todo, DataJson.Options), DataJson.Options)!;
        var title = Ui.Input(copy.Title); title.MaxLength = 300;
        var date = Ui.Input(copy.DueDate ?? "", "YYYY-MM-DD，留空表示无截止日期");
        var time = Ui.Input(copy.DueTime ?? "", "HH:mm；留空默认 23:59");
        var names = new[] { Loc.T("不关联课程") }.Concat((courseNames ?? []).Append(copy.CourseName).Where(n => n.Length > 0).Distinct()).ToList();
        var course = new ComboBox { ItemsSource = names, SelectedIndex = Math.Max(0, names.IndexOf(copy.CourseName)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var kind = new ComboBox { ItemsSource = TaskWorkload.Kinds.Select(Loc.Kind).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(TaskWorkload.Kinds, copy.TaskKind)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var hours = new NumericUpDown { Minimum = .1m, Maximum = 100, Increment = .5m, Value = (decimal)copy.EstimatedHours };
        var repeat = new NumericUpDown { Minimum = 0, Maximum = 365, Increment = 1, Value = copy.RepeatDays };
        var important = new CheckBox { Content = Loc.T("重要"), IsChecked = copy.Important };
        var plan = new CheckBox { Content = Loc.T("安排在今天"), IsChecked = copy.PlannedDate is not null && string.CompareOrdinal(copy.PlannedDate, DateTime.Today.ToString("yyyy-MM-dd")) <= 0 };
        var requires = new CheckBox { Content = Loc.T("需要提交（作业、报告等）"), IsChecked = copy.RequiresSubmission };
        var submitted = new CheckBox { Content = Loc.T("已经提交"), IsChecked = copy.Submitted, IsEnabled = copy.RequiresSubmission };
        requires.IsCheckedChanged += (_, _) => { submitted.IsEnabled = requires.IsChecked == true; if (!submitted.IsEnabled) submitted.IsChecked = false; };
        var reminders = new CheckBox { Content = Loc.T("截止提醒：提前一天、一小时和到期时"), IsChecked = copy.RemindersEnabled };
        var steps = new StackPanel { Spacing = 6 }; var stepInput = Ui.Input("", "添加子任务"); stepInput.MaxLength = 200;
        void RenderSteps()
        {
            steps.Children.Clear();
            foreach (var item in copy.Steps.ToList())
            {
                var check = new CheckBox { IsChecked = item.Completed }; check.IsCheckedChanged += (_, _) => item.Completed = check.IsChecked == true;
                var text = Ui.Input(item.Title); text.MaxLength = 200; text.TextChanged += (_, _) => item.Title = text.Text ?? "";
                var delete = Ui.Button("×", () => { copy.Steps.Remove(item); RenderSteps(); }, "icon");
                steps.Children.Add(Ui.Columns("26,8,*,8,30", check, new Border(), text, new Border(), delete));
            }
        }
        void AddStep() { if (copy.Steps.Count >= 20 || string.IsNullOrWhiteSpace(stepInput.Text)) return; copy.Steps.Add(new TaskStep { Title = stepInput.Text.Trim() }); stepInput.Text = ""; RenderSteps(); }
        stepInput.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { AddStep(); e.Handled = true; } }; RenderSteps();
        var error = Ui.Text("", "danger");
        var form = Ui.Stack(Ui.Field("任务内容", title), Ui.Field("关联课程", course), Ui.Columns("*,12,*", Ui.Field("任务类型", kind), new Border(), Ui.Field("预计耗时（小时）", hours)),
            Ui.Field("截止日期", date), Ui.Field("截止时间", time), Ui.Field("重复间隔（天，0 为不重复；完成后生成下一次）", repeat), plan, important, requires, submitted, reminders, Ui.Text("子任务", "h2"), steps,
            Ui.Columns("*,8,Auto", stepInput, new Border(), Ui.Button("添加", AddStep, "pill")));
        var scroll = Ui.Scroll(form); scroll.MaxHeight = 520;
        var panel = Ui.Stack(scroll, error); var dialog = Window("编辑待办", panel, 520);
        panel.Children.Add(Ui.Row(Ui.Button("保存", () =>
        {
            copy.Title = title.Text?.Trim() ?? ""; copy.DueDate = string.IsNullOrWhiteSpace(date.Text) ? null : date.Text.Trim(); copy.DueTime = string.IsNullOrWhiteSpace(time.Text) ? null : time.Text.Trim();
            copy.CourseName = course.SelectedIndex > 0 ? (string)course.SelectedItem! : ""; copy.TaskKind = TaskWorkload.Kinds[Math.Max(0, kind.SelectedIndex)]; copy.EstimatedHours = (double)(hours.Value ?? 1);
            copy.Important = important.IsChecked == true; copy.PlannedDate = plan.IsChecked == true ? copy.PlannedDate ?? DateTime.Today.ToString("yyyy-MM-dd") : null;
            copy.RequiresSubmission = requires.IsChecked == true; copy.Submitted = copy.RequiresSubmission && submitted.IsChecked == true; if (copy.Submitted) copy.Completed = true;
            if (repeat.Value is { } interval && interval != decimal.Truncate(interval)) { error.Text = "重复间隔需要是整数天。"; return; }
            copy.RemindersEnabled = reminders.IsChecked == true; copy.RepeatDays = (int)(repeat.Value ?? 0);
            try { Store.ValidateTodo(copy); dialog.Close(copy); } catch (Exception e) { error.Text = e.Message; }
        }, "accent"), Ui.Button("取消", () => dialog.Close())));
        return await dialog.ShowDialog<Todo?>(owner);
    }
    public static async Task<StudyTask?> EditTask(Window owner, StudyTask task, Semester semester)
    {
        var title = Ui.Input(task.Name); title.MaxLength = 100;
        var course = new ComboBox { ItemsSource = semester.Courses.Select(c => c.Name).ToArray(), SelectedIndex = Math.Max(0, semester.Courses.ToList().FindIndex(c => c.Key == task.CourseKey)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var kind = new ComboBox { ItemsSource = Rules.Types.Select(Loc.Kind).ToArray(), SelectedIndex = Array.IndexOf(Rules.Types, task.Kind), HorizontalAlignment = HorizontalAlignment.Stretch };
        var week = Ui.Input(task.Week?.ToString() ?? "", "留空表示待确认");
        var weight = new NumericUpDown { Minimum = 0, Maximum = 10, Increment = .5m, Value = (decimal)task.Weight };
        kind.SelectionChanged += (_, _) => { if (kind.SelectedIndex >= 0) weight.Value = (decimal)Rules.Weights[Rules.Types[kind.SelectedIndex]]; };
        var hours = Ui.Input(task.EstHours?.ToString() ?? "", "留空表示按类型估时");
        var source = Ui.Input(task.Source, multiline: true); source.MaxLength = 500;
        var error = Ui.Text("", "danger");
        var panel = Ui.Stack(Ui.Field("课程", course), Ui.Field("任务", title), Ui.Field("类型", kind), Ui.Field("截止周", week), Ui.Field("权重", weight), Ui.Field("估时（小时）", hours), Ui.Field("原文出处", source), error);
        var dialog = Window("编辑任务", Ui.Scroll(panel), 500);
        panel.Children.Add(Ui.Row(Ui.Button("保存", () =>
        {
            int? due = null; double? estimate = null;
            if (!string.IsNullOrWhiteSpace(week.Text)) { if (!int.TryParse(week.Text, out var number) || !Rules.ValidWeek(number, semester.TotalWeeks)) { error.Text = "截止周超出学期范围。"; return; } due = number; }
            if (!string.IsNullOrWhiteSpace(hours.Text)) { if (!double.TryParse(hours.Text, out var value) || !double.IsFinite(value) || value is < 0 or > 200) { error.Text = "估时需要在 0 到 200 小时之间。"; return; } estimate = value; }
            if (string.IsNullOrWhiteSpace(title.Text) || course.SelectedIndex < 0) { error.Text = "请填写任务并选择课程。"; return; }
            var selected = semester.Courses[course.SelectedIndex];
            dialog.Close(new StudyTask { CourseKey = selected.Key, CourseName = selected.Name, Name = title.Text.Trim(), Kind = Rules.Types[kind.SelectedIndex], Week = due, Weight = (double)(weight.Value ?? 2), EstHours = estimate, Source = source.Text ?? "" });
        }, "accent"), Ui.Button("取消", () => dialog.Close())));
        return await dialog.ShowDialog<StudyTask?>(owner);
    }
}
