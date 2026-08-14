# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-08-14

### Removed

- The GUI-less CLI (`WordBombCli` / `WordBombCLI.exe`) and its publish profiles.
  The project is now GUI-only. `WordBombTool.Core` is unaffected — nothing it
  exposed was CLI-only — and the installer no longer offers the "add CLI to
  PATH" task.
- `appicon.png` as an embedded `<Resource>`. Nothing loaded it at runtime, so it
  was dead payload in every published binary. The file stays in the repo as the
  source art for regenerating the `.ico`.

### Fixed

- **Crash that terminated the app during auto mode.** `OcrPreprocess.ToGray`
  used `using var bmp = ... ? src : clone`, which disposed the *caller's* bitmap
  whenever the input was already 32bppArgb (always, in practice). The OCR
  pipeline reuses one bitmap for its soft and hard passes, so the first time a
  turn box OCR'd as empty the second pass hit a disposed bitmap and threw
  `ArgumentException` out of a bare background thread, killing the process.
  Now covered by a regression test.
- `AutoModeWatcher` ran its whole loop body outside any `try`. Because it runs on
  a bare `Thread`, any exception terminated the process rather than failing one
  poll; it now logs and continues.
- The app could become impossible to quit. `GracefulExit` latched its
  "already exiting" flag before teardown, so if teardown threw, `Environment.Exit`
  was never reached and every later exit attempt returned early. Exit is now in a
  `finally`, and the tray teardown is marshalled to the UI thread.
- Opening definition windows permanently blocked all typing. The dialog was shown
  via `Dispatcher.Invoke` from a worker holding one of only two semaphore permits,
  so two open windows starved the pool forever. Modal dialogs are now dispatched
  without blocking the caller.
- Dialogs showed no icon. `HelpWindow`, `DefinitionWindow`, and
  `InputDialogWindow` set no `Icon` and defaulted to `ShowInTaskbar="True"`, and
  only set `Owner` when the main window was visible — but the main window is
  routinely hidden via Caps Lock. They now set the icon explicitly and stay out
  of the taskbar.
- The tray icon was blurry: `new Icon(stream)` best-fits to 32×32, which the shell
  then downscaled into a 16×16 slot. It now requests `SmallIconSize` directly. The
  `Icon` is also disposed with the tray instead of leaking its GDI handle.
- **Auto mode typed out of turn.** The "YOUR TURN" gate ended with
  `|| (hasYour && text.Length >= 4)`, which is always true when `hasYour` is —
  "your" is itself four characters — so the whole expression collapsed to a bare
  `Contains("your")`. Any stray "your" from chat or adjacent UI opened the gate.
  It now requires both words, and is unit-tested.
- Config and metrics writes were neither serialised nor atomic. Eight call sites
  across UI and worker threads used plain `File.WriteAllText`, so concurrent saves
  lost a setting to an `IOException`, and an interrupted write could leave a
  truncated `ocr_config.json` that silently dropped saved regions on next launch.
  Writes now go through a lock and a temp-file rename.
- The Datamuse HTTP timeout was 1 second, shared with `OCRTimeoutSeconds`. That has
  to cover DNS, TCP, TLS and the round trip, so it expired routinely on slower
  links and looked identical to "no words match". The API now has its own 8s
  budget, and a non-online status is logged distinctly from an empty result.
- A missed key-up (e.g. during a UAC secure desktop) wedged a key in the hook's
  pressed-key set, silently disabling that hotkey for the rest of the session.
  The pressed state is now re-validated against `GetAsyncKeyState`.
- Pressing F1 while a word was still being typed could be silently undone: the
  shift handler captured "resume auto mode" before queueing and re-applied it
  seconds later, overriding the user. Explicit toggles now win.
- The Tesseract check ran inline on the UI thread *before* any window was shown —
  a prompt, a ~50MB download and an installer, with the app marked "not
  responding" and nothing on screen. It now runs after the window is up, off the
  UI thread. The download also streams to disk (it previously buffered the whole
  file in memory), has no 100s timeout to abort slow links, and launches the
  installer with `UseShellExecute` so elevation prompts work.
- `RecordOCRAttempt` / `RecordAPICall` had no callers, so `ocr_metrics.json` was
  written with all-zero counters on every exit. OCR and API calls are now routed
  through timing wrappers.
- Pressing Tab while the region picker was open stacked a second picker on top of
  the first (Tab is a global hotkey and the picker is modal). Guarded against
  re-entry.
- `SWP_NOZORDER` in the region selector cancelled the `HWND_TOPMOST` it was passed
  with, making that argument a no-op.

### Performance

- OCR no longer re-encodes the same image once per page-segmentation mode; the
  preprocessed bitmap is PNG-encoded once and the buffer reused. Previously a
  ~2.8MB bitmap was encoded four times per call, twice a second.
- Letter OCR stops after two modes when they agree. The result is provably
  identical (that candidate holds 2 of 4 votes, is first in tie-break order, and
  only two modes remain), so this halves process launches in the common case.
- The turn gate returns as soon as a read opens the gate, instead of always running
  4 soft + 3 fallback modes. Each mode is a full `tesseract.exe` launch that
  reloads ~15MB of model data, and this path is polled on every letter change.
- `AutoModeWatcher` reads four scalars per poll instead of calling `Snapshot()`,
  which copied both suggestion lists, rehashed a 1000-entry set and cloned
  `Metrics` — up to ten times a second.
- `_typingRecords` is capped at `AppConfig.UndoBufferSize` (declared but never
  used until now). It previously grew unbounded for the whole session and was
  deep-copied by every snapshot.
- The OCR cache sweeps expired entries on insert and has a hard ceiling. Eviction
  only happened on a repeat lookup of the same key, but keys are exact pixel
  hashes, so entries accumulated indefinitely (~7k/hour at a 2Hz poll).
- Image hashing reads the locked bits directly instead of copying ~160KB into a
  fresh array per capture.
- Log-line brushes are cached rather than allocated per rendered line for three
  distinct colours.
- Worker tasks run on a fixed set of dedicated threads instead of the thread pool.
  They block for 4–6 seconds inside the typing delay, and because hotkey callbacks
  are also pool work items, Ctrl+Shift+Q and Caps Lock became unresponsive for
  seconds during typing.
- Tesseract's stdout/stderr are drained before stdin is written. Tesseract writes
  to stderr, so a large enough message could deadlock both processes against the
  4KB pipe buffer until the 15s timeout.

### Changed

- `appicon.ico` regenerated so every frame below 256×256 is BMP/DIB-encoded and
  only the 256×256 frame is PNG, matching the platform convention. All seven
  frames were previously PNG-compressed, which some legacy `ExtractIcon`-based
  consumers render blank. Artwork is unchanged.

## [1.0.0] - 2026-07-20

### Added

- Initial C# / .NET 8 WPF port of
  [mPhpMaster/word-bomb-tool](https://github.com/mPhpMaster/word-bomb-tool)
  (the original Python implementation), with full feature parity: screen-region
  OCR, Datamuse-backed word suggestions (5 search modes, 4 sort modes),
  auto-typing, global hotkeys, region overlays, system tray integration, and a
  GUI-less CLI.
- `WordBombTool.Core` shared library so the GUI and CLI reuse one copy of the
  config, Datamuse client, suggestion logic, and OCR preprocessing pipeline.
- xUnit test project (`WordBombTool.Tests`) covering suggestion sort/pick
  logic, config value clamping, the Datamuse client's status transitions, the
  OCR preprocessing invariants, and a regression test for the native
  `SendInput` struct layout.
- Three publish profiles (self-contained, self-contained + ReadyToRun,
  framework-dependent) and a `publish.ps1` driver script.
- An Inno Setup–based Windows installer (`installer/WordBombTool.iss`) with
  Start Menu / desktop shortcuts, an optional "add CLI to PATH" task, and a
  clean uninstaller.

### Changed (vs. the original Python version)

- GUI toolkit: WPF instead of Tkinter. WPF's native `AllowsTransparency` gives
  real per-pixel alpha compositing for the region selector and overlays.
- Hotkeys and typing: a native Win32 low-level keyboard hook and `SendInput`
  instead of the `keyboard` package.
- Distribution: self-contained single-file executables plus a proper Windows
  installer, instead of PyInstaller-built executables.
- Log view renders each line in its own color (info/warning/error) via a
  `RichTextBox`, and is capped at 500 rendered entries so long-running
  sessions don't grow memory/render cost without bound.

### Fixed

- Auto-typing not producing any keystrokes: the native `INPUT` struct used
  with `SendInput` was 8 bytes short of the real Win32 `sizeof(INPUT)` on
  x64 (a P/Invoke struct-layout/padding mismatch), which made `SendInput`
  silently fail every call. Now covered by a dedicated regression test.
- A crash on first launch caused by `InvariantGlobalization`, which breaks
  WPF's text-layout engine.
- Region selection/overlay drift on mixed-DPI multi-monitor setups — now
  reads physical pixel coordinates via `GetCursorPos` instead of a single
  captured DPI transform.
- A potential process crash from an unhandled exception at the native
  keyboard-hook callback boundary; it's now wrapped in try/catch.
