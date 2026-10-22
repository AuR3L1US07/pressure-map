using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PressureMap.Core;
using PressureMap.Services;
using PressureMap.Localization;

namespace PressureMap.Views;

public sealed class TodoWindow : Window, IDisposable
{
    private readonly Preferences preferences;
    private readonly string settingsDirectory;
    private readonly TodoControl? todos;
    private readonly ScheduleControl? courses;
    private readonly Control body;
    private readonly bool isSchedule;
    private readonly bool isPressure;
    private readonly PressureControl? pressure;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer preferenceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly TextBlock compact = Ui.Text("", "caption");
    private readonly Button lockButton;
    private readonly Border surface;
    private readonly Border sheen;
    private nint handle;
    private bool disposing, collapsed, nativeCorners;
    /// <summary>窗口圆角（DIP）。与系统 DWM 圆角一致，避免原生裁切和绘制圆角错位。</summary>
    private const double CornerDip = 20;
    public bool HasDraft => todos?.HasDraft == true;
    public bool NativeAvailable => handle == 0 || Services.DesktopNative.IsWindow(handle);
    public event Action? Changed, SettingsRequested, ManageRequested, HiddenByUser;
    public event Action<string>? StatusChanged;
    public event Action<string>? TaskRequested;
    public TodoWindow(Store store, Preferences preferences, string directory, bool schedule = false, bool pressurePanel = false)
    {
        this.preferences = preferences; settingsDirectory = directory; isSchedule = schedule; isPressure = pressurePanel;
        Title = Loc.T(pressurePanel ? "每日压力 · 学期地图" : schedule ? "今日课程 · 学期地图" : "桌面待办 · 学期地图"); ShowInTaskbar = false; ShowActivated = false; WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent]; Background = Brushes.Transparent;
        Classes.Add("desktop-glass");
        Width = Math.Clamp(WidgetWidth, pressurePanel ? 220 : 360, pressurePanel ? 280 : 900); Height = Math.Clamp(WidgetHeight, pressurePanel ? 236 : 360, pressurePanel ? 260 : 1000); MinWidth = pressurePanel ? 220 : 360; MinHeight = pressurePanel ? 236 : 88;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (WidgetX is { } x && WidgetY is { } y) Position = new PixelPoint(x, y);
        else if (Screens.Primary is { } screen) Position = new PixelPoint(screen.WorkingArea.Right - (int)(Width * screen.Scaling) - 24, screen.WorkingArea.Y + 80);
        if (pressurePanel && WidgetX is null && Screens.Primary is { } screenForPressure) Position = new PixelPoint(screenForPressure.WorkingArea.X + (int)((screenForPressure.WorkingArea.Width - Width * screenForPressure.Scaling) / 2), screenForPressure.WorkingArea.Y + 80);
        if (schedule && WidgetX is null && Screens.Primary is { } primary) Position = new PixelPoint(primary.WorkingArea.X + 24, primary.WorkingArea.Y + 80);
        if (pressurePanel) { pressure = new PressureControl(store, preferences, minimal: true); body = pressure; }
        else if (schedule)
        {
            courses = new ScheduleControl(store, true); body = courses;
            courses.SettingsRequested += () => SettingsRequested?.Invoke(); courses.TaskRequested += title => TaskRequested?.Invoke(title);
        }
        else
        {
            // 桌面只放「还需要做什么」：新增、编辑、删除、筛选、导入都回主界面做。
            todos = new TodoControl(store, minimal: true); body = todos;
            todos.Changed += () => { Refresh(); Changed?.Invoke(); };
            todos.ManageRequested += () => ManageRequested?.Invoke();
        }
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4, MinHeight = 32, Background = Brushes.Transparent };
        header.Children.Add(Ui.Row(Ui.Icon(pressurePanel ? "Clock" : schedule ? "Calendar" : "Todo"), Ui.Text(pressurePanel ? "每日压力" : schedule ? "今日课程" : "待办", "h2")));
        header.PointerPressed += (_, e) => { if (!WidgetLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        lockButton = Ui.Button("", ToggleLock, "icon");
        ApplyLockIcon();
        Grid.SetColumn(lockButton, 1); header.Children.Add(lockButton);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 8, Margin = new Thickness(14, 10, 12, 8) };
        grid.Children.Add(header); Grid.SetRow(compact, 1); grid.Children.Add(compact); Grid.SetRow(body, 2); grid.Children.Add(body);
        // 右下角留一块看不见的拖拽区调整大小；不再画「◢」，那种字符在小尺寸下会渲染成黑三角。
        var resize = new Border { Width = 16, Height = 16, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right };
        resize.PointerPressed += (_, e) => { if (!WidgetLocked && !collapsed) BeginResizeDrag(WindowEdge.SouthEast, e); };
        Grid.SetRow(resize, 3); grid.Children.Add(resize);
        var layers = new Grid();
        // 圆角完全交给系统窗口裁切（DWM 抗锯齿 / GDI 区域兜底），画布本身画成直角，
        // 这样原生裁切与绘制圆角不会互相错位，也不会露出底下一层。
        sheen = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(1) };
        layers.Children.Add(sheen); layers.Children.Add(grid);
        surface = new Border { Child = layers, CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0), ClipToBounds = true };
        var openItem = new MenuItem { Header = Loc.T("打开主窗口") }; openItem.Click += (_, _) => ManageRequested?.Invoke();
        var lockItem = new MenuItem { Header = Loc.T("锁定 / 解锁位置") }; lockItem.Click += (_, _) => ToggleLock();
        var hideItem = new MenuItem { Header = Loc.T("隐藏此组件") }; hideItem.Click += (_, _) => HideFromDesktop();
        surface.ContextMenu = new ContextMenu { ItemsSource = new[] { openItem, lockItem, hideItem } };
        Content = surface;
        ApplyMaterial();
        PropertyChanged += (_, e) => { if (e.Property == ActualTransparencyLevelProperty) ApplyMaterial(); };
        if (preferences.ReduceMotion || DesktopNative.ReducedMotion) Classes.Add("reduced-motion");
        timer.Tick += (_, _) =>
        {
            try { if (IsVisible) { if (collapsed) compact.Text = pressure?.Summary ?? courses?.Summary ?? todos?.Summary ?? ""; DesktopNative.Recover(handle); Opacity = 1; StatusChanged?.Invoke(MaterialStatus); } }
            catch (Exception exception) { Opacity = 0; StatusChanged?.Invoke(exception.Message); }
        };
        Closing += (_, e) => { if (!disposing) { e.Cancel = true; HideFromDesktop(); } };
        PositionChanged += (_, _) => { if (IsVisible) SaveGeometry(); };
        Resized += (_, _) => { if (IsVisible) { UpdateRoundedRegion(); SaveGeometry(); } };
        preferenceTimer.Tick += (_, _) => { preferenceTimer.Stop(); SnapToEdges(); PersistPreferences(); };
        Refresh(); if (WidgetCollapsed) ToggleCollapse();
    }
    public void Present()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("桌面待办需要 Windows Explorer。");
        try
        {
            if (!IsVisible) { Opacity = 0; Show(); }
            handle = TryGetPlatformHandle()?.Handle ?? 0;
            if (handle == 0) throw new InvalidOperationException("无法创建桌面组件。");
            ClampPosition(); DesktopNative.Attach(handle); DesktopNative.SetClickThrough(handle,WidgetClickThrough); ApplyMaterial(); UpdateRoundedRegion(); Opacity = 1; timer.Start(); WidgetEnabled = true; SaveGeometry();
        }
        catch { timer.Stop(); DesktopNative.Detach(handle); Hide(); throw; }
    }
    public bool HasNativeBlur => ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur || ActualTransparencyLevel == WindowTransparencyLevel.Blur;
    public string MaterialStatus => preferences.DesktopGlass && HasNativeBlur ? "已接入桌面 · 磨砂玻璃" : preferences.DesktopGlass ? "已接入桌面 · 当前环境使用清晰底色" : "已接入桌面 · 实色背景";
    public void ApplyMaterial()
    {
        var desired = preferences.DesktopGlass
            ? new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent }
            : new[] { WindowTransparencyLevel.Transparent };
        if (!TransparencyLevelHint.SequenceEqual(desired)) TransparencyLevelHint = desired;
        var dark = preferences.Theme == "Dark";
        // Keep glyphs fully opaque. Only the backdrop tint is translucent.
        var tint = double.IsFinite(preferences.DesktopGlassTint) ? Math.Clamp(preferences.DesktopGlassTint, .35, .85) : .48;
        var alpha = preferences.DesktopGlass && HasNativeBlur ? (byte)Math.Round(tint * 255) : (byte)255;
        surface.Background = new SolidColorBrush(dark ? Color.FromArgb(alpha, 22, 30, 43) : Color.FromArgb(alpha, 247, 250, 255));
        // 不再描一整圈白边：只在左上角留一道高光，其余边缘靠底色与壁纸的对比区分。
        sheen.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(Color.Parse(dark ? "#16FFFFFF" : "#38FFFFFF"), 0), new(Colors.Transparent, .45), new(Color.Parse(dark ? "#107F77DD" : "#148F9CEB"), 1) }
        };
        sheen.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(Color.Parse(dark ? "#40FFFFFF" : "#4DFFFFFF"), 0), new(Colors.Transparent, .28), new(Colors.Transparent, 1) }
        };
        Resources["TextPrimaryBrush"] = Brush.Parse(dark ? "#F5F7FC" : "#17243A");
        Resources["TextSecondaryBrush"] = Brush.Parse(dark ? "#D0D9E8" : "#43536B");
        Resources["GlassControlBrush"] = Brush.Parse(dark ? "#48263246" : "#78FFFFFF");
        Resources["GlassHoverBrush"] = Brush.Parse(dark ? "#88505B70" : "#B8FFFFFF");
        Resources["GlassSelectionBrush"] = new SolidColorBrush(((ISolidColorBrush)Ui.Brush("AccentBrush")).Color, .12);
        Resources["GlassLineBrush"] = Brush.Parse(dark ? "#55FFFFFF" : "#660F2748");
        Resources["SurfaceBrush"] = Brush.Parse(dark ? "#263246" : "#F5F8FD");
        Resources["DangerBrush"] = Brush.Parse(dark ? "#FFB1AA" : "#A92632");
        StatusChanged?.Invoke(MaterialStatus);
    }
    private void UpdateRoundedRegion()
    {
        if (handle != 0) nativeCorners = DesktopGlassNative.Round(handle, (int)Math.Ceiling(ClientSize.Width * RenderScaling), (int)Math.Ceiling(ClientSize.Height * RenderScaling), (int)Math.Round(CornerDip * RenderScaling));
    }
    public object VerifyGlass() => new { requested = preferences.DesktopGlass, actual = ActualTransparencyLevel.ToString(), native_blur = HasNativeBlur, corners = nativeCorners ? "dwm" : "region", tint = preferences.DesktopGlassTint, foreground_opacity = body.Opacity, window_opacity = Opacity };
    private double WidgetWidth { get => isPressure ? preferences.PressureWidth : isSchedule ? preferences.ScheduleWidth : preferences.DesktopWidth; set { if (isPressure) preferences.PressureWidth = value; else if (isSchedule) preferences.ScheduleWidth = value; else preferences.DesktopWidth = value; } }
    private double WidgetHeight { get => isPressure ? preferences.PressureHeight : isSchedule ? preferences.ScheduleHeight : preferences.DesktopHeight; set { if (isPressure) preferences.PressureHeight = value; else if (isSchedule) preferences.ScheduleHeight = value; else preferences.DesktopHeight = value; } }
    private int? WidgetX { get => isPressure ? preferences.PressureX : isSchedule ? preferences.ScheduleX : preferences.DesktopX; set { if (isPressure) preferences.PressureX = value; else if (isSchedule) preferences.ScheduleX = value; else preferences.DesktopX = value; } }
    private int? WidgetY { get => isPressure ? preferences.PressureY : isSchedule ? preferences.ScheduleY : preferences.DesktopY; set { if (isPressure) preferences.PressureY = value; else if (isSchedule) preferences.ScheduleY = value; else preferences.DesktopY = value; } }
    private bool WidgetCollapsed { get => isPressure ? preferences.PressureCollapsed : isSchedule ? preferences.ScheduleCollapsed : preferences.DesktopCollapsed; set { if (isPressure) preferences.PressureCollapsed = value; else if (isSchedule) preferences.ScheduleCollapsed = value; else preferences.DesktopCollapsed = value; } }
    private bool WidgetLocked { get => isPressure ? preferences.PressureLocked : isSchedule ? preferences.ScheduleLocked : preferences.DesktopLocked; set { if (isPressure) preferences.PressureLocked = value; else if (isSchedule) preferences.ScheduleLocked = value; else preferences.DesktopLocked = value; } }
    private bool WidgetEnabled { get => isPressure ? preferences.PressureEnabled : isSchedule ? preferences.ScheduleEnabled : preferences.DesktopEnabled; set { if (isPressure) preferences.PressureEnabled = value; else if (isSchedule) preferences.ScheduleEnabled = value; else preferences.DesktopEnabled = value; } }
    private bool WidgetClickThrough { get => isPressure ? preferences.PressureClickThrough : isSchedule ? preferences.ScheduleClickThrough : preferences.TodoClickThrough; set { if (isPressure) preferences.PressureClickThrough = value; else if (isSchedule) preferences.ScheduleClickThrough = value; else preferences.TodoClickThrough = value; } }
    public void SetClickThrough(bool enabled) { WidgetClickThrough = enabled; DesktopNative.SetClickThrough(handle,enabled); UpdateRoundedRegion(); ApplyMaterial(); PersistPreferences(); }
    public bool IsClickThrough => DesktopNative.IsClickThrough(handle);
    public void SnapToEdges()
    {
        if (!preferences.DesktopSnap || !IsVisible) return;
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary; if (screen is null) return;
        var area = screen.WorkingArea; var width = (int)(Width * screen.Scaling); var height = (int)(Height * screen.Scaling); var threshold = 20 * screen.Scaling;
        var x = Position.X; var y = Position.Y;
        if (Math.Abs(x-area.X) <= threshold) x=area.X; else if (Math.Abs(x+width-area.Right) <= threshold) x=area.Right-width;
        if (Math.Abs(y-area.Y) <= threshold) y=area.Y; else if (Math.Abs(y+height-area.Bottom) <= threshold) y=area.Bottom-height;
        if (x != Position.X || y != Position.Y) Position = new PixelPoint(x,y);
    }
    private void ClampPosition()
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        Position = new PixelPoint(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - (int)(Width * screen.Scaling))), Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)(Height * screen.Scaling))));
    }
    public void SetStore(Store store) { todos?.SetStore(store); courses?.SetStore(store); pressure?.SetStore(store); }
    public void Refresh() { todos?.Refresh(); if (courses is not null) _ = courses.ReloadAsync(); if (pressure is not null) _ = pressure.ReloadAsync(); compact.Text = pressure?.Summary ?? courses?.Summary ?? todos?.Summary ?? ""; compact.IsVisible = collapsed; }
    public void ToggleCollapse()
    {
        if (!collapsed) { WidgetHeight = Height; collapsed = true; body.IsVisible = false; MinHeight = 88; Height = 88; }
        else { collapsed = false; body.IsVisible = true; MinHeight = isPressure ? 236 : 88; Height = Math.Clamp(WidgetHeight, isPressure ? 236 : 360, isPressure ? 260 : 1000); }
        WidgetCollapsed = collapsed; Refresh(); if (IsVisible) SaveGeometry();
    }
    public void ToggleLock() { WidgetLocked = !WidgetLocked; ApplyLockIcon(); SaveGeometry(); }
    /// <summary>锁按钮只显示开合的锁形图标，状态用图标本身表达。</summary>
    private void ApplyLockIcon()
    {
        lockButton.Content = Ui.Icon(WidgetLocked ? "Lock" : "Unlock");
        var hint = Loc.T(WidgetLocked ? "已锁定 · 点击解锁" : "点击锁定位置");
        ToolTip.SetTip(lockButton, hint);
        Avalonia.Automation.AutomationProperties.SetName(lockButton, hint);
    }
    public void HideFromDesktop()
    {
        SaveGeometry(); timer.Stop(); DesktopNative.Detach(handle); Hide(); WidgetEnabled = false; PersistPreferences(); HiddenByUser?.Invoke();
    }
    private void SaveGeometry()
    {
        WidgetX = Position.X; WidgetY = Position.Y; WidgetWidth = Width;
        if (!collapsed) WidgetHeight = Height;
        WidgetCollapsed = collapsed; preferenceTimer.Stop(); preferenceTimer.Start();
    }
    private void PersistPreferences() { preferenceTimer.Stop(); try { preferences.Save(settingsDirectory); } catch (Exception error) { StatusChanged?.Invoke("设置未保存：" + error.Message); } }
    public object VerifyNativeLayer() => DesktopNative.Inspect(handle);
    public void Dispose() { if (disposing) return; disposing = true; SaveGeometry(); PersistPreferences(); timer.Stop(); DesktopNative.SetClickThrough(handle,false); DesktopNative.Detach(handle); Close(); }
}
