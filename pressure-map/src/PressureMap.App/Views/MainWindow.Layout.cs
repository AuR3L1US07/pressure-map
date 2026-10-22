using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PressureMap.Views;

public partial class MainWindow
{
    private void UpdatePageWidth()
    {
        // Sidebar and the two page margins stay fixed while the usable width grows.
        PageHost.Width = Math.Max(0, Math.Min(PageHost.MaxWidth, Bounds.Width - 230 - 64));
    }

    // One card per row, each spanning the full content width.
    private static Control SectionPage(string title, params Control[] sections)
    {
        var body = new StackPanel { Spacing = 16 };
        foreach (var section in sections) body.Children.Add(section);
        var page = new PageFrame(Ui.Text(title, "h1"), body);
        // Leave a gutter on the right so the overlay scrollbar never sits on a card edge.
        page.Margin = new Thickness(0);
        return page;
    }
}
