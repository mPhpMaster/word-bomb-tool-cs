// Command WordBombGUI is the desktop Word Bomb Tool: global hotkeys,
// screen-region OCR, auto-typing, overlays and a system-tray icon. WPF port of
// main.py / cmd/wordbombgui.
using System.Windows;
using System.Windows.Threading;

namespace WordBombTool;

public partial class App : System.Windows.Application
{
    // Rolling burst counter for DispatcherUnhandledException; see the handler below.
    private const int UiFaultBurstLimit = 10;
    private DateTime _lastUiFault = DateTime.MinValue;
    private int _uiFaultBurst;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Errorf("Unhandled UI exception: {0}\n{1}", args.Exception.Message, args.Exception.StackTrace);

            // Staying alive on a one-off UI fault is deliberate (mirrors the Go port's
            // panic-recovery philosophy). But swallowing *every* fault unconditionally
            // hid the case that actually matters: something on a repeating timer -- e.g.
            // the 100ms log DrainTick -- throwing every tick. The window silently stops
            // updating while the log file fills with identical traces. Detect that storm
            // and tell the user once, instead of degrading in silence.
            var now = DateTime.UtcNow;
            if (now - _lastUiFault > TimeSpan.FromSeconds(5)) _uiFaultBurst = 0;
            _lastUiFault = now;
            _uiFaultBurst++;

            if (_uiFaultBurst == UiFaultBurstLimit)
            {
                AppLog.Errorf("UI exception storm: {0} faults in <5s, surfacing to user", _uiFaultBurst);
                System.Windows.MessageBox.Show(
                    $"The interface is repeatedly failing and may have stopped updating.\n\n" +
                    $"{args.Exception.GetType().Name}: {args.Exception.Message}\n\n" +
                    $"Details are in {AppConfig.LogFile}",
                    "Word Bomb Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            args.Handled = true;
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
