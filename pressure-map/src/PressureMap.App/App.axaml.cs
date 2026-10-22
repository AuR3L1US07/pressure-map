using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using PressureMap.Core;
using PressureMap.Views;

namespace PressureMap;

public partial class App : Application
{
    private Mutex? mutex;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Resources["SystemAccentColor"] = Color.Parse("#5B4FCF");
        Resources["SystemAccentColorLight1"] = Color.Parse("#7468E0");
        Resources["SystemAccentColorDark1"] = Color.Parse("#4338A3");
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var smoke = Program.Arguments.Contains("--ui-smoke");
            var dataIndex = Array.IndexOf(Program.Arguments, "--data");
            var data = dataIndex >= 0 && dataIndex + 1 < Program.Arguments.Length ? Program.Arguments[dataIndex + 1] : Store.UserDirectory;
            if (!smoke)
            {
                var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data)))[..24];
                mutex = new Mutex(true, "Local\\PressureMap-Avalonia-" + key, out var created);
                if (!created) { Services.DesktopNative.RestoreMain(); desktop.Shutdown(); return; }
            }
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            var main = new MainWindow(new Store(data));
            desktop.MainWindow = main;
            desktop.Exit += (_, _) => { main.Cleanup(); mutex?.Dispose(); };
            var initialized = false;
            main.Opened += async (_, _) =>
            {
                if (initialized) return;
                initialized = true;
                await main.PlayStartupAsync();
                if (smoke) await main.RunSmokeAsync();
                else { await main.PromptLoginAtFirstRunAsync(); if (main.Preferences.DesktopEnabled) main.ShowDesktop(); if (main.Preferences.ScheduleEnabled) main.ShowScheduleDesktop(); if (main.Preferences.PressureEnabled) main.ShowPressureDesktop(); main.StartTaskServices(); await main.ScanOnlineCoursesAtStartupAsync(); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}


