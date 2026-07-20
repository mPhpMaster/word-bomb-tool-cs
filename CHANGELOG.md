# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
