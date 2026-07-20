// Port of suggest_test.go: Sort's per-mode ordering/stability and
// NextUntyped's wrap-around / skip-typed / skip-multi-word / all-typed
// behaviour.
using WordBombTool;
using Xunit;

namespace WordBombTool.Tests;

public class SuggestionLogicTests
{
    [Fact]
    public void Sort_Shortest_OrdersByAscendingLengthAndIsStable()
    {
        var input = new List<string> { "ccc", "a", "bb", "ddd", "e" };
        var sorted = SuggestionLogic.Sort(input, "Shortest");
        Assert.Equal(new[] { "a", "e", "bb", "ccc", "ddd" }, sorted);
    }

    [Fact]
    public void Sort_Longest_OrdersByDescendingLength()
    {
        var input = new List<string> { "ccc", "a", "bb", "ddd", "e" };
        var sorted = SuggestionLogic.Sort(input, "Longest");
        Assert.Equal(new[] { "ccc", "ddd", "bb", "a", "e" }, sorted);
    }

    [Fact]
    public void Sort_DoesNotMutateInputList()
    {
        var input = new List<string> { "zzz", "a", "mm" };
        var snapshot = new List<string>(input);
        _ = SuggestionLogic.Sort(input, "Shortest");
        Assert.Equal(snapshot, input);
    }

    [Fact]
    public void Sort_UnknownMode_ReturnsCopyInOriginalOrder()
    {
        var input = new List<string> { "z", "a", "m" };
        var sorted = SuggestionLogic.Sort(input, "NotARealMode");
        Assert.Equal(input, sorted);
        Assert.NotSame(input, sorted);
    }

    [Fact]
    public void Sort_EmptyList_ReturnsEmptyList()
    {
        Assert.Empty(SuggestionLogic.Sort(new List<string>(), "Shortest"));
    }

    [Fact]
    public void Sort_Random_PreservesMultiset()
    {
        var input = new List<string> { "a", "b", "c", "d", "e" };
        var sorted = SuggestionLogic.Sort(input, "Random");
        Assert.Equal(input.OrderBy(s => s), sorted.OrderBy(s => s));
    }

    [Fact]
    public void NextUntyped_ReturnsFirstUntypedFromStart()
    {
        var words = new List<string> { "cat", "dog", "fish" };
        var (word, next) = SuggestionLogic.NextUntyped(words, 0, new HashSet<string>());
        Assert.Equal("cat", word);
        Assert.Equal(1, next);
    }

    [Fact]
    public void NextUntyped_SkipsAlreadyTypedWords()
    {
        var words = new List<string> { "cat", "dog", "fish" };
        var typed = new HashSet<string> { "cat" };
        var (word, next) = SuggestionLogic.NextUntyped(words, 0, typed);
        Assert.Equal("dog", word);
        Assert.Equal(2, next);
    }

    [Fact]
    public void NextUntyped_SkipsMultiWordEntries()
    {
        var words = new List<string> { "two words", "single" };
        var (word, next) = SuggestionLogic.NextUntyped(words, 0, new HashSet<string>());
        Assert.Equal("single", word);
        Assert.Equal(2, next);
    }

    [Fact]
    public void NextUntyped_WrapsAroundToTheStart()
    {
        var words = new List<string> { "cat", "dog", "fish" };
        var typed = new HashSet<string> { "fish" };
        // Starting past the end of the untyped tail should wrap back to index 0.
        var (word, next) = SuggestionLogic.NextUntyped(words, 2, typed);
        Assert.Equal("cat", word);
        Assert.Equal(1, next);
    }

    [Fact]
    public void NextUntyped_AllTyped_ReturnsEmptyAndSameIndex()
    {
        var words = new List<string> { "cat", "dog" };
        var typed = new HashSet<string> { "cat", "dog" };
        var (word, next) = SuggestionLogic.NextUntyped(words, 1, typed);
        Assert.Equal("", word);
        Assert.Equal(1, next);
    }

    [Fact]
    public void NextUntyped_EmptyList_ReturnsEmptyAndSameIndex()
    {
        var (word, next) = SuggestionLogic.NextUntyped(new List<string>(), 5, new HashSet<string>());
        Assert.Equal("", word);
        Assert.Equal(5, next);
    }

    [Fact]
    public void NextUntyped_NegativeStartIndex_WrapsIntoRange()
    {
        var words = new List<string> { "cat", "dog", "fish" };
        var (word, next) = SuggestionLogic.NextUntyped(words, -1, new HashSet<string>());
        // -1 normalizes to index 2 ("fish"); the returned resume index is
        // idx + 1 (unwrapped), matching NextUntyped's own modulo-on-read
        // convention rather than pre-wrapping the output.
        Assert.Equal("fish", word);
        Assert.Equal(3, next);
    }
}
