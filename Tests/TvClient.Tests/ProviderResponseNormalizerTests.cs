using TvClient.Services;
using TvClient.Models;
using Xunit;

namespace TvClient.Tests;

public class ProviderResponseNormalizerTests
{
    [Fact]
    public void ParseMovieJson_ShouldExtractVoicesAndQualities()
    {
        string json = """
        {
          "type":"movie",
          "voice":[{"method":"link","url":"/lite/phantom?t=12","active":true,"name":"LostFilm"}],
          "data":[{
            "method":"play",
            "url":"https://example.com/auto.m3u8",
            "quality":{"1080p":"https://example.com/1080.m3u8","720p":"https://example.com/720.m3u8"},
            "translate":"LostFilm"
          }]
        }
        """;

        var sut = new ProviderResponseNormalizer();
        var parsed = sut.Parse(json);

        Assert.Equal(ProviderPayloadType.Movie, parsed.Type);
        Assert.Single(parsed.Voices);
        Assert.Single(parsed.Movies);
        Assert.Equal("12", parsed.Voices[0].id);
        Assert.True(parsed.Movies[0].quality.ContainsKey("1080p"));
    }

    [Fact]
    public void ParseEpisodeJson_ShouldReadSeasonEpisode()
    {
        string json = """
        {
          "type":"episode",
          "voice":[{"method":"link","url":"/lite/phantom?t=5","active":true,"name":"VO"}],
          "data":[{
            "method":"call",
            "url":"https://x/lite/phantom/video?id=1",
            "stream":"https://x/proxy/abc.m3u8",
            "s":2,
            "e":4,
            "name":"4 серия",
            "title":"Test",
            "details":"VO"
          }]
        }
        """;

        var sut = new ProviderResponseNormalizer();
        var parsed = sut.Parse(json);

        Assert.Equal(ProviderPayloadType.Episode, parsed.Type);
        Assert.Single(parsed.Episodes);
        Assert.Equal(2, parsed.Episodes[0].season);
        Assert.Equal(4, parsed.Episodes[0].episode);
    }

    [Fact]
    public void ParseHtmlFallback_ShouldReadDataJsonNodes()
    {
        string html = """
        <div class='videos__line'>
          <div class='videos__button active' data-json='{"method":"link","url":"/lite/p?t=9"}'>LostFilm</div>
          <div class='videos__item videos__movie selector' s='1' e='2' data-json='{"method":"call","url":"/lite/p/video?id=2"}'>2 серия</div>
        </div>
        """;

        var sut = new ProviderResponseNormalizer();
        var parsed = sut.Parse(html);

        Assert.Equal(ProviderPayloadType.Episode, parsed.Type);
        Assert.Single(parsed.Voices);
        Assert.Single(parsed.Episodes);
        Assert.Equal(2, parsed.Episodes[0].episode);
    }

    [Fact]
    public void ParseEpisodeJson_WithoutSeasonField_ShouldHaveSeasonZero()
    {
        // Episodes that omit "s" should parse as season=0.
        // BuildSeries then remaps season-0 to bucket 1 (fix #2).
        string json = """
        {
          "type":"episode",
          "data":[
            {"method":"play","url":"https://x/1.m3u8","e":1,"name":"E1"},
            {"method":"play","url":"https://x/2.m3u8","e":2,"name":"E2"}
          ]
        }
        """;

        var sut = new ProviderResponseNormalizer();
        var parsed = sut.Parse(json);

        Assert.Equal(ProviderPayloadType.Episode, parsed.Type);
        Assert.Equal(2, parsed.Episodes.Count);
        Assert.All(parsed.Episodes, ep => Assert.Equal(0, ep.season));
    }

    [Fact]
    public void Parse_EmptyInput_ShouldReturnUnknown()
    {
        var sut = new ProviderResponseNormalizer();
        Assert.Equal(ProviderPayloadType.Unknown, sut.Parse(string.Empty).Type);
        Assert.Equal(ProviderPayloadType.Unknown, sut.Parse("   ").Type);
        Assert.Equal(ProviderPayloadType.Unknown, sut.Parse(null).Type);
    }

    [Fact]
    public void Parse_MalformedJson_ShouldReturnUnknown()
    {
        var sut = new ProviderResponseNormalizer();
        var result = sut.Parse("{not valid json{{");
        Assert.Equal(ProviderPayloadType.Unknown, result.Type);
    }

    [Fact]
    public void Parse_JsonWithUnknownType_ShouldReturnUnknown()
    {
        var sut = new ProviderResponseNormalizer();
        var result = sut.Parse("""{"type":"playlist","data":[]}""");
        Assert.Equal(ProviderPayloadType.Unknown, result.Type);
    }

    [Fact]
    public void ParseSeasonJson_ShouldExtractSeasonList()
    {
        string json = """
        {
          "type":"season",
          "data":[
            {"id":1,"name":"Сезон 1","url":"/lite/phantom?s=1"},
            {"id":2,"name":"Сезон 2","url":"/lite/phantom?s=2"}
          ]
        }
        """;

        var sut = new ProviderResponseNormalizer();
        var parsed = sut.Parse(json);

        Assert.Equal(ProviderPayloadType.Season, parsed.Type);
        Assert.Equal(2, parsed.Seasons.Count);
        Assert.Equal(1, parsed.Seasons[0].id);
        Assert.Equal(2, parsed.Seasons[1].id);
    }

    [Fact]
    public void ExtractTranslationId_ShouldPreferUrlParam()
    {
        string id = ProviderResponseNormalizer.ExtractTranslationId("/lite/phantom?t=42&s=1", "LostFilm");
        Assert.Equal("42", id);
    }

    [Fact]
    public void ExtractTranslationId_ShouldFallbackToName()
    {
        string id = ProviderResponseNormalizer.ExtractTranslationId("/lite/phantom?s=1", "LostFilm");
        Assert.Equal("LostFilm", id);
    }
}
