using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SkiaSharp;
using PressureMap.Services;

internal static class Program
{
    [STAThread] public static void Main(string[] args) => AppBuilder.Configure<PlaygroundApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
public sealed class PlaygroundApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new Playground();
        base.OnFrameworkInitializationCompleted();
    }
}
public sealed class Playground : Window
{
    private readonly List<Probe> probes = [];
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    public Playground()
    {
        Title = "Glass Playground · A / B"; Width = 760; Height = 300;
        var rows = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        rows.Children.Add(new TextBlock { Text = "Glass Playground · 系统模糊 A / 壁纸预模糊 B", FontSize = 22 });
        rows.Children.Add(new TextBlock { Text = "独立测试，不修改正式小组件和系统壁纸。A 需观察真实桌面；合成背景只用于 B 算法预览。", TextWrapping = TextWrapping.Wrap });
        var real = new Button { Content = "真实桌面对比（关联现有桌面层）" };
        real.Click += (_, _) => ShowPair(null);
        rows.Children.Add(real);
        var fixtures = new Button { Content = "生成六张算法测试图并预览 B" };
        fixtures.Click += (_, _) => { GenerateFixtures(); ShowPair(Path.Combine(root, "tests/wallpapers/03-saturated.png")); };
        rows.Children.Add(fixtures);
        rows.Children.Add(new TextBlock { Text = "截图：测试窗口显示后 12 秒自动保存；期间点击本窗口使其失焦。输出到 output/glass-playground。", TextWrapping = TextWrapping.Wrap });
        Content = rows;
        Closed += (_, _) => { foreach (var p in probes.ToArray()) p.Close(); };
        if (Environment.GetCommandLineArgs().Contains("--capture")) Opened += async (_, _) => { GenerateFixtures(); ShowPair(null); await System.Threading.Tasks.Task.Delay(1000); Activate(); await System.Threading.Tasks.Task.Delay(12500); Close(); };
    }
    private void ShowPair(string? fixture)
    {
        foreach (var p in probes.ToArray()) p.Close(); probes.Clear();
        var wallpaper = fixture ?? Wallpaper();
        var output = Path.Combine(root, "output/glass-playground"); Directory.CreateDirectory(output);
        var screen = Screens.Primary!;
        for (var i = 0; i < 2; i++)
        {
            var p = new Probe(i == 0, wallpaper, screen.Bounds, output);
            p.Position = new PixelPoint(screen.WorkingArea.X + 30 + i * (int)(390 * screen.Scaling), screen.WorkingArea.Y + 80);
            p.Show(); probes.Add(p);
            if (fixture is null) DesktopNative.Attach(p.TryGetPlatformHandle()!.Handle);
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint flags);
    private static string Wallpaper() { var path = new StringBuilder(32768); return SystemParametersInfo(0x73, (uint)path.Capacity, path, 0) ? path.ToString() : ""; }
    private void GenerateFixtures()
    {
        var directory = Path.Combine(root, "tests/wallpapers"); Directory.CreateDirectory(directory);
        string[] names = ["01-white", "02-black", "03-saturated", "04-dense-landscape-synthetic", "05-night-synthetic", "06-pale-synthetic"];
        for (var i = 0; i < names.Length; i++)
        {
            using var bitmap = new SKBitmap(1920, 1080); using var canvas = new SKCanvas(bitmap);
            canvas.Clear(i == 0 ? SKColors.White : i == 1 ? SKColors.Black : i == 5 ? new SKColor(225, 231, 229) : new SKColor(13, 22, 48));
            using var paint = new SKPaint { IsAntialias = true };
            if (i == 2) { paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0,0), new SKPoint(1920,1080), [SKColors.Magenta, SKColors.Cyan, SKColors.Yellow], null, SKShaderTileMode.Clamp); canvas.DrawRect(0,0,1920,1080,paint); }
            if (i >= 3) { var random = new Random(42); for (var n = 0; n < 1800; n++) { paint.Color = i == 5 ? new SKColor(200,215,210,35) : new SKColor((byte)random.Next(256),(byte)random.Next(256),(byte)random.Next(256), (byte)(i == 4 ? 55 : 160)); canvas.DrawCircle(random.Next(1920), random.Next(1080), random.Next(2,50), paint); } }
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(Path.Combine(directory,names[i]+".png")); data.SaveTo(file);
        }
    }
}
public sealed class Probe : Window
{
    private readonly bool system;
    private readonly Bitmap? backdrop;
    private readonly DispatcherTimer capture = new() { Interval = TimeSpan.FromSeconds(12) };
    public Probe(bool system, string path, PixelRect screen, string output)
    {
        this.system = system; Width = 360; Height = 460; ShowInTaskbar = false; ShowActivated = false;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = system ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.None] : [WindowTransparencyLevel.Transparent];
        var watch = Stopwatch.StartNew();
        if (!system && File.Exists(path))
        {
            using var source = SKBitmap.Decode(path);
            if (source is not null)
            {
                using var reduced = new SKBitmap(Math.Max(1,screen.Width/4),Math.Max(1,screen.Height/4));
                using (var canvas = new SKCanvas(reduced))
                using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(6,6) })
                    {
                    var scale = Math.Max((float)reduced.Width/source.Width, (float)reduced.Height/source.Height);
                    var w = source.Width*scale; var h = source.Height*scale;
                    canvas.DrawBitmap(source, new SKRect((reduced.Width-w)/2,(reduced.Height-h)/2,(reduced.Width+w)/2,(reduced.Height+h)/2), paint);
                }
                using var data = reduced.Encode(SKEncodedImageFormat.Png,100); using var stream = data.AsStream(); backdrop = new Bitmap(stream);
            }
        }
        watch.Stop();
        var layers = new Grid();
        if (backdrop is not null)
        {
            var image = new Image { Source = backdrop, Stretch = Stretch.Fill, Width = screen.Width, Height = screen.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            layers.Children.Add(image);
            void Align() { image.Width = screen.Width/RenderScaling; image.Height = screen.Height/RenderScaling; image.RenderTransform = new TranslateTransform(-(Position.X-screen.X)/RenderScaling, -(Position.Y-screen.Y)/RenderScaling); }
            PositionChanged += (_, _) => Align(); Opened += (_, _) => Align();
        }
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 20 };
        content.Children.Add(new TextBlock { Text = system ? "A · 系统背景模糊" : "B · 壁纸预模糊（原型）", FontSize = 18, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = "今日课程", FontSize = 24 });
        content.Children.Add(new TextBlock { Text = "10月20日 星期二", FontSize = 17 });
        content.Children.Add(new Border { Background = Brush.Parse("#336552CC"), CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Child = new TextBlock { Text = "进行中 · 高等数学\n第6教研楼北202\n还有 35 分钟", TextWrapping = TextWrapping.Wrap } });
        content.Children.Add(new TextBlock { Text = "辅助文字 · 对比度检查 13 dip", FontSize = 13 });
        content.Children.Add(new TextBlock { Text = $"预处理 {watch.ElapsedMilliseconds} ms\nB 仅验证 Fill / 单屏；非完整壁纸服务", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        layers.Children.Add(new Border { Background = Brush.Parse("#3BFFFFFF"), BorderBrush = new LinearGradientBrush { StartPoint = new RelativePoint(0,0,RelativeUnit.Relative), EndPoint = new RelativePoint(1,1,RelativeUnit.Relative), GradientStops = new GradientStops { new(Colors.White,0), new(Color.Parse("#20FFFFFF"),.6), new(Color.Parse("#70FFFFFF"),1) } }, BorderThickness = new Thickness(1.25), CornerRadius = new CornerRadius(28), Child = content });
        Foreground = Brush.Parse("#14131F"); Content = new Border { CornerRadius = new CornerRadius(28), ClipToBounds = true, Child = layers };
        capture.Tick += (_, _) =>
        {
            capture.Stop(); var name = system ? "A" : "B";
            NativeCapture.Save(TryGetPlatformHandle()!.Handle, Path.Combine(output,name+"-unfocused.png"),false);
            File.WriteAllText(Path.Combine(output,name+"-probe.txt"), $"actual={ActualTransparencyLevel}; active={IsActive}; preprocess_ms={watch.ElapsedMilliseconds}; wallpaper_found={File.Exists(path)}; os={Environment.OSVersion}");
        };
        Opened += (_, _) => capture.Start();
        Closed += (_, _) => { capture.Stop(); DesktopNative.Detach(TryGetPlatformHandle()?.Handle ?? 0); backdrop?.Dispose(); };
    }
}


