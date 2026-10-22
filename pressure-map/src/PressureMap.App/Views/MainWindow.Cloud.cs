using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer syncTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CloudSyncEngine? syncEngine;
    private string syncMessage = "本地数据 · 无需登录";
    private TextBlock? syncStatus;
    private bool syncUiBusy;
    private Button? syncNowButton, conflictButton;
    private void UpdateCloudControls() { if (syncNowButton is not null) syncNowButton.IsEnabled = cloud.User is not null && !syncUiBusy; if (conflictButton is not null) conflictButton.IsEnabled = syncEngine?.Conflict is not null && !syncUiBusy; }
    private bool HasPendingTodoDraft => desktopTodo?.HasDraft == true || (todayTasks is { HasDraft:true } && TopLevel.GetTopLevel(todayTasks) is not null) || (todoPreview is { HasDraft:true } && TopLevel.GetTopLevel(todoPreview) is not null);
    private bool CanApplyCloudUpdate() => !dirty && !HasPendingTodoDraft &&
        !this.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsFocused) &&
        !(Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.Windows.Any(w => (w.Owner == this || w.Owner is TodoWindow) && w.IsVisible));
    private void SetSyncStatus(string message) { syncMessage = message; UpdateAccountChip(); if (syncStatus is not null) syncStatus.Text = message; UpdateCloudControls(); }
    private void UpdateAccountChip()
    {
        var name = cloud.User?.Name?.Replace("演示同学", "同学").Trim();
        var local = string.IsNullOrEmpty(name);
        AccountName.Text = local ? Loc.T("本地模式") : name!; AccountSubtitle.Text = Loc.T(local ? "未登录 · 点击登录" : "账号与云同步");
        AccountAvatar.IsVisible = local;
        AccountInitialChip.IsVisible = !local;
        AccountInitial.Text = local ? Loc.T("本") : char.ConvertFromUtf32(char.ConvertToUtf32(name!, 0)).ToUpperInvariant();
        // 侧栏只留一行名字，同步状态移到悬停提示里，避免信息丢失。
        var tip = Loc.T(local ? "点击登录，开启跨设备同步" : "点击进入账号与云同步设置");
        if (!local && !string.IsNullOrEmpty(syncMessage)) tip += "\n" + Loc.T(syncMessage);
        ToolTip.SetTip(AccountButton, tip);
    }
    private void RefreshActiveStoreViews()
    {
        semester = null; aiDraft = ""; reached = 1; dirty = false;
        RefreshSemesters(); desktopTodo?.SetStore(Store); desktopSchedule?.SetStore(Store); desktopPressure?.SetStore(Store);
        todoPreview?.SetStore(Store); schedulePreview?.SetStore(Store); todayTasks?.SetStore(Store); todayCourses?.SetStore(Store); settingsPressure?.SetStore(Store);
        reminderWindow?.Close(); Navigate(currentPage);
    }
    private async Task SyncNowAsync(bool manual)
    {
        var engine = syncEngine;
        if (syncUiBusy || engine is null || engine.IsBusy) return;
        if (cloud.User is null) { SetSyncStatus("会话已过期 · 本地缓存保留"); return; }
        if (!Save()) return;
        syncUiBusy = true; SetSyncStatus("正在同步…");
        try
        {
            var outcome = await engine.SyncAsync();
            if (outcome == SyncOutcome.Downloaded) RefreshActiveStoreViews();
            SetSyncStatus(outcome == SyncOutcome.Conflict ? "两端都有修改 · 请处理冲突" : Loc.T("已同步 · ") + DateTime.Now.ToString("HH:mm"));
            if (manual && outcome == SyncOutcome.Conflict) await ResolveConflictAsync();
        }
        catch (Exception e) { SetSyncStatus(cloud.User is null ? "会话已过期 · 本地缓存保留" : Loc.T("同步待重试 · ") + e.Message); if (manual) Error(e); }
        finally { syncUiBusy = false; UpdateCloudControls(); }
    }
    private async Task ResolveConflictAsync()
    {
        var engine = syncEngine; var conflict = engine?.Conflict;
        if (conflict is null) { Notice("当前没有待处理的同步冲突。"); return; }
        if (!Save()) return;
        var local = SnapshotData.Parse(Store.Snapshot()); var remote = SnapshotData.Parse(conflict.RemoteSnapshot);
        var separator = Loc.IsEnglish ? ", " : "、";
        var choice = await Dialogs.Choose(this,"处理同步冲突",Loc.F("这台设备：{0}\n云端：{1}\n\n本机待办：{2}\n云端待办：{3}\n\n选择一份作为当前数据，替换前会备份。取消将保留双方数据并暂停同步。",
            local.Summary, remote.Summary, string.Join(separator,local.DesktopTodos.Take(4).Select(t => t.Title)), string.Join(separator,remote.DesktopTodos.Take(4).Select(t => t.Title))), "使用本机版本", "使用云端版本");
        if (choice is null) return;
        try
        {
            var result = await engine!.ResolveAsync(choice == 0);
            if (result == SyncOutcome.Downloaded) RefreshActiveStoreViews();
            SetSyncStatus(Loc.T("冲突已处理 · ") + DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception e) { SetSyncStatus("同步待重试"); Error(e); }
    }
    private Control CloudSettingsCard()
    {
        syncStatus = Ui.Text(syncMessage, "caption");
        syncStatus.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        syncStatus.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        syncNowButton = conflictButton = null;
        var name = string.IsNullOrWhiteSpace(cloud.User?.Name) ? (cloud.User is null ? Loc.T("本地模式") : Loc.T("账号")) : cloud.User!.Name.Replace("演示同学", "同学");
        var initial = cloud.User is null || string.IsNullOrWhiteSpace(name) ? Loc.T("本") : char.ConvertFromUtf32(char.ConvertToUtf32(name, 0)).ToUpperInvariant();
        var accountAction = cloud.User is null ? Ui.AsyncButton("登录", ShowLoginAsync, "pill") : Ui.Button("退出登录", () => _ = ShowLoginAsync(), "pill");
        accountAction.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var identity = Ui.Stack(Ui.Text(name), Ui.Text(cloud.User is null ? "未登录 · 数据只保存在这台设备。" : "已登录 · 数据在云端与这台设备之间同步", "caption"));
        identity.Spacing = 2; identity.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        var header = Ui.Columns("*,Auto", Ui.Text("账号与云同步", "h2"), syncStatus);
        var accountRow = Ui.Columns("44,16,*,Auto", cloud.User is null ? Ui.AvatarImage(44) : Ui.Avatar(initial, 44), new Border(), identity, accountAction);
        var body = Ui.Stack(header, accountRow);
        if (cloud.User is not null)
        {
            syncNowButton = Ui.AsyncButton("立即同步", () => SyncNowAsync(true), "accent");
            conflictButton = Ui.AsyncButton("处理冲突", ResolveConflictAsync, "pill"); UpdateCloudControls();
            var migrate = Ui.AsyncButton("将本机数据复制到当前账号", CopyLocalToAccountAsync, "pill");
            migrate.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            var actions = Ui.Row(syncNowButton, conflictButton, migrate); actions.Spacing = 8;
            body.Children.Add(actions);
        }
        return Ui.Card(body);
    }
    private async Task CopyLocalToAccountAsync()
    {
        if (Store == LocalStore || cloud.User is null || syncEngine is null) { Error("请先登录账号。"); return; }
        if (syncUiBusy || syncEngine.IsBusy) { Notice("正在同步，请稍后重试。"); return; }
        var local = LocalStore.Snapshot(); var preview = SnapshotData.Parse(local); var expected = SnapshotData.Hash(Store.Snapshot());
        if (!SnapshotData.Parse(Store.Snapshot()).IsEmpty) { Error("账户缓存已有数据，请先备份并处理，不能直接覆盖。"); return; }
        if (!await Dialogs.Confirm(this,"复制本机数据",preview.Summary + "\n复制到当前账号缓存后同步，不改动游客数据。")) return;
        try { Store.ApplySnapshot(local,expected); RefreshActiveStoreViews(); await SyncNowAsync(true); }
        catch (Exception e) { Error(e); }
    }
}
