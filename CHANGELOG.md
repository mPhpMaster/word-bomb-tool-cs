# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
