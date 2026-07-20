// Streams a URL to a local file. Port of app/download_windows.go.
using System.IO;
using System.Net.Http;

namespace WordBombTool;

public static class Downloader
{
    private static readonly HttpClient Http = new();

    public static void DownloadFile(string url, string dst)
    {
        using var resp = Http.GetAsync(url).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var f = File.Create(dst);
        src.CopyTo(f);
    }
}
