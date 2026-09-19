// Wires the Word Bomb Tool GUI together: OCR, the Datamuse client, state,
// hotkeys, typing and the WPF UI. Port of app/app_windows.go (main.py).
using System.Collections.Concurrent;
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

    // Work items block: first waiting for a slot, then for 4-6 seconds inside
    // HumanTyping's Thread.Sleep chain. Running them as thread-pool items parked pool
    // threads for that whole time, and since hotkey callbacks are *also* pool items,
    // Ctrl+Shift+Q and Caps Lock stopped responding for seconds -- precisely when the
    // user is trying to intervene. A fixed set of dedicated threads keeps the same
    // MaxWorkerThreads concurrency limit while leaving the pool alone.
    private readonly BlockingCollection<Action> _work = new(new ConcurrentQueue<Action>());

    private MainWindow? _logWin;
    private RegionOverlayManager? _overlay;
    private int _autoWatcherReset; // 0/1 flag, single-writer/single-reader via Interlocked
    private CancellationTokenSource? _autoModeCts;

    // One OCR at a time: a second caller (Shift, Alt+1, auto watcher, turn gate) waits
    // for the running OCR to finish. Lives here rather than in OcrProcessor because
    // _ocr is replaced after a Tesseract install.
    private readonly object _ocrLock = new();
    // Serializes whole OCR -> fetch -> type actions (Shift, Alt+1, auto mode) so two
    // actions never type at once (interleaved keys like "llikike"). Later commands
    // wait for the running one to finish.
    private readonly object _actionLock = new();
    // Shift actions queued or running; auto mode stays out of the way meanwhile.
    private int _pendingManual;
    // Letters most recently handled by any action; auto mode skips these.
    private volatile string _lastHandledLetters = "";

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
        try { _work.Add(fn); }
        catch (InvalidOperationException) { /* queue completed: shutting down */ }
    }

    private void StartWorkers()
    {
        for (var i = 0; i < AppConfig.MaxWorkerThreads; i++)
        {
            var t = new Thread(WorkerLoop) { IsBackground = true, Name = $"WBT-Worker{i}" };
            t.Start();
        }
    }

    private void WorkerLoop()
    {
        foreach (var fn in _work.GetConsumingEnumerable())
        {
            try { fn(); }
            catch (Exception ex)
            {
                Log($"[panic in worker]: {ex.Message}", LogLevel.Error);
                AppLog.Errorf("panic in worker: {0}\n{1}", ex.Message, ex.StackTrace ?? "");
            }
        }
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

    // ---- instrumented wrappers ------------------------------------------------
    // AppState.RecordOCRAttempt / RecordAPICall existed but had no callers anywhere,
    // so ocr_metrics.json was written with all-zero counters on every exit. These
    // wrappers are the missing call sites; route OCR and API traffic through them.

    private (string letters, bool ok) TimedOcr(Region region)
    {
        lock (_ocrLock)
        {
            var sw = Stopwatch.StartNew();
            var result = _ocr.PerformOCR(region);
            _state.RecordOCRAttempt(result.ok, sw.Elapsed.TotalMilliseconds);
            return result;
        }
    }

    private string TimedOcrTurnGate(Region region)
    {
        lock (_ocrLock)
        {
            var sw = Stopwatch.StartNew();
            var text = _ocr.PerformOCRTurnGate(region);
            _state.RecordOCRAttempt(text != "", sw.Elapsed.TotalMilliseconds);
            return text;
        }
    }

    private List<string> TimedSuggestions(string letters, string mode)
    {
        var sw = Stopwatch.StartNew();
        var result = _api.Suggestions(letters, mode);
        _state.RecordAPICall(_api.Status() == AppConfig.StatusOnline, sw.Elapsed.TotalMilliseconds);
        return result;
    }

    private List<string> TimedDefinitions(string word)
    {
        var sw = Stopwatch.StartNew();
        var result = _api.Definitions(word);
        _state.RecordAPICall(_api.Status() == AppConfig.StatusOnline, sw.Elapsed.TotalMilliseconds);
        return result;
    }

    // ---- turn gate ------------------------------------------------------------

    private (bool ok, string text) AutoModeTurnOK()
    {
        var s = _state.Snapshot();
        if (s.TurnRegion == null) return (true, "");
        var text = TimedOcrTurnGate(s.TurnRegion);
        if (text == "") return (false, "");
        return (AppConfig.TurnGateAccepts(text), text);
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
        // Snapshot the intent generation alongside the decision to suspend. If the user
        // hits F1 while the word is still being typed, ToggleAutoMode bumps the
        // generation and the resume below is skipped -- previously the stale capture
        // silently turned auto mode back on seconds after they switched it off.
        var intent = Volatile.Read(ref _autoIntentGeneration);
        if (s.AutoModeActive)
        {
            _state.SetAutoModeActive(false);
            resumeAuto = true;
        }
        if (s.Region == null)
        {
            Log("Cannot perform WBT: No region selected.", LogLevel.Error);
            SelectRegion();
            ResumeAutoIfStillIntended(resumeAuto, intent);
            return;
        }
        Interlocked.Increment(ref _pendingManual);
        Submit(() =>
        {
            try
            {
                lock (_actionLock) HandleShiftAsync("shift");
            }
            finally
            {
                Interlocked.Decrement(ref _pendingManual);
                ResumeAutoIfStillIntended(resumeAuto, intent);
            }
        });
    }

    /// <summary>Restores auto mode after a shift-triggered suspension, unless the user
    /// explicitly toggled it in the meantime (tracked by _autoIntentGeneration).</summary>
    private void ResumeAutoIfStillIntended(bool resumeAuto, int intentAtSuspend)
    {
        if (!resumeAuto) return;
        if (Volatile.Read(ref _autoIntentGeneration) != intentAtSuspend) return;
        _state.SetAutoModeActive(true);
    }

    /// <summary>Reads letters, fetches suggestions and types the first/next word.
    /// Callers must hold _actionLock. Pass letters to reuse an OCR result already
    /// taken, or null to OCR the region now.</summary>
    private void HandleShiftAsync(string typingSource, string? letters = null)
    {
        var s = _state.Snapshot();
        if (s.Region == null) return;

        Log("Processing WBT...", LogLevel.Info);
        if (string.IsNullOrEmpty(letters))
        {
            var (ocrLetters, ok) = TimedOcr(s.Region);
            letters = ok ? ocrLetters : "";
        }
        if (letters == "")
        {
            Log("WBT returned no characters.", LogLevel.Warning);
            return;
        }
        _lastHandledLetters = letters;

        s = _state.Snapshot();
        var mode = AppConfig.SearchModes[ClampIndex(s.CurrentModeIndex, AppConfig.SearchModes.Length)];

        if (letters == s.LastOCRText && s.Suggestions.Count > 0)
        {
            TypeNextWord(typingSource);
            return;
        }

        _state.SetLastOcrText(letters);
        Log($"--- WBT: {letters} ---", LogLevel.Info);

        var suggestions = TimedSuggestions(letters, mode);
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
            // Distinguish "the API didn't answer" from "the API has no words for these
            // letters" -- both used to surface as a bare "No suggestions loaded."
            var status = _api.Status();
            if (status != AppConfig.StatusOnline)
                Log($"No suggestions: API {status} (letters: '{letters}').", LogLevel.Warning);
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
        // Wait for any running action; hold the lock only for OCR + fetch, not the window.
        string word;
        bool ok;
        List<string> defs = new();
        lock (_actionLock)
        {
            (word, ok) = TimedOcr(s.Region);
            if (ok && word != "") defs = TimedDefinitions(word);
        }
        if (!ok || word == "")
        {
            Log("WBT returned no definitions.", LogLevel.Warning);
            return;
        }

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
        // BeginInvoke, not Invoke: DefinitionWindow.Show is modal, and this runs on a
        // worker holding one of only MaxWorkerThreads permits. Blocking here until the
        // user closes the window would starve typing entirely.
        _logWin?.SynchronizeAsync(() => DefinitionWindow.Show(_logWin, word, defs));
    }

    // ---- region selection ------------------------------------------------------

    private void SelectRegion()
    {
        // Tab is a global hotkey and the picker is modal, so pressing Tab while the
        // picker is already open used to re-enter here through the nested message loop
        // and stack a second picker on top of the first.
        if (Interlocked.Exchange(ref _pickingRegion, 1) == 1) return;

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

        Volatile.Write(ref _pickingRegion, 0);
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
        // Any explicit user toggle invalidates a pending "resume after shift" (see
        // HandleShiftPress): whatever the user just chose must win over a decision
        // captured seconds ago, before the word finished typing.
        Interlocked.Increment(ref _autoIntentGeneration);

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
        var lastWarnEmpty = DateTime.MinValue;
        var lastWarnGate = DateTime.MinValue;
        var lastWarnPanic = DateTime.MinValue;
        // Kept across iterations so the catch below still has a sane sleep interval
        // even if the snapshot itself is what threw.
        var poll = TimeSpan.FromSeconds(AppConfig.OCRInterval);

        while (!ct.IsCancellationRequested)
        {
            // This runs on a bare background Thread, so an escaping exception would
            // terminate the process rather than just failing the poll. Every branch
            // below must stay inside this try.
            try
            {
                // Scalar read, not Snapshot(): this runs every poll and only needs four
                // fields, where Snapshot() copies both suggestion lists, rehashes a
                // 1000-entry history set and clones Metrics.
                var (autoActive, region, turnRegion, ocrInterval) = _state.AutoModePoll();
                poll = TimeSpan.FromSeconds(ocrInterval);
                if (!autoActive || region == null)
                {
                    Thread.Sleep(poll);
                    continue;
                }

                if (Interlocked.Exchange(ref _autoWatcherReset, 0) == 1)
                    _lastHandledLetters = "";

                // A Shift action is queued/running: let it finish instead of racing it.
                if (Volatile.Read(ref _pendingManual) > 0)
                {
                    Thread.Sleep(poll);
                    continue;
                }

                var (letters, ok) = TimedOcr(region);
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

                if (letters != _lastHandledLetters)
                {
                    var (gateOk, turnOcr) = AutoModeTurnOK();
                    if (!gateOk)
                    {
                        if (turnRegion != null && now - lastWarnGate > TimeSpan.FromSeconds(8))
                        {
                            Log($"Auto mode: waiting for YOUR TURN (turn OCR: \"{turnOcr}\")", LogLevel.Warning);
                            lastWarnGate = now;
                        }
                        Thread.Sleep(poll);
                        continue;
                    }
                    // Run inline (not queued) so the watcher waits for typing to finish
                    // before polling again.
                    lock (_actionLock)
                    {
                        // Re-check after waiting: Shift may have paused auto mode or
                        // already typed for these letters.
                        if (_state.IsAutoModeActive()
                            && Volatile.Read(ref _pendingManual) == 0
                            && letters != _lastHandledLetters)
                        {
                            Log($"Auto-detected: '{letters}'", LogLevel.Info);
                            HandleShiftAsync("auto", letters);
                        }
                    }
                }

                Thread.Sleep(poll);
            }
            catch (Exception ex)
            {
                // Log at most once every 8s so a persistent fault can't flood the
                // window, but always record the full trace to the log file.
                AppLog.Errorf("panic in auto mode: {0}\n{1}", ex.Message, ex.StackTrace ?? "");
                if (DateTime.Now - lastWarnPanic > TimeSpan.FromSeconds(8))
                {
                    Log($"[panic in auto mode]: {ex.Message}", LogLevel.Error);
                    lastWarnPanic = DateTime.Now;
                }
                Thread.Sleep(poll);
            }
        }
    }

    // ---- help -----------------------------------------------------------------------

    private void ShowHelp()
    {
        // Modal, same reasoning as the definition window: don't block the caller's thread.
        _logWin?.SynchronizeAsync(() => HelpWindow.Show(_logWin, HelpText()));
    }

    // ---- tesseract --------------------------------------------------------------------

    private bool CheckAndInstallTesseract()
    {
        if (_ocr.Available()) return true;

        Log("Tesseract WBT not found.", LogLevel.Warning);
        // This runs on a background task now, so marshal the prompt to the UI thread
        // rather than opening an unowned message box off it.
        var consent = _logWin != null
            ? _logWin.Dispatcher.Invoke(() => MessageBoxes.YesNo("Tesseract Not Found", "Tesseract WBT not found. Download and install?"))
            : MessageBoxes.YesNo("Tesseract Not Found", "Tesseract WBT not found. Download and install?");
        if (!consent) return false;

        Log("Downloading Tesseract (this can take a few minutes)...", LogLevel.Info);
        try
        {
            Downloader.DownloadFile(AppConfig.TesseractInstallerURL, AppConfig.TesseractInstallerPath);
        }
        catch (Exception ex)
        {
            Log($"Download failed: {ex.Message}", LogLevel.Error);
            return false;
        }

        Log("Running installer...", LogLevel.Info);
        try
        {
            // UseShellExecute so an installer that requests elevation gets a UAC prompt
            // instead of failing outright.
            var psi = new ProcessStartInfo(AppConfig.TesseractInstallerPath) { UseShellExecute = true };
            using var proc = Process.Start(psi);
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

        StartWorkers();
        RegisterHotkeys();
        _hook.Start();

        _autoModeCts = new CancellationTokenSource();
        var token = _autoModeCts.Token;
        var watcherThread = new Thread(() => AutoModeWatcher(token)) { IsBackground = true, Name = "WBT-AutoMode" };
        watcherThread.Start();

        // Deliberately last, and off the UI thread. This can prompt, download ~50MB and
        // run an installer to completion; doing it inline before logWin.Show() left the
        // user staring at nothing while Windows marked the process "not responding".
        Task.Run(() =>
        {
            try
            {
                if (!CheckAndInstallTesseract())
                    Log("Tesseract is required for OCR features.", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                Log($"Tesseract check failed: {ex.Message}", LogLevel.Error);
                AppLog.Errorf("tesseract check failed: {0}\n{1}", ex.Message, ex.StackTrace ?? "");
            }
        });
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

        // The exiting flag is latched above, so every later exit attempt returns
        // early. If teardown throws and we never reach Environment.Exit, the app
        // becomes impossible to quit — hence the finally.
        try
        {
            Log("Shutting down...", LogLevel.Info);
            _state.SetAutoModeActive(false);
            _state.SaveState();
            _state.SaveMetrics();

            _autoModeCts?.Cancel();
            _work.CompleteAdding(); // let the worker threads drain and exit
            _hook.Stop();
            _logWin?.DisposeAll();

            // Give the UI a moment to tear down.
            Thread.Sleep(150);
        }
        catch (Exception ex)
        {
            AppLog.Errorf("error during shutdown: {0}\n{1}", ex.Message, ex.StackTrace ?? "");
        }
        finally
        {
            Environment.Exit(code);
        }
    }

    private int _exitingFlag;

    // Bumped on every explicit auto-mode toggle; see HandleShiftPress /
    // ResumeAutoIfStillIntended.
    private int _autoIntentGeneration;

    // 0/1 re-entrancy guard for the modal region picker; see SelectRegion.
    private int _pickingRegion;

    private static int ClampIndex(int i, int n)
    {
        if (n == 0) return 0;
        if (i < 0 || i >= n) return ((i % n) + n) % n;
        return i;
    }

    private static double Round4(double v) => Math.Round(v, 4);
}
