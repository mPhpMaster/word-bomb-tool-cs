// Callback bag the UI layer invokes to ask the app controller to do things.
// Port of ui.Callbacks in the Go version.
namespace WordBombTool;

public sealed class Callbacks
{
    public required Action SelectRegion { get; init; }
    public required Action ClearTurnRegion { get; init; }
    public required Action<int> SetSearchMode { get; init; }
    public required Action<int> SetSortMode { get; init; }
    public required Action ClearHistory { get; init; }
    public required Action UndoWord { get; init; }
    public required Action ShowHelp { get; init; }
    public required Action ToggleWindow { get; init; }
    public required Action FetchSuggestions { get; init; }
    public required Action FetchDefinitions { get; init; }
    public required Action SetTypingDelay { get; init; }
    public required Action SetOCRInterval { get; init; }
    public required Action Exit { get; init; }
}
