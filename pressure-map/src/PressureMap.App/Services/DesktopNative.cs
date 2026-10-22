using System.Runtime.InteropServices;
using System.Text;

namespace PressureMap.Services;

/// <summary>关联 Explorer 已有的 SHELLDLL_DefView 所有者，并保持在普通应用之下。</summary>
public static class DesktopNative
{
    private const int ExStyle = -20, Owner = -8;
    private const long ToolWindow = 0x80, AppWindow = 0x40000, Topmost = 8, NoActivate = 0x08000000;
    private const uint NoSize = 1, NoMove = 2, NoZOrder = 4, NoActivation = 0x10, FrameChanged = 0x20;
    private delegate bool EnumCallback(nint handle, nint value);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint value);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string title);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")] private static extern nint GetLong(nint handle, int index);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")] private static extern nint SetLong(nint handle, int index, nint value);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder name, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint handle, string text, string title, uint flags);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    public static bool ReducedMotion => OperatingSystem.IsWindows() && SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled == 0;
    private static uint Pid(nint handle) { GetWindowThreadProcessId(handle, out var pid); return pid; }
    public static nint FindHost()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var shell = GetShellWindow(); if (shell == 0) return 0;
        var pid = Pid(shell); nint host = 0;
        EnumWindows((handle, _) =>
        {
            if (Pid(handle) != pid) return true;
            var child = FindWindowEx(handle, 0, "SHELLDLL_DefView", null);
            if (child == 0) return true; host = child; return false;
        }, 0);
        return host;
    }
    public static nint Attach(nint handle)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("桌面待办需要 Windows Explorer 环境。");
        var host = FindHost(); if (host == 0) throw new InvalidOperationException("Explorer 桌面正在加载，请稍后重新显示待办。");
        SetEditing(handle, false);
        if (GetLong(handle, Owner) != host) SetLong(handle, Owner, host);
        if (GetLong(handle, Owner) != host) throw new InvalidOperationException("无法接入 Explorer 桌面层，请用普通权限运行应用。");
        Bed(handle); return host;
    }
    public static void SetEditing(nint handle, bool editing)
    {
        if (!OperatingSystem.IsWindows() || handle == 0 || !IsWindow(handle)) return;
        long current = GetLong(handle, ExStyle);
        var desired = (current | ToolWindow) & ~(AppWindow | Topmost);
        desired = editing ? desired & ~NoActivate : desired | NoActivate;
        if (current != desired) { SetLong(handle, ExStyle, (nint)desired); SetWindowPos(handle, 0, 0, 0, 0, 0, NoSize | NoMove | NoZOrder | NoActivation | FrameChanged); }
        if (editing) { SetForegroundWindow(handle); Bed(handle); }
    }
    private static readonly Dictionary<nint,bool> originallyLayered = [];
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    public static bool IsClickThrough(nint handle) => handle != 0 && ((long)GetLong(handle,ExStyle) & 0x20) != 0;
    public static void SetClickThrough(nint handle, bool enabled)
    {
        if (!OperatingSystem.IsWindows() || handle == 0 || !IsWindow(handle)) return;
        var style = (long)GetLong(handle,ExStyle);
        if (enabled)
        {
            originallyLayered.TryAdd(handle,(style & 0x80000) != 0);
            SetLong(handle,ExStyle,(nint)(style | 0x80020)); SetLayeredWindowAttributes(handle,0,255,2);
        }
        else
        {
            style &= ~0x20;
            if (originallyLayered.Remove(handle,out var wasLayered) && !wasLayered) style &= ~0x80000;
            SetLong(handle,ExStyle,(nint)style);
        }
        SetWindowPos(handle,0,0,0,0,0,NoSize | NoMove | NoZOrder | NoActivation | FrameChanged);
    }
    public static bool Bedded(nint handle)
    {
        var cursor = GetWindow(handle, 2);
        for (var i = 0; i < 256 && cursor != 0; i++)
        {
            var name = new StringBuilder(128); GetClassName(cursor, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView") return true;
            DwmGetWindowAttribute(cursor, 14, out var cloaked, sizeof(int));
            if (IsWindowVisible(cursor) && !IsIconic(cursor) && cloaked == 0 && Pid(cursor) != Environment.ProcessId) return false;
            cursor = GetWindow(cursor, 2);
        }
        return cursor == 0;
    }
    public static void Bed(nint handle)
    {
        if (((long)GetLong(handle, ExStyle) & Topmost) != 0) SetWindowPos(handle, -2, 0, 0, 0, 0, NoSize | NoMove | NoActivation);
        if (!Bedded(handle) && !SetWindowPos(handle, 1, 0, 0, 0, 0, NoSize | NoMove | NoActivation)) throw new InvalidOperationException("无法保持桌面层级，请重新连接桌面。");
    }
    public static void Recover(nint handle)
    {
        if (!IsWindow(handle)) throw new InvalidOperationException("桌面已重建，请重新显示待办。");
        var host = FindHost(); if (host == 0) throw new InvalidOperationException("Explorer 桌面正在恢复，请稍候。");
        if (GetLong(handle, Owner) != host) Attach(handle);
        if (!IsWindowVisible(handle) || IsIconic(handle)) ShowWindow(handle, 4);
        Bed(handle);
    }
    public static void Detach(nint handle) { if (OperatingSystem.IsWindows() && IsWindow(handle)) SetLong(handle, Owner, 0); }
    public static object Inspect(nint handle)
    {
        var host = FindHost(); var owner = GetLong(handle, Owner); var style = (long)GetLong(handle, ExStyle);
        if (host == 0 || owner != host || (style & ToolWindow) == 0 || (style & AppWindow) != 0 || (style & Topmost) != 0 || !Bedded(handle)) throw new InvalidOperationException("桌面层验证失败。");
        ShowWindow(handle, 6); Recover(handle);
        if (IsIconic(handle)) throw new InvalidOperationException("最小化恢复失败。");
        SetLong(handle, Owner, 0); Recover(handle);
        if (GetLong(handle, Owner) != host) throw new InvalidOperationException("桌面关联恢复失败。");
        return new { host = (long)host, owner = (long)owner, toolwindow = true, appwindow = false, topmost = false, bedded = true, minimize_recovery = true, owner_recovery = true };
    }
    public static void RestoreMain() { if (!OperatingSystem.IsWindows()) return; var handle = FindWindow(null, "学期地图"); if (handle != 0) { ShowWindow(handle, 9); SetForegroundWindow(handle); } }
    public static void ShowStartupError(string message) => MessageBox(0, message, "学期地图启动失败", 0x10);
}
