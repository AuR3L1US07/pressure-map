using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public sealed record TodoProfile(string Name, int Count);
public partial class MainWindow
{
    private ListBox? profiles;
    private TextBlock? desktopStatus;
    private ToggleSwitch? desktopToggle;
    private bool updatingDesktopToggle;
    private readonly string[] profileNames = ["待完成", "重要", "今天及逾期", "已完成", "全部"];
    private Control TodosPage()
    {
        var left = new StackPanel { Spacing = 12 };
        var search = Ui.Input("", "搜索清单"); left.Children.Add(search);
        profiles = new ListBox { Classes = { "soft" }, Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        profiles.ItemTemplate = new FuncDataTemplate<TodoProfile>((item, _) =>
        {
            if (item is null) return new Border();
            var index = Array.IndexOf(profileNames, item.Name);
            var icon = Ui.Icon(index == 1 ? "Star" : index == 2 ? "Calendar" : index == 3 ? "Check" : "Todo");
            var count = Ui.Text(item.Count.ToString(), "caption");
            var badge = new Border { Child = count, CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), Background = item.Count > 0 ? Ui.Brush("AccentSoftBrush") : Avalonia.Media.Brushes.Transparent };
            return Ui.Columns("20,8,*,Auto", icon, new Border(), Ui.Text(item.Name), badge);
        });
        left.Children.Add(profiles);
        todoPreview = new TodoControl(Store, embedded: true) { MinHeight = 360 };
        todoPreview.Changed += RefreshTodoViews; todoPreview.SettingsRequested += () => SelectPage(3); todoPreview.ImportRequested += async () => await ImportTodosAsync();
        var listTitle = Ui.Text("待完成", "h2");
        search.TextChanged += (_, _) =>
        {
            var query = search.Text ?? ""; var all = Counts();
            profiles.ItemsSource = profileNames.Select((name, index) => new TodoProfile(name, all[index])).Where(x => Loc.T(x.Name).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        };
        profiles.SelectionChanged += (_, _) => { if (profiles.SelectedItem is TodoProfile item && todoPreview is not null) { todoPreview.Filter = Array.IndexOf(profileNames, item.Name); listTitle.Text = Loc.T(item.Name); } };
        RefreshProfileCounts(); profiles.SelectedIndex = 0;
        desktopToggle = Ui.Switch("接入桌面", Preferences.DesktopEnabled);
        desktopToggle.IsCheckedChanged += (_, _) => { if (updatingDesktopToggle) return; if (desktopToggle.IsChecked == true) ShowDesktop(); else HideDesktop(); };
        desktopStatus = Ui.Text(desktopTodo?.IsVisible == true ? "已接入 Explorer 桌面层" : "尚未显示桌面待办", "caption");
        var desktop = Ui.IconButton("Window", "桌面显示", () => { });
        desktop.Flyout = new Flyout { Content = Ui.Stack(desktopToggle, desktopStatus,
            Ui.Button("桌面模式", DesktopMode, "tonal"),
            Ui.Button("锁定 / 解锁位置", () => { ShowDesktop(); desktopTodo?.ToggleLock(); }, "tonal"),
            Ui.Button("收起 / 展开", () => { ShowDesktop(); desktopTodo?.ToggleCollapse(); }, "tonal"),
            Ui.Button("隐藏待办", HideDesktop, "ghost")) };
        var more = Ui.IconButton("Info", "清单设置", () => { });
        more.Flyout = new Flyout { Content = Ui.Stack(Ui.AsyncButton("导入当前学期任务", ImportTodosAsync, "tonal"), Ui.Button("设置", () => SelectPage(3), "ghost")) };
        var header = Ui.Columns("*,Auto,8,Auto,8,Auto", listTitle, Ui.AsyncButton("新增待办", () => todoPreview.CreateTaskAsync(), "accent"), new Border(), desktop, new Border(), more);
        var center = new Grid { RowDefinitions = new RowDefinitions("Auto,16,*") }; center.Children.Add(header); Grid.SetRow(todoPreview, 2); center.Children.Add(todoPreview);
        return new PageFrame(Ui.Text("待办任务", "h1"), Ui.Columns("260,16,*", Ui.Card(left), new Border(), Ui.Card(center)), false);
    }
    private int[] Counts()
    {
        var todos = Store.ListTodos(); var today = DateTime.Today.ToString("yyyy-MM-dd");
        return [todos.Count(t => !t.IsFinished), todos.Count(t => !t.IsFinished && t.Important), todos.Count(t => TaskWorkload.IsToday(t, DateTime.Now)), todos.Count(t => t.IsFinished), todos.Count];
    }
    private void RefreshProfileCounts()
    {
        if (profiles is null) return;
        var selected = (profiles.SelectedItem as TodoProfile)?.Name ?? profileNames[0];
        var counts = Counts(); profiles.ItemsSource = profileNames.Select((name, index) => new TodoProfile(name, counts[index])).ToList();
        profiles.SelectedIndex = Array.IndexOf(profileNames, selected);
    }
    private void UpdateDesktopToggle(bool enabled)
    {
        updatingDesktopToggle = true; if (desktopToggle is not null) desktopToggle.IsChecked = enabled; updatingDesktopToggle = false;
        if (!enabled && desktopStatus is not null) desktopStatus.Text = "桌面待办已隐藏";
    }
    private async Task ImportTodosAsync()
    {
        if (semester is null) { Error("请先在学期规划中打开一个学期。"); return; }
        if (!Save()) return;
        if (!await Dialogs.Confirm(this, "导入学期任务", "复制当前学期任务为待办。已有来源会跳过，之后两边独立编辑。具体截止日按截止周周日计算。")) return;
        try { var count = Store.ImportTodos(semester); RefreshTodoViews(); if (desktopStatus is not null) desktopStatus.Text = Loc.F("已导入 {0} 条待办。", count); }
        catch (Exception error) { Error(error); }
    }
}
