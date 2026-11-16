using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ShaderCacheCleaner.Helpers;

public static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // Fond app #0B0B0B en COLORREF (0x00BBGGRR)
    private const uint CaptionColorRef = 0x000B0B0B;
    private const uint BorderColorRef = 0x002A2A2A;
    private const uint TextColorRef = 0x00FFFFFF;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    public static readonly SolidColorBrush WindowBackgroundBrush =
        CreateFrozenBrush(0x0B, 0x0B, 0x0B);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static void Apply(Window window)
    {
        window.Background = WindowBackgroundBrush;

        void ApplyNow() => ApplyImmediate(window);

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyNow();
        else
            window.SourceInitialized += (_, _) => ApplyNow();

        window.Loaded += (_, _) => ApplyImmediate(window);
    }

    public static void ApplyImmediate(Window window)
    {
        window.Background = WindowBackgroundBrush;

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
            return;

        int useDark = 1;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));

        uint caption = CaptionColorRef;
        uint border = BorderColorRef;
        uint text = TextColorRef;
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(uint));
    }
}
