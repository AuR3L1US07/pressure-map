using Avalonia.Animation.Easings;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Styling;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PressureMap.Services;

namespace PressureMap.Views;

public partial class MainWindow
{
    private readonly List<Control> pageMotionItems = [];
    private readonly List<(Control Target, double Opacity)> pageMotionOriginals = [];
    private CompositionVisual? startupVisual;
    private CancellationTokenSource? pageMotionLifetime, startupLifetime;
    private TaskCompletionSource? startupCompletion;
    private bool startupPlayed;
    private int pageMotionRevision;
    private Task pageMotionCompletion = Task.CompletedTask;
    private bool MotionReduced => Preferences.ReduceMotion || DesktopNative.ReducedMotion;
    private void InitializeChrome()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.Property != WindowStateProperty) return;
            if (WindowState is WindowState.Maximized or WindowState.FullScreen) { WindowState = WindowState.Normal; FitWindowToScreen(true); return; }
            ApplyRoundedCorners();
            var maximized = WindowState == WindowState.Maximized;
            MaximizeGlyph.Data = Geometry.Parse(maximized ? "M 8,5 L 18,5 L 18,15 M 5,8 L 15,8 L 15,18 L 5,18 Z" : "M 5,5 L 17,5 L 17,17 L 5,17 Z");
            AutomationProperties.SetName(MaximizeButton, maximized ? "还原" : "最大化");
            ToolTip.SetTip(MaximizeButton, maximized ? "还原" : "最大化");
            if (WindowState != WindowState.Normal) StopMotion();
        };
        AddHandler(PointerPressedEvent, (_, _) => FinishStartup(), RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, _) => FinishStartup(), RoutingStrategies.Tunnel, true);
        Opened += (_, _) => { FitWindowToScreen(); ApplyRoundedCorners(); };
        SizeChanged += (_, _) => { ApplyRoundedCorners(); ResetPageMotion(); };
        Closed += (_, _) => StopMotion();
    }
    private void FitWindowToScreen(bool largest = false)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea; var scale = screen.Scaling;
        MaxWidth = Math.Min(1280, Math.Max(1, area.Width / scale - 32));
        MaxHeight = Math.Min(820, Math.Max(1, area.Height / scale - 32));
        MinWidth = Math.Min(960, MaxWidth); MinHeight = Math.Min(640, MaxHeight);
        Width = Math.Min(largest ? MaxWidth : Width, MaxWidth);
        Height = Math.Min(largest ? MaxHeight : Height, MaxHeight);
        Position = new PixelPoint(area.X + (int)((area.Width - Width * scale) / 2), area.Y + (int)((area.Height - Height * scale) / 2));
    }
    private void MinimizeWindow(object? sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;
    private void ToggleMaximize(object? sender, RoutedEventArgs args) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object? sender, RoutedEventArgs args) => Close();

    // Offset belongs to Avalonia layout. Never animate it: its value can be
    // resynchronized during a commit and move the content over the heading.
    // Startup opacity stays on the render thread and leaves geometry untouched.
    private static void StartEntrance(CompositionVisual visual, TimeSpan duration)
    {
        var easing = new CubicEaseOut();
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = duration;
        fade.InsertKeyFrame(0, .85f);
        fade.InsertKeyFrame(1, 1f, easing);
        visual.StartAnimation("Opacity", fade);
    }
    private static void ResetVisual(CompositionVisual? visual)
    {
        visual?.StopAnimation("Opacity");
    }
    public Task PlayStartupAsync()
    {
        if (startupPlayed || MotionReduced) return Task.CompletedTask;
        startupPlayed = true;
        var visual = ElementComposition.GetElementVisual(PageHost);
        if (visual is null) return Task.CompletedTask;
        var completion = startupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        startupLifetime = new CancellationTokenSource();
        var cancellation = startupLifetime.Token;
        _ = PlayNavigationStartupAsync(cancellation);
        visual.Compositor.RequestCompositionUpdate(() =>
        {
            if (cancellation.IsCancellationRequested || MotionReduced) { FinishStartup(); return; }
            startupVisual = visual;
            StartEntrance(visual, TimeSpan.FromMilliseconds(200));
            _ = CompleteStartupAsync(cancellation);
        });
        return completion.Task;
    }
    private async Task CompleteStartupAsync(CancellationToken cancellation)
    {
        try { await Task.Delay(480, cancellation); FinishStartup(); }
        catch (OperationCanceledException) { }
    }
    private void FinishStartup()
    {
        startupLifetime?.Cancel(); startupLifetime?.Dispose(); startupLifetime = null;
        ResetVisual(startupVisual); startupVisual = null;
        var pending = startupCompletion; startupCompletion = null; pending?.TrySetResult();
    }
    private Task PlayNavigationStartupAsync(CancellationToken cancellation)
    {
        if (MotionReduced) return Task.CompletedTask;
        var items = Navigation.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        return Task.WhenAll(items.Select((item, index) => EnterNavigationItemAsync(item, index, cancellation)));
    }
    private static async Task EnterNavigationItemAsync(Control item, int index, CancellationToken cancellation)
    {
        var originalTransform = item.RenderTransform;
        var originalOpacity = item.Opacity;
        item.RenderTransform = new TranslateTransform(-25, 0);
        item.SetCurrentValue(Visual.OpacityProperty, 0d);
        var delay = TimeSpan.FromMilliseconds(index * (31 - Math.Min(index, 15)));
        var movement = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300), Delay = delay,
            FillMode = FillMode.Backward, Easing = new NavigationEntranceEase(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, -25d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, 0d) } }
            }
        };
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(100), Delay = delay,
            FillMode = FillMode.Backward, Easing = new QuadraticEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, originalOpacity) } }
            }
        };
        async Task FadeAsync()
        {
            await fade.RunAsync(item, cancellation);
            if (!cancellation.IsCancellationRequested) item.SetCurrentValue(Visual.OpacityProperty, originalOpacity);
        }
        try { await Task.WhenAll(movement.RunAsync(item, cancellation), FadeAsync()); }
        catch (OperationCanceledException) { }
        finally
        {
            item.RenderTransform = originalTransform;
            item.SetCurrentValue(Visual.OpacityProperty, originalOpacity);
        }
    }
    private sealed class NavigationEntranceEase : Easing
    {
        public override double Ease(double progress)
        {
            var t = Math.Clamp(progress, 0, 1);
            var fluent = 1 - Math.Pow(1 - Math.Min(t * 300 / 200, 1), 3);
            var back = 1 - Math.Pow(1 - t, 2) * Math.Cos(1.5 * Math.PI * t);
            return (5 * fluent + 20 * back) / 25;
        }
    }
    private void AnimatePage()
    {
        ResetPageMotion();
        if (MotionReduced || !IsVisible || PageHost.Content is not Control page) return;
        var revision = pageMotionRevision;
        var items = page is PageFrame frame ? frame.MotionItems : [page];
        foreach (var item in items)
        {
            pageMotionItems.Add(item);
            pageMotionOriginals.Add((item, item.Opacity));
            item.SetCurrentValue(Visual.OpacityProperty, 0d);
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pageMotionCompletion = completion.Task;
        // Begin after bindings/layout. A superseded callback cannot affect a newer page.
        Dispatcher.UIThread.Post(async () =>
        {
            if (revision != pageMotionRevision || MotionReduced || !IsVisible || !ReferenceEquals(PageHost.Content, page)) { completion.TrySetResult(); return; }
            pageMotionLifetime = new CancellationTokenSource();
            try
            {
                await CompletePageMotionAsync(revision, pageMotionItems.ToArray(), pageMotionLifetime.Token);
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        }, DispatcherPriority.Loaded);
    }
    private static async Task SlidePageAsync(Control target, int index, CancellationToken cancellation)
    {
        // PCL's card entrance: -16px, 5px cubic-out/250ms + 11px back-out/350ms.
        // Each card follows the previous one by 25ms, with a separate 100ms fade.
        var delay = TimeSpan.FromMilliseconds(Math.Min(index * 25, 200));
        var originalTransform = target.RenderTransform;
        target.RenderTransform = new TranslateTransform();
        var entrance = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(350), Delay = delay,
            FillMode = FillMode.Backward, Easing = new CardEntranceEase(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, -16d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0d) } }
            }
        };
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(100), Delay = delay,
            FillMode = FillMode.Backward, Easing = new QuadraticEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
            }
        };
        async Task FadeAsync()
        {
            await fade.RunAsync(target, cancellation);
            if (!cancellation.IsCancellationRequested) target.SetCurrentValue(Visual.OpacityProperty, 1d);
        }
        try { await Task.WhenAll(entrance.RunAsync(target, cancellation), FadeAsync()); }
        catch (OperationCanceledException) { }
        finally { target.RenderTransform = originalTransform; }
    }
    private sealed class CardEntranceEase : Easing
    {
        public override double Ease(double progress)
        {
            var t = Math.Clamp(progress, 0, 1);
            var fluent = 1 - Math.Pow(1 - Math.Min(t * 350 / 250, 1), 3);
            var back = 1 - Math.Pow(1 - t, 1.5) * Math.Cos(1.5 * Math.PI * t);
            return (5 * fluent + 11 * back) / 16;
        }
    }
    private async Task CompletePageMotionAsync(int revision, Control[] items, CancellationToken cancellation)
    {
        try { await Task.WhenAll(items.Select((item, index) => SlidePageAsync(item, index, cancellation))); }
        finally { if (revision == pageMotionRevision) ResetPageMotion(); }
    }
    private void ResetPageMotion()
    {
        pageMotionRevision++;
        pageMotionLifetime?.Cancel(); pageMotionLifetime?.Dispose(); pageMotionLifetime = null;
        foreach (var (target, opacity) in pageMotionOriginals) target.SetCurrentValue(Visual.OpacityProperty, opacity);
        pageMotionOriginals.Clear();
        pageMotionItems.Clear();
    }
    private void StopMotion() { ResetPageMotion(); FinishStartup(); }
}
