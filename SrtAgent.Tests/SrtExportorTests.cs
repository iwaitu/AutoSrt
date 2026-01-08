using Xunit.Abstractions;

namespace SrtAgent.Tests;

public sealed class SrtExportorTests
{
    private readonly ITestOutputHelper _output;
    public SrtExportorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ExportEmbeddedSubtitlesAsSrtTextAsync_WithValidVideo_ReturnsNonEmptySrt()
    {
        TestAssets.AssertSampleMkvExists();

        var exportor = new SrtExportor();

        var srt = await exportor.ExportEmbeddedSubtitlesAsSrtTextAsync(TestAssets.SampleMkvPath);

        _output.WriteLine($"Extracted SRT content: {srt.Substring(100)}");
        Assert.False(string.IsNullOrWhiteSpace(srt));
        Assert.Contains("-->", srt);
        
    }

    [Fact]
    public async Task ExportEmbeddedSubtitlesAsSrtTextAsync_WithEmptyPath_Throws()
    {
        var exportor = new SrtExportor();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            exportor.ExportEmbeddedSubtitlesAsSrtTextAsync(""));
    }
}
