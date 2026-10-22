using Avalonia.Controls;
using System.Text.Json.Nodes;

namespace PressureMap.Views;

public sealed record OnlineLoginInput(string Username, string Password, bool SaveAccount, bool AutoLogin, bool BrowserLogin = false);
public static class OnlineLoginDialog
{
    public static async Task<OnlineLoginInput?> Show(Window owner, string platformName, JsonObject saved, JsonObject options, Func<OnlineLoginInput, Task<string?>>? authenticate = null, Action? cancelLogin = null)
    {
        var username = Ui.Input(saved["username"]?.GetValue<string>() ?? "");
        var password = Ui.Input(saved["password"]?.GetValue<string>() ?? ""); password.PasswordChar = '●';
        var showPassword = new Button
        {
            Content = new PathIcon { Width = 18, Height = 18, Data = Avalonia.Media.Geometry.Parse("M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5 M12,17A5,5 0 1,1 12,7A5,5 0 1,1 12,17 M12,9A3,3 0 1,0 12,15A3,3 0 1,0 12,9") },
            MinWidth = 32, MinHeight = 30, Padding = new Avalonia.Thickness(6), Margin = new Avalonia.Thickness(4, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        showPassword.Classes.Add("link");
        Avalonia.Automation.AutomationProperties.SetName(showPassword, "显示密码");
        ToolTip.SetTip(showPassword, "显示密码");
        showPassword.Click += (_, _) =>
        {
            password.RevealPassword = !password.RevealPassword;
            var label = password.RevealPassword ? "隐藏密码" : "显示密码";
            Avalonia.Automation.AutomationProperties.SetName(showPassword, label);
            ToolTip.SetTip(showPassword, label);
        };
        password.InnerRightContent = showPassword;
        var passwordField = Ui.Field("密码", password);
        var save = new CheckBox { Content = "保存账号信息", IsChecked = options["saveAccount"]?.GetValue<bool>() ?? true };
        var auto = new CheckBox { Content = "自动登录", IsChecked = options["autoLogin"]?.GetValue<bool>() ?? true };
        var error = Ui.Text("", "danger"); error.IsVisible = false;
        var panel = Ui.Stack(Ui.Text(platformName, "h2"), Ui.Field("账号 / 手机号", username), passwordField, save, error);
        var dialog = Dialogs.CreateWindow("登录账号", panel);
        var webLogin = Ui.Link("网页登录", () => dialog.Close(new OnlineLoginInput("", "", save.IsChecked == true, auto.IsChecked == true, true)));
        webLogin.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        panel.Children.Insert(4, Ui.Columns("*,Auto", auto, webLogin));
        var busy = false;
        var login = Ui.Button("登录", () => { }, "accent");
        login.Click += async (_, _) =>
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(username.Text) || string.IsNullOrEmpty(password.Text)) { error.Text = "请输入账号和密码。"; error.IsVisible = true; return; }
            var input = new OnlineLoginInput(username.Text.Trim(), password.Text, save.IsChecked == true, auto.IsChecked == true);
            busy = true; login.IsEnabled = false; login.Content = "正在登录…"; error.IsVisible = false;
            username.IsEnabled = password.IsEnabled = save.IsEnabled = auto.IsEnabled = webLogin.IsEnabled = false;
            try
            {
                var failure = authenticate is null ? null : await authenticate(input);
                if (failure is null) dialog.Close(input);
                else { error.Text = failure; error.IsVisible = true; }
            }
            catch (Exception exception) { error.Text = exception.Message; error.IsVisible = true; }
            finally
            {
                busy = false; login.IsEnabled = true; login.Content = "登录";
                username.IsEnabled = password.IsEnabled = save.IsEnabled = auto.IsEnabled = webLogin.IsEnabled = true;
            }
        };
        panel.Children.Add(login);
        dialog.Closing += (_, _) => { if (busy) cancelLogin?.Invoke(); };
        panel.Children.Add(Ui.Button("取消", () => dialog.Close()));
        dialog.Opened += (_, _) => username.Focus();
        return await dialog.ShowDialog<OnlineLoginInput?>(owner);
    }
}

