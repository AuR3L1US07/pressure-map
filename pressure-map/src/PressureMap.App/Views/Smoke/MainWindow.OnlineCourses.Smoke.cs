using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PressureMap.Services;

namespace PressureMap.Views;

public partial class MainWindow
{
    private async Task VerifyOnlineCoursesAsync(string output)
    {
        if (OnlineCourseService.TimeoutReason("scan", 90, 90) is null || OnlineCourseService.TimeoutReason("scan", 600, 1) is null || OnlineCourseService.TimeoutReason("scan", 20, 20) is not null)
            throw new Exception("课程读取超时保护无效。");
        if (Preferences.ScanOnlineCoursesOnStartup) throw new Exception("启动扫描应默认关闭。");
        SelectPage(3); await Task.Delay(200);
        var scanToggle = this.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "启动时自动扫描网课");
        scanToggle.IsChecked = true;
        if (!Preferences.Load(LocalStore.DirectoryPath).ScanOnlineCoursesOnStartup) throw new Exception("启动扫描开关未保存。");
        scanToggle.IsChecked = false;
        if (Preferences.Load(LocalStore.DirectoryPath).ScanOnlineCoursesOnStartup) throw new Exception("关闭启动扫描未保存。");
        SelectPage(9);
        var service = onlineCourses!;
        await Task.Delay(150);
        var readControl = this.GetVisualDescendants().OfType<OnlineReadControl>().Single();
        var failureText = "尚未登录或登录已失效，请点击登录账号后重试。";
        await readControl.ExecuteAsync(_ => Task.FromException(new InvalidOperationException(failureText)), _ => { });
        if (readControl.IsRunning || !readControl.ReadButton.IsEnabled || readControl.Feedback.Text != failureText || readControl.ReadButton.Content as string != "重试读取")
            throw new Exception("读取失败后界面未恢复或未显示原因。");
        await Task.Delay(150);
        Capture(Path.Combine(output, "online-read-failure.png"));
        var cancelled = new TaskCompletionSource();
        readControl.CancelRequested = () => cancelled.TrySetResult();
        var pendingRead = readControl.ExecuteAsync(async update => { update("正在读取课程列表…"); await cancelled.Task; update("已取消读取，已有记录保留。"); }, _ => { });
        if (readControl.ReadButton.Content as string != "取消读取") throw new Exception("读取中未提供取消入口。");
        readControl.ReadButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await pendingRead;
        if (readControl.IsRunning || !readControl.ReadButton.IsEnabled) throw new Exception("取消后读取按钮未恢复。");
        if (this.GetVisualDescendants().OfType<Button>().Any(b => new[] { "自动登录", "打开网页登录", "清除账号与登录状态", "停止" }.Contains(b.Content as string))) throw new Exception("网课主页面仍显示已移除按钮。");
        var dialogTask = OnlineLoginDialog.Show(this, "学习通", new JsonObject(), new JsonObject());
        await Task.Delay(150);
        var life = (Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
        var loginDialog = life.Windows.Single(w => w.Title == "登录账号");
        var checks = loginDialog.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Content as string != "显示密码").ToArray();
        if (checks.Length != 2 || checks.Any(c => c.IsChecked != true)) throw new Exception("登录勾选项应默认选中。");
        loginDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "登录").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (!loginDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "请输入账号和密码。" && t.IsVisible)) throw new Exception("登录表单未验证空账号密码。");
        CaptureWindow(loginDialog, Path.Combine(output, "online-login-dialog.png"));
        loginDialog.Close();
        if (await dialogTask is not null) throw new Exception("取消登录不应提交账号。");        var webTask = OnlineLoginDialog.Show(this, "中国大学 MOOC", new JsonObject(), new JsonObject());
        await Task.Delay(150);
        var webDialog = life.Windows.Single(w => w.Title == "登录账号");
        webDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "网页登录").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if ((await webTask)?.BrowserLogin != true) throw new Exception("网页登录入口未返回网页登录操作。");
        var attempts = 0;
        var retryTask = OnlineLoginDialog.Show(this, "学习通", new JsonObject { ["username"] = "test-user", ["password"] = "test-password" }, new JsonObject(), _ =>
        {
            attempts++; return Task.FromResult<string?>("账号或密码错误，请重新输入。");
        });
        await Task.Delay(150);
        var retryDialog = life.Windows.Single(w => w.Title == "登录账号");
        var retryButton = retryDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "登录");
        retryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100);
        if (retryTask.IsCompleted || !retryButton.IsEnabled || !retryDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "账号或密码错误，请重新输入。")) throw new Exception("错误密码未保留弹窗并提示重试。");
        CaptureWindow(retryDialog, Path.Combine(output, "online-login-error.png"));
        retryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (attempts != 2) throw new Exception("错误密码后无法重新登录。");
        retryDialog.Close(); await retryTask;
        service.SaveAccount("chaoxing", "fixture-user", "fixture-password");
        await ScanOnlineCoursesAtStartupAsync();
        if (service.Busy || service.Courses("chaoxing").Count != 0) throw new Exception("默认关闭时启动仍扫描课程。");
        var secret = Path.Combine(Store.DirectoryPath, "online-courses", "chaoxing.account");
        if (System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(secret)).Contains("fixture-password")) throw new Exception("平台密码未加密。");
        if (service.Account("chaoxing")["password"]?.GetValue<string>() != "fixture-password") throw new Exception("平台密码解密失败。");
        if (service.Account("mooc")["username"] is not null) throw new Exception("平台账号未隔离。");
        var cacheFile = Path.Combine(Store.DirectoryPath, "online-courses", "chaoxing.courses.json");
        File.WriteAllText(cacheFile, new JsonObject {
            ["https://mooc1.chaoxing.com/course/fixture"] = new JsonObject {
                ["name"] = "高等数学 · 课程读取示例", ["counts"] = new JsonArray(12, 24),
                ["metrics"] = new JsonObject {
                    ["homework"] = new JsonObject { ["required"] = 3, ["done"] = 1 },
                    ["discussion"] = new JsonObject { ["required"] = null, ["done"] = null, ["error"] = "讨论要求尚未识别。" }
                }, ["updated"] = "2026-10-19 21:00"
            }
        }.ToJsonString());
        onlinePage = null; SelectPage(9); await Task.Delay(250);
        var tableView = this.GetVisualDescendants().OfType<OnlineCourseTable>().Single();
        if (tableView.Rows.Single().Percent != 50) throw new Exception("任务点完成度计算错误。");
        tableView.SelectCategory("homework");
        if (tableView.Rows.Single().Done != 1 || tableView.Rows.Single().Remaining != 2) throw new Exception("作业分类统计未显示。");
        tableView.SelectCategory("discussion");
        if (tableView.Rows.Single().Status != "待确认" || tableView.Rows.Single().Percent is not null) throw new Exception("未知进度被当作零。");
        var fixtureCourses = service.Courses("chaoxing");
        foreach (var (title, done, required) in new[] { ("网络空间安全导论",26,42), ("形势与政策 1",0,0), ("中国共产党党史",12,12), ("大学英语",3,18), ("计算机网络",10,16), ("概率论与数理统计",8,20), ("跨学科研究方法与学术写作 · Research Methods and Academic Writing",5,8) })
        {
            fixtureCourses["fixture-" + title] = new JsonObject {
                ["name"] = title, ["counts"] = new JsonArray(done,required),
                ["visits"] = new JsonObject { ["done"] = 165, ["required"] = 0 },
                ["updated"] = "2026-10-19 22:20"
            };
        }
        tableView.SelectCategory("primary"); tableView.Load(fixtureCourses); await Task.Delay(600);
        tableView.Table.SelectedIndex = 1;
        await Task.Delay(180);
        Capture(Path.Combine(output, "online-courses.png"));        var originalWidth = Width; var originalTheme = Preferences.Theme;
        Width = 1080; await Task.Delay(180); Capture(Path.Combine(output, "online-courses-compact.png"));
        Preferences.Theme = "Dark"; ApplyTheme(); await Task.Delay(180); Capture(Path.Combine(output, "online-courses-dark.png"));
        Width = originalWidth; Preferences.Theme = originalTheme; ApplyTheme(); await Task.Delay(180);
        var noRequirement = OnlineCourseRow.Read(fixtureCourses["fixture-形势与政策 1"]!, "visits", 1);
        if (noRequirement.Percent is not null || noRequirement.Status != "无此要求") throw new Exception("零要求不应计算完成率。");
        var originalPage = onlinePage; SelectPage(0); SelectPage(9);
        if (!ReferenceEquals(originalPage, onlinePage)) throw new Exception("网课页面切换丢失状态。");
        service.SaveAccount("chaoxing", "different-user", "other-password");
        if (service.Courses("chaoxing").Count != 0) throw new Exception("切换平台账号未清除旧课程。");
        service.ConfigureLogin("mooc", "temporary-user", "temporary-password", false, true);
        if (service.Account("mooc")["password"] is not null || File.Exists(Path.Combine(Store.DirectoryPath, "online-courses", "mooc.account"))) throw new Exception("取消保存后仍写入密码。");
        service.ConfigureLogin("mooc", "remember-user", "remember-password", true, false);
        if (service.HasSavedLogin("mooc") || service.AutoLogin("mooc")) throw new Exception("取消自动登录未生效。");
        if (service.Account("mooc")["username"]?.GetValue<string>() != "remember-user") throw new Exception("保存账号选项未生效。");
        service.Forget("mooc");
        service.Forget("chaoxing");
        if (File.Exists(secret)) throw new Exception("清除平台账号失败。");
        onlinePlatform = "mooc"; onlinePage = null; SelectPage(9); await Task.Delay(200);
        Capture(Path.Combine(output, "online-courses-mooc-empty.png"));
        onlinePlatform = "chaoxing"; onlinePage = null;
    }
}



