# Contributing to Word Bomb Tool (C#/WPF)

Thanks for considering a contribution! This is a small hobby project, so the
process is intentionally lightweight.

## Getting set up

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Clone the repo and build:

   ```powershell
   git clone https://github.com/mPhpMaster/word-bomb-tool-cs.git
   cd word-bomb-tool-cs
   dotnet build .\WordBombTool.sln -c Release
   dotnet test .\src\WordBombTool.Tests\WordBombTool.Tests.csproj -c Release
   ```

3. The app is Windows-only (it depends on Win32 keyboard hooks and
   `SendInput`), so building/running the GUI and CLI requires Windows. The
   `WordBombTool.Core` library and its tests are plain `net8.0` and are the
   easiest place to iterate if you're on a different OS.

## Project layout

See the [README's project layout section](README.md#project-layout) for
where things live. In short: put platform-agnostic logic in
`src/WordBombTool.Core` (so it stays unit-testable), and keep
`src/WordBombGui` / `src/WordBombCli` as thin as possible around it.

## Making changes

- Keep pull requests focused — one change per PR is easier to review.
- Add or update tests in `src/WordBombTool.Tests` for logic changes in
  `WordBombTool.Core`. GUI-only changes (XAML, window behavior) don't need
  unit tests, but please describe how you tested them manually in the PR.
- Run `dotnet test` before opening a PR — CI will also run it, but it's
  faster to catch failures locally.
- Match the existing code style (nullable reference types enabled, explicit
  `using` statements, XML doc comments on public members where the original
  didn't already have them).
- If you change the config file format (`ocr_config.json`), please keep it
  backward-compatible where possible — it's shared with the original Python
  and Go versions.

## Reporting bugs / requesting features

Open an [issue](../../issues) with:

- What you expected to happen vs. what actually happened.
- Repro steps, if applicable.
- Your Windows version and whether you're using the installer, a
  self-contained build, or building from source.
- Relevant excerpts from `ocr_helper.log` (written next to the executable),
  if the issue involves OCR, typing, or hotkeys.

## Code of Conduct

By participating, you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).
