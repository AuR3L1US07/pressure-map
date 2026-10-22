using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    private async Task NewSemesterAsync()
    {
        if (!Save()) return;
        var name = await Dialogs.Ask(this, "新建学期", Store.AcademicSemesterName(DateTime.Today));
        if (name is null) return;
        try { semester = Store.CreateSemester(name); reached = 1; aiDraft = ""; RefreshSemesters(); SelectPage(1); }
        catch (Exception error) { Error(error); }
    }
    private Control PlannerPage()
    {
        var list = new ListBox { ItemsSource = semesters, Classes = { "soft" }, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        list.ItemTemplate = new FuncDataTemplate<Semester>((s, _) => {
            if (s is null) return new Border();
            var words = Ui.Stack(4, Ui.Text(s.Name), Ui.Text(Loc.F("{0} 门课程 · {1} 个任务", s.Courses.Count, s.Tasks.Count), "caption"));
            var more = Ui.IconButton("Info", "学期操作", () => { });
            var rename = new MenuItem { Header = Loc.T("重命名") }; rename.Click += async (_, _) => { list.SelectedItem = s; await RenameSemesterAsync(); };
            var delete = new MenuItem { Header = Loc.T("删除") }; delete.Foreground = Ui.Brush("DangerBrush"); delete.Click += async (_, _) => { list.SelectedItem = s; await DeleteSemesterAsync(); };
            more.ContextMenu = new ContextMenu { ItemsSource = new[] { rename, delete } }; more.Click += (_, _) => more.ContextMenu.Open(more);
            return Ui.Columns("*,Auto", words, more);
        });
        var renameSelected = new MenuItem { Header = Loc.T("重命名") }; renameSelected.Click += async (_, _) => await RenameSemesterAsync();
        var deleteSelected = new MenuItem { Header = Loc.T("删除"), Foreground = Ui.Brush("DangerBrush") }; deleteSelected.Click += async (_, _) => await DeleteSemesterAsync();
        list.ContextMenu = new ContextMenu { ItemsSource = new[] { renameSelected, deleteSelected } };
        if (semester is not null) list.SelectedItem = semesters.FirstOrDefault(x => x.Id == semester.Id);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Semester selected || selected.Id == semester?.Id) return;
            if (!Save()) return;
            try { semester = Store.Load(selected.Id); reached = semester.CurrentStep; aiDraft = ""; RebuildPlanner(); }
            catch (Exception error) { Error(error); }
        };
        var search = Ui.Input("", "搜索学期");
        search.TextChanged += (_, _) => list.ItemsSource = semesters.Where(s => s.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,8,Auto,8,*,8,Auto,8,Auto") };
        void Left(Control c, int row) { Grid.SetRow(c, row); left.Children.Add(c); }
        Left(Ui.Columns("*,Auto", Ui.Text("学期", "h2"), Ui.IconButton("Plus", "新建学期 (Ctrl+N)", () => _ = NewSemesterAsync())), 0); Left(search, 2); Left(list, 4);
        
        if (semester is null)
        {
            var welcome = Ui.Card(Ui.Stack(Ui.Text("看清这学期哪几周会忙。", "h2"), Ui.Text("新建学期或从左侧选择，也可以使用模拟示例体验。", "muted"),
                Ui.Actions(Ui.AsyncButton("新建学期", NewSemesterAsync, "accent"), Ui.Button("加载示例学期", LoadSample)), Ui.Text("示例：每个方块代表一周，颜色越深越忙", "caption"), WeekPreview()));
            return PlannerFrame(left, welcome);
        }
        foreach (var task in semester.Tasks) task.CourseName = semester.Courses.FirstOrDefault(c => c.Key == task.CourseKey)?.Name ?? "";
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,12,Auto,12,*,8,Auto") };
        saveStatus = Ui.Text(dirty ? "有未保存的修改" : "已保存", "caption"); saveStatus.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        var heading = Ui.Columns("*,Auto,14,Auto", Ui.Text(semester.Name, "h2"), saveStatus, new Border(), Ui.Button("保存修改", () => Save(), "accent")); panel.Children.Add(heading);
        var stepButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        var names = new[] { "导入", "确认", "压力地图", "开工建议" };
        for (var index = 1; index <= 4; index++)
        {
            var step = index; var b = Ui.Button($"{index} {Loc.T(names[index - 1])}", () => SetStep(step));
            b.Margin = new Thickness(0, 0, 4, 4); b.IsEnabled = index <= reached; if (index == semester.CurrentStep) b.Classes.Add("accent"); stepButtons.Children.Add(b);
        }
        Grid.SetRow(stepButtons, 2); panel.Children.Add(stepButtons);
        var body = semester.CurrentStep switch { 1 => ImportPage(), 2 => ConfirmPage(), 3 => MapPage(), _ => PlanPage() };
        Grid.SetRow(body, 4); panel.Children.Add(body);
        return PlannerFrame(left, panel);
    }
    private Control PlannerFrame(Control list, Control content)
    {
        var collapsed = Preferences.SemesterListCollapsed;
        var toggle = Ui.Button(collapsed ? "展开学期" : "收起学期", () =>
        {
            Preferences.SemesterListCollapsed = !Preferences.SemesterListCollapsed;
            SavePreferences();
            RebuildPlanner();
        });
        Avalonia.Automation.AutomationProperties.SetName(toggle, Loc.T(collapsed ? "展开学期" : "收起学期"));
        var heading = Ui.Columns("*,Auto", Ui.Text("学期规划", "h1"), toggle);
        return new PageFrame(heading, collapsed ? content : Ui.Columns("280,16,*", Ui.Card(list), new Border(), content), false);
    }
    private static Control WeekPreview()
    {
        var weeks = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 640 };
        for (var i = 0; i < 18; i++) weeks.Children.Add(new Border { Width = 28, Height = 28, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(4), Background = Ui.Brush(i % 5 == 0 ? "AccentBrush" : i % 5 < 3 ? "AccentSoftBrush" : "FocusBrush") });
        return weeks;
    }
    private void RebuildPlanner() { if (Navigation.SelectedIndex == 1) PageHost.Content = PlannerPage(); else SelectPage(1); }
    private void SetStep(int step)
    {
        if (semester is null || step > reached || !Save()) return;
        semester.CurrentStep = step; dirty = true; if (Save()) RebuildPlanner();
    }
    private async Task RenameSemesterAsync()
    {
        if (semester is null || !Save()) return;
        var name = await Dialogs.Ask(this, "重命名学期", semester.Name); if (name is null) return;
        semester.Name = name; dirty = true; if (Save()) RebuildPlanner();
    }
    private async Task DeleteSemesterAsync()
    {
        if (semester is null || !Save()) return;
        if (!await Dialogs.Confirm(this, "删除学期", Loc.F("删除“{0}”及其课程和任务？已导入的桌面待办会保留。", semester.Name))) return;
        try { Store.DeleteSemester(semester.Id); semester = null; reached = 1; aiDraft = ""; RefreshSemesters(); RebuildPlanner(); }
        catch (Exception error) { Error(error); }
    }
    private void LoadSample()
    {
        if (!Save()) return;
        try
        {
            var sample = Rules.Sample(); var created = Store.CreateSemester("示例学期"); sample.Id = created.Id; sample.CreatedAt = created.CreatedAt; sample.CurrentStep = 3;
            Store.Save(sample); semester = sample; reached = 4; RefreshSemesters(); SelectPage(1);
        }
        catch (Exception error) { Error(error); }
    }
    private NumericUpDown Number(double value, decimal min, decimal max, Action<double> change, decimal increment = 1)
    {
        var control = new NumericUpDown { Minimum = min, Maximum = max, Value = (decimal)value, Increment = increment, Width = 150, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        control.ValueChanged += (_, _) => { if (control.Value is { } number) { change((double)number); Changed(); } }; return control;
    }
    private async Task RemoveCourseAsync(Course course)
    {
        if (!await Dialogs.Confirm(this, "删除课程", "删除课程及其全部任务？")) return;
        var current = semester!;
        foreach (var task in current.Tasks.Where(t => t.CourseKey == course.Key).ToList()) current.Tasks.Remove(task);
        current.Courses.Remove(course); Changed(); RebuildPlanner();
    }
    private Control ImportPage()
    {
        var current = semester!;
        var weeks = Number(current.TotalWeeks, 1, 30, value => current.TotalWeeks = (int)value);
        var monday = Ui.Input(current.Week1Monday ?? "", "YYYY-MM-DD（周一）");
        monday.TextChanged += (_, _) => { current.Week1Monday = string.IsNullOrWhiteSpace(monday.Text) ? null : monday.Text.Trim(); Changed(); };
        var settings = Ui.Card(Ui.Columns("*,16,2*", Ui.Field("总周数", weeks), new Border(), Ui.Field("第 1 周周一（可选）", monday)));
        var courses = Ui.Stack(Ui.Text("课程与原文", "h2"));
        foreach (var course in current.Courses)
        {
            var name = Ui.Input(course.Name); name.MaxLength = 100;
            name.TextChanged += (_, _) => { course.Name = name.Text ?? ""; Changed(); };
            var raw = Ui.Input(course.RawText, "粘贴课程通知、考核要求或教学大纲", true); raw.MaxLength = 20000;
            raw.TextChanged += (_, _) => { course.RawText = raw.Text ?? ""; Changed(); };
            var remove = Ui.Link("删除课程", () => _ = RemoveCourseAsync(course));

            courses.Children.Add(Ui.Card(Ui.Stack(Ui.Columns("*,8,Auto", name, new Border(), remove), raw)));
        }
        courses.Children.Add(Ui.Button("+ 添加课程", () => { if (current.Courses.Count >= 10) { Error("每学期最多 10 门课程。"); return; } current.Courses.Add(new Course { Name = "新课程 " + (current.Courses.Count + 1) }); Changed(); RebuildPlanner(); }));
        var prompt = Ui.Input("", multiline: true); prompt.IsReadOnly = true;
        var generate = Ui.Button("生成提示词", () => prompt.Text = Rules.BuildPrompt(current.Courses));
        var copy = Ui.AsyncButton("复制提示词", async () => { prompt.Text = Rules.BuildPrompt(current.Courses); if (Clipboard is not null) await Clipboard.SetTextAsync(prompt.Text); });
        var json = Ui.Input(aiDraft, "粘贴 AI 返回的 JSON 数组", true); json.MinHeight = 140;
        json.TextChanged += (_, _) => aiDraft = json.Text ?? "";
        var parse = Ui.AsyncButton("解析并核对任务", async () =>
        {
            if (current.Tasks.Count > 0 && !await Dialogs.Confirm(this, "替换任务", "解析结果将替换这个学期已有的任务，是否继续？")) return;
            try
            {
                var (tasks, added) = Rules.ParseAi(aiDraft, current);
                foreach (var course in added) current.Courses.Add(course);
                current.Tasks.Clear(); foreach (var task in tasks) current.Tasks.Add(task);
                reached = Math.Max(reached, 2); current.CurrentStep = 2; Changed(); if (Save()) RebuildPlanner();
            }
            catch (Exception error) { Error(error); }
        }, "accent");
        return Ui.Scroll(Ui.Stack(settings, courses, Ui.Card(Ui.Stack(Ui.Text("提取任务", "h2"), Ui.Row(generate, copy), prompt, json, parse)), Ui.Button("加载一个新的示例学期", LoadSample)));
    }
    private Control ConfirmPage()
    {
        var current = semester!;
        var table = new DataGrid { ItemsSource = current.Tasks, AutoGenerateColumns = false, IsReadOnly = true, CanUserResizeColumns = true, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, MinHeight = 220, RowHeight = 40 };
        foreach (var (title, property, width) in new[] { ("课程", "CourseName", 100), ("任务", "Name", 180), ("类型", "KindLabel", 70), ("截止周", "Week", 70), ("权重", "Weight", 60), ("估时", "EstHours", 60) })
            table.Columns.Add(new DataGridTextColumn { Header = Loc.T(title), Binding = new Binding(property), Width = new DataGridLength(width) });
        async Task Edit()
        {
            if (table.SelectedItem is not StudyTask task) return;
            if (await Dialogs.EditTask(this, task, current) is { } updated)
            { current.Tasks[current.Tasks.IndexOf(task)] = updated; Changed(); if (Save()) RebuildPlanner(); }
        }
        table.DoubleTapped += async (_, _) => await Edit(); table.KeyDown += async (_, e) => { if (e.Key == Key.F2) { e.Handled = true; await Edit(); } };
        var add = Ui.AsyncButton("+ 添加任务", async () =>
        {
            if (current.Courses.Count == 0) { Error("请先添加课程。"); return; }
            if (current.Tasks.Count >= 300) { Error("每学期最多 300 个任务。"); return; }
            var task = new StudyTask { CourseKey = current.Courses[0].Key };
            if (await Dialogs.EditTask(this, task, current) is { } created) { current.Tasks.Add(created); Changed(); if (Save()) RebuildPlanner(); }
        });
        var remove = Ui.AsyncButton("删除所选", async () =>
        {
            if (table.SelectedItem is StudyTask selected && await Dialogs.Confirm(this, "删除任务", Loc.F("删除“{0}”？", selected.Name))) { current.Tasks.Remove(selected); Changed(); if (Save()) RebuildPlanner(); }
        });
        var next = Ui.Button("生成压力地图", () => { reached = 4; SetStep(3); }, "accent");
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,12,*,12,Auto") };
        grid.Children.Add(Ui.Text("双击任务或按 F2 编辑；截止周留空表示待确认。", "caption")); Grid.SetRow(table, 2); grid.Children.Add(table);
        var actions = Ui.Row(add, Ui.AsyncButton("编辑所选", Edit), remove, next); Grid.SetRow(actions, 4); grid.Children.Add(actions); return grid;
    }
    private Control MapPage()
    {
        var current = semester!; var pressure = Rules.Compute(current.Tasks, current.TotalWeeks);
        var busiest = Enumerable.Range(1, current.TotalWeeks).MaxBy(w => pressure.Total[w]);
        var warnings = Enumerable.Range(1, current.TotalWeeks).Count(w => pressure.Total[w] >= current.Threshold);
        var panel = Ui.Stack(Ui.Text(Loc.F("第 {0} 周最忙，{1} 个预警周。", busiest, warnings), "h2"));
        var pending = current.Tasks.Count(t => t.Week is null); if (pending > 0) panel.Children.Add(Ui.Text(Loc.F("{0} 个任务待确认截止周，尚未计入地图。", pending), "muted"));
        var threshold = Number(current.Threshold, 1, 30, value => current.Threshold = value, .5m);
        panel.Children.Add(Ui.Row(Ui.Text("预警阈值"), threshold, Ui.Button("更新地图", () => { if (Save()) RebuildPlanner(); })));
        var heat = new Grid { ColumnDefinitions = new ColumnDefinitions("100," + string.Join(",", Enumerable.Repeat("42", current.TotalWeeks))), RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("46", current.Courses.Count + 2))) };
        void Cell(Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); heat.Children.Add(control); }
        for (var week = 1; week <= current.TotalWeeks; week++)
        {
            var label = Ui.Text(week.ToString(), "caption"); label.HorizontalAlignment = HorizontalAlignment.Center; label.TextAlignment = TextAlignment.Center;
            Cell(label, 0, week);
        }
        var rows = current.Courses.Select(c => (c.Name, c.Key, Values: pressure.Per.GetValueOrDefault(c.Key, new double[current.TotalWeeks + 1]))).Append(("总压力", "", pressure.Total)).ToList();
        var colors = new[] { "#EFEDE6", "#E8E5F8", "#D8D2F2", "#B5ACE8", "#8D82D5", "#5F59A6" };
        for (var row = 0; row < rows.Count; row++)
        {
            var (name, key, values) = rows[row]; Cell(Ui.Text(name, "caption"), row + 1, 0);
            for (var week = 1; week <= current.TotalWeeks; week++)
            {
                var value = values[week]; var limit = key.Length == 0 ? current.Threshold : 5;
                var bucket = value <= 0 ? 0 : value < limit * .25 ? 1 : value < limit * .5 ? 2 : value < limit * .75 ? 3 : value < limit ? 4 : 5;
                var w = week; var related = current.Tasks.Where(t => t.Week == w && (key.Length == 0 || t.CourseKey == key)).ToList();
                var control = Ui.AsyncButton(value.ToString("0.#"), async () =>
                {
                    var details = related.Count == 0 ? "该周没有截止任务，压力来自邻近任务的提前准备。" : string.Join("\n\n", related.Select(t => t.Name + " · " + Loc.Kind(t.Kind) + "\n" + t.Source));
                    await Dialogs.Info(this, Loc.F("{0} · 第 {1} 周", name, w), details);
                });
                control.Padding = new Thickness(0); control.Margin = new Thickness(1); control.CornerRadius = new CornerRadius(4); control.Background = Brush.Parse(colors[bucket]); control.Foreground = bucket >= 4 ? Brushes.White : Brush.Parse("#1F1E1D");
                Control cell = control;
                if (pressure.Total[week] >= current.Threshold)
                {
                    // A one-sided border changes the content box and pushes the number up.
                    // Keep the warning stripe outside the button's layout instead.
                    var overlay = new Grid(); overlay.Children.Add(control);
                    overlay.Children.Add(new Border { Height = 3, Background = Ui.Brush(pressure.Total[week] >= current.Threshold * 1.4 ? "DangerBrush" : "WarningBrush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 1), IsHitTestVisible = false });
                    cell = overlay;
                }
                ToolTip.SetTip(control, Loc.F("第 {0} 周 · 压力 {1:0.##} · {2}", week, value, Rules.Warning(pressure.Total[week], current.Threshold))); Cell(cell, row + 1, week);
            }
        }
        panel.Children.Add(new ScrollViewer { Content = heat, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        panel.Children.Add(Ui.Text("颜色越深压力越高；橙色下划线表示预警，红色下划线表示高于阈值 1.4 倍。", "caption"));
        panel.Children.Add(Ui.Row(Ui.Button("查看开工建议", () => SetStep(4), "accent"), Ui.AsyncButton("导入桌面待办", ImportTodosAsync)));
        return Ui.Scroll(panel);
    }
    private Control PlanPage()
    {
        var current = semester!;
        var settings = Ui.Stack(Ui.Text("可用时间", "h2"), Ui.Field("每周可用小时", Number(current.HoursPerWeek, 1, 100, value => current.HoursPerWeek = value)));
        var defaults = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var kind in Rules.Types)
        { var k = kind; var field = Ui.Field(Loc.Kind(kind) + (Loc.IsEnglish ? " estimate" : "估时"), Number(current.TypeHours[kind], 0, 200, value => current.TypeHours[k] = value)); field.Width = 110; field.Margin = new Thickness(0, 0, 8, 4); defaults.Children.Add(field); }
        settings.Children.Add(defaults); settings.Children.Add(Ui.Button("更新安排", () => { if (Save()) RebuildPlanner(); }));
        var panel = Ui.Stack(Ui.Card(settings), Ui.Text("按每周可用时间的 40% 安排，高压周再减半；任务共享同一份时间容量。", "caption"));
        foreach (var plan in Rules.Plan(current))
        {
            var details = Ui.Stack(Ui.Text(plan.Task.Name + " · " + plan.Task.CourseName, "h2"), Ui.Text(plan.Summary, plan.Late ? "danger" : "muted"), Ui.Text(plan.Reason, "caption"));
            var track = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", current.TotalWeeks))), Height = 22 };
            for (var week = 1; week <= current.TotalWeeks; week++)
            {
                var allocation = plan.Allocations.FirstOrDefault(a => a.Week == week).Hours;
                var cell = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(3), Background = allocation > 0 ? Brush.Parse("#7F77DD") : Brush.Parse("#EFEDE6") };
                ToolTip.SetTip(cell, Loc.F("第 {0} 周：{1:0.##} 小时", week, allocation)); Grid.SetColumn(cell, week - 1); track.Children.Add(cell);
            }
            details.Children.Add(track); panel.Children.Add(Ui.Card(details));
        }
        var pending = current.Tasks.Count(t => t.Week is null); if (pending > 0) panel.Children.Add(Ui.Text(Loc.F("{0} 个任务截止周待确认，暂不安排。", pending), "muted"));
        return Ui.Scroll(panel);
    }
}
