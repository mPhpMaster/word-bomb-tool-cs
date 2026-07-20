// The main application window: a menu plus a scrolling, colored log view, with
// a system-tray icon. Port of ui_manager.LogDisplay and tray_manager.TrayIcon
// (ui/logwindow_windows.go in the Go port).
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WordBombTool.Views;

public partial class MainWindow : Window
{
    private readonly LogQueue _queue;
    private readonly Callbacks _cb;
    private readonly Action<bool>? _onVisibility;
    private Forms.NotifyIcon? _tray;
    private DispatcherTimer? _drainTimer;
    private int _tick;
    private bool _closingForReal;

    public MainWindow(LogQueue queue, Callbacks cb, Action<bool>? onVisibility)
    {
        _queue = queue;
        _cb = cb;
        _onVisibility = onVisibility;
        InitializeComponent();

        BuildOptionsMenu();
        SetupTray();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32Interop.SetTopmost(hwnd);
            Win32Interop.SetWindowAlpha(hwnd, (byte)(Theme.UnfocusedAlpha * 255));
        };

        Activated += (_, _) => Win32Interop.SetWindowAlpha(new WindowInteropHelper(this).Handle, (byte)(Theme.FocusedAlpha * 255));
        Deactivated += (_, _) => Win32Interop.SetWindowAlpha(new WindowInteropHelper(this).Handle, (byte)(Theme.UnfocusedAlpha * 255));

        Closing += (_, e) =>
        {
            if (_closingForReal) return;
            e.Cancel = true;
            Task.Run(_cb.Exit);
        };

        _drainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _drainTimer.Tick += DrainTick;
        _drainTimer.Start();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Task.Run(_cb.Exit);
    private void OnShowHelpClick(object sender, RoutedEventArgs e) => Task.Run(_cb.ShowHelp);

    private void BuildOptionsMenu()
    {
        void Add(ItemCollection items, string header, string? gesture, Action? click)
        {
            var mi = new MenuItem { Header = header, Foreground = Win32Interop.ParseHexBrush(Theme.Fg) };
            if (gesture != null) mi.InputGestureText = gesture;
            if (click != null) mi.Click += (_, _) => click();
            else mi.IsEnabled = false;
            items.Add(mi);
        }

        OptionsMenu.Items.Clear();
        Add(OptionsMenu.Items, "Select Region", "Tab", _cb.SelectRegion);
        Add(OptionsMenu.Items, "Clear turn region", "Ctrl+F2", _cb.ClearTurnRegion);
        OptionsMenu.Items.Add(new Separator());

        var searchMenu = new MenuItem { Header = "Search Mode" };
        for (var i = 0; i < AppConfig.SearchModes.Length; i++)
        {
            var idx = i;
            var mi = new MenuItem { Header = AppConfig.SearchModes[i] };
            mi.Click += (_, _) => _cb.SetSearchMode(idx);
            searchMenu.Items.Add(mi);
        }
        OptionsMenu.Items.Add(searchMenu);

        var sortMenu = new MenuItem { Header = "Sort Mode" };
        for (var i = 0; i < AppConfig.SortModes.Length; i++)
        {
            var idx = i;
            var mi = new MenuItem { Header = AppConfig.SortModes[i] };
            mi.Click += (_, _) => _cb.SetSortMode(idx);
            sortMenu.Items.Add(mi);
        }
        OptionsMenu.Items.Add(sortMenu);

        Add(OptionsMenu.Items, "Typing delay...", null, _cb.SetTypingDelay);
        Add(OptionsMenu.Items, "OCR interval...", null, _cb.SetOCRInterval);
        OptionsMenu.Items.Add(new Separator());
        Add(OptionsMenu.Items, "Clear Typed History", "Delete", _cb.ClearHistory);
        Add(OptionsMenu.Items, "Undo Last Word", "Ctrl+Z", _cb.UndoWord);
    }

    private void SetupTray()
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri("Resources/appicon.ico", UriKind.Relative))?.Stream;
            var icon = stream != null ? new Icon(stream) : SystemIcons.Application;

            var tray = new Forms.NotifyIcon
            {
                Icon = icon,
                Text = "WBT",
                Visible = true,
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Select Region", null, (_, _) => _cb.SelectRegion());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Fetch Suggestions", null, (_, _) => _cb.FetchSuggestions());
            menu.Items.Add("Fetch Definitions", null, (_, _) => _cb.FetchDefinitions());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Toggle Window", null, (_, _) => _cb.ToggleWindow());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => _cb.Exit());
            tray.ContextMenuStrip = menu;

            tray.MouseUp += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) _cb.ToggleWindow();
            };

            _tray = tray;
        }
        catch (Exception ex)
        {
            AppLog.Warnf("Tray icon disabled: {0}", ex.Message);
        }
    }

    // Keep the visible log bounded to roughly the same size as the underlying
    // LogQueue (AppConfig.MaxLogQueueSize). Without this, every drained entry
    // becomes a permanent Paragraph in the RichTextBox's FlowDocument -- over a
    // long play session that grows without limit and eventually makes the
    // window slow to render and scroll.
    private const int MaxVisibleLogBlocks = AppConfig.MaxLogQueueSize;

    private void DrainTick(object? sender, EventArgs e)
    {
        _tick++;
        if (_queue.HasMessages())
        {
            var entries = _queue.PopAll();
            var doc = LogBox.Document;
            foreach (var entry in entries)
            {
                var para = new Paragraph(new Run(entry.Message) { Foreground = Win32Interop.ParseHexBrush(entry.Color) });
                doc.Blocks.Add(para);
            }

            var overflow = doc.Blocks.Count - MaxVisibleLogBlocks;
            if (overflow > 0)
            {
                var toRemove = doc.Blocks.Take(overflow).ToList();
                foreach (var block in toRemove) doc.Blocks.Remove(block);
            }

            LogBox.ScrollToEnd();
        }

        // Re-assert topmost about every 2 seconds (only while visible) so the
        // window can't fall behind the game.
        if (_tick % 20 == 0 && IsVisible)
        {
            Win32Interop.SetTopmost(new WindowInteropHelper(this).Handle);
        }
    }

    public void ToggleVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible)
            {
                Hide();
                _onVisibility?.Invoke(false);
            }
            else
            {
                Show();
                Activate();
                _onVisibility?.Invoke(true);
            }
        });
    }

    /// <summary>Runs f on the UI thread (mirrors walk's Synchronize).</summary>
    public void Synchronize(Action f) => Dispatcher.Invoke(f);

    public void DisposeAll()
    {
        _drainTimer?.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _closingForReal = true;
        Dispatcher.Invoke(Close);
    }
}
