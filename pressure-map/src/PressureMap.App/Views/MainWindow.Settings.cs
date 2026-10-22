using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using PressureMap.Core;
using PressureMap.Localization;
using PressureMap.Services;
using Avalonia.Platform.Storage;

namespace PressureMap.Views;

public partial class MainWindow
{
    private Control GeneralPage()
    {
        var language = new ComboBox { ItemsSource = new[] { "中文", "English" }, SelectedIndex = Loc.IsEnglish ? 1 : 0, Width = 160, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        language.SelectionChanged += (_, _) => SetLanguage(language.SelectedIndex == 1 ? Loc.English : Loc.Chinese);
        var onlineScan = Ui.Switch("启动时自动扫描网课", Preferences.ScanOnlineCoursesOnStartup, showLabel: false);
        onlineScan.IsCheckedChanged += (_, _) => { Preferences.ScanOnlineCoursesOnStartup = onlineScan.IsChecked == true; SavePreferences(); };
        var reminders = Ui.Switch("启用任务截止提醒", Preferences.TaskRemindersEnabled, showLabel: false);
        reminders.IsCheckedChanged += (_, _) => { Preferences.TaskRemindersEnabled = reminders.IsChecked == true; if (!Preferences.TaskRemindersEnabled) reminderWindow?.Close(); SavePreferences(); };
        var capacity = new NumericUpDown { Minimum = 1, Maximum = 16, Increment = .5m, Value = (decimal)Preferences.DailyCapacityHours, Width = 160, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        capacity.ValueChanged += (_, _) => { Preferences.DailyCapacityHours = (double)(capacity.Value ?? 6); SavePreferences(); _ = RefreshTodayOverviewAsync(); if (settingsPressure is not null) _ = settingsPressure.ReloadAsync(); desktopPressure?.Refresh(); };
        // 圆环预览已从设置页移除；实例保留供同步刷新与界面自检读取分数。
        settingsPressure = new PressureControl(Store, Preferences, compact: true);
        pressureToggleControl = Ui.Switch("在桌面显示压力圆环", Preferences.PressureEnabled, showLabel: false);
        pressureToggleControl.IsCheckedChanged += (_, _) => { if (updatingPressureToggle) return; if (pressureToggleControl.IsChecked == true) ShowPressureDesktop(); else HidePressureDesktop(); };
        var sync = Ui.Switch("开启云同步", cloud.User is not null, showLabel: false);
        var syncBusy = false;
        sync.IsCheckedChanged += async (_, _) =>
        {
            if (syncBusy) return;
            var wanted = sync.IsChecked == true;
            if (wanted == (cloud.User is not null)) return;
            syncBusy = true;
            try { await ShowLoginAsync(); }
            finally { sync.IsChecked = cloud.User is not null; syncBusy = false; }
        };
        return SectionPage("设置",
            SettingsCard("通用", Ui.SettingRow("界面语言", language, "切换语言后界面会立即刷新，已打开的桌面组件在下次打开时生效。")),
            SettingsCard("云同步", Ui.SettingRow("开启云同步", sync, "开启后在「数据与备份」中登录并管理账号。")),
            SettingsCard("网课助手", Ui.SettingRow("启动时自动扫描网课", onlineScan, "默认关闭。开启后，下次启动会读取已登录的学习通和 MOOC 课程；手动登录成功后始终自动读取。"), OnlineAccountSettings()),
            SettingsCard("每日压力", Ui.SettingRow("每日容量（等效小时）", CapacityStepper(capacity), "该数值用于计算每日压力。"), Ui.SettingRow("在桌面显示压力圆环", pressureToggleControl)),
            SettingsCard("任务提醒", Ui.SettingRow("启用任务截止提醒", reminders, "有待提醒任务时，关闭主窗口会留在系统托盘；从托盘退出后停止提醒。")),
            AiApiSettingsCard());
    }

    // 一张卡占满一行：标题 + 若干「标签左、控件右」的设置项。
    private static Control SettingsCard(string title, params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Ui.Text(title, "h2"));
        foreach (var row in rows) { if (panel.Children.Count > 1) panel.Children.Add(Ui.Divider()); panel.Children.Add(row); }
        return Ui.Card(panel);
    }

    private static Control CapacityStepper(NumericUpDown capacity)
    {
        var value = Ui.Text((capacity.Value ?? 6).ToString("0.#")); value.Width = 36; value.TextAlignment = Avalonia.Media.TextAlignment.Center;
        capacity.ValueChanged += (_, _) => value.Text = (capacity.Value ?? 6).ToString("0.#");
        var minus = Ui.IconButton("Minus", "减少每日容量", () => capacity.Value = Math.Max(capacity.Minimum, (capacity.Value ?? 6) - capacity.Increment)); minus.Content = "−";
        var plus = Ui.IconButton("Plus", "增加每日容量", () => capacity.Value = Math.Min(capacity.Maximum, (capacity.Value ?? 6) + capacity.Increment));
        return Ui.Row(minus, value, plus, Ui.Text("小时", "caption"));
    }
    private Control OnlineAccountSettings()
    {
        var platform = new ComboBox { ItemsSource = new[] { Loc.T("学习通"), Loc.T("中国大学 MOOC") }, SelectedIndex = 0, MinWidth = 160 };
        var clear = Ui.AsyncButton("清除账号与登录状态", async () =>
        {
            var selected = platform.SelectedIndex == 1 ? "mooc" : "chaoxing";
            var name = platform.SelectedIndex == 1 ? "中国大学 MOOC" : "学习通";
            if (!await Dialogs.Confirm(this, "清除平台账号", $"清除{name}的账号、登录状态及本机课程读取记录？")) return;
            try
            {
                var service = EnsureOnlineCourses();
                onlineStartupCancelled = true; await service.StopAndWaitAsync();
                service.Forget(selected); onlinePage = null;
                Notice($"已清除{name}的账号与登录状态。");
            }
            catch (Exception error) { Error(error); }
        });
        // 平台选择与清除按钮排在同一行右侧，省掉一行空白。
        platform.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        clear.Classes.Add("danger");
        clear.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        clear.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        var platformRow = Ui.Actions(platform, clear); platformRow.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        return Ui.SettingRow("平台账号", platformRow);
    }
    private Control ShortcutsPage()
    {
        var rows = Ui.Stack();
        foreach (var (key, description) in new[] { ("Ctrl+N", "新建学期"), ("Ctrl+S", "保存修改"), ("Ctrl+Shift+E", "导出数据"), ("Alt+1～Alt+4", "切换学期规划步骤"), ("Enter", "在待办输入框中新增事项"), ("F2 / 双击", "编辑所选学期任务") })
            {
                var caps = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                foreach (var part in key.Split('+')) { if (caps.Children.Count > 0) caps.Children.Add(Ui.Text("+", "caption")); var text = Ui.Text(part); text.FontSize = 12; text.FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas"); caps.Children.Add(new Border { Child = text, Classes = { "kbd" } }); }
                var row = Ui.Columns("220,*", caps, Ui.Text(description)); row.MinHeight = 44; rows.Children.Add(row); rows.Children.Add(Ui.Divider());
            }
        return Page("快捷键", Ui.Card(rows));
    }
    private Control AppearancePage()
    {
        var theme = new ComboBox { ItemsSource = new[] { Loc.T("浅色"), Loc.T("深色") }, SelectedIndex = Preferences.Theme == "Dark" ? 1 : 0 };
        theme.SelectionChanged += (_, _) => { Preferences.Theme = theme.SelectedIndex == 1 ? "Dark" : "Light"; ApplyTheme(); SavePreferences(); };
        var themeChoices = Ui.Actions();
        for (var index = 0; index < 2; index++)
        {
            var own = index; var button = Ui.Button(index == 0 ? "浅色" : "深色", () => theme.SelectedIndex = own, "ghost");
            var previewTheme = index == 0 ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
            Avalonia.Media.IBrush PreviewBrush(string key) => (Avalonia.Media.IBrush)Application.Current!.FindResource(previewTheme, key)!;
            var sample = new Border { Width = 120, Height = 80, CornerRadius = new Avalonia.CornerRadius(8), Background = PreviewBrush("PageBrush"), BorderBrush = PreviewBrush("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(1), Padding = new Avalonia.Thickness(10) };
            sample.Child = Ui.Columns("22,6,*", new Border { Background = PreviewBrush("AccentSoftBrush"), CornerRadius = new Avalonia.CornerRadius(4) }, new Border(), Ui.Stack(6, new Border { Height = 12, Background = PreviewBrush("AccentBrush"), CornerRadius = new Avalonia.CornerRadius(3) }, new Border { Height = 25, Background = PreviewBrush("SurfaceAltBrush"), CornerRadius = new Avalonia.CornerRadius(4) }));
            var label = Ui.Text(index == 0 ? "浅色" : "深色");
            label.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 8);
            button.Content = Ui.Stack(4, sample, label);
            button.CornerRadius = new CornerRadius(12);
            button.Padding = new Thickness(6);
            button.BorderThickness = new Thickness(2);
            button.BorderBrush = Ui.Brush(theme.SelectedIndex == own ? "AccentBrush" : "SubtleBorderBrush");
            button.Margin = new Thickness(0, 0, 12, 0);
            Avalonia.Automation.AutomationProperties.SetName(button, Loc.T(index == 0 ? "浅色" : "深色"));
            theme.SelectionChanged += (_, _) => button.BorderBrush = Ui.Brush(theme.SelectedIndex == own ? "AccentBrush" : "SubtleBorderBrush");
            themeChoices.Children.Add(button);
        }
        var motion = Ui.Switch("减少动画", Preferences.ReduceMotion, showLabel: false);
        motion.IsCheckedChanged += (_, _) => { Preferences.ReduceMotion = motion.IsChecked == true; ApplyTheme(); SavePreferences(); };
        var glass = Ui.Switch("桌面待办使用玻璃背景", Preferences.DesktopGlass, showLabel: false);
        glass.IsCheckedChanged += (_, _) => { Preferences.DesktopGlass = glass.IsChecked == true; desktopTodo?.ApplyMaterial(); desktopSchedule?.ApplyMaterial(); desktopPressure?.ApplyMaterial(); SavePreferences(); };
        var amount = Ui.Text(Loc.F("{0:P0}", Preferences.DesktopGlassTint), "caption");
        amount.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var tint = new Slider { Minimum = 35, Maximum = 85, Value = Preferences.DesktopGlassTint * 100, TickFrequency = 5, Width = 220, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        Avalonia.Automation.AutomationProperties.SetName(tint, "桌面玻璃底色浓度");
        tint.ValueChanged += (_, _) => { Preferences.DesktopGlassTint = tint.Value / 100; amount.Text = Loc.F("{0:P0}", Preferences.DesktopGlassTint); desktopTodo?.ApplyMaterial(); desktopSchedule?.ApplyMaterial(); desktopPressure?.ApplyMaterial(); SavePreferences(); };
        var preview = new DesktopComponentPreview(); preview.Update(Preferences.DesktopGlass, Preferences.DesktopGlassTint);
        var tintRow = Ui.SettingRow("底色浓度", Ui.Row(tint, amount), "浓度越高，文字越容易辨认。");
        glass.IsCheckedChanged += (_, _) => { preview.Update(Preferences.DesktopGlass, Preferences.DesktopGlassTint); tintRow.Opacity = glass.IsChecked == true ? 1 : .5; tint.IsEnabled = glass.IsChecked == true; };
        tint.ValueChanged += (_, _) => preview.Update(Preferences.DesktopGlass, Preferences.DesktopGlassTint);
        tintRow.Opacity = glass.IsChecked == true ? 1 : .5; tint.IsEnabled = glass.IsChecked == true;
        amount.Width = 48;
        return SectionPage("外观", Ui.Card(Ui.Stack(14,
            Ui.SettingRow("主题", themeChoices, "选择界面配色"),
            Ui.SettingRow("减少动画", motion, "关闭界面过渡与动画效果"))), DesktopSettingsCard(), SettingsCard("桌面组件样式", preview, Ui.SettingRow("桌面待办使用玻璃背景", glass), tintRow));
    }
    private Control DataPage()
    {
        var backups = new StackPanel { Spacing = 4 };
        void Reload()
        {
            backups.Children.Clear();
            foreach (var path in Store.ListBackups())
            {
                var item = BackupPresentation.FromPath(path);
                var title = Ui.Text(item.Title); title.FontWeight = Avalonia.Media.FontWeight.Medium; title.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis; title.MaxLines = 1; ToolTip.SetTip(title, Path.GetFileName(path));
                var detail = Ui.Text(item.Detail.Length > 0 ? item.Detail : Path.GetFileName(path), "caption"); detail.MaxLines = 1; detail.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
                var labels = Ui.Stack(2, Ui.Row(title, Ui.Text(item.Kind, "caption")), detail);
                var restore = Ui.AsyncButton("预览并恢复", () => RestoreBackupAsync(item.Path), "ghost");
                var row = Ui.Columns("20,12,*,12,Auto", Ui.Icon("Database"), new Border(), labels, new Border(), restore); row.MinHeight = 56;
                backups.Children.Add(row); backups.Children.Add(Ui.Divider());
            }
            if (backups.Children.Count == 0) backups.Children.Add(Ui.EmptyState("Database", "暂无备份", "立即备份后，可在这里预览并恢复。"));
        }
        Reload();
        var list = Ui.Scroll(backups); list.MaxHeight = 280;
        var undo = Ui.Button("撤销最近删除的待办", () => { try { if (!Save()) return; Store.UndoDeletion(); RefreshActiveStoreViews(); Notice("已撤销删除。"); } catch (Exception e) { Error(e); } }, "ghost"); undo.IsEnabled = Store.CanUndoDeletion;
        return SectionPage("数据与备份", CloudSettingsCard(), SettingsCard("本地备份与恢复",
            Ui.Row(Ui.Icon("Clock"), Ui.Text("每天自动备份，保留最近 14 份。", "caption")),
            Ui.Row(Ui.Icon("Check"), Ui.Text("删除和恢复前另存当前数据。", "caption")),
            Ui.Row(Ui.Icon("Database"), Ui.Text("备份按本机或当前账号隔离。", "caption")),
            Ui.Actions(Ui.Button("立即备份", () => { try { if (!Save()) return; Store.Backup(); Reload(); Notice("备份已保存。"); } catch (Exception e) { Error(e); } }, "accent"),
                Ui.AsyncButton("导出 JSON…", ExportAsync, "outline"), Ui.AsyncButton("从文件恢复…", PickRestoreAsync, "outline")),
            list, Ui.Actions(undo, Ui.Button("打开数据目录", () => { try { Process.Start(new ProcessStartInfo(Store.DirectoryPath) { UseShellExecute = true }); } catch (Exception e) { Error(e); } }, "ghost"))), CacheSettingsCard());
    }
    // 缓存 = 网课课程记录、网课账号与登录状态、临时文件和日志；学期、课表、待办和备份不在清除范围内。
    private Control CacheSettingsCard()
    {
        var amount = Ui.Text("", "caption");
        amount.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var detail = Ui.Text("", "caption");
        detail.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        void Reload()
        {
            var groups = LocalCache.Inspect(Store.DirectoryPath);
            amount.Text = groups.Count == 0 ? Loc.T("已清空") : LocalCache.Size(groups.Sum(group => group.Bytes));
            detail.Text = groups.Count == 0 ? Loc.T("暂无可清除的缓存文件。")
                : string.Join(" · ", groups.Select(group => Loc.T(group.Label) + " " + LocalCache.Size(group.Bytes)));
        }
        Reload();
        var clear = Ui.AsyncButton("清除本地缓存", async () =>
        {
            var groups = LocalCache.Inspect(Store.DirectoryPath);
            if (groups.Count == 0) { Notice("本地缓存已经是空的。"); return; }
            var bytes = groups.Sum(group => group.Bytes);
            if (!await Dialogs.Confirm(this, "清除本地缓存", Loc.F("将删除网课课程记录、已保存的网课账号与登录状态、临时文件和应用日志（{0}）。\n学期数据、课表、备份不受影响。", LocalCache.Size(bytes)))) return;
            try
            {
                var service = onlineCourses;
                if (service is { Busy: true }) { onlineStartupCancelled = true; await service.StopAndWaitAsync(); }
                // 登录会话同时留在内存里，只删文件不够，当前进程也要一起丢掉。
                if (service is { Busy: false } live) foreach (var platform in new[] { "chaoxing", "mooc" }) live.Forget(platform);
                var result = await Task.Run(() => LocalCache.Clear(Store.DirectoryPath));
                onlinePage = null; Reload();
                Notice(result.Skipped == 0 ? Loc.F("已清除本地缓存，释放 {0}。", LocalCache.Size(result.Freed))
                    : Loc.F("已释放 {0}，另有 {1} 个文件正被占用，请稍后再试。", LocalCache.Size(result.Freed), result.Skipped));
            }
            catch (Exception error) { Error(error); }
        });
        clear.Classes.Add("danger");
        return SettingsCard("本地缓存",
            Ui.SettingRow("缓存占用", amount, "网课课程记录、账号与登录状态、临时文件和应用日志。清除后需重新登录网课助手。"),
            detail,
            Ui.Actions(clear, Ui.Button("刷新", Reload, "ghost")));
    }
    private async Task PickRestoreAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.T("选择 JSON 备份"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType(Loc.T("JSON 备份")) { Patterns = ["*.json"] }] });
        if (files.Count > 0) await RestoreBackupAsync(files[0].Path.LocalPath);
    }
    private async Task RestoreBackupAsync(string path)
    {
        if (syncUiBusy || syncEngine?.IsBusy == true) { Notice("正在同步，请完成后恢复。"); return; }
        if (!Save()) return;
        syncUiBusy = true;
        try
        {
            var data = Store; var expected = SnapshotData.Hash(data.Snapshot());
            var json = await Task.Run(() => Store.ReadBackup(path)); var preview = await Task.Run(() => SnapshotData.Parse(json));
            var target = data == LocalStore ? Loc.T("本机") : Loc.T("账号缓存");
            var followUp = data == LocalStore ? "" : Loc.T("之后将参与云同步。");
            if (!await Dialogs.Confirm(this,"预览恢复备份",Loc.F("文件：{0}\n备份内容：{1}\n当前内容：{2}\n\n确认后替换当前{3}数据，并自动备份当前内容。{4}",
                Path.GetFileName(path), preview.Summary, SnapshotData.Parse(data.Snapshot()).Summary, target, followUp))) return;
            if (data != Store || dirty || !CanApplyCloudUpdate()) throw new InvalidOperationException("当前数据已变化，请重新预览。");
            data.ApplySnapshot(json,expected); RefreshActiveStoreViews(); Notice("备份已恢复，原数据已另存备份。");
        }
        catch (Exception e) { Error(e); }
        finally { syncUiBusy = false; }
    }
    private Control AboutPage()
    {
        var brand = Ui.Stack(Ui.Text("学期地图", "h1"), Ui.Text("规划学期，安排每一天。", "caption"), Ui.Text("v2.0.0 · C# · Avalonia · SQLite", "caption"));
        var logo = Ui.AvatarImage(56);
        const string githubUrl = "https://github.com/AuR3L1US07/";
        var github = Ui.Button("GitHub · AuR3L1US07", () =>
        {
            try { Process.Start(new ProcessStartInfo(githubUrl) { UseShellExecute = true }); }
            catch (Exception e) { Error(e); }
        }, "ghost");
        var githubIcon = new Avalonia.Controls.Shapes.Path
        {
            Data = (Avalonia.Media.Geometry)Avalonia.Application.Current!.FindResource("Icon.GitHub")!,
            Width = 24, Height = 24, Stretch = Avalonia.Media.Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        githubIcon.Bind(Avalonia.Controls.Shapes.Path.FillProperty, github.GetObservable(Button.ForegroundProperty));
        github.Content = Ui.Row(githubIcon, Ui.Text("GitHub · AuR3L1US07"));
        ToolTip.SetTip(github, githubUrl);
        var credits = SettingsCard("致谢与许可", github);
        return SectionPage("关于", Ui.Card(Ui.Columns("56,16,*", logo, new Border(), brand)), credits);
    }
    private async Task ExportAsync()
    {
        if (!Save()) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Loc.T("导出数据"), SuggestedFileName = Loc.T("学期地图-") + DateTime.Today.ToString("yyyyMMdd") + ".json", DefaultExtension = "json" });
            if (file is not null) { Store.Export(file.Path.LocalPath); Notice("数据已导出到：" + file.Name); }
        }
        catch (Exception exception) { Error(exception); }
    }
}

