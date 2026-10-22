using Avalonia;
using Avalonia.Controls;

namespace PressureMap.Views;

public partial class MainWindow
{
    private async Task VerifyMotionGeometryAsync(string output)
    {
        var reduced = Preferences.ReduceMotion;
        try
        {
            Preferences.ReduceMotion = false; ApplyTheme();
            foreach (var destination in new[] { 0, 3, 5, 2 })
            {
                SelectPage(destination);
                foreach (var (delay, frameNumber) in new[] { (20, 0), (60, 1), (120, 2) })
                {
                    await Task.Delay(delay);
                    var frame = (PageFrame)PageHost.Content!;
                    var heading = frame.Children[0]; var body = frame.MotionContent;
                    var titlePoint = heading.TranslatePoint(new Point(), this) ?? throw new InvalidOperationException("Detached heading.");
                    var bodyPoint = body.TranslatePoint(new Point(), this) ?? throw new InvalidOperationException("Detached content.");
                    if (bodyPoint.Y < titlePoint.Y + heading.Bounds.Height - .5 || bodyPoint.X < titlePoint.X - .5)
                        throw new InvalidOperationException("Page content overlaps the heading or starts outside its left edge.");
                    if (WindowShell.RenderTransform is not null || heading.RenderTransform is not null || body.RenderTransform is not null)
                        throw new InvalidOperationException("Page navigation must not transform the shell, title or content layout.");
                    Capture(Path.Combine(output, $"motion-page-{destination}-frame-{frameNumber}.png"));
                }
                await Task.Delay(450);
                if (((PageFrame)PageHost.Content!).MotionItems.Any(item => item.RenderTransform is not null || item.Opacity != 1))
                    throw new InvalidOperationException("Page entrance left a render transform behind.");
            }
        }
        finally { Preferences.ReduceMotion = reduced; ApplyTheme(); }
    }
}
