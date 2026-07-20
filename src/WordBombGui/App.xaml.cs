// Command WordBombGUI is the desktop Word Bomb Tool: global hotkeys,
// screen-region OCR, auto-typing, overlays and a system-tray icon. WPF port of
// main.py / cmd/wordbombgui.
using System.Windows;
using System.Windows.Threading;

namespace WordBombTool;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Errorf("Unhandled UI exception: {0}\n{1}", args.Exception.Message, args.Exception.StackTrace);
            args.Handled = true; // keep the app alive; mirrors the Go port's panic-recovery philosophy
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppLog.Errorf("Unhandled fatal exception: {0}", args.ExceptionObject);
        };

        try
        {
            new AppController().Run();
        }
        catch (Exception ex)
        {
            AppLog.Errorf("Fatal error: {0}\n{1}", ex.Message, ex.StackTrace);
            System.Windows.MessageBox.Show($"FATAL ERROR: {ex.Message}", "Word Bomb Tool", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
