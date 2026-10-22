using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    private Control SettingsNavigation(int selected)
    {
        var tabs = Ui.Actions();
        foreach (var (page, title, icon) in new[] {
            (3, "通用", "Settings"), (5, "外观", "Palette"), (6, "数据与备份", "Database"),
            (4, "快捷键", "Keyboard"), (7, "关于", "Info") })
        {
            var own = page;
            var button = Ui.Button(title, () => SelectPage(own), selected == own ? "tonal" : "ghost");
            button.Content = Ui.Row(Ui.Icon(icon), Ui.Text(title));
            button.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(button, Loc.T(title));
            tabs.Children.Add(button);
        }
        return tabs;
    }
}
