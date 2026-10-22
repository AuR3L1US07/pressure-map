using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using PressureMap.Core;

namespace PressureMap.Views;

public partial class MainWindow
{
    private async Task VerifyDesktopTaskStylingAsync(string output)
    {
        ShowDesktop();
        var widget = desktopTodo ?? throw new InvalidOperationException("Desktop todo window is missing.");
        var control = widget.GetVisualDescendants().OfType<TodoControl>().Single();
        control.Refresh(); await Task.Delay(220);
        CaptureWindow(widget, Path.Combine(output, "desktop-todo.png"));
        VerifyButtonTextCenters(widget, "desktop-todo");
        // 桌面待办只做展示：任何输入框、筛选框、行内操作菜单都属于「管理」，应该只在主界面出现。
        if (control.GetVisualDescendants().OfType<TextBox>().Any())
            throw new InvalidOperationException("桌面待办不应出现输入框：新增待办改为在应用内完成。");
        if (control.GetVisualDescendants().OfType<ComboBox>().Any())
            throw new InvalidOperationException("桌面待办不应出现筛选下拉：桌面只显示待完成的事。");
        if (control.GetVisualDescendants().OfType<MenuItem>().Any())
            throw new InvalidOperationException("桌面待办不应出现增删菜单。");
        var list = control.GetVisualDescendants().OfType<ListBox>().Single();
        var rows = list.ItemsSource?.Cast<Todo>().ToList() ?? [];
        if (rows.Any(t => t.IsFinished)) throw new InvalidOperationException("桌面待办混入了已完成的任务。");
        if (rows.Count == 0) throw new InvalidOperationException("桌面待办没有可展示的待完成任务。");
        var checks = control.GetVisualDescendants().OfType<CheckBox>().ToArray();
        if (checks.Length != rows.Count)
            throw new InvalidOperationException($"桌面待办渲染了 {checks.Length} 个勾选框，但列表有 {rows.Count} 条任务：子任务不应在桌面展开。");
        if (checks.Any(c => c.Background is ISolidColorBrush { Color.A: > 0 }))
            throw new InvalidOperationException("A desktop task checkbox still paints a background behind its label.");
        // 桌面组件不写任何管理文案；连「在应用内管理」这类入口也去掉了，改走右键菜单与托盘。
        var texts = control.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).OfType<string>().ToArray();
        if (texts.Any(t => t.Contains("应用内"))) throw new InvalidOperationException("桌面待办仍显示「在应用内管理」之类的管理入口。");
        // 锁按钮只显示锁形图标：锁定/解锁用图标本身表达，不再写「锁定 / 解锁」文字。
        var lockButton = widget.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content is IconView);
        if (lockButton is null) throw new InvalidOperationException("桌面组件的锁定按钮没有使用锁形图标。");
        var lockKey = ((IconView)lockButton.Content!).Key;
        if (lockKey is not ("Lock" or "Unlock")) throw new InvalidOperationException($"锁定按钮的图标不是锁形：{lockKey}");
        widget.ToggleLock(); var toggledKey = ((IconView)lockButton.Content!).Key; widget.ToggleLock();
        if (toggledKey == lockKey) throw new InvalidOperationException("锁定状态切换后图标没有变化。");
        if (widget.GetVisualDescendants().OfType<Button>().Any(b => b.Content is "×" or "－"))
            throw new InvalidOperationException("桌面组件不应再有隐藏 / 关闭按钮。");
        var menu = (widget.Content as Border)?.ContextMenu;
        var items = menu?.ItemsSource?.Cast<MenuItem>().Select(i => i.Header as string).ToArray() ?? [];
        if (!items.Contains("打开主窗口") || !items.Contains("隐藏此组件"))
            throw new InvalidOperationException("桌面组件缺少右键菜单：打开主窗口 / 隐藏此组件。");
        foreach (var button in control.GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button) && b.Content is string && b.TemplatedParent is null))
            if (button.HorizontalContentAlignment != HorizontalAlignment.Center || button.VerticalContentAlignment != VerticalAlignment.Center)
                throw new InvalidOperationException($"Desktop button '{button.Content}' is not centered: {button.HorizontalContentAlignment}/{button.VerticalContentAlignment}.");
        var verify = new { rows = rows.Count, checkboxes = checks.Length, lock_icon = lockKey, menu = items };
        File.WriteAllText(Path.Combine(output, "desktop-todo.json"), System.Text.Json.JsonSerializer.Serialize(verify, DataJson.Options));
    }
}
