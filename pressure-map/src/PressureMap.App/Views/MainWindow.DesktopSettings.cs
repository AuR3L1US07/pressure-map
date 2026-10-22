using Avalonia.Controls;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    private Control DesktopSettingsCard()
    {
        var panel = Ui.Stack(Ui.Text("桌面组件","h2"));
        panel.Spacing = 16;
        void Widget(string title, bool enabled, Action show, Action hide, bool through, Action<bool> setThrough, Func<TodoWindow?> window)
        {
            var visible = Ui.Switch(title, enabled, showLabel: false);
            var updating = false; TodoWindow? observed = null;
            EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs> changed = (_,e) => {
                if (e.Property != Window.IsVisibleProperty || observed is null) return;
                updating=true; visible.IsChecked=observed.IsVisible; updating=false;
            };
            void Observe()
            {
                var current=window(); if (current == observed) return;
                if (observed is not null) observed.PropertyChanged-=changed;
                observed=current; if (observed is not null) observed.PropertyChanged+=changed;
            }
            void ShowWidget() { show(); Observe(); updating=true; visible.IsChecked=window()?.IsVisible == true; updating=false; }
            visible.IsCheckedChanged += (_,_) => { if (updating) return; if (visible.IsChecked == true) ShowWidget(); else hide(); };
            Observe(); panel.DetachedFromVisualTree += (_,_) => { if (observed is not null) observed.PropertyChanged-=changed; };
            var clickThrough = new CheckBox { Content = Loc.T("鼠标穿透"), IsChecked = through };
            clickThrough.IsCheckedChanged += (_,_) => { setThrough(clickThrough.IsChecked == true); SavePreferences(); };
            panel.Children.Add(Ui.Stack(8, Ui.Columns("*,Auto", Ui.Text(title), visible),
                Ui.Columns("*,Auto", Ui.Row(Ui.Button("显示",ShowWidget,"pill"), Ui.Button("锁定 / 解锁",() => window()?.ToggleLock(),"pill"),Ui.Button("收起 / 展开",() => window()?.ToggleCollapse(),"pill")), clickThrough)));
        }
        Widget("桌面待办",Preferences.DesktopEnabled,ShowDesktop,HideDesktop,Preferences.TodoClickThrough,v => { Preferences.TodoClickThrough=v; desktopTodo?.SetClickThrough(v); },() => desktopTodo);
        Widget("今日课程",Preferences.ScheduleEnabled,ShowScheduleDesktop,HideScheduleDesktop,Preferences.ScheduleClickThrough,v => { Preferences.ScheduleClickThrough=v; desktopSchedule?.SetClickThrough(v); },() => desktopSchedule);
        Widget("每日压力",Preferences.PressureEnabled,ShowPressureDesktop,HidePressureDesktop,Preferences.PressureClickThrough,v => { Preferences.PressureClickThrough=v; desktopPressure?.SetClickThrough(v); },() => desktopPressure);
        var snap = new CheckBox { Content = Loc.T("拖动后吸附到屏幕边缘"), IsChecked = Preferences.DesktopSnap };
        snap.IsCheckedChanged += (_,_) => { Preferences.DesktopSnap = snap.IsChecked == true; SavePreferences(); };
        panel.Children.Add(snap); panel.Children.Add(Ui.Text("开启鼠标穿透后，可在此关闭穿透或从托盘打开主窗口。","caption"));
        var restoreAll = Ui.Button("恢复全部组件交互",() => {
            Preferences.TodoClickThrough = Preferences.ScheduleClickThrough = Preferences.PressureClickThrough = false;
            desktopTodo?.SetClickThrough(false); desktopSchedule?.SetClickThrough(false); desktopPressure?.SetClickThrough(false); SavePreferences(); SelectPage(3);
        },"pill");
        restoreAll.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        panel.Children.Add(restoreAll);
        return Ui.Card(panel);
    }
}
