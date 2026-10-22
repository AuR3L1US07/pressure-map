using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    private async Task ReviewPagesAsync(string output)
    {
        var directory = Path.Combine(output, "review"); Directory.CreateDirectory(directory);
        var width = Width; var height = Height; var theme = Preferences.Theme; var language = Preferences.Language; var motion = Preferences.ReduceMotion;
        Preferences.ReduceMotion = true; ApplyTheme();
        if (!Navigation.ItemsSource!.Cast<NavigationItem>().Select(item => item.Page).SequenceEqual(new[] { 0, 2, 8, 1, 10, 9, 3 }) || Navigation.ItemsSource!.Cast<NavigationItem>().Any(item => item.Page is 4 or 5 or 6 or 7))
            throw new InvalidOperationException("Secondary settings destinations are still in the main sidebar.");
        SelectPage(3);
        foreach (var (destination, label) in new[] { (5, "外观"), (6, "数据与备份"), (4, "快捷键"), (7, "关于"), (3, "通用") })
        {
            PageHost.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == Loc.T(label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (currentPage != destination || Navigation.SelectedItem is not NavigationItem { Page: 3 })
                throw new InvalidOperationException("Settings subnavigation lost its sidebar selection.");
        }
        foreach (var lang in new[] { Loc.Chinese, Loc.English })
        {
            SetLanguage(lang);
            foreach (var size in new[] { new Size(1180, 820), new Size(960, 640) })
            {
                Width = size.Width; Height = size.Height; await Task.Delay(100);
                for (var page = 0; page < 11; page++)
                {
                    ErrorBanner.IsVisible = false; onlinePage = null; SelectPage(page); await Task.Delay(130);
                    if (ErrorBanner.IsVisible) throw new InvalidOperationException($"UI review {lang}/{page}: {ErrorText.Text}");
                    if (PageHost.Content is not PageFrame) throw new InvalidOperationException($"Page {page} is missing its shared frame.");
                    VerifyButtonTextCenters(PageHost, $"{lang}/{size.Width}/page-{page}");
                    Capture(Path.Combine(directory, $"{lang}-{(int)size.Width}-page-{page}.png"));
                }
            }
        }
        SetLanguage(Loc.Chinese); Width = 1180; Height = 820;
        foreach (var mode in new[] { "Light", "Dark" })
        {
            Preferences.Theme = mode; ApplyTheme();
            for (var page = 0; page < 11; page++)
            {
                onlinePage = null; SelectPage(page); await Task.Delay(130);
                VerifyButtonTextCenters(PageHost, $"{mode}/page-{page}");
                Capture(Path.Combine(directory, $"{mode}-page-{page}.png"));
            }
        }
        // Render-scale coverage supplements native screenshots; it does not emulate OS DPI changes.
        SelectPage(0); await Task.Delay(130);
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(ClientSize.Width * scale), (int)(ClientSize.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(this); bitmap.Save(Path.Combine(directory, $"render-{(int)(scale * 100)}.png"), new PngBitmapEncoderOptions());
        }
        Preferences.Theme = theme; Preferences.ReduceMotion = motion; ApplyTheme(); SetLanguage(language); Width = width; Height = height;
        SaveCenteringAudit(output);
    }
}
