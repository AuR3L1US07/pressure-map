using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PressureMap.Views;

public partial class MainWindow
{
    private readonly List<object> centeringAudit = [];
    private void VerifyButtonTextCenters(Control root, string context)
    {
        foreach (var button in root.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("centered-control-content") && b.IsEffectivelyVisible && b.Bounds.Width > 0))
        {
            if (button.Content is not Control content) continue;
            var origin = content.TranslatePoint(new Point(), button) ?? throw new InvalidOperationException("Detached button content.");
            var errorX = origin.X + content.Bounds.Width / 2 - button.Bounds.Width / 2;
            var errorY = origin.Y + content.Bounds.Height / 2 - button.Bounds.Height / 2;
            centeringAudit.Add(new { context, surface = "icon/content", errorX, errorY });
            if (Math.Abs(errorX) > 1 || Math.Abs(errorY) > 1) throw new InvalidOperationException($"Misaligned button content: {context}, {errorX:0.##},{errorY:0.##} dip.");
        }
        foreach (var button in root.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("centered-content") && b.IsEffectivelyVisible && b.Bounds.Width > 0))
        {
            if (button.Content is not string { Length: > 0 } caption) continue;
            var label = button.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == "CenteredButtonLabel")
                ?? throw new InvalidOperationException($"Missing centered text template: {context}/{caption}");
            var origin = label.TranslatePoint(new Point(), button) ?? throw new InvalidOperationException("Detached button label.");
            var errorX = origin.X + label.Bounds.Width / 2 - button.Bounds.Width / 2;
            var errorY = origin.Y + label.Bounds.Height / 2 - button.Bounds.Height / 2;
            centeringAudit.Add(new { context, caption, errorX, errorY });
            if (Math.Abs(errorX) > 1 || Math.Abs(errorY) > 1 || label.TextAlignment != TextAlignment.Center)
                throw new InvalidOperationException($"Misaligned text: {context}/{caption}, center error {errorX:0.##},{errorY:0.##} dip.");
        }
        foreach (var border in root.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0 && (b.Classes.Contains("kbd") || b.Classes.Contains("badge") || b.Classes.Contains("status-chip"))))
        {
            if (border.Child is not TextBlock label) continue;
            var origin = label.TranslatePoint(new Point(), border) ?? throw new InvalidOperationException("Detached badge text.");
            var errorX = origin.X + label.Bounds.Width / 2 - border.Bounds.Width / 2;
            var errorY = origin.Y + label.Bounds.Height / 2 - border.Bounds.Height / 2;
            centeringAudit.Add(new { context, caption = label.Text, surface = "badge/keycap", errorX, errorY });
            if (Math.Abs(errorX) > 1 || Math.Abs(errorY) > 1 || label.TextAlignment != TextAlignment.Center)
                throw new InvalidOperationException($"Misaligned badge/keycap: {context}/{label.Text}, center error {errorX:0.##},{errorY:0.##} dip.");
        }
        foreach (var center in root.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("pressure-center") && p.IsEffectivelyVisible))
            foreach (var label in center.Children.OfType<TextBlock>())
                if (Math.Abs(label.Bounds.Center.X - center.Bounds.Width / 2) > 1 || label.TextAlignment != TextAlignment.Center)
                    throw new InvalidOperationException("Pressure ring score and denominator are not centered on the same axis.");
    }
    private void SaveCenteringAudit(string output) => File.WriteAllText(Path.Combine(output, "centering.json"), JsonSerializer.Serialize(centeringAudit));
}
