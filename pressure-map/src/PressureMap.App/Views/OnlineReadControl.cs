using Avalonia.Controls;
using Avalonia.Media;

namespace PressureMap.Views;

public sealed class OnlineReadControl : StackPanel
{
    public Button ReadButton { get; }
    public TextBlock Feedback { get; } = Ui.Text("");
    public bool IsRunning { get; private set; }
    public Func<Task>? ReadRequested { get; set; }
    public Action? CancelRequested { get; set; }
    public OnlineReadControl()
    {
        Spacing = 12;
        Feedback.TextWrapping = TextWrapping.Wrap; Feedback.IsVisible = false; Feedback.FontSize = 13;
        ReadButton = Ui.Button("读取课程任务", () => { }, "accent");
        ReadButton.Click += async (_, _) =>
        {
            if (IsRunning) { ReadButton.IsEnabled = false; ShowFeedback("正在取消读取…"); CancelRequested?.Invoke(); }
            else if (ReadRequested is not null) await ReadRequested();
        };
        Children.Add(ReadButton); Children.Add(Feedback);
    }
    public void ArrangeToolbar(Control platform, Control login)
    {
        Children.Clear();
        ReadButton.MinWidth = 150; login.MinWidth = 100;
        Children.Add(Ui.Actions(platform, login, ReadButton)); Children.Add(Feedback);
    }
    public void ShowFeedback(string message, bool error = false)
    {
        Feedback.Text = message; Feedback.IsVisible = !string.IsNullOrWhiteSpace(message);
        Feedback.Classes.Set("danger", error);
    }
    public void SetExternalBusy(bool busy, string message)
    {
        IsRunning = busy; ReadButton.Content = busy ? "取消读取" : "读取课程任务";
        if (busy) ShowFeedback(message);
    }
    public async Task ExecuteAsync(Func<Action<string>, Task> operation, Action<bool> busyChanged)
    {
        if (IsRunning) return;
        IsRunning = true; ReadButton.Content = "取消读取"; ReadButton.IsEnabled = true;
        ShowFeedback("正在连接平台，检查登录状态…"); busyChanged(true);
        var failed = false;
        try { await operation(text => ShowFeedback(text)); }
        catch (Exception error) { failed = true; ShowFeedback(error.Message, true); }
        finally
        {
            IsRunning = false; ReadButton.IsEnabled = true;
            ReadButton.Content = failed ? "重试读取" : "读取课程任务";
            busyChanged(false);
        }
    }
}

