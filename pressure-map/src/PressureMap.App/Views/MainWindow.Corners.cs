using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace PressureMap.Views;

public partial class MainWindow
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private void ApplyRoundedCorners() => ApplyRoundedCorners(this);
    internal static void ApplyRoundedCorners(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { } handle) return;
        // Let DWM preserve native shadows and resize borders on Windows 11.
        var preference = window.WindowState == WindowState.Maximized ? 1 : 2;
        if (DwmSetWindowAttribute(handle.Handle, 33, ref preference, sizeof(int)) == 0) return;
        // Older Windows versions need a real native region, not just rounded content.
        if (window.WindowState != WindowState.Normal) { SetWindowRgn(handle.Handle, 0, true); return; }
        if (!GetWindowRect(handle.Handle, out var rect)) return;
        var diameter = (int)Math.Round(24 * window.RenderScaling);
        var region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1, diameter, diameter);
        if (region != 0 && SetWindowRgn(handle.Handle, region, true) == 0) DeleteObject(region);
    }
}
