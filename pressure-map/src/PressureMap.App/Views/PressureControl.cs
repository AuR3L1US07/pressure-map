using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PressureMap.Core;
using PressureMap.Services;
using PressureMap.Localization;

namespace PressureMap.Views;

public sealed class PressureRing : Control
{
    public int Score { get; set; }
    public Color Color { get; set; } = Avalonia.Media.Color.Parse("#7F77DD");
    private bool hasScore;
    private CancellationTokenSource? fade;
    public PressureRing() { DetachedFromVisualTree += (_,_) => { fade?.Cancel(); Opacity = 1; }; }
    public void SetScore(int value, Color color, bool reducedMotion)
    {
        var changed = hasScore && value != Score; hasScore = true;
        Score = value; Color = color; InvalidateVisual(); fade?.Cancel(); Opacity = 1;
        if (!changed || reducedMotion || !IsVisible) return;
        fade = new CancellationTokenSource(); _ = FadeAsync(fade.Token);
    }
    private async Task FadeAsync(CancellationToken cancellation)
    {
        // Only a short opacity cue; the numeric value remains exact and immediate.
        var animation = new Avalonia.Animation.Animation { Duration = TimeSpan.FromMilliseconds(200), Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            Children = { new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty,.65d) } },
                         new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty,1d) } } } };
        try { await animation.RunAsync(this,cancellation); } catch (OperationCanceledException) { }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var center = new Point(Bounds.Width / 2, Bounds.Height / 2); var radius = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - (Bounds.Width < 80 ? 4 : 9));
        context.DrawEllipse(null, new Pen(Ui.Brush("AccentSoftBrush"), Bounds.Width < 80 ? 6 : 12), center, radius, radius);
        var pen = new Pen { Brush = new SolidColorBrush(Color), Thickness = Bounds.Width < 80 ? 6 : 12, LineCap = PenLineCap.Round };
        if (Score >= 100) context.DrawEllipse(null, pen, center, radius, radius);
        else if (Score > 0)
        {
            var angle = Score / 100d * Math.PI * 2; var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var path = new StreamGeometry(); using (var draw = path.Open()) { draw.BeginFigure(new Point(center.X, center.Y - radius), false); draw.ArcTo(end, new Size(radius, radius), 0, Score > 50, SweepDirection.Clockwise); draw.EndFigure(false); }
            context.DrawGeometry(null, pen, path);
        }
    }
}
public sealed class PressureControl : UserControl
{
    private readonly bool compact;
    private Store store; private readonly Preferences preferences;
    private readonly PressureRing ring = new() { Width = 154, Height = 154 };
    private readonly TextBlock score = Ui.Text("—"), level = Ui.Text("正在计算", "h2"), description = Ui.Text("", "caption"), error = Ui.Text("", "danger");
    private readonly StackPanel sources = new() { Spacing = 7 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) }; private int revision;
    public DailyPressure? Report { get; private set; }
    public string Summary => Report is { } r ? Loc.F("今日压力 {0}/100 · {1}", r.Score, r.Level) : "正在计算每日压力";
    public PressureControl(Store store, Preferences preferences, bool compact = false, bool minimal = false)
    {
        this.store = store; this.preferences = preferences; this.compact = compact; score.FontSize = 40; score.FontWeight = FontWeight.SemiBold;
        var center = Ui.Stack(score, Ui.Text("/ 100", "caption")); center.Spacing = 0; center.HorizontalAlignment = HorizontalAlignment.Center; center.VerticalAlignment = VerticalAlignment.Center;
        center.Classes.Add("pressure-center");
        foreach (var text in center.Children.OfType<TextBlock>()) { text.HorizontalAlignment = HorizontalAlignment.Center; text.TextAlignment = TextAlignment.Center; }
        var donut = new Grid { Width = 154, Height = 154, HorizontalAlignment = HorizontalAlignment.Center }; donut.Children.Add(ring); donut.Children.Add(center);
        var button = Ui.Button("查看计算方式", async () => { if (TopLevel.GetTopLevel(this) is Window owner) await Dialogs.Info(owner, "每日压力怎么算", "分数 = 当天等效负荷 ÷ 每日容量 × 100，上限 100。\n\n课程每小时折算 0.35 小时负荷，重叠课时不重复累计。未完成任务按预计耗时、截止紧迫度和类型加权；考试 ×1.6，报告/实验 ×1.15，重要任务额外 ×1.1。\n\n今天到期 ×1.6，逾期 ×1.8。未来七天到期的任务按剩余天数分摊；安排到今天的任务计入全部预计耗时。子任务进度会按比例减少预计耗时，保留 10% 用于最终检查。已完成但未提交仅按 15 分钟收尾负荷计算，已完成/已提交的不再计入。\n\n没有安排日期、也没有临近截止日期的任务暂不计入。每日容量可在常规设置调整。"); }, "pill");
        if (minimal)
        {
            ring.Width = ring.Height = donut.Width = donut.Height = 120; score.FontSize = 32;
            level.FontSize = 16; level.HorizontalAlignment = HorizontalAlignment.Center;
            level.TextAlignment = TextAlignment.Center;
            error.IsVisible = false;
            Content = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Children = { donut, level } };
        }
        else if (compact)
        {
            ring.Width = ring.Height = donut.Width = donut.Height = 128; score.FontSize = 34;
            Content = Ui.Columns("128,12,*", donut, new Border(), Ui.Stack(level, description, error, button));
        }
        else Content = Ui.Stack(donut, level, description, Ui.Text("主要来源", "label"), sources, error, button);
        timer.Tick += async (_, _) => await ReloadAsync(); AttachedToVisualTree += (_, _) => timer.Start(); DetachedFromVisualTree += (_, _) => { timer.Stop(); revision++; };
        _ = ReloadAsync();
    }
    public void SetStore(Store value) { store = value; _ = ReloadAsync(); }
    public async Task ReloadAsync()
    {
        var current = ++revision; var data = store; var today = DateTime.Today;
        try
        {
            var report = await Task.Run(() => TaskWorkload.Compute(data.ListTodos(), CourseCalendar.Day(data.ListCalendars(), today), today, preferences.DailyCapacityHours));
            if (current != revision) return; Report = report;
            ring.SetScore(report.Score, ((ISolidColorBrush)Ui.Brush(report.Score >= 85 ? "DangerBrush" : report.Score >= 65 ? "WarningBrush" : report.Score < 35 ? "SuccessBrush" : "AccentBrush")).Color,preferences.ReduceMotion || DesktopNative.ReducedMotion);
            score.Text = report.Score.ToString(); level.Text = report.Level;
            description.Text = Loc.F("课程 {0:0.#} 小时 · 任务折算 {1:0.#} 小时\n每日容量 {2:0.#} 等效小时", report.CourseHours, report.TaskHours, preferences.DailyCapacityHours);
            sources.Children.Clear(); foreach (var item in report.Sources.Take(3)) sources.Children.Add(Ui.Text(Loc.F("{0} · {1:0.##} 等效小时", item.Label, item.EquivalentHours), "caption")); error.Text = "";
        }
        catch (Exception e) { if (current == revision) { error.Text = Loc.T("压力计算失败：") + e.Message; level.Text = Loc.T("压力计算失败：").TrimEnd('：'); } }
    }
}
