using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    public async Task RunSmokeAsync()
    {
        var outputIndex = Array.IndexOf(Program.Arguments, "--out");
        var output = outputIndex >= 0 && outputIndex + 1 < Program.Arguments.Length ? Program.Arguments[outputIndex + 1] : Path.Combine(Store.DirectoryPath, "screenshots");
        Directory.CreateDirectory(output);
        try
        {
            if (!Program.Arguments.Contains("--data")) throw new InvalidOperationException("UI 验证必须显式指定临时数据目录。");
            if (Program.Arguments.Contains("--ai-smoke"))
            {
                if (File.Exists(Path.Combine(LocalStore.DirectoryPath, "ai-api.protected"))) throw new InvalidOperationException("AI smoke requires an unconfigured temporary profile.");
                Preferences.ReduceMotion = true; ApplyTheme();
                Width = 1180; Height = 820; SelectPage(10); await Task.Delay(350);
                var labels = PageHost.GetVisualDescendants().OfType<TextBlock>().ToList();
                var adviceTitle = labels.Single(t => t.Text == Loc.T("安排建议"));
                var coursesTitle = labels.Single(t => t.Text == Loc.T("今日课程"));
                if (adviceTitle.TranslatePoint(default, PageHost)!.Value.Y >= coursesTitle.TranslatePoint(default, PageHost)!.Value.Y)
                    throw new InvalidOperationException("AI advice must precede course details.");
                Capture(Path.Combine(output, "ai-top.png"));
                var generate = PageHost.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Loc.T("生成今日建议"));
                var advice = PageHost.GetVisualDescendants().OfType<TextBox>().Single(b => b.IsReadOnly);
                generate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var until = DateTime.UtcNow.AddSeconds(95);
                while (!generate.IsEnabled && DateTime.UtcNow < until) await Task.Delay(200);
                if (string.IsNullOrWhiteSpace(advice.Text)) throw new InvalidOperationException("Shared AI did not return advice through the UI.");
                Capture(Path.Combine(output, "ai-generated.png"));
                File.WriteAllText(Path.Combine(output, "smoke.json"), "{\"passed\":true,\"no_local_key\":true,\"no_login\":true,\"advice_before_courses\":true}");
                exiting = true; Cleanup(); ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0); return;
            }
            if (Program.Arguments.Contains("--home-layout-smoke"))
            {
                var csv = $"日期,课程,开始时间,结束时间,教室\n{DateTime.Today:yyyy-MM-dd},高等数学,08:00,09:30,3教311\n{DateTime.Today:yyyy-MM-dd},大学英语,10:00,11:30,6教205\n";
                Store.SaveCalendar(CourseCalendar.Preview("home.csv", csv, DateTime.Today).Source);
                for (var i = 0; i < 9; i++) Store.CreateTodo(new Todo { Title = "完成课程作业 " + (i + 1), PlannedDate = DateTime.Today.ToString("yyyy-MM-dd"), SourceLabel = "虚构演示数据", Steps = [new TaskStep { Title = "阅读要求与整理资料" }, new TaskStep { Title = "完成内容并自查" }] });
                Preferences.ReduceMotion = true; ApplyTheme();
                foreach (var size in new[] { new Size(1180, 820), new Size(960, 640) })
                {
                    Width = size.Width; Height = size.Height; SelectPage(0);
                    await todayCourses!.ReloadAsync(); await RefreshTodayOverviewAsync(); await Task.Delay(350);
                    var date = todayCourses.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Loc.Date(DateTime.Today, "M月d日 dddd", "dddd, MMM d"));
                    if (date.TranslatePoint(default, PageHost) is not { } point || point.Y > 280) throw new InvalidOperationException("课程日期未贴近卡片顶部。");
                    if (PageHost.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("虚构演示数据") == true)) throw new InvalidOperationException("首页仍显示演示标注。");
                    todayTasks!.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
                    await Task.Delay(80);
                    Capture(Path.Combine(output, $"home-{size.Width}.png"));
                }
                File.WriteAllText(Path.Combine(output, "smoke.json"), "{\"passed\":true}");
                exiting = true; Cleanup(); ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0); return;
            }
            if (Program.Arguments.Contains("--style-smoke"))
            {
                await VerifyNavigationMotionAsync(output);
                SelectPage(8); await Task.Delay(200);
                var picker = PageHost.GetVisualDescendants().OfType<CalendarDatePicker>().First();
                picker.SelectedDate = DateTime.Today; picker.IsDropDownOpen = true;
                await Task.Delay(300);
                var popup = picker.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First();
                var popupCalendar = (Calendar)popup.Child!;
                using (var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(popupCalendar.Bounds.Width), (int)Math.Ceiling(popupCalendar.Bounds.Height))))
                { image.Render(popupCalendar); image.Save(Path.Combine(output, "calendar-popup.png")); }
                if (popupCalendar.CornerRadius.TopLeft < 10) throw new InvalidOperationException("Calendar popup is missing rounded corners.");
                picker.IsDropDownOpen = false;
                WindowState = WindowState.Maximized; await Task.Delay(150);
                if (WindowState != WindowState.Normal || Width > 1280 || Height > 820) throw new InvalidOperationException("Main window exceeded compact bounds.");
                Capture(Path.Combine(output, "compact-window.png"));
                File.WriteAllText(Path.Combine(output, "smoke.json"), "{\"passed\":true,\"calendar\":true,\"window_bounds\":true,\"navigation\":true}");
                exiting = true; Cleanup(); ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0);
                return;
            }
            var initialSemesters = Store.ListSemesters();
            var defaultNames = Enumerable.Range(0, 4).Select(i => Store.AcademicSemesterName(DateTime.Today, i)).ToHashSet();
            if (initialSemesters.Count != 4 || initialSemesters.Any(s => !defaultNames.Contains(s.Name) || s.Courses.Count != 0 || s.Tasks.Count != 0) || Store.ListTodos().Count != 0) throw new InvalidOperationException("UI 验证只允许使用仅含预设学期的临时数据库。");
            SelectPage(1);
            await Task.Delay(340); Capture(Path.Combine(output, "semester-defaults.png"));
            this.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Loc.T("收起学期")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!Preferences.SemesterListCollapsed) throw new InvalidOperationException("学期列表未收起。");
            await Task.Delay(340); Capture(Path.Combine(output, "semester-collapsed.png"));
            this.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Loc.T("展开学期")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await VerifyChromeAndMotionAsync(output);
            await VerifyNavigationMotionAsync(output);
            if (Program.Arguments.Contains("--motion-smoke"))
            {
                File.WriteAllText(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(new { passed = true, page_motion = true, reduced_motion = true, motion_geometry = true }, DataJson.Options));
                exiting = true; Cleanup(); ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0);
                return;
            }
            await VerifyOnlineCoursesAsync(output);
            SelectPage(3); SetLanguage(Loc.English); await Task.Delay(350);
            if (currentPage != 3 || Navigation.SelectedItem is not NavigationItem { Page: 3 }) throw new InvalidOperationException("切换英文后导航选中状态丢失。");
            Capture(Path.Combine(output, "settings-english.png"));
            SetLanguage(Loc.Chinese); await Task.Delay(350);
            if (currentPage != 3 || Navigation.SelectedItem is not NavigationItem { Page: 3 }) throw new InvalidOperationException("切换中文后导航选中状态丢失。");
            var sample = Rules.Sample(); var created = Store.CreateSemester("界面验证示例"); sample.Id = created.Id; sample.CreatedAt = created.CreatedAt; Store.Save(sample);
            semester = sample; reached = 4; Store.AddTodo("完成高等数学第六章习题", DateTime.Today.ToString("yyyy-MM-dd"), true);
            Store.AddTodo("和小组成员确认报告分工", DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"));
            Store.AddTodo("整理本周的课堂笔记"); RefreshSemesters();
            for (var index = 0; index < 9; index++)
            {
                SelectPage(index); await Task.Delay(340); Capture(Path.Combine(output, $"page-{index}.png"));
            }
            WindowState = WindowState.Maximized;
            await Task.Delay(250);
            foreach (var page in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })
            {
                SelectPage(page); await Task.Delay(350);
                if (PageHost.Bounds.Right > Bounds.Width + 0.5 || PageHost.Bounds.Width <= 0)
                    throw new InvalidOperationException("宽屏页面超出内容区域。");
                Capture(Path.Combine(output, $"wide-page-{page}.png"));
            }
            WindowState = WindowState.Normal;
            await Task.Delay(200);
            for (var step = 1; step <= 4; step++)
            { semester.CurrentStep = step; SelectPage(1); await Task.Delay(340); Capture(Path.Combine(output, $"planner-{step}.png")); }
            // 保存失败必须保留当前页及修改，不能出现导航与正文不一致。
            semester.Threshold = 0; dirty = true; SelectPage(2);
            if (Navigation.SelectedIndex != NavigationIndex(1) || !dirty) throw new InvalidOperationException("保存失败未阻止导航。");
            semester.Threshold = 7; if (!Save()) throw new InvalidOperationException("无法恢复有效数据。");
            using (var loginCloud = new CloudClient(""))
            {
                var loginTask = Dialogs.Login(this, loginCloud); await Task.Delay(100);
                var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                var dialog = lifetime.Windows.Single(w => w.Title == "账号 · 学期地图");
                var login = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "登录");
                if (dialog.WindowDecorations != WindowDecorations.BorderOnly || !dialog.ExtendClientAreaToDecorationsHint) throw new InvalidOperationException("账号弹窗未使用统一标题栏。");
                if (dialog.GetVisualDescendants().OfType<Button>().Any(b => (b.Content as string)?.Contains("服务地址") == true)) throw new InvalidOperationException("账号弹窗不应显示服务地址。");
                CaptureWindow(dialog, Path.Combine(output, "login.png"));
                Services.NativeCapture.Save(dialog.TryGetPlatformHandle()!.Handle, Path.Combine(output, "login-native.png"));
                login.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
                if (loginCloud.Token is not null || !login.IsEnabled) throw new InvalidOperationException("空账号不应发起登录。");
                CaptureWindow(dialog, Path.Combine(output, "login-error.png"));
                dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "没有账号？注册").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100); CaptureWindow(dialog, Path.Combine(output, "register.png"));
                if (login.Content as string != "注册并登录") throw new InvalidOperationException("注册模式切换失败。");
                login.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (loginCloud.User is not null) throw new InvalidOperationException("无效注册不应登录。");
                dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "暂不登录，继续使用").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (await loginTask is not null) throw new InvalidOperationException("跳过登录返回了账户。");
            }
            SelectPage(2); await Task.Delay(350); Capture(Path.Combine(output, "main.png"));
            Services.NativeCapture.Save(TryGetPlatformHandle()!.Handle, Path.Combine(output, "main-native.png"));
            var layout = this.GetVisualDescendants().OfType<Avalonia.Controls.Control>().Where(c => c is TodoControl || c is Avalonia.Controls.TextBlock text && new[] { "待办", "桌面显示", "接入桌面" }.Contains(text.Text)).Select(c => new { type = c.GetType().Name, text = (c as Avalonia.Controls.TextBlock)?.Text, bounds = c.Bounds.ToString(), position = c.TranslatePoint(new Point(), this)?.ToString(), parent = c.GetVisualParent()?.GetType().Name });
            File.WriteAllText(Path.Combine(output, "layout.json"), JsonSerializer.Serialize(layout, DataJson.Options));
            if (todoPreview?.LastError is { } todoError) throw new InvalidOperationException(todoError);
            var before = Store.ListTodos().Count; todoPreview!.AddTodo("验证新增待办");
            if (Store.ListTodos().Count != before + 1) throw new InvalidOperationException("界面新增待办未保存。");
            var temporaryTodo = Store.ListTodos().Single(t => t.Title == "验证新增待办"); Store.DeleteTodo(temporaryTodo.Id); RefreshTodoViews();
            ShowDesktop(); if (desktopTodo is null || !desktopTodo.IsVisible) throw new InvalidOperationException("桌面组件没有显示。");
            await Task.Delay(250); var desktop = desktopTodo.VerifyNativeLayer();
            var glass = await VerifyDesktopGlassAsync(output);
            var calendar = await VerifyScheduleAsync(output);
            var studentWorkflow = await VerifyStudentWorkflowAsync(output); await VerifyDesktopTaskStylingAsync(output);
            var optimizations = await VerifyOptimizationsAsync(output); await ReviewPagesAsync(output);
            Services.NativeCapture.Save(desktopTodo.TryGetPlatformHandle()!.Handle, Path.Combine(output, "desktop-native.png"));
            CaptureWindow(desktopTodo, Path.Combine(output, "desktop-todo.png"));
            desktopTodo.ToggleCollapse(); await Task.Delay(200); CaptureWindow(desktopTodo, Path.Combine(output, "desktop-capsule.png"));
            File.WriteAllText(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(new { passed = true, pages = 9, planner_steps = 4, login_skip_and_failure = true, navigation_save_guard = true, todo_add = true, integrated_chrome = true, page_motion = true, reduced_motion = true, optimizations, studentWorkflow, calendar, glass, desktop }, DataJson.Options));
            exiting = true; Cleanup(); ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0);
        }
        catch (Exception error)
        {
            Capture(Path.Combine(output, "failure.png")); File.WriteAllText(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(new { passed = false, error = error.ToString(), uiError = ErrorText.Text }, DataJson.Options));
            exiting = true; Cleanup(); ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(1);
        }
    }
    private async Task<object> VerifyOptimizationsAsync(string output)
    {
        var life = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        SelectPage(0); await RefreshTodayOverviewAsync(); await Task.Delay(180);
        var pressureTask = ShowPressureDetailsAsync(); await Task.Delay(180);
        var detail = life.Windows.Single(w => w.Title == "今日压力来源");
        if (!detail.GetVisualDescendants().OfType<ProgressBar>().Any()) throw new InvalidOperationException("压力来源没有列出占比。");
        CaptureWindow(detail,Path.Combine(output,"pressure-sources.png")); detail.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await pressureTask;
        var one = Store.CreateTodo(new Todo { Title="批量任务一",PlannedDate=DateTime.Today.ToString("yyyy-MM-dd") });
        var two = Store.CreateTodo(new Todo { Title="批量任务二",PlannedDate=DateTime.Today.ToString("yyyy-MM-dd") });
        todayTasks!.Refresh(); var list = todayTasks.GetVisualDescendants().OfType<ListBox>().Single();
        foreach (var item in list.ItemsSource!.Cast<Todo>().Where(t => t.Id == one.Id || t.Id == two.Id)) list.SelectedItems!.Add(item);
        var operations = todayTasks.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "搜索与批量操作");
        operations.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        operations.ContextMenu!.ItemsSource!.Cast<MenuItem>().Single(i => i.Header as string == "完成所选任务").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(120);
        life.Windows.Single(w => w.Title == "批量完成").GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "确认").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
        if (Store.ListTodos().Where(t => t.Id == one.Id || t.Id == two.Id).Any(t => !t.Completed)) throw new InvalidOperationException("批量完成操作未保存。");
        operations.ContextMenu.Close(); Store.DeleteTodo(one.Id);
        SelectPage(6); await Task.Delay(150);
        PageHost.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "撤销最近删除的待办").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!Store.ListTodos().Any(t => t.Id == one.Id)) throw new InvalidOperationException("撤销删除入口失效。");
        var backup = Store.Backup(); Store.AddTodo("恢复预览期间新增的测试数据");
        var restore = RestoreBackupAsync(backup); await Task.Delay(180);
        var preview = life.Windows.Single(w => w.Title == "预览恢复备份"); CaptureWindow(preview,Path.Combine(output,"backup-restore-preview.png"));
        preview.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await restore;
        if (!Store.ListTodos().Any(t => t.Title == "恢复预览期间新增的测试数据")) throw new InvalidOperationException("取消恢复仍修改了数据。");
        restore = RestoreBackupAsync(backup); await Task.Delay(160);
        life.Windows.Single(w => w.Title == "预览恢复备份").GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "确认").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await restore;
        if (Store.ListTodos().Any(t => t.Title == "恢复预览期间新增的测试数据")) throw new InvalidOperationException("确认恢复未替换数据。");
        ShowDesktop(); desktopTodo!.SetClickThrough(true); if (!desktopTodo.IsClickThrough) throw new InvalidOperationException("鼠标穿透未开启。");
        desktopTodo.SetClickThrough(false); if (desktopTodo.IsClickThrough) throw new InvalidOperationException("鼠标穿透未关闭。");
        var area = Screens.Primary!.WorkingArea; desktopTodo.Position = new PixelPoint(area.X+8,area.Y+8); desktopTodo.SnapToEdges();
        if (desktopTodo.Position.X != area.X || desktopTodo.Position.Y != area.Y) throw new InvalidOperationException("边缘吸附未生效。");
        var originalCloud = cloud;
        var backend = new SmokeSyncHttp { Snapshot = JsonNode.Parse(Store.Snapshot())!.AsObject(), Revision = "initial" };
        cloud = new CloudClient("https://ui-smoke.invalid",backend);
        try
        {
            var login = ShowLoginAsync(); await Task.Delay(150);
            var loginWindow = life.Windows.Single(w => w.Title == "账号 · 学期地图");
            loginWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "没有账号？注册").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var loginFields = loginWindow.GetVisualDescendants().OfType<TextBox>().ToList();
            loginFields.Single(f => f.PlaceholderText == "账号").Text = "ui-student";
            loginFields.Single(f => f.PlaceholderText == "显示名").Text = "界面验证";
            foreach (var field in loginFields.Where(f => f.PasswordChar == '●')) field.Text = "only-a-test-password";
            loginWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "注册并登录").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await login;
            if (!backend.Registered || Store == LocalStore || !syncMessage.StartsWith("已同步")) throw new InvalidOperationException("登录后没有切换缓存并同步。");
            backend.Offline=true; Store.AddTodo("界面离线编辑"); await SyncNowAsync(false);
            if (!syncMessage.StartsWith("同步待重试")) throw new InvalidOperationException("离线同步状态未显示。");
            backend.Offline=false; await SyncNowAsync(true);
            if (!backend.Snapshot["desktop_todos"]!.AsArray().Any(n => n!["title"]!.GetValue<string>() == "界面离线编辑")) throw new InvalidOperationException("重试未上传界面编辑。");
            SelectPage(0); await Task.Delay(120);
            var draftInput=todayTasks!.GetVisualDescendants().OfType<TextBox>().Single(b => b.PlaceholderText == "添加一件要做的事"); draftInput.Text="未添加的输入草稿";
            backend.Snapshot["desktop_todos"]!.AsArray().Add(JsonSerializer.SerializeToNode(new Todo { Title="草稿保护云端更新" },DataJson.Options)); backend.Revision="remote-draft-edit";
            await SyncNowAsync(false);
            if (draftInput.Text != "未添加的输入草稿" || Store.ListTodos().Any(t => t.Title == "草稿保护云端更新")) throw new InvalidOperationException("云同步覆盖了未提交的输入草稿。");
            draftInput.Text=""; await SyncNowAsync(true);
            if (!Store.ListTodos().Any(t => t.Title == "草稿保护云端更新")) throw new InvalidOperationException("清空草稿后未恢复云同步。");
            Store.AddTodo("界面本地冲突");
            backend.Snapshot["desktop_todos"]!.AsArray().Add(JsonSerializer.SerializeToNode(new Todo { Title="界面云端冲突" },DataJson.Options)); backend.Revision="remote-edit";
            await SyncNowAsync(false); if (syncEngine?.Conflict is null) throw new InvalidOperationException("界面未提示同步冲突。");
            var resolving=ResolveConflictAsync(); await Task.Delay(150); var conflictWindow=life.Windows.Single(w => w.Title == "处理同步冲突");
            CaptureWindow(conflictWindow,Path.Combine(output,"sync-conflict.png")); conflictWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "使用云端版本").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await resolving;
            if (!Store.ListTodos().Any(t => t.Title == "界面云端冲突") || Store.ListTodos().Any(t => t.Title == "界面本地冲突")) throw new InvalidOperationException("界面冲突选择未应用。");
            AccountButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(200);
            if (currentPage != 6 || !PageHost.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "退出登录")) throw new InvalidOperationException($"账号入口未打开账号管理页：page={currentPage}, error={ErrorText.Text}");
            await Task.Delay(200); Capture(Path.Combine(output,"signed-in-sync.png"));
            var logout=ShowLoginAsync(); await Task.Delay(120); life.Windows.Single(w => w.Title == "退出登录").GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "确认").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await logout;
            if (Store != LocalStore || cloud.User is not null) throw new InvalidOperationException("退出后未回到游客数据库。");
        }
        finally { cloud.Dispose(); cloud=originalCloud; }
        SelectPage(3); await Task.Delay(180); PageHost.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "桌面组件").BringIntoView(); await Task.Delay(180); Capture(Path.Combine(output,"desktop-settings.png"));
        SelectPage(6); await Task.Delay(250); Capture(Path.Combine(output,"sync-and-backups.png"));
        SelectPage(0); await RefreshTodayOverviewAsync(); await Task.Delay(250); Capture(Path.Combine(output,"optimized-home.png"));
        return new { cloud_login_and_sync=true,draft_preservation=true,offline_retry=true,conflict_selection=true,logout_isolation=true,pressure_sources=true,batch_complete=true,undo_delete=true,restore_preview_and_cancel=true,restore_apply=true,click_through=true,edge_snap=true };
    }
    private async Task<object> VerifyStudentWorkflowAsync(string output)
    {
        foreach (var old in Store.ListTodos()) { old.Completed = true; Store.UpdateTodo(old); }
        var create = NewTodoForCourseAsync("高等数学A1"); await Task.Delay(160);
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var dialog = lifetime.Windows.Single(w => w.Title == "编辑待办");
        var boxes = dialog.GetVisualDescendants().OfType<TextBox>().ToList(); boxes[0].Text = "完成高数习题并提交";
        boxes.Single(b => b.PlaceholderText?.StartsWith("YYYY-MM-DD") == true).Text = DateTime.Today.ToString("yyyy-MM-dd");
        boxes.Single(b => b.PlaceholderText?.StartsWith("HH:mm") == true).Text = "23:59";
        dialog.GetVisualDescendants().OfType<NumericUpDown>().First().Value = 1.5m;
        if (!dialog.GetVisualDescendants().OfType<ComboBox>().Any(b => b.SelectedItem as string == "高等数学A1")) throw new InvalidOperationException("从课程创建任务未预填课程。");
        var stepInput = boxes.Single(b => b.PlaceholderText == "添加子任务");
        var add = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "添加");
        stepInput.Text = "完成习题"; add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        stepInput.Text = "检查并上传"; add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.GetVisualDescendants().OfType<CheckBox>().First(b => b.Content is null).IsChecked = true;
        CaptureWindow(dialog, Path.Combine(output, "student-task-editor.png"));
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await create;
        var task = Store.ListTodos().Single(t => t.Title == "完成高数习题并提交");
        if (task.Steps.Count != 2 || task.Steps.Count(s => s.Completed) != 1 || task.DueTime != "23:59") throw new InvalidOperationException("任务详情未完整保存。");
        Store.CreateTodo(new Todo { Title = "实验报告提交", CourseName = "大学物理", DueDate = DateTime.Today.ToString("yyyy-MM-dd"), DueTime = "23:59", TaskKind = "报告", RequiresSubmission = true, Completed = true });
        Store.CreateTodo(new Todo { Title = "下周测验复习", CourseName = "高等数学A1", TaskKind = "考试", EstimatedHours = 2, DueDate = DateTime.Today.AddDays(4).ToString("yyyy-MM-dd") });
        SelectPage(0); await todayCourses!.ReloadAsync(); await RefreshTodayOverviewAsync(); await Task.Delay(450);
        if (!PageHost.GetVisualDescendants().OfType<PressureRing>().Any() || PageHost.GetVisualDescendants().OfType<ToggleSwitch>().Any()) throw new InvalidOperationException("首页压力圆环或设置分离错误。");
        if (courseCount?.Text != "2" || taskCount?.Text != "2" || submissionCount?.Text != "1") throw new InvalidOperationException("首页关键概览统计错误。");
        Capture(Path.Combine(output, "today-dashboard.png"));
        if (OperatingSystem.IsWindows()) Services.NativeCapture.Save(TryGetPlatformHandle()!.Handle, Path.Combine(output, "rounded-home-composited.png"), true);
        var originalWidth = Width; var originalHeight = Height; Width = 1080; Height = 640; await Task.Delay(200);
        Capture(Path.Combine(output, "today-compact.png")); Width = originalWidth; Height = originalHeight;
        SelectPage(3); await settingsPressure!.ReloadAsync(); await Task.Delay(200); Capture(Path.Combine(output, "pressure-settings.png"));
        var settingsControls = PageHost.GetVisualDescendants().ToList();
        if (settingsControls.OfType<PressureControl>().Any()) throw new InvalidOperationException("设置页不应再显示压力圆环预览。");
        if (!settingsControls.OfType<ComboBox>().Any(c => c.ItemsSource is string[] names && names.Contains("English"))) throw new InvalidOperationException("设置页缺少语言选项。");
        if (!settingsControls.OfType<ToggleSwitch>().Any(t => Avalonia.Automation.AutomationProperties.GetName(t) == "开启云同步")) throw new InvalidOperationException("设置页缺少云同步开关。");
        if (!settingsControls.OfType<ToggleSwitch>().Any(t => Avalonia.Automation.AutomationProperties.GetName(t) == "在桌面显示压力圆环")) throw new InvalidOperationException("设置页缺少压力圆环开关。");
        var before = settingsPressure.Report!.Score; if (homePressureScore?.Text != before.ToString()) throw new InvalidOperationException("首页与设置压力分数不同。"); if (before is <= 0 or > 100) throw new InvalidOperationException("今日压力未正常显示。");
        SelectPage(0); await Task.Delay(200);
        ShowPressureDesktop(); if (desktopPressure is null || !desktopPressure.IsVisible) throw new InvalidOperationException("桌面压力栏目未打开。");
        var pressure = desktopPressure.GetVisualDescendants().OfType<PressureControl>().Single(); await pressure.ReloadAsync();
        var layer = desktopPressure.VerifyNativeLayer(); if (pressure.Report?.Score != before) throw new InvalidOperationException("软件和桌面压力分数不一致。");
        Hide(); await Task.Delay(200);
        try { Services.NativeCapture.Save(desktopPressure.TryGetPlatformHandle()!.Handle, Path.Combine(output, "desktop-daily-pressure.png"), true); }
        finally { Restore(); }
        task.Completed = true; Store.UpdateTodo(task); RefreshTodoViews(); await settingsPressure.ReloadAsync();
        var after = settingsPressure.Report!.Score; if (after >= before) throw new InvalidOperationException("完成任务后压力值没有下降。");
        var submittedBefore = Store.ListTodos().Count(t => t.Submitted); await Task.Delay(120);
        var submit = todayTasks!.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "标记已提交"); submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Store.ListTodos().Count(t => t.Submitted) != submittedBefore + 1) throw new InvalidOperationException("提交按钮没有保存状态。");
        var reminder = new TaskReminder(task, "smoke-reminder", "界面验证：任务即将截止"); ShowReminder([reminder]); await Task.Delay(120);
        CaptureWindow(reminderWindow!, Path.Combine(output, "task-reminder.png"));
        reminderWindow!.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "延后 15 分钟").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Store.ReminderFor(task.Id)?.SnoozedUntil is not { } snoozedUntil || snoozedUntil <= DateTime.Now || reminderWindow is not null) throw new InvalidOperationException("延后提醒未保存。");
        var near = DateTime.Now.AddMinutes(30);
        Store.CreateTodo(new Todo { Title = "自动提醒验证", DueDate = near.ToString("yyyy-MM-dd"), DueTime = near.ToString("HH:mm") });
        CheckTaskReminders(); if (reminderWindow is null) throw new InvalidOperationException("到期提醒轮询未显示提醒。");
        reminderWindow.Close(); CheckTaskReminders(); if (reminderWindow is not null) throw new InvalidOperationException("同一阶段提醒发生重复。");
        return new { reminder_poll_dedup = true, course_task = true, subtasks = true, precise_deadline = true, submission_action = true, pressure_before = before, pressure_after = after, reminder_snooze = true, desktop_layer = layer };
    }
    private async Task<object> VerifyScheduleAsync(string output)
    {
        var today = DateTime.Today; var tomorrow = today.AddDays(1);
        var text = $"日期,课程,开始时间,结束时间,教室,教师\r\n{today:yyyy-MM-dd},大学英语 A,10:00,11:35,6教中205,\r\n{today:yyyy-MM-dd},高等数学A1,13:30,16:00,3教311,\r\n{tomorrow:yyyy-MM-dd},高等数学A1,08:05,09:40,6教北202,\r\n{tomorrow:yyyy-MM-dd},中国近现代史纲要,10:00,12:25,6教北304,\r\n{tomorrow:yyyy-MM-dd},大学生心理健康教育1,13:30,15:05,6教北304,\r\n{tomorrow:yyyy-MM-dd},中国共产党党史,18:30,20:05,6教北204,\r\n";
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var emptyImport = ImportCalendarDialogAsync(); await Task.Delay(220);
        var emptyDialog = lifetime.Windows.Single(w => w.Title == "导入课表");
        if (emptyDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "确认").IsEnabled) throw new InvalidOperationException("未选择文件时确认按钮应禁用。");
        CaptureWindow(emptyDialog, Path.Combine(output, "calendar-import-empty.png"));
        emptyDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await emptyImport;
        var pdfText = HduPdfCalendar.ToIcs([new CourseOccurrence("PDF 转换预览", today.AddHours(10), today.AddHours(11), "第6教研楼北202")]);
        var pdfImport = ImportCalendarDialogAsync("杭电课表.pdf", null, new HduPdfResult("杭电课表.ics", pdfText, 1, [])); await Task.Delay(260);
        var pdfDialog = lifetime.Windows.Single(w => w.Title == "导入课表");
        if (!pdfDialog.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && c.ItemsSource is IEnumerable<HduSemester> list && list.Any())) throw new InvalidOperationException("PDF 导入缺少学期预设。");
        if (!pdfDialog.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "另存为 ICS…")) throw new InvalidOperationException("PDF 导入缺少 ICS 导出。");
        CaptureWindow(pdfDialog, Path.Combine(output, "pdf-import.png"));
        pdfDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await pdfImport;
        var badImport = ImportCalendarDialogAsync("未知课表.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 not a schedule")); await Task.Delay(220);
        var badDialog = lifetime.Windows.Single(w => w.Title == "导入课表");
        if (badDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "确认").IsEnabled) throw new InvalidOperationException("无法识别的 PDF 仍可确认导入。");
        CaptureWindow(badDialog, Path.Combine(output, "pdf-import-invalid.png"));
        badDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await badImport;
        var csvBytes = Encoding.UTF8.GetBytes(text);
        var cancel = ImportCalendarDialogAsync("界面课表示例.csv", csvBytes); await Task.Delay(220);
        var dialog = lifetime.Windows.Single(w => w.Title == "导入课表");
        if (dialog.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && c.ItemsSource is IEnumerable<HduSemester>)) throw new InvalidOperationException("普通课表不应要求选择学期。");
        if (dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "确认").IsEnabled is false) throw new InvalidOperationException("选好文件后确认按钮未启用。");
        CaptureWindow(dialog, Path.Combine(output, "calendar-import.png"));
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await cancel; if (Store.ListCalendars().Count != 0) throw new InvalidOperationException("取消课表导入仍写入了数据。");
        var import = ImportCalendarDialogAsync("界面课表示例.csv", csvBytes); await Task.Delay(220);
        dialog = lifetime.Windows.Single(w => w.Title == "导入课表");
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "确认").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await import; await schedulePreview!.ReloadAsync(); await Task.Delay(450);
        if (schedulePreview.VisibleCourseCount != 2 || schedulePreview.LastError is not null) throw new InvalidOperationException("今日课程导入后未正确显示。");
        Capture(Path.Combine(output, "calendar-main.png"));
        await schedulePreview.SelectDateAsync(tomorrow); await Task.Delay(160);
        if (schedulePreview.VisibleCourseCount != 4) throw new InvalidOperationException("课程切换日期错误。");
        Capture(Path.Combine(output, "calendar-tomorrow.png"));
        await schedulePreview.SelectDateAsync(today.AddDays(3));
        if (schedulePreview.VisibleCourseCount != 0) throw new InvalidOperationException("无课日期状态错误。");
        await schedulePreview.SelectDateAsync(today);
        ShowScheduleDesktop(); if (desktopSchedule is null || !desktopSchedule.IsVisible) throw new InvalidOperationException("桌面今日课程未打开。");
        var courseView = desktopSchedule.GetVisualDescendants().OfType<ScheduleControl>().Single(); await courseView.ReloadAsync();
        var layer = desktopSchedule.VerifyNativeLayer();
        if (courseView.VisibleCourseCount != 2) throw new InvalidOperationException("桌面课程与主界面数据不一致。");
        if (desktopTodo is null || !desktopTodo.IsVisible) throw new InvalidOperationException("课程与待办无法同时显示。");
        Hide(); await Task.Delay(250);
        try { Services.NativeCapture.Save(desktopSchedule.TryGetPlatformHandle()!.Handle, Path.Combine(output, "calendar-desktop.png"), true); }
        finally { Restore(); }
        desktopSchedule.ToggleCollapse(); await Task.Delay(160);
        CaptureWindow(desktopSchedule, Path.Combine(output, "calendar-collapsed.png"));
        desktopSchedule.ToggleCollapse(); desktopSchedule.ToggleLock();
        if (!Preferences.ScheduleLocked || Preferences.DesktopLocked) throw new InvalidOperationException("课程和待办的独立锁定状态错误。");
        desktopSchedule.ToggleLock();
        return new { imported = true, cancel_safe = true, today_courses = 2, tomorrow_courses = 4, empty_day = true, independent_widgets = true, layer };
    }
    private async Task<object> VerifyDesktopGlassAsync(string output)
    {
        var widget = desktopTodo!;
        if (widget.Opacity != 1) throw new InvalidOperationException("玻璃文字不可整体降低透明度。");
        var enabled = Preferences.DesktopGlass;
        Preferences.DesktopGlass = false; widget.ApplyMaterial(); await Task.Delay(120);
        if (widget.HasNativeBlur) throw new InvalidOperationException("无法关闭原生模糊。");
        Preferences.DesktopGlass = true; widget.ApplyMaterial(); await Task.Delay(200);
        var report = widget.VerifyGlass();
        File.WriteAllText(Path.Combine(output, "glass.json"), JsonSerializer.Serialize(report, DataJson.Options));
        // Isolated high-frequency fixture: it never changes the system wallpaper.
        var pattern = new Grid { Background = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new Avalonia.Media.GradientStops { new(Avalonia.Media.Color.Parse("#3476D4"), 0), new(Avalonia.Media.Color.Parse("#7D67BA"), .45), new(Avalonia.Media.Color.Parse("#E8A99A"), 1) }
        }, ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", 100))) };
        for (var i = 0; i < 100; i++)
        {
            var stripe = new Border { Background = Avalonia.Media.Brush.Parse(i % 2 == 0 ? "#48376FD2" : "#48F0BE8F") };
            Grid.SetColumn(stripe, i); pattern.Children.Add(stripe);
        }
        var fixture = new Window { Width = widget.Width, Height = widget.Height, Position = widget.Position, WindowStartupLocation = WindowStartupLocation.Manual, WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false, Content = pattern };
        try
        {
            fixture.Show(); var fixtureHandle = fixture.TryGetPlatformHandle()!.Handle;
            Services.DesktopNative.Attach(fixtureHandle);
            PlaceGlassFixture(fixtureHandle, widget.TryGetPlatformHandle()!.Handle, 0, 0, 0, 0, 0x13);
            Hide(); await Task.Delay(300);
            Services.NativeCapture.Save(fixtureHandle, Path.Combine(output, "glass-pattern.png"));
            Services.NativeCapture.Save(widget.TryGetPlatformHandle()!.Handle, Path.Combine(output, "glass-proof.png"), true);
            var originalTheme = Preferences.Theme;
            try
            {
                Preferences.Theme = "Dark"; ApplyTheme(); await Task.Delay(180);
                Services.NativeCapture.Save(widget.TryGetPlatformHandle()!.Handle, Path.Combine(output, "glass-dark.png"), true);
            }
            finally { Preferences.Theme = originalTheme; ApplyTheme(); }
        }
        finally { fixture.Close(); }
        try
        {
            await Task.Delay(250);
            Services.NativeCapture.Save(widget.TryGetPlatformHandle()!.Handle, Path.Combine(output, "desktop-composited.png"), true);
        }
        finally { Restore(); Preferences.DesktopGlass = enabled; widget.ApplyMaterial(); }
        return report;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    private static extern bool PlaceGlassFixture(nint window, nint after, int x, int y, int width, int height, uint flags);
    private async Task VerifyChromeAndMotionAsync(string output)
    {
        if (WindowDecorations != WindowDecorations.BorderOnly || !ExtendClientAreaToDecorationsHint)
            throw new InvalidOperationException("原生标题栏未被替换。");
        if (OperatingSystem.IsWindows())
        {
            int Hit(Point point)
            {
                var pixel = this.PointToScreen(point);
                return (int)ChromeHitTest(TryGetPlatformHandle()!.Handle, 0x84, 0, (nint)((pixel.Y << 16) | (pixel.X & 0xFFFF)));
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                var cornerResult = DwmGetCornerPreference(TryGetPlatformHandle()!.Handle, 33, out var cornerPreference, sizeof(int));
                if (cornerResult != 0 || cornerPreference != 2) throw new InvalidOperationException("主窗口圆角设置未生效。");
            }
            var titleHit = Hit(new Point(450, 28));
            var edges = Enumerable.Range(-8, 17).Select(x => Hit(new Point(x, ClientSize.Height / 2))).ToArray();
            var edgeHit = edges.Contains(10) ? 10 : edges[8];
            File.WriteAllText(Path.Combine(output, "chrome-native.json"), JsonSerializer.Serialize(new { titleHit, edgeHit, edges }));
            if (titleHit != 2) throw new InvalidOperationException($"标题栏拖动区域错误：{titleHit}");
            if (edgeHit != 10) throw new InvalidOperationException($"窗口边缘缩放区域错误：{edgeHit}");
        }
        MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120);
        if (WindowState != WindowState.Maximized || Avalonia.Automation.AutomationProperties.GetName(MaximizeButton) != "还原") throw new InvalidOperationException("最大化按钮状态错误。");
        Capture(Path.Combine(output, "maximized.png"));
        MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120);
        if (WindowState != WindowState.Normal) throw new InvalidOperationException("还原按钮状态错误。");
        await Task.Delay(350); MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(350);
        if (WindowState != WindowState.Minimized) throw new InvalidOperationException("最小化按钮无效。");
        Restore(); await Task.Delay(350);
        var reduced = Preferences.ReduceMotion;
        if (!Services.DesktopNative.ReducedMotion)
        {
            Preferences.ReduceMotion = false; SelectPage(0); await Task.Delay(50);
            if (pageMotionItems.Count < 3) throw new InvalidOperationException("今天页卡片逐个进入动画未启动。");
            var samples = new List<object>();
            var moved = false;
            for (var sampleIndex = 0; sampleIndex < 8; sampleIndex++)
            {
                var values = pageMotionItems.Select(item => new { y = item.RenderTransform?.Value.M32 ?? 0, opacity = item.Opacity }).ToArray();
                samples.Add(values);
                moved |= values.Any(value => Math.Abs(value.y) > .1);
                await Task.Delay(25);
            }
            File.WriteAllText(Path.Combine(output, "card-motion-samples.json"), JsonSerializer.Serialize(samples, DataJson.Options));
            if (!moved) throw new InvalidOperationException("动画中途没有实际的卡片位移。");
            await Task.Delay(40); Capture(Path.Combine(output, "page-entering.png"));
            for (var i = 0; i < 8; i++) SelectPage(i);
            await Task.Delay(650);
            await pageMotionCompletion.WaitAsync(TimeSpan.FromSeconds(3));
            if (pageMotionItems.Count != 0) throw new InvalidOperationException("快速切换后动画未复位。");
        }
        Preferences.ReduceMotion = true; SelectPage(2);
        if (pageMotionItems.Count != 0 || startupVisual is not null) throw new InvalidOperationException("减少动画设置没有生效。");
        Preferences.ReduceMotion = reduced;
        Capture(Path.Combine(output, "custom-titlebar.png")); await VerifyMotionGeometryAsync(output);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint ChromeHitTest(nint window, uint message, nint wParam, nint lParam);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetCornerPreference(nint window, int attribute, out int value, int size);
    private void Capture(string file) => CaptureWindow(this, file);
    private void CaptureWindow(Avalonia.Controls.Window window, string file)
    {
        if (Path.GetFileName(file) != "failure.png") VerifyButtonTextCenters(window, Path.GetFileNameWithoutExtension(file));
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle) { Services.NativeCapture.Save(handle.Handle, file); return; }
        var size = new PixelSize((int)Math.Ceiling(window.ClientSize.Width * window.RenderScaling), (int)Math.Ceiling(window.ClientSize.Height * window.RenderScaling));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * window.RenderScaling, 96 * window.RenderScaling)); bitmap.Render(window); bitmap.Save(file, new PngBitmapEncoderOptions());
    }
}



// Only reached by --ui-smoke after enforcing an explicitly selected empty test database.
internal sealed class SmokeSyncHttp : HttpMessageHandler
{
    public bool Offline, Registered;
    public JsonObject Snapshot = new() { ["schema_version"]=1,["semesters"]=new JsonArray(),["desktop_todos"]=new JsonArray(),["course_calendars"]=new JsonArray() };
    public string? Revision;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("simulated offline");
        object response;
        if (request.RequestUri!.AbsolutePath.EndsWith("/login")) response = new { access_token="ui-smoke-token", user=new { id="ui-smoke-account",name="界面验证" } };
        else if (request.RequestUri.AbsolutePath.EndsWith("/register")) { Registered = true; response = new { ok = true }; }
        else if (request.Method == HttpMethod.Get) response = new { revision=Revision,snapshot=Snapshot };
        else if (request.Method == HttpMethod.Put)
        {
            var input=JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            if (input["base_revision"]?.GetValue<string>() != Revision) return new HttpResponseMessage(HttpStatusCode.Conflict) { Content=new StringContent("{}") };
            Snapshot=input["snapshot"]!.AsObject().DeepClone().AsObject(); Revision=Guid.NewGuid().ToString("N"); response=new { revision=Revision };
        }
        else response=new { ok=true };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(response,DataJson.Compact),System.Text.Encoding.UTF8,"application/json") };
    }
}
