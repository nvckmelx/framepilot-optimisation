using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FramePilot.App.Views;

/// <summary>Asks DWM for a dark title bar (Windows 11). Purely cosmetic; failures are ignored.</summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            var color = 0x00121410; // COLORREF 0x00BBGGRR for #101412
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
        }
        catch
        {
            // older Windows builds: keep the default title bar
        }
    }
}
