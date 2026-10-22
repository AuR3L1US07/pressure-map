using PressureMap.Server;

if (args.Length > 0 && args[0] is "user-create" or "user-password" or "user-disable" or "backup")
{
    try
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
        var options = config.GetSection("Cloud").Get<CloudOptions>() ?? new(); var repository = new ServerRepository(options);
        string Arg(string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ApiValidationException("缺少参数 " + name); }
        string Password() {
            Console.Error.Write("密码（至少 12 字符，输入隐藏）：");
            if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
            var value = new System.Text.StringBuilder();
            while (true) { var key = Console.ReadKey(true); if (key.Key == ConsoleKey.Enter) break; if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; } else if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar); }
            Console.Error.WriteLine(); return value.ToString();
        }
        switch (args[0]) {
            case "user-create": repository.CreateUser(Arg("--account"), Arg("--name"), Password()); break;
            case "user-password": repository.ResetPassword(Arg("--account"), Password()); break;
            case "user-disable": repository.DisableUser(Arg("--account")); break;
            case "backup": repository.Backup(Arg("--output")); break;
        }
        Console.WriteLine("操作完成。");
    }
    catch (Exception e) when (e is ApiValidationException or IOException) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
}
else await ServerApplication.Build(args).RunAsync();
