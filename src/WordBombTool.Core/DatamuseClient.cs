// A small client for the Datamuse words API. Port of api_client.py / datamuse.go.
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Web;

namespace WordBombTool;

public sealed class DatamuseClient
{
    private readonly HttpClient _http;
    private string _baseUrl;
    private readonly object _statusLock = new();
    private string _status;

    public DatamuseClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(AppConfig.OCRTimeoutSeconds) };
        _baseUrl = AppConfig.DatamuseAPI;
        _status = AppConfig.StatusOnline;
    }

    /// <summary>Overrides the API endpoint (used in tests).</summary>
    public void SetBaseUrl(string url) => _baseUrl = url;

    public string Status()
    {
        lock (_statusLock) return _status;
    }

    private void SetStatus(string s)
    {
        lock (_statusLock) _status = s;
    }

    private sealed class WordItem
    {
        [JsonPropertyName("word")] public string Word { get; set; } = "";
        [JsonPropertyName("defs")] public List<string>? Defs { get; set; }
    }

    /// <summary>Fetches word suggestions for the given letters and search mode.
    /// Returns an empty list on any error (matching the original behaviour) and
    /// updates Status accordingly.</summary>
    public List<string> Suggestions(string letters, string mode)
    {
        if (string.IsNullOrEmpty(letters)) return new List<string>();

        var q = HttpUtility.ParseQueryString(string.Empty);
        q["max"] = AppConfig.MaxSuggestionsDisplay.ToString();
        switch (mode)
        {
            case "Starts With": q["sp"] = letters + "*"; break;
            case "Ends With": q["sp"] = "*" + letters; break;
            case "Contains": q["sp"] = "*" + letters + "*"; break;
            case "Rhymes": q["rel_rhy"] = letters; break;
            case "Related Words": q["rel_jja"] = letters; break;
        }

        var items = Get<List<WordItem>>(q);
        if (items == null) return new List<string>();

        var outList = new List<string>();
        foreach (var it in items)
        {
            // Keep single-word results only (no spaces), as in the original.
            if (!string.IsNullOrWhiteSpace(it.Word) && it.Word.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 1)
                outList.Add(it.Word);
        }
        if (outList.Count > AppConfig.MaxSuggestionsDisplay)
            outList = outList.GetRange(0, AppConfig.MaxSuggestionsDisplay);
        SetStatus(AppConfig.StatusOnline);
        return outList;
    }

    /// <summary>Fetches the definitions for a word. Returns an empty list on any
    /// error and updates Status accordingly.</summary>
    public List<string> Definitions(string word)
    {
        if (string.IsNullOrEmpty(word)) return new List<string>();

        var q = HttpUtility.ParseQueryString(string.Empty);
        q["sp"] = word;
        q["qe"] = "sp";
        q["md"] = "d";
        q["max"] = "1";

        var items = Get<List<WordItem>>(q);
        SetStatus(AppConfig.StatusOnline);
        if (items != null && items.Count > 0)
            return items[0].Defs ?? new List<string>();
        return new List<string>();
    }

    private T? Get<T>(System.Collections.Specialized.NameValueCollection q) where T : class
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.OCRTimeoutSeconds));
            var url = _baseUrl + "?" + q.ToString();
            var resp = _http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                SetStatus(AppConfig.StatusError);
                return null;
            }
            var body = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            return System.Text.Json.JsonSerializer.Deserialize<T>(body);
        }
        catch (OperationCanceledException)
        {
            SetStatus(AppConfig.StatusTimeout);
            return null;
        }
        catch (HttpRequestException)
        {
            SetStatus(AppConfig.StatusOffline);
            return null;
        }
        catch
        {
            SetStatus(AppConfig.StatusError);
            return null;
        }
    }
}
