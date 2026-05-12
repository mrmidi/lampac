using System.Linq;
using Newtonsoft.Json.Linq;
using TvClient.Models;
using TvClient.Services;
using Xunit;

namespace TvClient.Tests;

public class ProviderServicesTests
{
    sealed class FakeInternalApiClient : InternalApiClient
    {
        readonly JToken _events;
        readonly string _authQuery;
        readonly string _rawPayload;
        public readonly List<string> RequestedPaths = new();

        public FakeInternalApiClient(JToken events, string authQuery = "", string rawPayload = "")
            : base("localhost", "http", new AuthContext("", "", "", "", ""))
        {
            _events = events;
            _authQuery = authQuery;
            _rawPayload = rawPayload;
        }

        public override string BuildAuthQuery() => _authQuery;

        public override Task<JToken> GetJsonToken(string pathAndQuery, int timeoutSec = 15, bool statusCodeOK = true)
        {
            RequestedPaths.Add(pathAndQuery);
            return Task.FromResult(_events);
        }

        public override Task<string> GetRaw(string pathAndQuery, int timeoutSec = 15, bool statusCodeOK = true)
        {
            RequestedPaths.Add(pathAndQuery);
            return Task.FromResult(_rawPayload);
        }
    }

    sealed class FakeCatalogService : CatalogService
    {
        readonly JObject _season;

        public FakeCatalogService(InternalApiClient api, JObject season)
            : base(api)
        {
            _season = season;
        }

        public override Task<JObject> GetTvSeason(long tmdbId, int season, string lang)
            => Task.FromResult(_season);
    }

    [Fact]
    public async Task ProviderDiscovery_ShouldAppendTmdbAndLiteContextToProviderUrls()
    {
        var events = JArray.Parse("""
        [
          {
            "balanser":"phantom",
            "name":"Phantom",
            "url":"https://example.test/lite/phantom?rjson=false"
          }
        ]
        """);

        var api = new FakeInternalApiClient(events, "uid=42");
        var svc = new ProviderDiscoveryService(api);
        var ctx = new ProviderContext("tv", 76479, "The Boys", "The Boys", "en", 2019, "tt1190634", "111", true);

        var result = await svc.Discover(ctx);

        Assert.Equal("phantom", result.default_provider);
        Assert.True(result.provider_urls.ContainsKey("phantom"));

        string url = result.provider_urls["phantom"];
        Assert.Contains("id=76479", url);
        Assert.Contains("serial=1", url);
        Assert.Contains("source=tmdb", url);
        Assert.Contains("islite=true", url);
        Assert.Contains("uid=42", url);
    }

    [Fact]
    public async Task ProviderOptionsSeries_ShouldKeepTmdbOnlyEpisodesInPlannedList()
    {
        var seasonPayload = """
        {
          "type":"episode",
          "voice":[{"name":"LostFilm","active":true,"url":"/lite/phantom?t=lf"}],
          "data":[
            {
              "method":"play",
              "url":"https://cdn.example/s5e1.m3u8",
              "stream":"https://cdn.example/s5e1.m3u8",
              "s":5,
              "e":1,
              "name":"Episode 1",
              "title":"Episode 1",
              "details":"LostFilm",
              "quality":{"1080p":"https://cdn.example/s5e1_1080.m3u8"}
            }
          ]
        }
        """;

        var tmdbSeason = JObject.Parse("""
        {
          "season_number": 5,
          "episodes": [
            {"episode_number": 1, "name":"Episode 1", "air_date":"2026-04-01"},
            {"episode_number": 2, "name":"Episode 2", "air_date":"2026-04-08"}
          ]
        }
        """);

        var api = new FakeInternalApiClient(JValue.CreateNull(), rawPayload: seasonPayload);
        var catalog = new FakeCatalogService(api, tmdbSeason);
        var normalizer = new ProviderResponseNormalizer();
        var svc = new ProviderOptionsService(api, catalog, normalizer);
        var context = new ProviderContext("tv", 76479, "The Boys", "The Boys", "en", 2019, "tt1190634", "111", true);
        var detail = JObject.Parse("""
        {
          "seasons":[
            {"season_number":5}
          ]
        }
        """);

        var snapshot = await svc.Build(
            context,
            "phantom",
            "/lite/phantom?rjson=true",
            5,
            null,
            null,
            "en-US",
            detail
        );

        Assert.NotNull(snapshot);
        Assert.Equal("tv", snapshot.Response.media);
        Assert.Equal(5, snapshot.Response.selected_season);

        // Main episode list must contain only provider-playable entries.
        Assert.Single(snapshot.Response.episodes);
        Assert.Equal(1, snapshot.Response.episodes[0].episode);
        Assert.Equal("available", snapshot.Response.episodes[0].status);
        Assert.NotNull(snapshot.Response.episodes[0].play);

        // TMDB-only episode is exposed as planned/upcoming, not selectable episode option.
        Assert.Contains(snapshot.Response.planned_episodes, e => e.episode == 2);
        Assert.DoesNotContain(snapshot.Response.episodes, e => e.episode == 2);
    }
}
