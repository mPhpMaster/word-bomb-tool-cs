// Config holds application-wide constants, tunables, file paths and the UI
// theme. It is the C#/WPF port of the original config.py / config.go.
using System.IO;
using System.Text.Json.Serialization;

namespace WordBombTool;

/// <summary>A screen rectangle to capture. JSON property names match the
/// original Python/Go apps so ocr_config.json stays compatible.</summary>
public sealed class Region
{
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("top")] public int Top { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }

    public Region() { }
    public Region(int left, int top, int width, int height)
    {
        Left = left; Top = top; Width = width; Height = height;
    }
}

public static class Theme
{
    public const string Bg = "#282c34";
    public const string Fg = "#abb2bf";
    public const string LogBg = "#21252b";
    public const string LogFg = "#98c379";
    public const string SelectBg = "#3e4452";
    public const string Accent = "#61afef";
    public const string Error = "#e06c75";
    public const string Success = "#98c379";
    public const string Warning = "#e5c07b";
    public const string FontFamily = "Consolas";
    public const int DefinitionFont = 14;
    public const int FontSize = 10;
    public const int FontSizeSmall = 9;
    public const double FocusedAlpha = 1.0;
    public const double UnfocusedAlpha = 0.85;
}

public static class AppConfig
{
    /// <summary>Directory next to the running executable, used for config/log/metrics
    /// files (matches the frozen-exe behaviour of the original apps).</summary>
    public static readonly string BaseDir =
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
        ?? AppContext.BaseDirectory;

    public static readonly string ConfigFile = Path.Combine(BaseDir, "ocr_config.json");
    public static readonly string LogFile = Path.Combine(BaseDir, "ocr_helper.log");
    public static readonly string MetricsFile = Path.Combine(BaseDir, "ocr_metrics.json");
    public static readonly string TesseractInstallerPath = Path.Combine(BaseDir, "tesseract_installer.exe");

    public const double OCRInterval = 0.5;
    public const double OCRIntervalMin = 0.1;
    public const double OCRIntervalMax = 10.0;
    public const int OCRTimeoutSeconds = 1;

    // Letter OCR is only trusted when two captures in a row agree, which filters
    // one-off misreads of transition frames (letters animating in / fading out).
    // Up to OCRStableAttempts captures, OCRStableGapMs apart.
    public const int OCRStableAttempts = 5;
    public const int OCRStableGapMs = 120;
    // How many times one action may switch to newly read letters before giving up.
    public const int MaxLetterChanges = 3;

    // Deliberately NOT OCRTimeoutSeconds. One second has to cover DNS + TCP + TLS +
    // request + response to api.datamuse.com; on a cold connection or any link with
    // >250ms RTT that expires routinely, and the user just sees an empty suggestion
    // list that looks identical to "no words match these letters".
    public const int ApiTimeoutSeconds = 8;

    public const double TypingDelay = 0.28;
    public const double TypingDelayMin = 0.01;
    public const double TypingDelayMax = 2.0;

    public const string TurnGateNeedYour = "your";
    public const string TurnGateNeedTurn = "turn";

    /// <summary>True when OCR'd turn-box text indicates it is actually the player's
    /// turn. <paramref name="text"/> is expected to be already normalised (lowercase,
    /// whitespace stripped) by the OCR layer.</summary>
    /// <remarks>
    /// This used to end with `|| (hasYour &amp;&amp; text.Length >= 4)`. That clause was
    /// always true whenever hasYour was — "your" is itself 4 characters — so the whole
    /// expression collapsed to a bare Contains("your"), and auto mode would fire on any
    /// stray "your"/"yours"/"your move" picked up from chat or adjacent UI. Requiring
    /// both words (or the run-together "yourturn") is the behaviour that was intended.
    /// </remarks>
    public static bool TurnGateAccepts(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains("yourturn")) return true;
        return text.Contains(TurnGateNeedYour) && text.Contains(TurnGateNeedTurn);
    }

    public static double ClampOCRInterval(double v)
    {
        if (double.IsNaN(v)) return OCRInterval;
        if (v < OCRIntervalMin) return OCRIntervalMin;
        if (v > OCRIntervalMax) return OCRIntervalMax;
        return v;
    }

    public static double ClampTypingDelay(double v)
    {
        if (double.IsNaN(v)) return TypingDelay;
        if (v < TypingDelayMin) return TypingDelayMin;
        if (v > TypingDelayMax) return TypingDelayMax;
        return v;
    }

    public const string DatamuseAPI = "https://api.datamuse.com/words";

    public const int CacheExpiryMinutes = 5;
    public const int MaxSuggestionsDisplay = 50;
    public const int MaxTypedHistory = 1000;
    public const int UndoBufferSize = 20;

    public const int MaxWorkerThreads = 2;

    public static readonly string[] SearchModes =
        { "Starts With", "Ends With", "Contains", "Rhymes", "Related Words" };
    public static readonly string[] SortModes =
        { "Shortest", "Longest", "Random", "Frequency" };

    public const string TesseractInstallerURL =
        "https://github.com/tesseract-ocr/tesseract/releases/download/5.5.0/tesseract-ocr-w64-setup-5.5.0.20241111.exe";

    public const int MaxLogQueueSize = 500;

    public const string StatusOnline = "[OK] Online";
    public const string StatusOffline = "[XX] Offline";
    public const string StatusTimeout = "[--] Timeout";
    public const string StatusError = "[!!] Error";
}
