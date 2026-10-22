using System.Runtime.InteropServices;

namespace PressureMap.Services;

/// <summary>桌面玻璃组件的窗口圆角：优先交给 DWM 裁切（带抗锯齿），不支持时退回 GDI 区域。</summary>
internal static class DesktopGlassNative
{
    private const int CornerAttribute = 33, CornerRound = 2;
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    /// <summary>true 表示圆角由 DWM 处理（抗锯齿）；false 表示退回 GDI 区域或未处理。</summary>
    public static bool Round(nint window, int width, int height, int radius)
    {
        if (!OperatingSystem.IsWindows() || width < 1 || height < 1) return false;
        var preference = CornerRound;
        if (DwmSetWindowAttribute(window, CornerAttribute, ref preference, sizeof(int)) == 0)
        {
            // DWM 接管圆角后必须清掉旧区域，否则区域会先把抗锯齿边缘切掉。
            SetWindowRgn(window, 0, true);
            return true;
        }
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == 0) return false;
        // On success Windows owns the region and releases it when replaced.
        if (SetWindowRgn(window, region, true) != 0) return false;
        DeleteObject(region);
        return false;
    }
}
