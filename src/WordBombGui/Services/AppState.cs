// AppState holds the central, thread-safe application state together with its
// persistence (config + metrics files). Port of state.py / state.go.
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordBombTool;

public sealed class TypingRecord
{
    public required string Word { get; init; }
    public DateTime Timestamp { get; init; }
    public required string SearchTerm { get; init; }
}

public sealed class Metrics
{
    public int TotalOCRAttempts;
    public int SuccessfulOCRCount;
    public int FailedOCRCount;
    public int APIRequests;
    public int SuccessfulAPICalls;
    public int FailedAPICalls;
    public double AverageOCRTimeMS;
    public double AverageAPITimeMS;
    public DateTime SessionStartTime = DateTime.Now;
}

/// <summary>A defensive snapshot of AppState, safe to read without holding any lock.</summary>
public sealed class AppStateSnapshot
{
    public Region? Region;
    public Region? TurnRegion;
    public List<string> Suggestions = new();
    public List<string> Definitions = new();
    public string LastOCRText = "";
    public bool AutoModeActive;
    public int CurrentModeIndex;
    public int CurrentSortModeIndex;
    public int SuggestionIndex;
    public int DefinitionIndex;
    public HashSet<string> TypedWordsHistory = new();
    public List<TypingRecord> TypingRecords = new();
    public int TotalTypedCount;
    public double TypingDelay;
    public double OCRInterval;
    public string APIStatus = AppConfig.StatusOnline;
    public Metrics Metrics = new();
}

/// <summary>Thread-safe manager for the mutable AppState, mirroring state.Manager.</summary>
public sealed class StateManager
{
    private readonly object _lock = new();

    private Region? _region;
    private Region? _turnRegion;
    private List<string> _suggestions = new();
    private List<string> _definitions = new();
    private string _lastOcrText = "";
    private bool _autoModeActive;
    private int _currentModeIndex = 2;
    private int _currentSortModeIndex = 2;
    private int _suggestionIndex;
    private int _definitionIndex;
    private readonly HashSet<string> _typedWordsHistory = new();
    private readonly List<TypingRecord> _typingRecords = new();
    private int _totalTypedCount;
    private double _typingDelay = AppConfig.TypingDelay;
    private double _ocrInterval = AppConfig.OCRInterval;
    private string _apiStatus = AppConfig.StatusOnline;
    private readonly Metrics _metrics = new();

    public AppStateSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new AppStateSnapshot
            {
                Region = _region,
                TurnRegion = _turnRegion,
                Suggestions = new List<string>(_suggestions),
                Definitions = new List<string>(_definitions),
                LastOCRText = _lastOcrText,
                AutoModeActive = _autoModeActive,
                CurrentModeIndex = _currentModeIndex,
                CurrentSortModeIndex = _currentSortModeIndex,
                SuggestionIndex = _suggestionIndex,
                DefinitionIndex = _definitionIndex,
                TypedWordsHistory = new HashSet<string>(_typedWordsHistory),
                TypingRecords = new List<TypingRecord>(_typingRecords),
                TotalTypedCount = _totalTypedCount,
                TypingDelay = _typingDelay,
                OCRInterval = _ocrInterval,
                APIStatus = _apiStatus,
                Metrics = new Metrics
                {
                    TotalOCRAttempts = _metrics.TotalOCRAttempts,
                    SuccessfulOCRCount = _metrics.SuccessfulOCRCount,
                    FailedOCRCount = _metrics.FailedOCRCount,
                    APIRequests = _metrics.APIRequests,
                    SuccessfulAPICalls = _metrics.SuccessfulAPICalls,
                    FailedAPICalls = _metrics.FailedAPICalls,
                    AverageOCRTimeMS = _metrics.AverageOCRTimeMS,
                    AverageAPITimeMS = _metrics.AverageAPITimeMS,
                    SessionStartTime = _metrics.SessionStartTime,
                },
            };
        }
    }

    /// <summary>Mutation helpers — small, explicit setters instead of a Go-style
    /// "give me the struct pointer" closure, which doesn't translate cleanly to C#.</summary>
    public void SetRegions(Region? region, Region? turnRegion)
    {
        lock (_lock) { _region = region; _turnRegion = turnRegion; }
    }

    public void SetTurnRegion(Region? turnRegion) { lock (_lock) _turnRegion = turnRegion; }

    public void SetAutoModeActive(bool active) { lock (_lock) _autoModeActive = active; }

    public void SetLastOcrText(string text) { lock (_lock) _lastOcrText = text; }

    public void SetSuggestions(List<string> suggestions, int index)
    {
        lock (_lock) { _suggestions = suggestions; _suggestionIndex = index; }
    }

    public void SetSuggestionIndex(int index) { lock (_lock) _suggestionIndex = index; }

    public void SetDefinitions(List<string> definitions, int index)
    {
        lock (_lock) { _definitions = definitions; _definitionIndex = index; }
    }

    public void SetSearchModeIndex(int index)
    {
        lock (_lock)
        {
            _currentModeIndex = index;
            _suggestions = new List<string>();
            _suggestionIndex = 0;
            _lastOcrText = "";
        }
    }

    public void SetSortModeIndex(int index, List<string>? resorted)
    {
        lock (_lock)
        {
            _currentSortModeIndex = index;
            if (resorted != null) { _suggestions = resorted; _suggestionIndex = 0; }
        }
    }

    public void SetTypingDelay(double v) { lock (_lock) _typingDelay = v; }
    public void SetOCRInterval(double v) { lock (_lock) _ocrInterval = v; }
    public void SetApiStatus(string status) { lock (_lock) _apiStatus = status; }

    public void ClearTypedHistory()
    {
        lock (_lock)
        {
            _typedWordsHistory.Clear();
            _typingRecords.Clear();
            _totalTypedCount = 0;
        }
    }

    public void AddTypingRecord(string word, string searchTerm)
    {
        lock (_lock)
        {
            _typingRecords.Add(new TypingRecord { Word = word, Timestamp = DateTime.Now, SearchTerm = searchTerm });
            _typedWordsHistory.Add(word);
            _totalTypedCount++;
            if (_typedWordsHistory.Count > AppConfig.MaxTypedHistory)
            {
                // Best-effort eviction, matching the original's arbitrary single-entry drop.
                foreach (var k in _typedWordsHistory) { _typedWordsHistory.Remove(k); break; }
            }
        }
    }

    public string UndoLastWord()
    {
        lock (_lock)
        {
            if (_typingRecords.Count == 0) return "";
            var rec = _typingRecords[^1];
            _typingRecords.RemoveAt(_typingRecords.Count - 1);
            _typedWordsHistory.Remove(rec.Word);
            if (_totalTypedCount > 0) _totalTypedCount--;
            return rec.Word;
        }
    }

    public void RecordOCRAttempt(bool success, double durationMs)
    {
        lock (_lock)
        {
            _metrics.TotalOCRAttempts++;
            if (success) _metrics.SuccessfulOCRCount++; else _metrics.FailedOCRCount++;
            var total = _metrics.AverageOCRTimeMS * (_metrics.TotalOCRAttempts - 1);
            _metrics.AverageOCRTimeMS = (total + durationMs) / _metrics.TotalOCRAttempts;
        }
    }

    public void RecordAPICall(bool success, double durationMs)
    {
        lock (_lock)
        {
            _metrics.APIRequests++;
            if (success) _metrics.SuccessfulAPICalls++; else _metrics.FailedAPICalls++;
            var total = _metrics.AverageAPITimeMS * (_metrics.APIRequests - 1);
            _metrics.AverageAPITimeMS = (total + durationMs) / _metrics.APIRequests;
        }
    }

    private sealed class PersistedConfig
    {
        [JsonPropertyName("region")] public Region? Region { get; set; }
        [JsonPropertyName("turn_region")] public Region? TurnRegion { get; set; }
        [JsonPropertyName("current_mode_index")] public int CurrentModeIndex { get; set; }
        [JsonPropertyName("current_sort_mode_index")] public int CurrentSortIndex { get; set; }
        [JsonPropertyName("total_typed_count")] public int TotalTypedCount { get; set; }
        [JsonPropertyName("typing_delay")] public double TypingDelay { get; set; }
        [JsonPropertyName("ocr_interval")] public double OCRInterval { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public void SaveState()
    {
        PersistedConfig cfg;
        lock (_lock)
        {
            cfg = new PersistedConfig
            {
                Region = _region,
                TurnRegion = _turnRegion,
                CurrentModeIndex = _currentModeIndex,
                CurrentSortIndex = _currentSortModeIndex,
                TotalTypedCount = _totalTypedCount,
                TypingDelay = _typingDelay,
                OCRInterval = _ocrInterval,
            };
        }
        try
        {
            var json = JsonSerializer.Serialize(cfg, JsonOpts);
            File.WriteAllText(AppConfig.ConfigFile, json);
            AppLog.Infof("Configuration saved");
        }
        catch (Exception ex)
        {
            AppLog.Errorf("Error saving config: {0}", ex.Message);
        }
    }

    public void LoadState()
    {
        if (!File.Exists(AppConfig.ConfigFile))
        {
            AppLog.Infof("No config file found, using defaults");
            return;
        }
        try
        {
            var json = File.ReadAllText(AppConfig.ConfigFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            lock (_lock)
            {
                if (root.TryGetProperty("region", out var r) && r.ValueKind == JsonValueKind.Object)
                    _region = JsonSerializer.Deserialize<Region>(r.GetRawText());
                if (root.TryGetProperty("turn_region", out var tr) && tr.ValueKind == JsonValueKind.Object)
                    _turnRegion = JsonSerializer.Deserialize<Region>(tr.GetRawText());
                if (root.TryGetProperty("current_mode_index", out var mi) && mi.TryGetInt32(out var miv))
                    _currentModeIndex = miv;
                if (root.TryGetProperty("current_sort_mode_index", out var si) && si.TryGetInt32(out var siv))
                    _currentSortModeIndex = siv;
                if (root.TryGetProperty("total_typed_count", out var tc) && tc.TryGetInt32(out var tcv))
                    _totalTypedCount = tcv;
                if (root.TryGetProperty("typing_delay", out var td) && td.TryGetDouble(out var tdv))
                    _typingDelay = AppConfig.ClampTypingDelay(tdv);
                if (root.TryGetProperty("ocr_interval", out var oi) && oi.TryGetDouble(out var oiv))
                    _ocrInterval = AppConfig.ClampOCRInterval(oiv);
            }
            AppLog.Infof("Configuration loaded from file");
        }
        catch (Exception ex)
        {
            AppLog.Errorf("Error loading config: {0}", ex.Message);
        }
    }

    public void SaveMetrics()
    {
        Metrics mt;
        lock (_lock)
        {
            mt = new Metrics
            {
                TotalOCRAttempts = _metrics.TotalOCRAttempts,
                SuccessfulOCRCount = _metrics.SuccessfulOCRCount,
                FailedOCRCount = _metrics.FailedOCRCount,
                APIRequests = _metrics.APIRequests,
                SuccessfulAPICalls = _metrics.SuccessfulAPICalls,
                FailedAPICalls = _metrics.FailedAPICalls,
                AverageOCRTimeMS = _metrics.AverageOCRTimeMS,
                AverageAPITimeMS = _metrics.AverageAPITimeMS,
                SessionStartTime = _metrics.SessionStartTime,
            };
        }
        try
        {
            var outObj = new Dictionary<string, object>
            {
                ["total_ocr_attempts"] = mt.TotalOCRAttempts,
                ["successful_ocr_count"] = mt.SuccessfulOCRCount,
                ["failed_ocr_count"] = mt.FailedOCRCount,
                ["api_requests"] = mt.APIRequests,
                ["successful_api_calls"] = mt.SuccessfulAPICalls,
                ["failed_api_calls"] = mt.FailedAPICalls,
                ["average_ocr_time_ms"] = mt.AverageOCRTimeMS,
                ["average_api_time_ms"] = mt.AverageAPITimeMS,
                ["session_start_time"] = mt.SessionStartTime.ToString("o"),
            };
            File.WriteAllText(AppConfig.MetricsFile, JsonSerializer.Serialize(outObj, JsonOpts));
            AppLog.Infof("Metrics saved");
        }
        catch (Exception ex)
        {
            AppLog.Errorf("Error saving metrics: {0}", ex.Message);
        }
    }
}
