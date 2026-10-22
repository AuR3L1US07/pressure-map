using Avalonia;

namespace PressureMap;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        if (args.Contains("--check"))
        {
            var temp = Path.Combine(Path.GetTempPath(), "PressureMap-check-" + Guid.NewGuid().ToString("N"));
            try { var store = new Core.Store(temp); var sample = Core.Rules.Sample(); Core.Rules.Validate(sample); return 0; }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception error)
        {
            var index = Array.IndexOf(args, "--data");
            var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Core.Store.UserDirectory;
            var path = Path.Combine(directory, "startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, error.ToString());
            if (OperatingSystem.IsWindows() && !args.Contains("--ui-smoke")) Services.DesktopNative.ShowStartupError(error.Message);
            return 1;
        }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
