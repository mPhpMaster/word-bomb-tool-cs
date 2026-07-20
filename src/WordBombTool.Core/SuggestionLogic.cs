// Suggestion list operations (sorting and picking the next untyped word).
// Port of suggestion_manager.py / suggest.go.
namespace WordBombTool;

public static class SuggestionLogic
{
    /// <summary>Returns a new list of suggestions ordered according to sortMode.
    /// The input list is never mutated. Sorting is stable.</summary>
    public static List<string> Sort(List<string> suggestions, string sortMode)
    {
        if (suggestions.Count == 0) return new List<string>();
        var outList = new List<string>(suggestions);

        switch (sortMode)
        {
            case "Shortest":
                outList = outList.OrderBy(s => s.Length).ToList();
                break;
            case "Longest":
                outList = outList.OrderByDescending(s => s.Length).ToList();
                break;
            case "Frequency":
                // Mirrors the original: sort by descending count of uppercase letters.
                // For typical lowercase results this leaves order effectively unchanged.
                outList = outList.OrderByDescending(UpperCount).ToList();
                break;
            case "Random":
                var rng = Random.Shared;
                for (var i = outList.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (outList[i], outList[j]) = (outList[j], outList[i]);
                }
                break;
        }
        return outList;
    }

    private static int UpperCount(string s) => s.Count(char.IsUpper);

    /// <summary>Finds the next untyped word starting at startIndex, wrapping around
    /// the list. Words containing a space are skipped. Returns the chosen word and
    /// the index to resume from next time. When every candidate has already been
    /// typed it returns ("", startIndex).</summary>
    public static (string word, int nextIndex) NextUntyped(List<string> suggestions, int startIndex, HashSet<string> typed)
    {
        var n = suggestions.Count;
        if (n == 0) return ("", startIndex);
        var start = ((startIndex % n) + n) % n;
        for (var i = 0; i < n; i++)
        {
            var idx = (start + i) % n;
            var word = suggestions[idx];
            if (word.Contains(' ')) continue;
            if (!typed.Contains(word)) return (word, idx + 1);
        }
        return ("", startIndex);
    }
}
