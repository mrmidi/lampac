using TvClient.Services;
using TvClient.Models;
using Xunit;

namespace TvClient.Tests;

public class PlaybackSelectionTests
{
    [Fact]
    public void PickQuality_ShouldPreferRequestedThenHighest()
    {
        var q = new[] { "720p", "2160p", "1080p" };

        Assert.Equal("1080p", PlaybackSelection.PickQuality(q, "1080"));
        Assert.Equal("2160p", PlaybackSelection.PickQuality(q, null));
    }

    [Fact]
    public void ToPlayResult_ShouldPickQualityUrl()
    {
        var candidate = new PlayCandidateDto(
            "https://example.com/auto.m3u8",
            "",
            "play",
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["720p"] = "https://example.com/720.m3u8",
                ["1080p"] = "https://example.com/1080.m3u8"
            },
            Array.Empty<SubtitleOptionDto>()
        );

        var result = PlaybackSelection.ToPlayResult(candidate, "1080p", out var selected);

        Assert.Equal("1080p", selected);
        Assert.Equal("https://example.com/1080.m3u8", result.url);
        Assert.Equal("hls", result.stream_type);
    }

    [Fact]
    public void PickQuality_EmptyList_ShouldReturnEmpty()
    {
        Assert.Equal(string.Empty, PlaybackSelection.PickQuality(Array.Empty<string>()));
        Assert.Equal(string.Empty, PlaybackSelection.PickQuality(null));
    }

    [Fact]
    public void PickQuality_RequestedNotAvailable_ShouldReturnHighest()
    {
        var q = new[] { "720p", "1080p" };
        Assert.Equal("1080p", PlaybackSelection.PickQuality(q, "2160p"));
    }

    [Fact]
    public void DetectStreamType_ShouldClassifyUrls()
    {
        Assert.Equal("hls", PlaybackSelection.DetectStreamType("https://cdn.example.com/index.m3u8"));
        Assert.Equal("dash", PlaybackSelection.DetectStreamType("https://cdn.example.com/manifest.mpd"));
        Assert.Equal("file", PlaybackSelection.DetectStreamType("https://cdn.example.com/video.mp4"));
        Assert.Equal("unknown", PlaybackSelection.DetectStreamType("https://cdn.example.com/stream"));
        Assert.Equal("unknown", PlaybackSelection.DetectStreamType(null));
    }

    [Fact]
    public void ToPlayResult_WithNullCandidate_ShouldReturnEmptyUrl()
    {
        var result = PlaybackSelection.ToPlayResult(null, "1080p", out var selected);
        Assert.Equal(string.Empty, result.url);
        Assert.Equal(string.Empty, selected);
    }

    [Fact]
    public void ToPlayResult_NoQualityMap_ShouldFallbackToUrl()
    {
        var candidate = new PlayCandidateDto(
            "https://example.com/auto.m3u8",
            "",
            "play",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Array.Empty<SubtitleOptionDto>()
        );

        var result = PlaybackSelection.ToPlayResult(candidate, "1080p", out _);
        Assert.Equal("https://example.com/auto.m3u8", result.url);
    }
}
