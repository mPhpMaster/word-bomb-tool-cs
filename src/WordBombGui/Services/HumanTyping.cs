// Types a word with variable gaps between keys so the rhythm isn't perfectly
// metronomic. Port of app/typing_windows.go (_type_word_human_like in main.py).
namespace WordBombTool;

public static class HumanTyping
{
    /// <summary>baseDelay is the typical seconds between keystrokes;
    /// interKeyScale nudges speed without changing the saved setting.</summary>
    public static void TypeWordHumanLike(string word, double baseDelay, double interKeyScale)
    {
        if (string.IsNullOrEmpty(word)) return;
        if (baseDelay <= 0)
        {
            InputSimulator.TypeString(word);
            return;
        }

        var baseVal = Math.Max(AppConfig.TypingDelayMin, baseDelay * interKeyScale);
        var low = Math.Max(0.03, baseVal * 0.52);
        var high = Math.Min(AppConfig.TypingDelayMax, baseVal * 2.45);
        var mode = Math.Clamp(baseVal, low, high);

        for (var i = 0; i < word.Length; i++)
        {
            InputSimulator.TypeChar(word[i]);
            if (i >= word.Length - 1) break;

            // Hesitation / micro-pauses.
            var r = Random.Shared.NextDouble();
            if (r < 0.34) SleepSeconds(Uniform(0.1, 0.34));
            else if (r < 0.42) SleepSeconds(Uniform(0.16, 0.45));
            SleepSeconds(Triangular(low, high, mode));
        }
    }

    public static double Uniform(double a, double b) => a + Random.Shared.NextDouble() * (b - a);

    /// <summary>Samples the triangular distribution on [low,high] with the given mode.</summary>
    public static double Triangular(double low, double high, double mode)
    {
        if (high <= low) return low;
        var u = Random.Shared.NextDouble();
        var c = (mode - low) / (high - low);
        if (u > c)
        {
            u = 1 - u;
            c = 1 - c;
            (low, high) = (high, low);
        }
        return low + (high - low) * Math.Sqrt(u * c);
    }

    public static void SleepSeconds(double s)
    {
        if (s <= 0) return;
        Thread.Sleep(TimeSpan.FromSeconds(s));
    }
}
