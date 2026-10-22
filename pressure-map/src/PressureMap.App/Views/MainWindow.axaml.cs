using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PressureMap.Core;
using PressureMap.Localization;
using PressureMap.Services;

namespace PressureMap.Views;

public sealed record NavigationItem(string Title, string Icon, int Page);
public partial class MainWindow : Window
{
    public Store Store { get; private set; }
    public Store LocalStore { get; }
    public Preferences Preferences { get; }
    private CloudClient cloud = null!;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private Semester? semester;
    private bool dirty, exiting, restoringNavigation;
    private int currentPage = 0;
    private TodoWindow? desktopTodo;
    private TodoWindow? desktopSchedule;
    private ScheduleControl? schedulePreview;
    private TodoControl? todoPreview;
    private TrayIcon? tray;
    private readonly List<Semester> semesters = [];
    private string aiDraft = "";
    private TextBlock? saveStatus;
    private int reached = 1;
    public MainWindow() : this(new Store()) { }
    public MainWindow(Store store)
    {
        InitializeComponent(); InitializeChrome();
        SizeChanged += (_, _) => UpdatePageWidth(); Store = LocalStore = store;
        Preferences = Preferences.Load(store.DirectoryPath); Loc.Use(Environment.GetEnvironmentVariable("PRESSURE_MAP_LANG") ?? Preferences.Language); cloud = new(Preferences.ApiUrl.Length > 0 ? Preferences.ApiUrl : null); ApplyTheme();
        Title = Loc.T("学期地图"); BrandTitle.Text = Loc.T("学期地图"); BrandSubtitle.Text = Loc.T("规划学期，安排每一天。");
        try
        {
            if (CloudSessionCache.Restore(LocalStore.DirectoryPath, cloud))
            {
                Store = new Store(cloud.AccountDirectory(LocalStore.DirectoryPath));
                syncEngine = new(cloud, Store, CanApplyCloudUpdate);
                syncMessage = "登录状态已恢复 · 等待同步";
            }
        }
        catch { cloud.Logout(); Store = LocalStore; syncEngine = null; }
        cloud.SessionChanged += () =>
        {
            try { CloudSessionCache.Save(LocalStore.DirectoryPath, cloud); }
            catch { SetSyncStatus("登录状态保存失败，请检查本机目录权限。"); }
        };
        UpdateAccountChip();
        noticeTimer.Tick += (_, _) => { noticeTimer.Stop(); ErrorBanner.IsVisible = false; };
        syncTimer.Tick += async (_, _) => await SyncNowAsync(false); syncTimer.Start();
        Navigation.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        Navigation.ItemTemplate = new FuncDataTemplate<NavigationItem>((item, _) => {
            if (item is null) return new Border();
            var row = Ui.Row(Ui.Icon(item.Icon), Ui.Text(item.Title)); row.Spacing = 12;
            ToolTip.SetTip(row, item.Title); return row;
        });
        Navigation.SelectionChanged += (_, _) => { if (!restoringNavigation && Navigation.SelectedItem is NavigationItem selected) Navigate(selected.Page); };
        BuildNavigation();
        AccountButton.Click += (_, _) => SelectPage(6);
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        Closing += async (_, args) =>
        {
            if (exiting) return;
            args.Cancel = true;
            if ((desktopTodo?.IsVisible == true || desktopSchedule?.IsVisible == true || desktopPressure?.IsVisible == true || Preferences.TaskRemindersEnabled && Store.ListTodos().Any(t => !t.IsFinished && t.RemindersEnabled && t.Deadline is not null)) && EnsureTray()) { Hide(); return; }
            await RequestExitAsync();
        };
        KeyDown += async (_, args) =>
        {
            if (args.KeyModifiers == KeyModifiers.Control && args.Key == Key.N) { args.Handled = true; await NewSemesterAsync(); }
            else if (args.KeyModifiers == KeyModifiers.Control && args.Key == Key.S) { args.Handled = true; Save(); }
            else if (args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && args.Key == Key.E) { args.Handled = true; await ExportAsync(); }
            else if (args.KeyModifiers == KeyModifiers.Alt && args.Key is >= Key.D1 and <= Key.D4 && semester is not null) { args.Handled = true; SetStep((int)args.Key - (int)Key.D1 + 1); }
        };
        RefreshSemesters(); SelectPage(0);
    }
    public void Error(Exception error) => Error(error.Message);
    public void Error(string message) { noticeTimer.Stop(); ErrorBanner.Background = Brush.Parse("#FFF0EC"); ErrorText.Text = message; ErrorText.Foreground = Brush.Parse("#C73A39"); ErrorClose.IsVisible = true; ErrorBanner.IsVisible = true; }
    private void Notice(string message) { noticeTimer.Stop(); noticeTimer.Start(); ErrorBanner.Background = Brush.Parse("#EAF5EF"); ErrorText.Text = message; ErrorText.Foreground = Brush.Parse("#0F7A5A"); ErrorClose.IsVisible = false; ErrorBanner.IsVisible = true; }
    private void DismissError(object? sender, RoutedEventArgs args) => ErrorBanner.IsVisible = false;
    private void Navigate(int index)
    {
        if (!Save()) { restoringNavigation = true; Navigation.SelectedIndex = NavigationIndex(currentPage); restoringNavigation = false; return; }
        try
        {
            PageHost.Content = index switch { 0 => TodayPage(), 1 => PlannerPage(), 2 => TodosPage(), 3 => GeneralPage(), 4 => ShortcutsPage(), 5 => AppearancePage(), 6 => DataPage(), 8 => SchedulePage(), 9 => OnlineCoursesPage(), 10 => AiPlannerPage(), _ => AboutPage() };
            if (index is >= 3 and <= 7 && PageHost.Content is PageFrame settingsPage)
                settingsPage.SetNavigation(SettingsNavigation(index));
            PageHost.MaxWidth = 1200;
            PageHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            currentPage = index;
            UpdatePageWidth();
            AnimatePage();
        }
        catch (Exception error) { Error(error); }
    }
    private int NavigationIndex(int page) => Navigation.ItemsSource!.Cast<NavigationItem>().ToList().FindIndex(item => item.Page == (page is >= 3 and <= 7 ? 3 : page));
    private void BuildNavigation() => Navigation.ItemsSource = new NavigationItem[] { new(Loc.T("今天"), "Home", 0), new(Loc.T("待办任务"), "Todo", 2), new(Loc.T("课程日历"), "Calendar", 8), new(Loc.T("学期规划"), "Map", 1), new(Loc.IsEnglish ? "AI Assistant" : "AI助手", "GPT", 10), new(Loc.T("网课助手"), "Monitor", 9), new(Loc.T("设置"), "Settings", 3) };
    private void SetLanguage(string language)
    {
        if (Preferences.Language == language) return;
        Preferences.Language = language; SavePreferences(); Loc.Use(language); ApplyLanguage();
    }
    private void ApplyLanguage()
    {
        Title = Loc.T("学期地图"); BrandTitle.Text = Loc.T("学期地图"); BrandSubtitle.Text = Loc.T("规划学期，安排每一天。");
        var page = currentPage;
        restoringNavigation = true;
        try { BuildNavigation(); Navigation.SelectedIndex = NavigationIndex(page); }
        finally { restoringNavigation = false; }
        UpdateAccountChip(); Navigate(page);
    }
    public void SelectPage(int page)
    {
        restoringNavigation = true;
        try { Navigation.SelectedIndex = NavigationIndex(page); }
        finally { restoringNavigation = false; }
        Navigate(page);
    }
    private static Control Page(string title, params Control[] children) => new PageFrame(Ui.Text(title, "h1"), Ui.Stack(children));
    private Control HomePage() => Page("首页", Ui.Card(Ui.Stack(Ui.Text("把学期安排和今天要做的事放在一起。", "h2"),
        Ui.Text("先规划每周的压力，再把具体任务放到桌面。", "muted"), Ui.Row(Ui.AsyncButton("新建学期", NewSemesterAsync, "accent"), Ui.Button("查看待办", () => SelectPage(2))))),
        Ui.Text(Loc.F("{0} 个学期 · {1} 件待办", semesters.Count, Store.ListTodos().Count(t => !t.Completed)), "muted"));
    private void RefreshSemesters() { Store.EnsureDefaultSemesters(DateTime.Today); semesters.Clear(); semesters.AddRange(Store.ListSemesters()); }
    private void Changed()
    {
        dirty = true; if (saveStatus is not null) saveStatus.Text = Loc.T("有未保存的修改"); saveTimer.Stop(); saveTimer.Start();
    }
    private bool Save()
    {
        saveTimer.Stop(); if (!dirty || semester is null) return true;
        try { Store.Save(semester); dirty = false; if (saveStatus is not null) saveStatus.Text = Loc.T("已保存") + " · " + DateTime.Now.ToString("HH:mm"); ErrorBanner.IsVisible = false; RefreshSemesters(); return true; }
        catch (Exception error) { Error(error); if (saveStatus is not null) saveStatus.Text = Loc.T("保存失败，修改仍保留在界面中"); return false; }
    }
    /// <summary>首次启动时提示一次登录，之后不再自动弹出，改由侧栏账号区进入。</summary>
    public async Task PromptLoginAtFirstRunAsync()
    {
        if (Preferences.LoginPrompted) return;
        Preferences.LoginPrompted = true; SavePreferences();
        if (cloud.User is not null) return;
        // 已有本机数据的老用户不打扰，只有真正的空数据首次启动才提示。
        if (!SnapshotData.Parse(LocalStore.Snapshot()).IsEmpty) return;
        await ShowLoginAsync();
    }
    public async Task ShowLoginAsync()
    {
        if (HasPendingTodoDraft) { Error("请先添加或清空待办输入，再切换账号。"); return; }
        if (!Save()) return;
        if (Store != LocalStore)
        {
            if (!await Dialogs.Confirm(this, "退出登录", "返回本机数据，账户缓存仍保留在这台设备。")) return;
            if (syncUiBusy || syncEngine?.IsBusy == true) { Notice("正在同步，请完成后退出登录。"); return; }
            try { await cloud.LogoutAsync(); } catch (Exception) { cloud.Logout(); }
            syncEngine = null; Store = LocalStore; syncMessage = "本地数据 · 无需登录";
        }
        else
        {
            if (await Dialogs.Login(this, cloud) is null) return;
            try {
                Store = new Store(cloud.AccountDirectory(LocalStore.DirectoryPath));
                if (SnapshotData.Parse(Store.Snapshot()).IsEmpty && !SnapshotData.Parse(LocalStore.Snapshot()).IsEmpty)
                {
                    var remote = await cloud.DownloadAsync();
                    if (remote["revision"] is null && await Dialogs.Confirm(this, "同步本机数据", "是否将这台设备已有的课程、课表和待办复制到当前账号并同步？"))
                        Store.ApplySnapshot(LocalStore.Snapshot(), SnapshotData.Hash(Store.Snapshot()));
                }
                syncEngine = new(cloud,Store,CanApplyCloudUpdate); syncMessage = "等待首次同步";
            }
            catch (Exception error) { cloud.Logout(); Store = LocalStore; syncEngine = null; Error(error); return; }
        }
        semester = null; aiDraft = ""; reached = 1; dirty = false;
        UpdateAccountChip();
        if (syncEngine is not null) await SyncNowAsync(true);
        RefreshActiveStoreViews();
    }
    private bool EnsureTray()
    {
        if (tray is not null) return true;
        if (!OperatingSystem.IsWindows()) return false;
        var open = new NativeMenuItem("打开主窗口"); open.Click += (_, _) => Restore();
        var todos = new NativeMenuItem("显示桌面待办"); todos.Click += (_, _) => ShowDesktop();
        var calendar = new NativeMenuItem("显示今日课程"); calendar.Click += (_, _) => ShowScheduleDesktop();
        var pressureMenu = new NativeMenuItem("显示每日压力"); pressureMenu.Click += (_, _) => ShowPressureDesktop();
        var quit = new NativeMenuItem("退出"); quit.Click += async (_, _) => await RequestExitAsync();
        tray = new TrayIcon { ToolTipText = "学期地图", Icon = Icon, Menu = new NativeMenu { Items = { open, todos, calendar, pressureMenu, new NativeMenuItemSeparator(), quit } }, IsVisible = true };
        tray.Clicked += (_, _) => Restore(); return true;
    }
    public void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    public void ShowDesktop()
    {
        try
        {
            if (desktopTodo?.NativeAvailable == false) { desktopTodo.Dispose(); desktopTodo = null; }
            if (desktopTodo is null)
            {
                desktopTodo = new TodoWindow(Store, Preferences, LocalStore.DirectoryPath);
                desktopTodo.Changed += RefreshTodoViews;
                desktopTodo.ManageRequested += () => { Restore(); SelectPage(2); };
                desktopTodo.StatusChanged += status => { if (desktopStatus is not null) desktopStatus.Text = status; };
                desktopTodo.HiddenByUser += () => { UpdateDesktopToggle(false); Preferences.DesktopEnabled = false; SavePreferences(); };
            }
            desktopTodo.Present(); EnsureTray(); Preferences.DesktopEnabled = true; SavePreferences(); UpdateDesktopToggle(true);
            if (desktopStatus is not null) desktopStatus.Text = desktopTodo.MaterialStatus;
            ErrorBanner.IsVisible = false;
        }
        catch (Exception error) { Preferences.DesktopEnabled = false; desktopTodo?.HideFromDesktop(); UpdateDesktopToggle(false); Error(error); }
    }
    private void HideDesktop() { desktopTodo?.HideFromDesktop(); Preferences.DesktopEnabled = false; SavePreferences(); UpdateDesktopToggle(false); }
    private void DesktopMode() { ShowDesktop(); if (Preferences.DesktopEnabled && EnsureTray()) Hide(); }
    private void RefreshTodoViews() { _ = RefreshTodayOverviewAsync(); todoPreview?.Refresh(); desktopTodo?.Refresh(); RefreshProfileCounts(); todayTasks?.Refresh(); desktopPressure?.Refresh(); if (settingsPressure is not null) _ = settingsPressure.ReloadAsync(); }
    private void SavePreferences() { try { Preferences.Save(LocalStore.DirectoryPath); } catch (Exception error) { Error("设置未保存：" + error.Message); } }
    private void ApplyTheme()
    {
        if (Application.Current is { } application) application.RequestedThemeVariant = Preferences.Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Classes.Set("reduced-motion", Preferences.ReduceMotion || DesktopNative.ReducedMotion);
        if (MotionReduced) StopMotion();
        desktopTodo?.Classes.Set("reduced-motion", Preferences.ReduceMotion || DesktopNative.ReducedMotion);
        desktopTodo?.ApplyMaterial();
        desktopSchedule?.ApplyMaterial(); desktopPressure?.ApplyMaterial();
        desktopSchedule?.Classes.Set("reduced-motion", Preferences.ReduceMotion || DesktopNative.ReducedMotion);
        if (MotionReduced) StopMotion();
    }
    public async Task RequestExitAsync()
    {
        if (!Save() && !await Dialogs.Confirm(this, "仍有修改未保存", "保存失败，是否退出并放弃未保存的学期修改？")) return;
        exiting = true; Cleanup(); if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime) lifetime.Shutdown();
    }
    public void Cleanup() { onlineCourses?.Dispose(); syncTimer.Stop(); noticeTimer.Stop(); taskTimer.Stop(); reminderWindow?.Close(); desktopPressure?.Dispose(); desktopPressure = null; StopMotion(); saveTimer.Stop(); desktopTodo?.Dispose(); desktopTodo = null; desktopSchedule?.Dispose(); desktopSchedule = null; if (tray is not null) { tray.IsVisible = false; tray.Dispose(); tray = null; } cloud.Dispose(); }
}

