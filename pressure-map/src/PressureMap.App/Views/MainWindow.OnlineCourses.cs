using System.Text.Json.Nodes;
using Avalonia.Controls;
using PressureMap.Services;

namespace PressureMap.Views;

public partial class MainWindow
{
    private OnlineCourseService? onlineCourses;
    private string? onlineDirectory;
    private Control? onlinePage;
    private string? onlinePagePlatform;
    private string onlinePlatform = "chaoxing";
    private bool onlineStartupCancelled;
    private OnlineCourseService EnsureOnlineCourses()
    {
        if (onlineDirectory != Store.DirectoryPath)
        {
            onlineCourses?.Dispose(); onlineCourses = new(Store.DirectoryPath); onlineDirectory = Store.DirectoryPath; onlinePage = null;
        }
        return onlineCourses!;
    }
    public async Task ScanOnlineCoursesAtStartupAsync()
    {
        if (!Preferences.ScanOnlineCoursesOnStartup) return;
        var service = EnsureOnlineCourses();
        var directory = Store.DirectoryPath;
        void Refresh() { onlinePage = null; if (currentPage == 9 && Store.DirectoryPath == directory) SelectPage(9); }
        foreach (var platform in new[] { "chaoxing", "mooc" })
        {
            if (onlineStartupCancelled || Store.DirectoryPath != directory || exiting) break;
            if (service.Busy || !service.HasSavedLogin(platform)) continue;
            try { await service.RunAsync(platform, "scan", _ => Refresh(), Refresh); }
            catch (Exception error) { Error(error); }
            finally { Refresh(); }
        }
    }
    private Control OnlineCoursesPage()
    {
        var service = EnsureOnlineCourses();
        if (onlinePage is not null && onlinePagePlatform == onlinePlatform) return onlinePage;

        var platforms = new ComboBox { ItemsSource = new[] { PressureMap.Localization.Loc.T("学习通"), PressureMap.Localization.Loc.T("中国大学 MOOC") }, SelectedIndex = onlinePlatform == "mooc" ? 1 : 0, MinWidth = 180, IsEnabled = !service.Busy };
        platforms.SelectionChanged += (_, _) => { onlinePlatform = platforms.SelectedIndex == 1 ? "mooc" : "chaoxing"; SelectPage(9); };
        var platform = onlinePlatform;
        var table = new OnlineCourseTable(platform);
        void RenderCourses() => table.Load(service.Courses(platform));
        RenderCourses();
        var scanActions = new OnlineReadControl();
        scanActions.CancelRequested = () => { onlineStartupCancelled = true; service.Stop(); };
        async Task Run(string action, JsonObject? credentials = null)
        {
            if (service.Busy) { scanActions.ShowFeedback("已有读取操作正在进行，可取消读取或重新登录。", true); return; }
            await scanActions.ExecuteAsync(async update =>
                await service.RunAsync(platform, action, update, RenderCourses, credentials),
                busy => platforms.IsEnabled = !busy);
        }
        scanActions.ReadRequested = () => Run("scan");
        scanActions.SetExternalBusy(service.Busy, service.LastMessage);
        var openingLogin = false;
        var loginButton = Ui.Button("登录账号", async () =>
        {
            try
            {
                if (openingLogin) return;
                openingLogin = true;
                onlineStartupCancelled = true; await service.StopAndWaitAsync();
                var input = await OnlineLoginDialog.Show(this, platform == "mooc" ? "中国大学 MOOC" : "学习通", service.Account(platform), service.LoginOptions(platform), async input =>
                {
                    service.ConfigureLogin(platform, input.Username, input.Password, input.SaveAccount, input.AutoLogin);
                    var result = await service.RunAsync(platform, "authenticate", _ => { }, RenderCourses,
                        new JsonObject { ["username"] = input.Username, ["password"] = input.Password });
                    return result switch
                    {
                        "success" => null,
                        "invalid_credentials" => "账号或密码错误，请重新输入。",
                        "captcha" => "需要验证码，请使用右侧的网页登录完成验证。",
                        "timeout" => "登录超时，请检查网络后重试。",
                        "cancelled" => "登录已取消。",
                        _ => "登录未完成，请检查账号密码或稍后重试。"
                    };
                }, service.Stop);
                openingLogin = false;
                if (input is null) return;
                if (input.BrowserLogin)
                {
                    service.SetLoginOptions(platform, input.SaveAccount, input.AutoLogin);
                    await Run("manual"); return;
                }
                RenderCourses();
                await Run("scan");
            }
            catch (Exception error) { openingLogin = false; Error(error); }
        }, "segment");
        scanActions.ArrangeToolbar(platforms, loginButton);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,14,*") };
        Grid.SetRow(scanActions, 0); page.Children.Add(scanActions);
        Grid.SetRow(table, 2); page.Children.Add(table);
        onlinePage = new PageFrame(Ui.Text("网课助手", "h1"), page, false); onlinePagePlatform = platform; return onlinePage;
    }
}



