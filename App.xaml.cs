using System.IO;
using System.Windows;
using SkyrimVersionManager.Services;

namespace SkyrimVersionManager;

public partial class App : Application
{
    public App()
    {
        // Without these, any unhandled exception silently kills the app - useless for
        // remote troubleshooting. Log the details and keep the UI alive where possible.
        DispatcherUnhandledException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            MessageBox.Show(
                "An unexpected error occurred:\n\n" + e.Exception.Message +
                "\n\nDetails were written to the log file:\n" + Paths.LogFile,
                "Skyrim Version Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception);
    }

    private static void WriteCrashLog(Exception? ex)
    {
        try
        {
            File.AppendAllText(Paths.LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED ERROR: {ex}\r\n");
        }
        catch
        {
            // Logging must never crash the crash handler.
        }
    }
}
