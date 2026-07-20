// Port of the Go config tests: boundary clamping and NaN handling for the
// two user-adjustable tunables (OCR poll interval, typing delay).
using WordBombTool;
using Xunit;

namespace WordBombTool.Tests;

public class ConfigTests
{
    [Theory]
    [InlineData(0.05, AppConfig.OCRIntervalMin)]   // below min -> clamped up
    [InlineData(20.0, AppConfig.OCRIntervalMax)]   // above max -> clamped down
    [InlineData(AppConfig.OCRIntervalMin, AppConfig.OCRIntervalMin)] // exactly at min -> unchanged
    [InlineData(AppConfig.OCRIntervalMax, AppConfig.OCRIntervalMax)] // exactly at max -> unchanged
    [InlineData(1.0, 1.0)] // mid-range -> unchanged
    public void ClampOCRInterval_ClampsToRange(double input, double expected)
    {
        Assert.Equal(expected, AppConfig.ClampOCRInterval(input));
    }

    [Fact]
    public void ClampOCRInterval_NaN_FallsBackToDefault()
    {
        Assert.Equal(AppConfig.OCRInterval, AppConfig.ClampOCRInterval(double.NaN));
    }

    [Theory]
    [InlineData(0.001, AppConfig.TypingDelayMin)]  // below min -> clamped up
    [InlineData(5.0, AppConfig.TypingDelayMax)]    // above max -> clamped down
    [InlineData(AppConfig.TypingDelayMin, AppConfig.TypingDelayMin)]
    [InlineData(AppConfig.TypingDelayMax, AppConfig.TypingDelayMax)]
    [InlineData(0.5, 0.5)]
    public void ClampTypingDelay_ClampsToRange(double input, double expected)
    {
        Assert.Equal(expected, AppConfig.ClampTypingDelay(input));
    }

    [Fact]
    public void ClampTypingDelay_NaN_FallsBackToDefault()
    {
        Assert.Equal(AppConfig.TypingDelay, AppConfig.ClampTypingDelay(double.NaN));
    }

    [Fact]
    public void ClampOCRInterval_PositiveInfinity_ClampsToMax()
    {
        Assert.Equal(AppConfig.OCRIntervalMax, AppConfig.ClampOCRInterval(double.PositiveInfinity));
    }

    [Fact]
    public void ClampTypingDelay_NegativeInfinity_ClampsToMin()
    {
        Assert.Equal(AppConfig.TypingDelayMin, AppConfig.ClampTypingDelay(double.NegativeInfinity));
    }
}
