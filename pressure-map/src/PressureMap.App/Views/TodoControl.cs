using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public sealed class TodoControl : UserControl
{
    public string? LastError { get; private set; }
    public string Summary => summary.Text ?? "";
    public bool HasDraft => !minimal && !string.IsNullOrWhiteSpace(input.Text);
    private Store store;
    private readonly bool todayMode;
    /// <summary>桌面组件形态：只展示待完成的事，增删改都在主界面里做。</summary>
    private readonly bool minimal;
    private readonly TextBlock summary = Ui.Text("", "caption"), error = Ui.Text("", "danger");
    private readonly ComboBox filter = new() { ItemsSource = new[] { "待完成", "重要", "今天及逾期", "已完成", "全部" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox search = Ui.Input("", "搜索待办"), input = Ui.Input("", "添加一件要做的事");
    private readonly ListBox list = new() { SelectionMode = SelectionMode.Multiple, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly Control empty;
    public event Action? Changed, SettingsRequested, ImportRequested, ManageRequested;
    public event Action<bool>? EditingChanged;
    public int Filter { get => filter.SelectedIndex; set { filter.SelectedIndex = value; Refresh(); } }
    public TodoControl(Store store, bool todayMode = false, bool embedded = false, bool minimal = false)
    {
        this.store = store; this.todayMode = todayMode; this.minimal = minimal;
        empty = minimal
            ? Ui.EmptyState("Check", "没有待完成的事了", "新增、编辑或删除待办，请在应用内操作。", "在应用内打开", () => ManageRequested?.Invoke())
            : Ui.EmptyState("Check", todayMode ? "今天没有待办" : "清单是空的", "添加一件要做的事，或从课程里创建任务", todayMode ? "添加第一个任务" : "导入当前学期任务", () => { if (todayMode) input.Focus(); else ImportRequested?.Invoke(); });
        if (todayMode) filter.SelectedIndex = 2; input.MaxLength = 300; MinWidth = 260;
        HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        ClipToBounds = true; FontSize = 14;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        list.ItemTemplate = new FuncDataTemplate<Todo>((todo, _) => todo is null ? new Border() : BuildRow(todo)); list.Classes.Add("todo-list");
        filter.SelectionChanged += (_, _) => Refresh(); search.TextChanged += (_, _) => Refresh();
        if (!minimal)
        {
            input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTodo(); e.Handled = true; } };
            foreach (var field in new[] { input, search }) { field.GotFocus += (_, _) => EditingChanged?.Invoke(true); field.LostFocus += (_, _) => EditingChanged?.Invoke(false); }
        }
        error.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) error.IsVisible = !string.IsNullOrEmpty(error.Text); };
        error.IsVisible = false;
        Content = minimal ? CompactLayout() : FullLayout(embedded); Refresh();
    }
    /// <summary>桌面组件布局：摘要 + 待完成清单，不放任何管理入口。</summary>
    private Grid CompactLayout()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        var results = new Grid(); results.Children.Add(list); results.Children.Add(empty);
        void Add(Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
        Add(Ui.Stack(2, summary, error), 0);
        Add(results, 1);
        return grid;
    }
    private Grid FullLayout(bool embedded)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions(todayMode ? "Auto,Auto,*,Auto,Auto" : "Auto,Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 10 };
        void Add(Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
        if (!todayMode && !embedded) Add(Ui.Columns("*,8,Auto", filter, new Border(), Ui.Columns("Auto,4,Auto", Ui.AsyncButton("新建…", NewTaskAsync, "pill"), new Border(), Ui.Button("设置", () => SettingsRequested?.Invoke(), "pill"))), 1);
        var results = new Grid(); results.Children.Add(list); results.Children.Add(empty);
        var operations = Ui.Button("⋯", () => { }, "icon"); operations.Width = 32;
        Avalonia.Automation.AutomationProperties.SetName(operations,"搜索与批量操作");
        operations.Click += (_,_) => {
            var toggleSearch = new MenuItem { Header = search.IsVisible ? "收起搜索" : "搜索待办" }; toggleSearch.Click += (_,_) => { search.IsVisible = !search.IsVisible; if (search.IsVisible) search.Focus(); else search.Text = ""; };
            var selectAll = new MenuItem { Header = "选中当前列表" }; selectAll.Click += (_,_) => list.SelectAll();
            var complete = new MenuItem { Header = "完成所选任务" }; complete.Click += async (_,_) => {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                var ids = list.SelectedItems?.OfType<Todo>().Select(t => t.Id).ToArray() ?? [];
                if (ids.Length == 0) { SetError("请先选择任务，或使用“选中当前列表”。"); return; }
                if (!await Dialogs.Confirm(owner,"批量完成",Loc.F("将 {0} 条任务标记为做完，需要提交的任务仍需单独确认提交。", ids.Length))) return;
                try { store.CompleteTodos(ids); Refresh(); Changed?.Invoke(); } catch (Exception e) { SetError(e.Message); }
            };
            var undo = new MenuItem { Header = "撤销最近删除", IsEnabled = store.CanUndoDeletion }; undo.Click += (_,_) => { try { store.UndoDeletion(); Refresh(); Changed?.Invoke(); } catch (Exception e) { SetError(e.Message); } };
            operations.ContextMenu = new ContextMenu { ItemsSource = new[] { toggleSearch,selectAll,complete,undo } }; operations.ContextMenu.Open(operations);
        };
        ToolTip.SetTip(operations,"搜索、批量完成与撤销删除");
        Add(Ui.Columns("*,6,32",summary,new Border(),operations),0);
        search.IsVisible = !todayMode;
        Add(search, todayMode ? 1 : 2); Add(results, todayMode ? 2 : 3); Add(error, todayMode ? 3 : 4);
        var add = Ui.Button("添加", AddTodo, "accent"); add.MinHeight = 40; add.IsEnabled = !string.IsNullOrWhiteSpace(input.Text); input.TextChanged += (_, _) => add.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        Add(Ui.Columns("*,8,Auto", input, new Border(), add), todayMode ? 4 : 5);
        return grid;
    }
    public void SetStore(Store value) { store = value; input.Text = ""; search.Text = ""; Filter = todayMode ? 2 : 0; error.Text = ""; Refresh(); }
    public Task CreateTaskAsync() => NewTaskAsync();
    public void FocusInput() { Filter = 0; input.Focus(); }
    public void SetError(string message) => error.Text = message;
    public void AddTodo(string title) { input.Text = title; AddTodo(); }
    private void AddTodo()
    {
        try { var added = store.AddTodo(input.Text ?? ""); if (todayMode) { added.PlannedDate = DateTime.Today.ToString("yyyy-MM-dd"); store.UpdateTodo(added); } input.Text = ""; search.Text = ""; Filter = todayMode ? 2 : 0; error.Text = ""; Refresh(); Changed?.Invoke(); }
        catch (Exception exception) { LastError = exception.ToString(); error.Text = exception.Message; }
    }
    private Control BuildRow(Todo todo)
    {
        var check = new CheckBox { IsChecked = todo.Completed, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0), MinWidth = 24, MinHeight = 24, Padding = new Thickness(0), Background = Brushes.Transparent };
        Avalonia.Automation.AutomationProperties.SetName(check, "完成待办：" + todo.Title);
        check.Click += (_, _) =>
        {
            var original = todo.Completed; var originalSubmitted = todo.Submitted;
            try { todo.Completed = check.IsChecked == true; if (!todo.Completed) todo.Submitted = false; store.UpdateTodo(todo); error.Text = ""; Refresh(); Changed?.Invoke(); }
            catch (Exception exception) { todo.Completed = original; todo.Submitted = originalSubmitted; check.IsChecked = original; error.Text = exception.Message; }
        };
        var title = Ui.Text(todo.Title); title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(title, todo.Title);
        if (todo.IsFinished) { title.TextDecorations = TextDecorations.Strikethrough; title.Opacity = .6; }
        if (minimal)
        {
            check.VerticalAlignment = VerticalAlignment.Center; check.Margin = new Thickness(0);
            var meta = Ui.Text(todo.Detail, todo.IsOverdue ? "danger" : "caption"); meta.IsVisible = todo.Detail.Length > 0;
            var stack = Ui.Stack(2, title, meta); stack.VerticalAlignment = VerticalAlignment.Center;
            var compact = new Border { Child = Ui.Columns("26,8,*", check, new Border(), stack) };
            compact.Classes.Add("todo-row"); ToolTip.SetTip(compact, todo.Title + (todo.Detail.Length > 0 ? "\n" + todo.Detail : ""));
            return compact;
        }
        var description = Ui.Text(todo.Detail, todo.IsOverdue ? "danger" : "caption"); description.IsVisible = todo.Detail.Length > 0;
        var words = Ui.Stack(title, description);
        if (todo.RequiresSubmission && todo.Completed && !todo.Submitted)
        {
            var submit = Ui.AsyncButton("标记已提交", async () => { try { todo.Submitted = true; store.UpdateTodo(todo); Refresh(); Changed?.Invoke(); } catch (Exception e) { todo.Submitted = false; error.Text = e.Message; } await Task.CompletedTask; }, "link");
            submit.HorizontalAlignment = HorizontalAlignment.Left;
            words.Children.Add(submit);
        }
        var steps = new StackPanel { Spacing = 3 };
        foreach (var step in todo.Steps)
        {
            var child = new CheckBox { Content = step.Title, IsChecked = step.Completed, Background = Brushes.Transparent, MinHeight = 32, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
            child.Click += (_, _) => { var prior = step.Completed; try { step.Completed = child.IsChecked == true; store.UpdateTodo(todo); Refresh(); Changed?.Invoke(); } catch (Exception e) { step.Completed = prior; child.IsChecked = prior; error.Text = e.Message; } };
            steps.Children.Add(child);
        }
        if (todo.Steps.Count > 0)
        {
            if (todayMode)
            {
                steps.IsVisible = false;
                var toggle = Ui.Button(Loc.F("子任务 {0}/{1}", todo.Steps.Count(s => s.Completed), todo.Steps.Count) + " ▾", () => { }, "link");
                toggle.HorizontalAlignment = HorizontalAlignment.Left;
                toggle.Click += (_, _) => { steps.IsVisible = !steps.IsVisible; };
                words.Children.Add(toggle); words.Children.Add(steps);
            }
            else words.Children.Add(steps);
        }
        words.Spacing = 3; words.VerticalAlignment = VerticalAlignment.Center;
        var more = Ui.Button("⋯", () => { }, "icon"); more.Width = 32; ToolTip.SetTip(more, Loc.T("编辑或删除待办"));
        Avalonia.Automation.AutomationProperties.SetName(more, "操作待办：" + todo.Title);
        more.Click += (_, _) =>
        {
            var edit = new MenuItem { Header = "编辑…" }; edit.Click += async (_, _) => await EditAsync(todo);
            var important = new MenuItem { Header = todo.Important ? "取消重要标记" : "标记为重要" };
            important.Click += (_, _) => { var original = todo.Important; try { todo.Important = !original; store.UpdateTodo(todo); Refresh(); Changed?.Invoke(); } catch (Exception e) { todo.Important = original; Refresh(); error.Text = e.Message; } };
            var delete = new MenuItem { Header = "删除…" }; delete.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                EditingChanged?.Invoke(true);
                try { if (await Dialogs.Confirm(owner, "删除待办", "删除这条待办？\n" + todo.Title)) { store.DeleteTodo(todo.Id); Refresh(); Changed?.Invoke(); } }
                catch (Exception e) { error.Text = e.Message; }
                finally { EditingChanged?.Invoke(false); }
            };
            var tomorrow = new MenuItem { Header = "推迟到明天（截止日期不变）" }; tomorrow.Click += (_,_) => { try { var date = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"); todo.PlannedDate = date; store.UpdateTodo(todo); Refresh(); Changed?.Invoke(); } catch (Exception e) { error.Text = e.Message; } };
            var today = new MenuItem { Header = "安排到今天" }; today.Click += (_, _) => { try { todo.PlannedDate = DateTime.Today.ToString("yyyy-MM-dd"); store.UpdateTodo(todo); Refresh(); Changed?.Invoke(); } catch (Exception e) { error.Text = e.Message; } };
            var menu = new ContextMenu { ItemsSource = new MenuItem[] { edit, today, tomorrow, important, delete } }; more.ContextMenu = menu; menu.Open(more);
        };
        more.VerticalAlignment = VerticalAlignment.Top;
        var row = Ui.Columns("26,8,*,8,32", check, new Border(), words, new Border(), more);
        var border = new Border { Child = row }; border.Classes.Add("todo-row");
        border.DoubleTapped += async (_, _) => await EditAsync(todo);
        return border;
    }
    private async Task EditAsync(Todo todo)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        EditingChanged?.Invoke(true);
        try { if (await Dialogs.EditTodo(owner, todo, store.CourseNames()) is { } updated) { store.UpdateTodo(updated); Refresh(); Changed?.Invoke(); } }
        catch (Exception exception) { error.Text = exception.Message; }
        finally { EditingChanged?.Invoke(false); }
    }
    private async Task NewTaskAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        EditingChanged?.Invoke(true);
        try { var draft = new Todo { PlannedDate = todayMode ? DateTime.Today.ToString("yyyy-MM-dd") : null }; if (await Dialogs.EditTodo(owner, draft, store.CourseNames()) is { } item) { store.CreateTodo(item); Refresh(); Changed?.Invoke(); } }
        catch (Exception e) { error.Text = e.Message; }
        finally { EditingChanged?.Invoke(false); }
    }
    public void Refresh()
    {
        try
        {
            LastError = null; var todos = store.ListTodos(); var today = DateTime.Today.ToString("yyyy-MM-dd");
            var query = search.Text?.Trim() ?? "";
            summary.Text = Loc.F("{0} 件待完成", todos.Count(t => !t.IsFinished)) + (todos.Any(t => t.IsOverdue) ? Loc.F(" · {0} 件已逾期", todos.Count(t => t.IsOverdue)) : "");
            var filtered = TaskWorkload.Ordered(todos, DateTime.Now).Where(t => (todayMode ? TaskWorkload.IsToday(t, DateTime.Now) : Filter switch { 0 => !t.IsFinished, 1 => t.Important && !t.IsFinished, 2 => TaskWorkload.IsToday(t, DateTime.Now), 3 => t.IsFinished, _ => true }))
                .Where(t => query.Length == 0 || (t.Title + " " + t.CourseName + " " + t.SourceLabel).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (todayMode) summary.Text = Loc.F("{0} 件今日任务", filtered.Count) + (filtered.Any(t => t.Completed && t.RequiresSubmission) ? Loc.F(" · {0} 件待提交", filtered.Count(t => t.Completed && t.RequiresSubmission)) : "");
            list.ItemsSource = filtered;
            if (todayMode)
            {
                list.MinHeight = filtered.Count == 0 ? 140 : 0;
                list.MaxHeight = double.PositiveInfinity;
                list.VerticalAlignment = VerticalAlignment.Stretch;
            }

            error.Text = "";
            empty.IsVisible = filtered.Count == 0;
        }
        catch (Exception exception) { LastError = exception.ToString(); error.Text = exception.Message; }
    }
}
