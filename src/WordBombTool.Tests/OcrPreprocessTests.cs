// Port of ocr/preprocess_test.go: verifies the preprocessing pipeline's
// invariants (binary output, auto-invert to a light background, Otsu threshold
// placement, small-crop upscaling) rather than exact pixel values, since the
// pipeline is deliberately adaptive.
using System.Drawing;
using System.Drawing.Imaging;
using WordBombTool;
using Xunit;

namespace WordBombTool.Tests;

public class OcrPreprocessTests
{
    private static void AssertBinaryMostlyWhite(GrayImage img, string name)
    {
        var white = 0;
        foreach (var v in img.Pix)
        {
            Assert.True(v == 0 || v == 255, $"{name}: non-binary pixel after threshold: {v}");
            if (v == 255) white++;
        }
        // The pipeline guarantees dark text on a light background, so most
        // pixels (background + padding) must be white regardless of the input
        // polarity.
        Assert.True(white * 2 > img.Pix.Length, $"{name}: expected majority-white (dark-text-on-light), got {white}/{img.Pix.Length} white");
    }

    [Fact]
    public void PreprocessLetters_LightBackground_IsBinaryAndMajorityWhite()
    {
        using var light = new Bitmap(30, 12, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(light))
        {
            g.Clear(Color.FromArgb(235, 235, 235));
            using var brush = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillRectangle(brush, 12, 3, 6, 6); // dark glyph
        }
        AssertBinaryMostlyWhite(OcrPreprocess.PreprocessLetters(light), "light-bg");
    }

    [Fact]
    public void PreprocessLetters_DarkBackground_AutoInvertsToMajorityWhite()
    {
        // Light "text" glyph on a dark background (the game's case). Auto-invert
        // must still yield a light background.
        using var dark = new Bitmap(30, 12, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dark))
        {
            g.Clear(Color.FromArgb(25, 25, 30));
            using var brush = new SolidBrush(Color.FromArgb(240, 240, 240));
            g.FillRectangle(brush, 12, 3, 6, 6); // light glyph
        }
        AssertBinaryMostlyWhite(OcrPreprocess.PreprocessLetters(dark), "dark-bg");
    }

    [Fact]
    public void Otsu_SeparatesBimodalHistogram()
    {
        var g = new GrayImage(100, 1);
        for (var i = 0; i < 100; i++) g.Pix[i] = (byte)(i < 50 ? 30 : 220);

        var level = OcrPreprocess.OtsuLevel(g);
        Assert.True(level > 30 && level < 220, $"otsu level {level} should fall between the two clusters");
    }

    [Fact]
    public void UpscaleIfSmall_GrowsToMinimum_CappedAt4x()
    {
        var g = new GrayImage(20, 10);
        var outImg = OcrPreprocess.UpscaleIfSmall(g, 140, 48);
        Assert.True(outImg.Width > 20, $"expected upscaled width, got {outImg.Width}");
        Assert.True(outImg.Width <= 80, $"width exceeded 4x cap: {outImg.Width}");
    }

    [Fact]
    public void UpscaleIfSmall_LeavesLargeImagesAlone()
    {
        var g = new GrayImage(400, 200);
        var outImg = OcrPreprocess.UpscaleIfSmall(g, 140, 48);
        Assert.Equal(400, outImg.Width);
        Assert.Equal(200, outImg.Height);
    }
}
