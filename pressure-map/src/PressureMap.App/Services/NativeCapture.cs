using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PressureMap.Services;

/// <summary>仅供 --ui-smoke 捕获本应用自己的 HWND，验证真实桌面渲染。</summary>
internal static class NativeCapture
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint value);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint count, byte[] buffer, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    public static void Save(nint handle, string file, bool composited = false)
    {
        GetWindowRect(handle, out var rect); var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var sourceWindow = composited ? 0 : handle;
        var dc = GetDC(sourceWindow); var memory = CreateCompatibleDC(dc); var bitmap = CreateCompatibleBitmap(dc, width, height); var original = SelectObject(memory, bitmap);
        try
        {
            var captured = composited ? BitBlt(memory, 0, 0, width, height, dc, rect.Left, rect.Top, 0x40CC0020) : PrintWindow(handle, memory, 2);
            if (!captured) throw new InvalidOperationException("无法捕获应用窗口。");
            SelectObject(memory, original);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            var bytes = new byte[width * height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)height, bytes, ref info, 0) == 0) throw new InvalidOperationException("无法读取窗口截图。");
            using var target = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var frame = target.Lock())
                for (var row = 0; row < height; row++) Marshal.Copy(bytes, row * width * 4, frame.Address + row * frame.RowBytes, width * 4);
            target.Save(file, new PngBitmapEncoderOptions());
        }
        finally { SelectObject(memory, original); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(sourceWindow, dc); }
    }
}
