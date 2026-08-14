// Streams a URL to a local file. Port of app/download_windows.go.
using System.IO;
using System.Net.Http;

namespace WordBombTool;

public static class Downloader
{
    // Timeout.InfiniteTimeSpan, not the 100s default: the only caller downloads a
    // ~50MB Tesseract installer, which exceeds 100s on any link slower than ~5Mbit/s.
    // The default made the download abort with a bare TaskCanceledException that
    // surfaced to the user as an unexplained "Installation failed".
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static void DownloadFile(string url, string dst)
    {
        // ResponseHeadersRead so the body streams to disk as it arrives. The default
        // (ResponseContentRead) buffers the entire file in memory first.
        using var resp = Http.Send(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        // Write to a temp file and move it into place so an interrupted download can't
        // leave a truncated installer that later fails to run.
        var tmp = dst + ".part";
        try
        {
            using (var src = resp.Content.ReadAsStream())
            using (var f = File.Create(tmp))
            {
                src.CopyTo(f);
            }
            File.Move(tmp, dst, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
