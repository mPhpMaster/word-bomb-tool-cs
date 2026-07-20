// Image preprocessing for OCR: grayscale -> autocontrast -> upscale -> Otsu
// binarize -> auto-invert -> pad. Port of preprocess.go (originally ported from
// the Python Pillow pipeline).
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WordBombTool;

/// <summary>A simple 8-bit grayscale image buffer (mirrors Go's image.Gray).</summary>
public sealed class GrayImage
{
    public readonly int Width;
    public readonly int Height;
    public readonly byte[] Pix;

    public GrayImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pix = new byte[Math.Max(0, width) * Math.Max(0, height)];
    }

    public byte this[int x, int y]
    {
        get => Pix[y * Width + x];
        set => Pix[y * Width + x] = value;
    }
}

public static class OcrPreprocess
{
    /// <summary>Converts any bitmap to 8-bit grayscale using ITU-R 601-2 luma,
    /// matching PIL's convert("L").</summary>
    public static GrayImage ToGray(Bitmap src)
    {
        var w = src.Width;
        var h = src.Height;
        var dst = new GrayImage(w, h);

        using var bmp = src.PixelFormat == PixelFormat.Format32bppArgb
            ? src
            : src.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = 0; y < h; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < w; x++)
                    {
                        var b = row[x * 4 + 0];
                        var g = row[x * 4 + 1];
                        var r = row[x * 4 + 2];
                        var lum = (299 * r + 587 * g + 114 * b) / 1000;
                        dst[x, y] = (byte)lum;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
            if (!ReferenceEquals(bmp, src)) bmp.Dispose();
        }
        return dst;
    }

    /// <summary>Stretches the gray histogram so the darkest kept pixel maps to 0
    /// and the brightest to 255. cutoff is the percentage of pixels trimmed from
    /// each end of the histogram before computing the range.</summary>
    public static GrayImage AutoContrast(GrayImage img, int cutoff)
    {
        var hist = new int[256];
        foreach (var v in img.Pix) hist[v]++;
        var total = img.Pix.Length;
        if (total == 0) return img;

        int lo, hi;
        if (cutoff > 0)
        {
            var trim = total * cutoff / 100;
            var n = 0;
            for (lo = 0; lo < 256; lo++) { n += hist[lo]; if (n > trim) break; }
            n = 0;
            for (hi = 255; hi >= 0; hi--) { n += hist[hi]; if (n > trim) break; }
        }
        else
        {
            for (lo = 0; lo < 256 && hist[lo] == 0; lo++) { }
            for (hi = 255; hi >= 0 && hist[hi] == 0; hi--) { }
        }
        if (hi <= lo) return img;

        var lut = new byte[256];
        var scale = 255.0 / (hi - lo);
        for (var i = 0; i < 256; i++)
        {
            var v = (i - lo) * scale;
            if (v < 0) v = 0; else if (v > 255) v = 255;
            lut[i] = (byte)(v + 0.5);
        }

        var res = new GrayImage(img.Width, img.Height);
        for (var i = 0; i < img.Pix.Length; i++) res.Pix[i] = lut[img.Pix[i]];
        return res;
    }

    /// <summary>Binarizes: pixels below t become 0, others 255.</summary>
    public static GrayImage Threshold(GrayImage img, byte t)
    {
        var res = new GrayImage(img.Width, img.Height);
        for (var i = 0; i < img.Pix.Length; i++) res.Pix[i] = img.Pix[i] < t ? (byte)0 : (byte)255;
        return res;
    }

    /// <summary>Enlarges tiny crops (Tesseract struggles on small UI text) while
    /// preserving aspect ratio, capped at 4x.</summary>
    public static GrayImage UpscaleIfSmall(GrayImage img, int minW, int minH)
    {
        var w = img.Width;
        var h = img.Height;
        if (w <= 0 || h <= 0) return img;

        var sx = Math.Max(1.0, (double)minW / w);
        var sy = Math.Max(1.0, (double)minH / h);
        var scale = Math.Min(4.0, Math.Max(sx, sy));
        if (scale <= 1.01) return img;

        var nw = (int)(w * scale);
        var nh = (int)(h * scale);

        using var srcBmp = ToBitmap(img);
        using var dstBmp = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dstBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(srcBmp, new Rectangle(0, 0, nw, nh));
        }
        return ToGray(dstBmp);
    }

    /// <summary>Computes an optimal global threshold via Otsu's method so
    /// binarization adapts to the button/background brightness.</summary>
    public static byte OtsuLevel(GrayImage img)
    {
        var hist = new int[256];
        foreach (var v in img.Pix) hist[v]++;
        var total = img.Pix.Length;
        if (total == 0) return 128;

        double sum = 0;
        for (var i = 0; i < 256; i++) sum += i * (double)hist[i];

        double sumB = 0;
        var wB = 0;
        var maxVar = -1.0;
        int first = 128, last = 128;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            var wF = total - wB;
            if (wF == 0) break;
            sumB += t * (double)hist[t];
            var mB = sumB / wB;
            var mF = (sum - sumB) / wF;
            var between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > maxVar) { maxVar = between; first = t; last = t; }
            else if (between == maxVar) { last = t; }
        }
        // The midpoint of the optimal plateau places the cut cleanly between the
        // two pixel clusters (matters for flat, bimodal histograms).
        return (byte)((first + last) / 2);
    }

    /// <summary>Reports whether most pixels are dark (i.e. a dark background).</summary>
    public static bool MajorityDark(GrayImage img)
    {
        var dark = 0;
        foreach (var v in img.Pix) if (v < 128) dark++;
        return dark * 2 > img.Pix.Length;
    }

    /// <summary>Returns a photo-negative of the image.</summary>
    public static GrayImage Invert(GrayImage img)
    {
        var res = new GrayImage(img.Width, img.Height);
        for (var i = 0; i < img.Pix.Length; i++) res.Pix[i] = (byte)(255 - img.Pix[i]);
        return res;
    }

    /// <summary>Adds a uniform border (a "quiet zone") around the image; Tesseract
    /// is noticeably more accurate when text has margin around it.</summary>
    public static GrayImage Pad(GrayImage img, int pad, byte fill)
    {
        var w = img.Width;
        var h = img.Height;
        var res = new GrayImage(w + 2 * pad, h + 2 * pad);
        Array.Fill(res.Pix, fill);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                res[x + pad, y + pad] = img[x, y];
        return res;
    }

    /// <summary>Produces a clean, upscaled, black-text-on-white image for the
    /// letter region: grayscale -> autocontrast -> upscale -> Otsu binarize ->
    /// auto-invert -> pad. Auto-invert is what makes light-text-on-dark UI buttons
    /// read reliably, since Tesseract expects dark text on a light background.</summary>
    public static GrayImage PreprocessLetters(Bitmap src)
    {
        var g = ToGray(src);
        g = AutoContrast(g, 2);
        g = UpscaleIfSmall(g, 260, 100);
        g = Threshold(g, OtsuLevel(g));
        if (MajorityDark(g)) g = Invert(g);
        return Pad(g, 14, 255);
    }

    /// <summary>The softer pipeline for colored "YOUR TURN" UI: grayscale,
    /// autocontrast (cutoff 1), upscale small crops.</summary>
    public static GrayImage PreprocessTurnGate(Bitmap src)
    {
        var g = ToGray(src);
        g = AutoContrast(g, 1);
        return UpscaleIfSmall(g, 140, 48);
    }

    public static Bitmap ToBitmap(GrayImage img)
    {
        var bmp = new Bitmap(Math.Max(1, img.Width), Math.Max(1, img.Height), PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = 0; y < img.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < img.Width; x++)
                    {
                        var v = img[x, y];
                        row[x * 4 + 0] = v;
                        row[x * 4 + 1] = v;
                        row[x * 4 + 2] = v;
                        row[x * 4 + 3] = 255;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }
}
