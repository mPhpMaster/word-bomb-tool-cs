// Wires the Word Bomb Tool GUI together: OCR, the Datamuse client, state,
// hotkeys, typing and the WPF UI. Port of app/app_windows.go (main.py).
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WordBombTool.Views;

namespace WordBombTool;

public sealed class AppController
{
    private readonly StateManager _state = new();
    private OcrProcessor _ocr = new();
    private readonly DatamuseClient _api = new();
    private readonly LogQueue _queue = new(AppConfig.MaxLogQueueSize);
    private readonly HotkeyHook _hook = new();
    private readonly SemaphoreSlim _sem = new(AppConfig.MaxWorkerThreads, AppConfig.MaxWorkerThreads);

    private MainWindow? _logWin;
    private RegionOverlayManager? _overlay;
    private int _autoWatcherReset; // 0/1 flag, single-writer/single-reader via Interlocked
    private CancellationTokenSource? _autoModeCts;

    // ---- logging ------------------------------------------------------------

    private void Log(string message, LogLevel level)
    {
        _queue.Add(message, level);
        switch (level)
        {
            case LogLevel.Error: AppLog.Errorf("{0}", message); break;
            case LogLevel.Warning: AppLog.Warnf("{0}", message); break;
            default: AppLog.Infof("{0}", message); break;
        }
    }

    private void Submit(Action fn)
    {
        _ = Task.Run(() =>
        {
            _sem.Wait();
            try { fn(); }
            catch (Exception ex)
            {
                Log($"[panic in worker]: {ex.Message}", LogLevel.Error);
                AppLog.Errorf("panic in worker: {0}\n{1}", ex.Message, ex.StackTrace);
            }
            finally { _sem.Release(); }
        });
    }

    /// <summary>Wraps a callback so an exception inside it is logged rather than
    /// crashing the hook thread.</summary>
    private Action Safe(string where, Action fn) => () =>
    {
        try { fn(); }
        catch (Exception ex)
        {
            Log($"[panic in {where}]: {ex.Message}", LogLevel.Error);
            AppLog.Errorf("panic in {0}: {1}\n{2}", where, ex.Message, ex.StackTrace);
        }
    };

    // ---- turn gate ------------------------------------------------------------

    private static bool TurnGateAccepts(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var hasYour = text.Contains(AppConfig.TurnGateNeedYour);
        var hasTurn = text.Contains(AppConfig.TurnGateNeedTurn);
        return (hasYour && hasTurn) || text.Contains("yourturn") || (hasYour && text.Length >= 4);
    }

    private (bool ok, string text) AutoModeTurnOK()
    {
        var s = _state.Snapshot();
        if (s.TurnRegion == null) return (true, "");
        var text = _ocr.PerformOCRTurnGate(s.TurnRegion);
        if (text == "") return (false, "");
        return (TurnGateAccepts(text), text);
    }

    // ---- display text -----------------------------------------------------

    private string StateText()
    {
        var s = _state.Snapshot();
        var mode = AppConfig.SearchModes[ClampIndex(s.CurrentModeIndex, AppConfig.SearchModes.Length)];
        var sortMode = AppConfig.SortModes[ClampIndex(s.CurrentSortModeIndex, AppConfig.SortModes.Length)];
        var tg = s.TurnRegion != null ? "On (turn region must OCR as YOUR TURN)" : "Off (no second region — auto types on any letter change)";
        var auto = s.AutoModeActive ? "On" : "Off";
        return "\n" +
            $"Current Mode: {mode}\n" +
            $"Current Sort: {sortMode}\n" +
            $"Typing delay: {s.TypingDelay:G}s (~avg between keys)\n" +
            $"OCR interval: {s.OCRInterval:G}s (auto mode poll)\n" +
            $"Auto mode only on your turn: {tg}\n" +
            $"Auto Mode: {auto}\n" +
            $"Words Typed: {s.TotalTypedCount}\n" +
            $"API Status: {s.APIStatus}\n";
    }

    private string HelpText() => StateText() + "\n" +
        "=====================================\n" +
        "HOTKEYS\n" +
        "=====================================\n" +
        "Fetch Suggestions:  SHIFT\n" +
        "Fetch Definitions:  Alt+1\n" +
        "Select Regions:     TAB — letters, then YOUR TURN box (Esc to skip second)\n" +
        "Clear turn region:  Ctrl+F2\n" +
        "\n" +
        "Change Search Mode: Page Up\n" +
        "Change Sort Mode:   Page Down\n" +
        "\n" +
        "Clear History:      Delete\n" +
        "Undo Last Word:     Ctrl+Z\n" +
        "Toggle Log + Region: Caps Lock\n" +
        "Toggle Auto Mode:   F1\n" +
        "\n" +
        "Show This Window:   . (period)\n" +
        "Quit Application:   Ctrl+Shift+Q  (or tray / File menu)\n";

    // ---- shift (suggestions) -----------------------------------------------

    private void HandleShiftPress()
    {
        var s = _state.Snapshot();
        var resumeAuto = false;
        if (s.AutoModeActive)
        {
            _state.SetAutoModeActive(false);
            resumeAuto = true;
        }
        if (s.Region == null)
        {
            Log("Cannot perform WBT: No region selected.", LogLevel.Error);
            SelectRegion();
            if (resumeAuto) _state.SetAutoModeActive(true);
            return;
        }
        Submit(() =>
        {
            try { HandleShiftAsync("shift"); }
            finally { if (resumeAuto) _state.SetAutoModeActive(true); }
        });
    }

    private void HandleShiftAsync(string typingSource)
    {
        var s = _state.Snapshot();
        if (s.Region == null) return;

        Log("Processing WBT...", LogLevel.Info);
        var (letters, ok) = _ocr.PerformOCR(s.Region);
        if (!ok || letters == "")
        {
            Log("WBT returned no characters.", LogLevel.Warning);
            return;
        }

        s = _state.Snapshot();
        var mode = AppConfig.SearchModes[ClampIndex(s.CurrentModeIndex, AppConfig.SearchModes.Length)];

        if (letters == s.LastOCRText && s.Suggestions.Count > 0)
        {
            TypeNextWord(typingSource);
            return;
        }

        _state.SetLastOcrText(letters);
        Log($"--- WBT: {letters} ---", LogLevel.Info);

        var suggestions = _api.Suggestions(letters, mode);
        _state.SetApiStatus(_api.Status());

        if (suggestions.Count > 0)
        {
            s = _state.Snapshot();
            suggestions = SuggestionLogic.Sort(suggestions, AppConfig.SortModes[ClampIndex(s.CurrentSortModeIndex, AppConfig.SortModes.Length)]);
            _state.SetSuggestions(suggestions, 0);
            Log($"Found {suggestions.Count} suggestions.", LogLevel.Info);
            for (var i = 0; i < suggestions.Count && i < 3; i++)
                Log($"\t{i + 1}. {suggestions[i]}", LogLevel.Info);
            if (suggestions.Count > 3)
                Log($"\n... and {suggestions.Count - 3} more", LogLevel.Info);
        }
        else
        {
            _state.SetSuggestions(new List<string>(), 0);
        }

        TypeNextWord(typingSource);
    }

    private void TypeNextWord(string typingSource)
    {
        var s = _state.Snapshot();
        if (s.Suggestions.Count == 0)
        {
            Log("No suggestions loaded.", LogLevel.Warning);
            return;
        }

        var (word, nextIdx) = SuggestionLogic.NextUntyped(s.Suggestions, s.SuggestionIndex, s.TypedWordsHistory);
        if (word == "")
        {
            Log("All available suggestions have been typed.", LogLevel.Warning);
            return;
        }

        // "Thinking" pause before typing (auto slightly longer than Shift).
        HumanTyping.SleepSeconds(typingSource == "auto"
            ? HumanTyping.Uniform(0.52, 1.12)
            : HumanTyping.Uniform(0.3, 0.72));

        Log($"Typing: '{word}'", LogLevel.Info);
        var scale = typingSource == "auto" ? 1.32 : 1.22;
        HumanTyping.TypeWordHumanLike(word, s.TypingDelay, scale);
        HumanTyping.SleepSeconds(HumanTyping.Uniform(0.26, 0.62));
        InputSimulator.PressEnter();

        _state.AddTypingRecord(word, s.LastOCRText);
        _state.SetSuggestionIndex(nextIdx);
    }

    // ---- alt+1 (definitions) -------------------------------------------------

    private void HandleAlt1Press()
    {
        var s = _state.Snapshot();
        if (s.Region == null)
        {
            Log("Cannot perform WBT: No region selected.", LogLevel.Error);
            SelectRegion();
            return;
        }
        Submit(HandleAlt1Async);
    }

    private void HandleAlt1Async()
    {
        var s = _state.Snapshot();
        if (s.Region == null) return;

        Log("Processing WBT...", LogLevel.Info);
        var (word, ok) = _ocr.PerformOCR(s.Region);
        if (!ok || word == "")
        {
            Log("WBT returned no definitions.", LogLevel.Warning);
            return;
        }

        var defs = _api.Definitions(word);
        _state.SetApiStatus(_api.Status());

        if (defs.Count > 0)
        {
            _state.SetDefinitions(defs, 0);
            Log($"Found {defs.Count} definitions.", LogLevel.Info);
        }
        else
        {
            _state.SetDefinitions(new List<string>(), 0);
            Log("No definitions found.", LogLevel.Warning);
        }

        Log($"Showing definition for: '{word}'", LogLevel.Info);
        _logWin?.Synchronize(() => DefinitionWindow.Show(_logWin, word, defs));
    }

    // ---- region selection ------------------------------------------------------

    private void SelectRegion()
    {
        _logWin?.Synchronize(Safe("selectRegion", () =>
        {
            _overlay?.ShowRegion(null, null);
            var region = RegionSelectorWindow.PickRegion(_logWin);
            if (region == null)
            {
                Log("Region selection cancelled.", LogLevel.Warning);
                var s0 = _state.Snapshot();
                _overlay?.ShowRegion(s0.Region, s0.TurnRegion);
                return;
            }

            MessageBoxes.Info(
                "Your turn region",
                "Select the box around YOUR TURN for auto mode (F1).\n\n" +
                "Press Esc in the next screen to skip — then auto mode will not wait for your turn.");

            var turnRegion = RegionSelectorWindow.PickRegion(_logWin);
            if (turnRegion == null)
                Log("Turn region skipped — letters only.", LogLevel.Warning);

            _state.SetRegions(region, turnRegion);
            Log(turnRegion != null ? "Regions saved (letters + your turn)." : "Regions saved (letters).", LogLevel.Info);
            _overlay?.ShowRegion(region, turnRegion);
            _state.SaveState();
        }));
    }

    private void ClearTurnRegion()
    {
        _state.SetTurnRegion(null);
        Log("Turn region cleared — auto mode no longer waits for your turn.", LogLevel.Info);
        _logWin?.Synchronize(() =>
        {
            var s = _state.Snapshot();
            _overlay?.ShowRegion(s.Region, null);
        });
        _state.SaveState();
    }

    // ---- modes ------------------------------------------------------------------

    private void SetSearchMode(int index)
    {
        var s = _state.Snapshot();
        if (s.CurrentModeIndex == index) return;
        _state.SetSearchModeIndex(index);
        Log($"Current Mode: {AppConfig.SearchModes[ClampIndex(index, AppConfig.SearchModes.Length)]}", LogLevel.Info);
        _state.SaveState();
    }

    private void SetSortMode(int index)
    {
        var s = _state.Snapshot();
        if (s.CurrentSortModeIndex == index) return;
        List<string>? resorted = null;
        if (s.Suggestions.Count > 0)
            resorted = SuggestionLogic.Sort(s.Suggestions, AppConfig.SortModes[ClampIndex(index, AppConfig.SortModes.Length)]);
        _state.SetSortModeIndex(index, resorted);
        Log($"Current Sort: {AppConfig.SortModes[ClampIndex(index, AppConfig.SortModes.Length)]}", LogLevel.Info);
        _state.SaveState();
    }

    private void SetTypingDelay()
    {
        _logWin?.Synchronize(() =>
        {
            var s = _state.Snapshot();
            var prompt =
                "Typical delay between keystrokes in seconds (timing varies slightly).\n" +
                "Slower, more human-like values are often ~0.25–0.45.\n" +
                $"Allowed range: {AppConfig.TypingDelayMin:G} to {AppConfig.TypingDelayMax:G}";
            var (val, ok) = InputDialogWindow.Ask(_logWin, "Typing delay", prompt,
                AppConfig.TypingDelayMin, AppConfig.TypingDelayMax, Round4(s.TypingDelay));
            if (!ok) return;
            _state.SetTypingDelay(val);
            _state.SaveState();
            Log($"Typing delay set to {val:G} s per character.", LogLevel.Info);
        });
    }

    private void SetOCRInterval()
    {
        _logWin?.Synchronize(() =>
        {
            var s = _state.Snapshot();
            var prompt = "Seconds between OCR checks in auto mode (F1).\n" +
                $"Allowed range: {AppConfig.OCRIntervalMin:G} to {AppConfig.OCRIntervalMax:G}";
            var (val, ok) = InputDialogWindow.Ask(_logWin, "OCR interval", prompt,
                AppConfig.OCRIntervalMin, AppConfig.OCRIntervalMax, Round4(s.OCRInterval));
            if (!ok) return;
            _state.SetOCRInterval(val);
            _state.SaveState();
            Log($"OCR interval set to {val:G} s.", LogLevel.Info);
        });
    }

    // ---- history ------------------------------------------------------------------

    private void ClearTypedHistory()
    {
        _state.ClearTypedHistory();
        Log("Cleared history of typed words.", LogLevel.Info);
        _state.SaveState();
    }

    private void UndoLastWord()
    {
        var word = _state.UndoLastWord();
        Log(word != "" ? $"Undone: '{word}'" : "Nothing to undo.", word != "" ? LogLevel.Info : LogLevel.Warning);
    }

    // ---- auto mode ------------------------------------------------------------------

    private void ToggleAutoMode()
    {
        var s = _state.Snapshot();
        var newState = !s.AutoModeActive;
        _state.SetAutoModeActive(newState);
        if (newState)
        {
            Interlocked.Exchange(ref _autoWatcherReset, 1);
            _ocr.ClearCache();
            Log("Auto mode ENABLED (fresh OCR scan).", LogLevel.Info);
        }
        else
        {
            Log("Auto mode DISABLED.", LogLevel.Info);
        }
    }

    private void AutoModeWatcher(CancellationToken ct)
    {
        var lastText = "";
        var haveLast = false;
        var lastWarnEmpty = DateTime.MinValue;
        var lastWarnGate = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            var s = _state.Snapshot();
            var poll = TimeSpan.FromSeconds(s.OCRInterval);
            if (!s.AutoModeActive || s.Region == null)
            {
                Thread.Sleep(poll);
                continue;
            }

            if (Interlocked.Exchange(ref _autoWatcherReset, 0) == 1)
            {
                lastText = "";
                haveLast = false;
            }

            var (letters, ok) = _ocr.PerformOCR(s.Region);
            var now = DateTime.Now;
            if (!ok || letters == "")
            {
                if (now - lastWarnEmpty > TimeSpan.FromSeconds(8))
                {
                    Log("Auto mode: letter OCR is empty — check the letter region (TAB).", LogLevel.Warning);
                    lastWarnEmpty = now;
                }
                Thread.Sleep(poll);
                continue;
            }

            if (!haveLast || letters != lastText)
            {
                var (gateOk, turnOcr) = AutoModeTurnOK();
                if (!gateOk)
                {
                    if (s.TurnRegion != null && now - lastWarnGate > TimeSpan.FromSeconds(8))
                    {
                        Log($"Auto mode: waiting for YOUR TURN (turn OCR: \"{turnOcr}\")", LogLevel.Warning);
                        lastWarnGate = now;
                    }
                    Thread.Sleep(poll);
                    continue;
                }
                Log($"Auto-detected: '{letters}'", LogLevel.Info);
                lastText = letters;
                haveLast = true;
                Submit(() => HandleShiftAsync("auto"));
            }

            Thread.Sleep(poll);
        }
    }

    // ---- help -----------------------------------------------------------------------

    private void ShowHelp()
    {
        _logWin?.Synchronize(() => HelpWindow.Show(_logWin, HelpText()));
    }

    // ---- tesseract --------------------------------------------------------------------

    private bool CheckAndInstallTesseract()
    {
        if (_ocr.Available()) return true;

        Log("Tesseract WBT not found.", LogLevel.Warning);
        if (!MessageBoxes.YesNo("Tesseract Not Found", "Tesseract WBT not found. Download and install?"))
            return false;

        Log("Downloading Tesseract...", LogLevel.Info);
        try
        {
            Downloader.DownloadFile(AppConfig.TesseractInstallerURL, AppConfig.TesseractInstallerPath);
        }
        catch (Exception ex)
        {
            Log($"Installation failed: {ex.Message}", LogLevel.Error);
            return false;
        }

        Log("Running installer...", LogLevel.Info);
        try
        {
            using var proc = Process.Start(AppConfig.TesseractInstallerPath);
            proc?.WaitForExit();
        }
        catch (Exception ex)
        {
            Log($"Installation failed: {ex.Message}", LogLevel.Error);
            return false;
        }

        // Re-resolve after installation.
        _ocr = new OcrProcessor();
        return _ocr.Available();
    }

    // ---- lifecycle ----------------------------------------------------------------------

    /// <summary>Starts the application: creates the UI, installs hotkeys, launches
    /// the auto-mode watcher, and lets WPF's own message loop run (the caller is
    /// expected to be App.xaml.cs's OnStartup, so no blocking call is made here).</summary>
    public void Run()
    {
        AppLog.Setup();
        AppLog.Infof("========== WBT STARTED ==========");
        _state.LoadState();

        if (!CheckAndInstallTesseract())
            Log("Tesseract is required for OCR features.", LogLevel.Warning);

        _overlay = new RegionOverlayManager();

        var logWin = new MainWindow(_queue, BuildCallbacks(), visible =>
        {
            _overlay?.SetBundleVisible(visible);
        });
        _logWin = logWin;
        Application.Current.MainWindow = logWin;
        logWin.Show();

        Log("========== WBT STARTED ==========", LogLevel.Info);
        foreach (var line in StateText().Split('\n'))
            Log(line, LogLevel.Info);

        var s = _state.Snapshot();
        if (s.Region == null)
            Log("Press TAB to select regions", LogLevel.Warning);
        else
            _overlay.ShowRegion(s.Region, s.TurnRegion);

        RegisterHotkeys();
        _hook.Start();

        _autoModeCts = new CancellationTokenSource();
        var token = _autoModeCts.Token;
        var watcherThread = new Thread(() => AutoModeWatcher(token)) { IsBackground = true, Name = "WBT-AutoMode" };
        watcherThread.Start();
    }

    private Callbacks BuildCallbacks() => new()
    {
        SelectRegion = SelectRegion,
        ClearTurnRegion = ClearTurnRegion,
        SetSearchMode = SetSearchMode,
        SetSortMode = SetSortMode,
        ClearHistory = ClearTypedHistory,
        UndoWord = UndoLastWord,
        ShowHelp = ShowHelp,
        ToggleWindow = () => _logWin?.ToggleVisibility(),
        FetchSuggestions = HandleShiftPress,
        FetchDefinitions = HandleAlt1Press,
        SetTypingDelay = SetTypingDelay,
        SetOCRInterval = SetOCRInterval,
        Exit = () => GracefulExit(0),
    };

    private void RegisterHotkeys()
    {
        _hook.Register("shift", Safe("shift", HandleShiftPress));
        _hook.Register("alt+1", Safe("alt+1", HandleAlt1Press));
        _hook.Register("tab", Safe("tab", SelectRegion));
        _hook.Register("page up", Safe("page up", () =>
        {
            var s = _state.Snapshot();
            SetSearchMode((s.CurrentModeIndex + 1) % AppConfig.SearchModes.Length);
        }));
        _hook.Register("page down", Safe("page down", () =>
        {
            var s = _state.Snapshot();
            SetSortMode((s.CurrentSortModeIndex + 1) % AppConfig.SortModes.Length);
        }));
        _hook.Register("delete", Safe("delete", ClearTypedHistory));
        _hook.Register("caps lock", Safe("caps lock", () => _logWin?.ToggleVisibility()));
        _hook.Register("f1", Safe("f1", ToggleAutoMode));
        _hook.Register("ctrl+f2", Safe("ctrl+f2", ClearTurnRegion));
        _hook.Register(".", Safe("period", ShowHelp));
        _hook.Register("ctrl+z", Safe("ctrl+z", UndoLastWord));
        // Quit is Ctrl+Shift+Q (not Ctrl+C) so copying text doesn't close the app.
        _hook.Register("ctrl+shift+q", Safe("ctrl+shift+q", () => GracefulExit(0)));
    }

    private void GracefulExit(int code)
    {
        if (Interlocked.Exchange(ref _exitingFlag, 1) == 1) return;
        Log("Shutting down...", LogLevel.Info);
        _state.SetAutoModeActive(false);
        _state.SaveState();
        _state.SaveMetrics();

        _autoModeCts?.Cancel();
        _hook.Stop();
        _logWin?.DisposeAll();

        // Give the UI a moment to tear down.
        Thread.Sleep(150);
        Environment.Exit(code);
    }

    private int _exitingFlag;

    private static int ClampIndex(int i, int n)
    {
        if (n == 0) return 0;
        if (i < 0 || i >= n) return ((i % n) + n) % n;
        return i;
    }

    private static double Round4(double v) => Math.Round(v, 4);
}
