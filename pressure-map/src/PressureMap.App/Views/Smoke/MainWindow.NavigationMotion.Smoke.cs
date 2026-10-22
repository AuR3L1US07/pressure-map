using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using PressureMap.Services;

namespace PressureMap.Views;

public partial class MainWindow
{
    private async Task VerifyNavigationMotionAsync(string output)
    {
        var reduced = Preferences.ReduceMotion;
        var oldIndex = Navigation.SelectedIndex;
        ListBoxItem? item = null;
        try
        {
            Preferences.ReduceMotion = false; ApplyTheme();
            Navigation.SelectedIndex = 0;
            await Task.Delay(380);
            var rows = Navigation.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            if (rows.Length < 2) throw new InvalidOperationException("Navigation startup requires multiple realized rows.");
            void VerifyRestingRows()
            {
                if (rows.Any(row => Math.Abs(row.Opacity - 1) > .001 ||
                    (row.RenderTransform is not null && !row.RenderTransform.Value.IsIdentity)))
                    throw new InvalidOperationException("Navigation startup left a translated or faded row behind.");
            }
            var entrance = PlayNavigationStartupAsync(CancellationToken.None);
            var sawLeftOffset = false;
            var sawTopToBottomFade = false;
            for (var tick = 0; tick < 8; tick++)
            {
                await Task.Delay(25);
                sawLeftOffset |= rows.Any(row => row.RenderTransform?.Value.M31 < -.1);
                if (tick < 6)
                    sawTopToBottomFade |= rows.Zip(rows.Skip(1), (earlier, later) => earlier.Opacity > later.Opacity + .01).Any(value => value);
                if (tick == 1) Capture(Path.Combine(output, "navigation-startup-stagger.png"));
            }
            await entrance.WaitAsync(TimeSpan.FromSeconds(3));
            if (!MotionReduced && (!sawLeftOffset || !sawTopToBottomFade))
                throw new InvalidOperationException("Navigation startup did not sweep from the left with rows entering from top to bottom.");
            VerifyRestingRows();
            using (var cancelledEntrance = new CancellationTokenSource())
            {
                var interrupted = PlayNavigationStartupAsync(cancelledEntrance.Token);
                await Task.Delay(40);
                cancelledEntrance.Cancel();
                await interrupted.WaitAsync(TimeSpan.FromSeconds(3));
                VerifyRestingRows();
            }
            item = Navigation.GetVisualDescendants().OfType<ListBoxItem>().First();
            Border Part(string name) => item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);
            var hover = Part("NavHover");
            var selected = Part("NavSelection");
            if (item.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "NavIndicator"))
                throw new InvalidOperationException("Navigation still has the unwanted left selection indicator.");
            var root = item.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "NavMotionRoot");
            var presenter = root.Children.OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            void VerifyPresenterSurface()
            {
                if (selected.CornerRadius != new CornerRadius(10) || hover.CornerRadius != new CornerRadius(10))
                    throw new InvalidOperationException("Navigation selection and hover must both retain 10 DIP rounded corners.");
                if (presenter.Background is not ISolidColorBrush { Color.A: 0 } || presenter.BorderBrush is not ISolidColorBrush { Color.A: 0 })
                    throw new InvalidOperationException("Fluent navigation state paints over the rounded animated surfaces.");
                if (presenter.CornerRadius != item.CornerRadius)
                    throw new InvalidOperationException("Navigation presenter does not inherit the rounded item corners.");
            }
            VerifyPresenterSurface();
            if (!NavigationMotion.GetEnabled(item)) throw new InvalidOperationException("Navigation press handlers are disabled.");
            if (selected.Opacity != 1) throw new InvalidOperationException("Selected navigation lost its rounded purple surface.");
            ((IPseudoClasses)item.Classes).Set(":pointerover", true);
            await Task.Delay(250);
            if (hover.Opacity < .99 || Math.Abs(hover.RenderTransform!.Value.M11 - 1) > .001)
                throw new InvalidOperationException("Navigation hover did not expand and fade in.");
            if (selected.Opacity != 1 || selected.Background is not ISolidColorBrush)
                throw new InvalidOperationException("Hover removed the persistent selection surface.");
            VerifyPresenterSurface();
            if (!MotionReduced && (Math.Abs(root.RenderTransform!.Value.M11 - 1.015) > .001 || Math.Abs(root.RenderTransform.Value.M32 + 1) > .03))
                throw new InvalidOperationException("Navigation hover does not float slightly upward and expand.");
            item.Classes.Add("nav-pressed");
            await Task.Delay(150);
            VerifyPresenterSurface();
            if (!MotionReduced && Math.Abs(root.RenderTransform!.Value.M11 - .98) > .001)
                throw new InvalidOperationException("Navigation press did not scale down.");
            Capture(Path.Combine(output, "navigation-hover-press.png"));
            item.Classes.Remove("nav-pressed");
            await Task.Delay(200);
            if (!MotionReduced && Math.Abs(root.RenderTransform!.Value.M11 - 1.015) > .001)
                throw new InvalidOperationException("Navigation release did not return to the floating hover state.");
            ((IPseudoClasses)item.Classes).Set(":pointerover", false);
            await Task.Delay(260);
            if (hover.Opacity > .001 || Math.Abs(hover.RenderTransform!.Value.M11 - .75) > .001 || Math.Abs(root.RenderTransform!.Value.M11 - 1) > .001)
                throw new InvalidOperationException("Navigation pointer exit did not reset its hover and press layers.");
            Preferences.ReduceMotion = true; ApplyTheme();
            ((IPseudoClasses)item.Classes).Set(":pointerover", true);
            item.Classes.Add("nav-pressed");
            await Task.Delay(30);
            if (hover.Transitions is not null || root.Transitions is not null || Math.Abs(root.RenderTransform!.Value.M11 - 1) > .001)
                throw new InvalidOperationException("Reduced motion did not disable navigation animations.");
            if (Math.Abs(root.RenderTransform!.Value.M32) > .001 || hover.BoxShadow.Count != 0)
                throw new InvalidOperationException("Reduced motion retained the floating offset or shadow.");
            Capture(Path.Combine(output, "navigation-reduced-motion.png"));
        }
        finally
        {
            if (item is not null)
            {
                item.Classes.Remove("nav-pressed");
                ((IPseudoClasses)item.Classes).Set(":pointerover", false);
            }
            Preferences.ReduceMotion = reduced; ApplyTheme();
            Navigation.SelectedIndex = oldIndex;
        }
    }
}
