// Captures screen regions and runs Tesseract on them. Shells out to the
// tesseract executable (found on PATH or the default install location) so no
// native binding is required. Port of ocr_processor.py / ocr.go.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WordBombTool;

public sealed class OcrProcessor
{
    private const string LetterWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private readonly struct CacheEntry
    {
        public CacheEntry(string text, DateTime when) { Text = text; When = when; }
        public string Text { get; }
        public DateTime When { get; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private string _tesseractPath;

    public OcrProcessor()
    {
        _tesseractPath = FindTesseractPath();
    }

    public string TesseractPath => _tesseractPath;

    /// <summary>Looks for tesseract on PATH, then at the default Windows install location.</summary>
    public static string FindTesseractPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, "tesseract.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore malformed PATH entries */ }
        }
        const string def = @"C:\Program Files\Tesseract-OCR\tesseract.exe";
        if (File.Exists(def)) return def;
        return "";
    }

    /// <summary>Reports whether tesseract can be invoked.</summary>
    public bool Available()
    {
        if (_tesseractPath == "") _tesseractPath = FindTesseractPath();
        if (_tesseractPath == "") return false;
        try
        {
            var psi = MakeStartInfo(_tesseractPath);
            psi.ArgumentList.Add("--version");
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            return proc.WaitForExit(5000) && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void ClearCache()
    {
        lock (_lock) _cache.Clear();
        AppLog.Infof("WBT cache cleared");
    }

    /// <summary>Captures the region, runs the harsh (letters-only) pipeline and
    /// returns lowercase letters. ok is false when nothing was recognised.</summary>
    public (string letters, bool ok) PerformOCR(Region region)
    {
        var sw = Stopwatch.StartNew();

        Bitmap img;
        try
        {
            img = Capture(region);
        }
        catch (Exception ex)
        {
            AppLog.Errorf("WBT Error: {0}", ex.Message);
            return ("", false);
        }

        using (img)
        {
            var hash = HashImage(img);
            if (TryCacheGet(hash, out var cached)) return (cached, cached != "");

            var pre = OcrPreprocess.PreprocessLetters(img);

            // Run several page-segmentation modes and keep the reading that the most
            // modes agree on (majority vote). PSM 8 (single word) and 7 (single line)
            // suit the game's short letter clusters; 6 and 13 cover odd layouts. Voting
            // is far more robust to a single mode misfiring than trusting one run, and
            // unlike tesseract's TSV "confidence" (which is unreliable across builds) it
            // needs no engine-specific metadata.
            var votes = new Dictionary<string, int>();
            var order = new List<string>(); // first-seen order -> tie-break favours earlier (PSM 8) reads
            foreach (var psm in new[] { "8", "7", "6", "13" })
            {
                var raw = RunTesseract(pre, "--psm", psm, "-c", "tessedit_char_whitelist=" + LetterWhitelist);
                if (raw == null) continue;
                var cand = KeepLetters(raw);
                if (cand == "") continue;
                if (!votes.ContainsKey(cand)) { order.Add(cand); votes[cand] = 0; }
                votes[cand]++;
            }

            var best = "";
            var bestN = 0;
            foreach (var c in order)
            {
                if (votes[c] > bestN) { bestN = votes[c]; best = c; }
            }

            var letters = best;
            if (letters != "") CachePut(hash, letters);
            AppLog.Infof("WBT completed in {0:F2}ms (\"{1}\", {2}/4 modes agreed)",
                sw.Elapsed.TotalMilliseconds, best, bestN);
            return (letters, letters != "");
        }
    }

    /// <summary>Captures the region and returns lowercase alphanumerics for
    /// auto-mode "YOUR TURN" detection, trying the soft pipeline with several PSMs
    /// and falling back to the harsh pipeline.</summary>
    public string PerformOCRTurnGate(Region region)
    {
        Bitmap img;
        try
        {
            img = Capture(region);
        }
        catch (Exception ex)
        {
            AppLog.Errorf("Turn gate WBT error: {0}", ex.Message);
            return "";
        }

        using (img)
        {
            string Run(GrayImage g, string psm)
            {
                var raw = RunTesseract(g, "--psm", psm);
                return raw == null ? "" : KeepAlnum(raw);
            }

            // 1) Soft path (colored buttons + white text).
            var soft = OcrPreprocess.PreprocessTurnGate(img);
            var best = "";
            foreach (var psm in new[] { "6", "7", "8", "13" })
            {
                var t = Run(soft, psm);
                if (t.Length > best.Length) best = t;
            }
            if (best != "") return best;

            // 2) Harsh binarization fallback.
            var hard = OcrPreprocess.PreprocessLetters(img);
            foreach (var psm in new[] { "7", "6", "8" })
            {
                var t = Run(hard, psm);
                if (t.Length > best.Length) best = t;
            }
            return best;
        }
    }

    private bool TryCacheGet(string hash, out string text)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(hash, out var e))
            {
                if (DateTime.Now - e.When < TimeSpan.FromMinutes(AppConfig.CacheExpiryMinutes))
                {
                    text = e.Text;
                    return true;
                }
                _cache.Remove(hash);
            }
        }
        text = "";
        return false;
    }

    private void CachePut(string hash, string text)
    {
        lock (_lock) _cache[hash] = new CacheEntry(text, DateTime.Now);
    }

    /// <summary>Pipes a PNG of img to `tesseract stdin stdout &lt;args...&gt;` and
    /// returns the recognised text, or null on failure.</summary>
    private string? RunTesseract(GrayImage img, params string[] args)
    {
        if (_tesseractPath == "") _tesseractPath = FindTesseractPath();
        if (_tesseractPath == "") return null;

        byte[] pngBytes;
        using (var bmp = OcrPreprocess.ToBitmap(img))
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        try
        {
            var psi = MakeStartInfo(_tesseractPath);
            psi.ArgumentList.Add("stdin");
            psi.ArgumentList.Add("stdout");
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            using (var stdin = proc.StandardInput.BaseStream)
                stdin.Write(pngBytes, 0, pngBytes.Length);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(15000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }
            var stdout = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }

    private static ProcessStartInfo MakeStartInfo(string exe)
    {
        return new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
    }

    private static Bitmap Capture(Region region)
    {
        var bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Left, region.Top, 0, 0, new Size(region.Width, region.Height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static string HashImage(Bitmap img)
    {
        var w = img.Width;
        var h = img.Height;
        var data = img.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var hash = MD5.HashData(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
        finally { img.UnlockBits(data); }
    }

    private static string KeepLetters(string s)
    {
        var sb = new StringBuilder();
        foreach (var r in s) if (char.IsLetter(r)) sb.Append(char.ToLowerInvariant(r));
        return sb.ToString();
    }

    private static string KeepAlnum(string s)
    {
        var sb = new StringBuilder();
        foreach (var r in s) if (char.IsLetterOrDigit(r)) sb.Append(char.ToLowerInvariant(r));
        return sb.ToString();
    }
}
