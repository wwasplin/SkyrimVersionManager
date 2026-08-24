using System.Windows;

namespace SkyrimVersionManager.Services;

public static class WindowTheming
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Asks DWM for a dark title bar so the window chrome matches the app theme.</summary>
    public static void EnableDarkTitleBar(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            int on = 1;
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        }
        catch
        {
            // Cosmetic only; older Windows builds just keep the light title bar.
        }
    }
}
