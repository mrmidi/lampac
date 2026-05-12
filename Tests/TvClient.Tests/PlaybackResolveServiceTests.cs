using System.Collections.Generic;
using TvClient.Services;
using TvClient.Models;
using Xunit;

namespace TvClient.Tests;

public class PlaybackResolveServiceTests
{
    static PlayCandidateDto MakeCandidate(string url, string quality = "1080p")
        => new(url, "", "play", new Dictionary<string, string>(), new Dictionary<string, string> { [quality] = url }, Array.Empty<SubtitleOptionDto>());

    static EpisodeOptionDto MakeEpisode(int season, int ep, string status, string translation = "lf", string translationName = "LostFilm")
        => new(season, ep, $"E{ep}", $"E{ep}", status, "", null,
            status == "available" ? new[] { "1080p" } : Array.Empty<string>(),
            translationName, translation, Array.Empty<SubtitleOptionDto>(),
            status == "available" ? MakeCandidate($"https://x/s{season}e{ep}.m3u8") : null);

    static ProviderOptionsSnapshot MakeSnapshot(string media, IReadOnlyList<EpisodeOptionDto> episodes, int selectedSeason = 1, IReadOnlyList<TranslationOptionDto> translations = null)
    {
        translations ??= new[] { new TranslationOptionDto("lf", "LostFilm", true) };
        var options = new ProviderOptionsResponseDto(
            media, 1, "phantom", "ok", "rjson", "proxy", selectedSeason,
            new SelectionDto(selectedSeason, 1, "LostFilm", "lf", "1080p"),
            new[] { new SeasonOptionDto(selectedSeason, $"S{selectedSeason}", true) },
            translations,
            new[] { "1080p" },
            episodes,
            Array.Empty<PlannedEpisodeDto>(),
            Array.Empty<SubtitleOptionDto>(),
            Array.Empty<MovieStreamOptionDto>()
        );
        return new ProviderOptionsSnapshot
        {
            Response = options,
            Provider = "phantom",
            Context = new ProviderContext(media, 1, "tmdb", "", "T", "T", "en", 2022, "tt1", "1", media == "tv")
        };
    }

    [Fact]
    public void ResolveSeries_ShouldBuildQueue()
    {
        var snapshot = MakeSnapshot("tv", new[]
        {
            MakeEpisode(1, 1, "available"),
            MakeEpisode(1, 2, "available"),
        });

        var svc = new PlaybackResolveService();
        var res = svc.Resolve(new PlayResolveRequestDto("tv", 1, "tmdb", "phantom", 1, 1, "lf", "1080p", "en-US", null, null, null, null, null), snapshot);

        Assert.NotNull(res);
        Assert.Equal(1, res.selected.episode);
        Assert.Single(res.up_next);
        Assert.Equal(2, res.up_next[0].episode);
    }

    [Fact]
    public void ResolveSeries_UpcomingEpisodesShouldBeExcludedFromResolutionAndQueue()
    {
        var snapshot = MakeSnapshot("tv", new[]
        {
            MakeEpisode(1, 1, "available"),
            MakeEpisode(1, 2, "upcoming"),
            MakeEpisode(1, 3, "upcoming"),
        });

        var svc = new PlaybackResolveService();
        var res = svc.Resolve(new PlayResolveRequestDto("tv", 1, "tmdb", "phantom", 1, null, null, null, "en-US", null, null, null, null, null), snapshot);

        Assert.NotNull(res);
        Assert.Equal(1, res.selected.episode);
        Assert.Empty(res.up_next);
    }

    [Fact]
    public void ResolveSeries_WithNoAvailableEpisodes_ShouldReturnNull()
    {
        var snapshot = MakeSnapshot("tv", new[]
        {
            MakeEpisode(1, 1, "upcoming"),
            MakeEpisode(1, 2, "upcoming"),
        });

        var svc = new PlaybackResolveService();
        var res = svc.Resolve(new PlayResolveRequestDto("tv", 1, "tmdb", "phantom", 1, null, null, null, "en-US", null, null, null, null, null), snapshot);

        Assert.Null(res);
    }

    [Fact]
    public void ResolveSeries_ShouldFallbackToActiveTranslationWhenRequestedNotFound()
    {
        var translations = new[]
        {
            new TranslationOptionDto("lf", "LostFilm", false),
            new TranslationOptionDto("kub", "Кубик в кубе", true),
        };
        var snapshot = MakeSnapshot("tv", new[]
        {
            MakeEpisode(1, 1, "available", "lf", "LostFilm"),
            MakeEpisode(1, 1, "available", "kub", "Кубик в кубе"),
        }, translations: translations);

        var svc = new PlaybackResolveService();
        var res = svc.Resolve(new PlayResolveRequestDto("tv", 1, "tmdb", "phantom", 1, 1, "nonexistent", null, "en-US", null, null, null, null, null), snapshot);

        Assert.NotNull(res);
        Assert.Equal("kub", res.selected.translation_id);
    }

    [Fact]
    public void ResolveSeries_QueueShouldStayInSelectedSeason()
    {
        var snapshot = MakeSnapshot("tv", new[]
        {
            MakeEpisode(1, 1, "available"),
            MakeEpisode(1, 2, "available"),
            MakeEpisode(2, 1, "available"),
        });

        var svc = new PlaybackResolveService();
        var res = svc.Resolve(new PlayResolveRequestDto("tv", 1, "tmdb", "phantom", 1, 1, null, null, "en-US", null, null, null, null, null), snapshot);

        Assert.NotNull(res);
        Assert.All(res.up_next, q => Assert.Equal(1, q.season));
    }

    [Fact]
    public void ResolveMovie_ShouldReturnEmptyQueue()
    {
        var options = new ProviderOptionsResponseDto(
            "movie", 1, "phantom", "ok", "rjson", "proxy", 1,
            new SelectionDto(1, 0, "LostFilm", "lf", "1080p"),
            Array.Empty<SeasonOptionDto>(),
            new[] { new TranslationOptionDto("lf", "LostFilm", true) },
            new[] { "1080p" },
            Array.Empty<EpisodeOptionDto>(),
            Array.Empty<PlannedEpisodeDto>(),
            Array.Empty<SubtitleOptionDto>(),
            new[]
            {
                new MovieStreamOptionDto("LostFilm", "lf", "1080p", new[] { "1080p" }, Array.Empty<SubtitleOptionDto>(),
                    MakeCandidate("https://x/movie.m3u8"))
            }
        );
        var snapshot = new ProviderOptionsSnapshot
        {
            Response = options,
            Provider = "phantom",
            Context = new ProviderContext("movie", 1, "tmdb", "", "T", "T", "en", 2022, "tt1", "1", false)
        };

        var svc = new PlaybackResolveService();
        var res = svc.Resolve(new PlayResolveRequestDto("movie", 1, "tmdb", "phantom", null, null, null, null, "en-US", null, null, null, null, null), snapshot);

        Assert.NotNull(res);
        Assert.Equal("movie", res.media);
        Assert.Empty(res.up_next);
        Assert.Equal("https://x/movie.m3u8", res.play.url);
    }

    [Fact]
    public void ResolveMovie_WithNoStreams_ShouldReturnNull()
    {
        var options = new ProviderOptionsResponseDto(
            "movie", 1, "phantom", "no_items", "none", "proxy", 1,
            new SelectionDto(1, 0, string.Empty, string.Empty, string.Empty),
            Array.Empty<SeasonOptionDto>(),
            Array.Empty<TranslationOptionDto>(),
            Array.Empty<string>(),
            Array.Empty<EpisodeOptionDto>(),
            Array.Empty<PlannedEpisodeDto>(),
            Array.Empty<SubtitleOptionDto>(),
            Array.Empty<MovieStreamOptionDto>()
        );
        var snapshot = new ProviderOptionsSnapshot
        {
            Response = options,
            Provider = "phantom",
            Context = new ProviderContext("movie", 1, "tmdb", "", "T", "T", "en", 2022, "tt1", "1", false)
        };

        var svc = new PlaybackResolveService();
        var res = svc.Resolve(new PlayResolveRequestDto("movie", 1, "tmdb", "phantom", null, null, null, null, "en-US", null, null, null, null, null), snapshot);

        Assert.Null(res);
    }

    [Fact]
    public void Resolve_WithNullSnapshot_ShouldReturnNull()
    {
        var svc = new PlaybackResolveService();
        Assert.Null(svc.Resolve(new PlayResolveRequestDto("tv", 1, "tmdb", "phantom", 1, 1, null, null, "en-US", null, null, null, null, null), null));
    }

    [Fact]
    public void ResolveSeries_WhenProxyWrapperFails_ShouldReturnNull()
    {
        var snapshot = MakeSnapshot("tv", new[]
        {
            MakeEpisode(1, 1, "available")
        });

        var svc = new PlaybackResolveService(_ => null);
        var res = svc.Resolve(new PlayResolveRequestDto("tv", 1, "tmdb", "phantom", 1, 1, null, null, "en-US", null, null, null, null, null), snapshot);

        Assert.Null(res);
    }
}
