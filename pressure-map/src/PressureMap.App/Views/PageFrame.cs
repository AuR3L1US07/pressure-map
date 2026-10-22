using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PressureMap.Localization;

namespace PressureMap.Views;

/// <summary>Shared fixed heading and independently scrolling page content.</summary>
public sealed class PageFrame : Grid
{
    public Control MotionContent { get; }
    public Control MotionLayer { get; }
    public IReadOnlyList<Control> MotionItems { get; }
    public void SetNavigation(Control navigation)
    {
        if (Children.FirstOrDefault() is TextBlock title) title.Text = Loc.T("设置");
        foreach (var child in Children.Where(c => GetRow(c) == 2)) SetRow(child, 4);
        RowDefinitions = new RowDefinitions("Auto,16,Auto,20,*");
        SetRow(navigation, 2); Children.Add(navigation);
    }
    public PageFrame(Control heading, Control content, bool scroll = true)
    {
        RowDefinitions = new RowDefinitions("Auto,20,*");
        if (!scroll) Margin = new Thickness(0, 0, 0, 32);
        Children.Add(heading);
        var items = new List<Control>();
        CollectMotionItems(content, items);
        MotionItems = items.Count > 0 ? items : [content];
        var motionLayer = new Border { Child = content };
        MotionLayer = motionLayer;
        Control body = new Border { Child = motionLayer, ClipToBounds = true };
        if (scroll)
        {
            var viewer = Ui.Scroll(body);
            viewer.Padding = new Thickness(0, 0, 12, 32);
            var divider = Ui.Divider(); divider.VerticalAlignment = VerticalAlignment.Bottom; divider.IsVisible = false;
            viewer.ScrollChanged += (_, _) => divider.IsVisible = viewer.Offset.Y > 0;
            Children.Add(divider);
            body = viewer;
        }
        MotionContent = body;
        SetRow(body, 2); Children.Add(body);
    }
    private static void CollectMotionItems(Control control, List<Control> items)
    {
        if (!control.IsVisible) return;
        // Cards remain a single visual unit; don't animate their individual fields.
        if (control.Classes.Contains("card") || control is TextBlock or Button)
        {
            items.Add(control);
            return;
        }
        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children) CollectMotionItems(child, items);
                break;
            case Decorator { Child: { } child }:
                CollectMotionItems(child, items);
                break;
            case ContentControl { Content: Control child }:
                CollectMotionItems(child, items);
                break;
        }
    }
}
