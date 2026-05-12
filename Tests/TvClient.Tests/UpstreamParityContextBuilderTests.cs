using TvClient.Models;
using TvClient.Services;
using Xunit;

namespace TvClient.Tests;

public class UpstreamParityContextBuilderTests
{
    [Fact]
    public void BuildEventsQuery_ShouldIncludeSourceAndAuthAndCubId()
    {
        var b = new UpstreamParityContextBuilder();
        var ctx = new ProviderContext("tv", 76479, "cub", "web", "The Boys", "The Boys", "en", 2019, "tt1190634", "111", true);
        var auth = new AuthContext("user@test.local", "uid42", "tok42", "nws42", "p42");

        var q = b.BuildEventsQuery(ctx, auth);

        Assert.Contains("source=cub", q);
        Assert.Contains("serial=1", q);
        Assert.Contains("uid=uid42", q);
        Assert.Contains("token=tok42", q);
        Assert.Contains("nws_id=nws42", q);
        Assert.Contains("profile_id=p42", q);
        Assert.Contains("cub_id=", q);
    }

    [Fact]
    public void BuildEventsQuery_ShouldFallbackSourceToTmdb()
    {
        var b = new UpstreamParityContextBuilder();
        var ctx = new ProviderContext("movie", 550, "", "", "Fight Club", "Fight Club", "en", 1999, "tt0137523", "0", false);

        var q = b.BuildEventsQuery(ctx, new AuthContext("", "", "", "", ""));

        Assert.Contains("source=tmdb", q);
        Assert.DoesNotContain("uid=", q);
        Assert.DoesNotContain("cub_id=", q);
    }

    [Fact]
    public void AppendMissingParams_ShouldKeepExistingAndAddMissing()
    {
        var b = new UpstreamParityContextBuilder();
        var url = "/lite/phantom?id=76479&source=cub";
        var map = new Dictionary<string, string>
        {
            ["id"] = "76479",
            ["source"] = "tmdb",
            ["serial"] = "1",
            ["uid"] = "u1"
        };

        var outUrl = b.AppendMissingParams(url, map);

        Assert.Contains("source=cub", outUrl);
        Assert.DoesNotContain("source=tmdb", outUrl);
        Assert.Contains("serial=1", outUrl);
        Assert.Contains("uid=u1", outUrl);
    }
}
