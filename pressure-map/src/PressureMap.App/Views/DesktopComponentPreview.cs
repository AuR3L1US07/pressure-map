using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PressureMap.Views;

/// <summary>Pure presentation of the existing material preference; no desktop/window operations.</summary>
public sealed class DesktopComponentPreview : Border
{
    private readonly Border widget;
    public DesktopComponentPreview()
    {
        Height = 160; CornerRadius = new CornerRadius(12); ClipToBounds = true; Padding = new Thickness(24);
        Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.Parse("#809BBE"), 0), new(Color.Parse("#C3AED5"), .5), new(Color.Parse("#DBB89C"), 1) } };
        var rows = Ui.Stack(8, Ui.Text("今天要完成", "h2"), Ui.Row(Ui.Icon("Check"), Ui.Text("规划学期，安排每一天。", "caption")));
        widget = new Border { Child = rows, CornerRadius = new CornerRadius(12), Padding = new Thickness(16), MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Child = widget;
    }
    public void Update(bool glass, double tint)
    {
        var color = ((ISolidColorBrush)Ui.Brush("SurfaceBrush")).Color;
        widget.Background = new SolidColorBrush(color, glass ? Math.Clamp(tint, .35, .85) : 1);
    }
}
