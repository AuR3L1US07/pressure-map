using Avalonia.Automation;
using Avalonia.Controls;
using PressureMap.Localization;
using PressureMap.Services;

namespace PressureMap.Views;

public partial class MainWindow
{
    private Control AiApiSettingsCard()
    {
        string T(string zh, string en) => Loc.IsEnglish ? en : zh;
        var config = new AiApiConfiguration();
        var loadFailed = false;
        try { config = AiApiConfiguration.Load(LocalStore.DirectoryPath); }
        catch { loadFailed = true; }
        var endpoint = Ui.Input(config.BaseUrl, "https://api.example.com/v1");
        var model = Ui.Input(config.Model, "model-name");
        var key = Ui.Input(config.ApiKey); key.PasswordChar = '●';
        var status = Ui.Text(loadFailed
            ? T("无法读取已保存的配置，请重新填写后保存。", "Could not read saved configuration. Enter it again and save.")
            : config.IsValid ? T("已保存到本机", "Saved on this device") : T("正在使用内置 DeepSeek 服务，无需填写。", "Using the built-in DeepSeek service. No configuration needed."), "caption");
        status.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        foreach (var (field, label) in new[] { (endpoint, T("接口地址（Base URL）", "Base URL")), (model, T("模型名称", "Model")), (key, "API Key") })
            AutomationProperties.SetName(field, label);
        foreach (var field in new[] { endpoint, model, key })
            field.TextChanged += (_, _) => status.Text = T("有未保存的修改", "Unsaved changes");
        var show = Ui.Switch(T("显示密钥", "Show API key"), false);
        show.IsCheckedChanged += (_, _) => key.PasswordChar = show.IsChecked == true ? '\0' : '●';
        var save = Ui.Button(T("保存配置", "Save configuration"), () =>
        {
            var candidate = new AiApiConfiguration((endpoint.Text ?? "").Trim().TrimEnd('/'), (model.Text ?? "").Trim(), (key.Text ?? "").Trim());
            if (!candidate.IsValid)
            {
                status.Text = T("请填写完整的接口地址、模型名称和密钥。地址须使用 HTTPS（本机接口可用 HTTP），且不含账号、查询参数或片段。", "Enter a complete URL, model and API key. Use HTTPS (HTTP is allowed for localhost), without credentials, query parameters or fragments.");
                return;
            }
            try { candidate.Save(LocalStore.DirectoryPath); status.Text = T("已保存到本机", "Saved on this device"); }
            catch { status.Text = T("保存失败，请检查本地目录权限后重试。", "Save failed. Check local folder permissions and try again."); }
        }, "accent");
        var clear = Ui.AsyncButton(T("清除配置", "Clear configuration"), async () =>
        {
            if (!await Dialogs.Confirm(this, T("清除 AI API 配置", "Clear AI API configuration"), T("清除本机保存的接口地址、模型名称和 API Key？", "Clear the URL, model and API key saved on this device?"))) return;
            try
            {
                AiApiConfiguration.Clear(LocalStore.DirectoryPath);
                endpoint.Text = model.Text = key.Text = ""; show.IsChecked = false;
                status.Text = T("已恢复内置 DeepSeek 服务", "Built-in DeepSeek service restored");
            }
            catch { status.Text = T("清除失败，请检查本地目录权限后重试。", "Clear failed. Check local folder permissions and try again."); }
        }, "ghost");
        return SettingsCard(T("AI API 配置", "AI API configuration"),
            Ui.Text(T("默认使用内置 DeepSeek 服务。以下为可选的自定义接口。", "Uses the built-in DeepSeek service by default. Custom API settings below are optional."), "caption"),
            Ui.Field(T("接口地址（Base URL）", "Base URL"), endpoint),
            Ui.Field(T("模型名称", "Model"), model), Ui.Field("API Key", key), show,
            Ui.Text(T("密钥在本机加密保存，不参与云同步。", "The key is encrypted on this device and excluded from cloud sync."), "caption"),
            Ui.Actions(save, clear), status);
    }
}
