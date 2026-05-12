using TvClient.Models;
using TvClient.Services;
using System.Reflection;
using Shared.Models.Base;
using Shared.Services;

namespace TvClient.Controllers;

public class TvClientController : BaseController
{
    AuthContext ReadAuth() => new(
        HttpContext.Request.Query["account_email"],
        HttpContext.Request.Query["uid"],
        HttpContext.Request.Query["token"],
        HttpContext.Request.Query["nws_id"],
        HttpContext.Request.Query["profile_id"]
    );

    JsonResult OkEnvelope<T>(T data)
        => Json(new ApiEnvelope<T>(data));

    ObjectResult ErrorEnvelope(string code, string message, int statusCode = 400)
    {
        return StatusCode(statusCode, new ApiEnvelope<object>(null, new ApiErrorDto(code, message)));
    }

    static int NormalizePage(int page)
        => page > 0 ? page : 1;

    static string NormalizeMedia(string media)
        => string.Equals(media, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";

    static bool IsProxyUrl(string url)
        => !string.IsNullOrWhiteSpace(url) && url.Contains("/proxy/", StringComparison.OrdinalIgnoreCase);

    string WrapProxyUrlStrict(string url, IReadOnlyDictionary<string, string> headers, out string proxyMode)
    {
        proxyMode = "proxy";
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (IsProxyUrl(url))
            return url;

        var conf = new BaseSettings
        {
            plugin = "tvclient",
            streamproxy = true
        };

        string wrapped = HostStreamProxy(conf, url, HeadersModel.Init(headers ?? new Dictionary<string, string>()), proxy: null, force_streamproxy: true, rch: null);
        if (!IsProxyUrl(wrapped))
            return string.Empty;

        return wrapped;
    }

    PlayResultDto WrapPlayStrict(PlayResultDto play, out string proxyMode)
    {
        proxyMode = "proxy";
        if (play == null || string.IsNullOrWhiteSpace(play.url))
            return null;

        string wrapped = WrapProxyUrlStrict(play.url, play.headers, out proxyMode);
        if (string.IsNullOrWhiteSpace(wrapped))
            return null;

        return play with { url = wrapped };
    }

    ProviderOptionsResponseDto WrapOptionsPlayStrict(ProviderOptionsResponseDto input, out bool ok, out string proxyMode)
    {
        ok = true;
        proxyMode = "proxy";
        if (input == null)
            return input;

        var episodes = (input.episodes ?? Array.Empty<EpisodeOptionDto>()).Select(ep =>
        {
            if (ep.play == null)
                return ep;

            string wrapped = WrapProxyUrlStrict(ep.play.url, ep.play.headers, out _);
            if (string.IsNullOrWhiteSpace(wrapped))
            {
                ok = false;
                return ep;
            }

            return ep with
            {
                play = ep.play with
                {
                    url = wrapped,
                    stream_type = PlaybackSelection.DetectStreamType(wrapped)
                }
            };
        }).ToArray();

        var movieStreams = (input.movie_streams ?? Array.Empty<MovieStreamOptionDto>()).Select(ms =>
        {
            if (ms.play == null)
                return ms;

            string wrapped = WrapProxyUrlStrict(ms.play.url, ms.play.headers, out _);
            if (string.IsNullOrWhiteSpace(wrapped))
            {
                ok = false;
                return ms;
            }

            return ms with
            {
                play = ms.play with
                {
                    url = wrapped,
                    stream_type = PlaybackSelection.DetectStreamType(wrapped)
                }
            };
        }).ToArray();

        return input with
        {
            proxy_mode = proxyMode,
            episodes = episodes,
            movie_streams = movieStreams
        };
    }

    [HttpGet]
    [Route("api/tv/v1/version")]
    public ActionResult Version()
    {
        var asm = typeof(TvClientController).Assembly;
        string asmVersion = asm.GetName().Version?.ToString() ?? "unknown";
        string fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
        string informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        string buildUtc = string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(asm.Location) && System.IO.File.Exists(asm.Location))
                buildUtc = System.IO.File.GetLastWriteTimeUtc(asm.Location).ToString("O");
        }
        catch { }
        string gitSha = string.Empty;
        if (!string.IsNullOrWhiteSpace(informational))
            gitSha = informational.Split('+').Skip(1).FirstOrDefault() ?? string.Empty;

        string modPath = ModInit.modpath ?? string.Empty;
        string manifestPath = string.IsNullOrWhiteSpace(modPath) ? string.Empty : Path.Combine(modPath, "manifest.json");
        string manifestUtc = string.Empty;
        if (!string.IsNullOrWhiteSpace(manifestPath) && System.IO.File.Exists(manifestPath))
            manifestUtc = System.IO.File.GetLastWriteTimeUtc(manifestPath).ToString("O");

        return OkEnvelope(new
        {
            module = "TvClient",
            asm_version = asmVersion,
            file_version = fileVersion,
            informational_version = informational,
            api_contract_version = "tvclient-v1.1",
            git_sha = gitSha,
            build_utc = buildUtc,
            mod_path = modPath,
            manifest_path = manifestPath,
            manifest_utc = manifestUtc,
            server_utc = DateTime.UtcNow.ToString("O")
        });
    }

    [HttpGet]
    [Route("api/tv/v1/home")]
    public async Task<ActionResult> Home(string lang = "en-US", int page = 1)
    {
        try
        {
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, ReadAuth());
            var catalog = new CatalogService(api);
            var result = await catalog.GetHome(lang, NormalizePage(page));
            return OkEnvelope(result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_home");
            return ErrorEnvelope("home_failed", "Failed to load home rails", 502);
        }
    }

    [HttpGet]
    [Route("api/tv/v1/movies")]
    public async Task<ActionResult> Movies(string feed = "trending", int page = 1, string lang = "en-US")
    {
        try
        {
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, ReadAuth());
            var catalog = new CatalogService(api);
            var result = await catalog.GetFeed("movie", feed, NormalizePage(page), lang);
            return OkEnvelope(result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_movies");
            return ErrorEnvelope("movies_failed", "Failed to load movies feed", 502);
        }
    }

    [HttpGet]
    [Route("api/tv/v1/tv")]
    public async Task<ActionResult> Tv(string feed = "trending", int page = 1, string lang = "en-US")
    {
        try
        {
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, ReadAuth());
            var catalog = new CatalogService(api);
            var result = await catalog.GetFeed("tv", feed, NormalizePage(page), lang);
            return OkEnvelope(result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_tv");
            return ErrorEnvelope("tv_failed", "Failed to load TV feed", 502);
        }
    }

    [HttpGet]
    [Route("api/tv/v1/search")]
    public async Task<ActionResult> Search(string q, string media = "all", int page = 1, string lang = "en-US")
    {
        if (string.IsNullOrWhiteSpace(q))
            return ErrorEnvelope("invalid_query", "Search query is required", 400);

        try
        {
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, ReadAuth());
            var catalog = new CatalogService(api);
            var result = await catalog.Search(q, media, NormalizePage(page), lang);
            return OkEnvelope(result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_search");
            return ErrorEnvelope("search_failed", "Failed to search", 502);
        }
    }

    [HttpGet]
    [Route("api/tv/v1/title/{media}/{tmdbId:long}")]
    public async Task<ActionResult> Title(string media, long tmdbId, string lang = "en-US")
    {
        string m = NormalizeMedia(media);

        try
        {
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, ReadAuth());
            var catalog = new CatalogService(api);
            var discovery = new ProviderDiscoveryService(api);

            var (context, detail) = await catalog.GetTitleContext(m, tmdbId, lang);
            if (context == null || detail == null)
                return ErrorEnvelope("title_not_found", "Title is not found", 404);

            var providers = await discovery.Discover(context);
            var providersSummary = new ProvidersSummaryDto(providers.default_provider, providers.items);

            var result = catalog.ToTitleDetailDto(m, tmdbId, detail, providersSummary);
            return OkEnvelope(result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_title");
            return ErrorEnvelope("title_failed", "Failed to load title details", 502);
        }
    }

    [HttpGet]
    [Route("api/tv/v1/title/{media}/{tmdbId:long}/providers")]
    public async Task<ActionResult> Providers(string media, long tmdbId, string lang = "en-US")
    {
        string m = NormalizeMedia(media);

        try
        {
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, ReadAuth());
            var catalog = new CatalogService(api);
            var discovery = new ProviderDiscoveryService(api);

            var (context, _) = await catalog.GetTitleContext(m, tmdbId, lang);
            if (context == null)
                return ErrorEnvelope("title_not_found", "Title is not found", 404);

            var providers = await discovery.Discover(context);
            var result = new ProvidersSummaryDto(providers.default_provider, providers.items);

            return OkEnvelope(result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_providers");
            return ErrorEnvelope("providers_failed", "Failed to load providers", 502);
        }
    }

    [HttpGet]
    [Route("api/tv/v1/title/{media}/{tmdbId:long}/providers/{provider}/options")]
    public async Task<ActionResult> ProviderOptions(string media, long tmdbId, string provider, int season = 1, string translation = null, string quality = null, string lang = "en-US")
    {
        string m = NormalizeMedia(media);

        try
        {
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, ReadAuth());
            var catalog = new CatalogService(api);
            var discovery = new ProviderDiscoveryService(api);
            var normalizer = new ProviderResponseNormalizer();
            var optionsSvc = new ProviderOptionsService(api, catalog, normalizer);

            var (context, detail) = await catalog.GetTitleContext(m, tmdbId, lang);
            if (context == null)
                return ErrorEnvelope("title_not_found", "Title is not found", 404);

            var providers = await discovery.Discover(context);
            string selectedProvider = string.IsNullOrWhiteSpace(provider) ? providers.default_provider : provider.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(selectedProvider) || !providers.provider_urls.TryGetValue(selectedProvider, out string providerUrl))
                return ErrorEnvelope("provider_not_found", "Provider is not available for this title", 404);

            var snapshot = await optionsSvc.Build(context, selectedProvider, providerUrl, season, translation, quality, lang, detail);
            var wrapped = WrapOptionsPlayStrict(snapshot.Response, out bool wrapOk, out string proxyMode);
            if (!wrapOk)
                return ErrorEnvelope("proxy_wrap_failed", "Failed to proxy one or more playable URLs", 502);
            return OkEnvelope(wrapped with { proxy_mode = proxyMode });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_options");
            return ErrorEnvelope("options_failed", "Failed to build provider options", 502);
        }
    }

    [HttpPost]
    [Route("api/tv/v1/play/resolve")]
    public async Task<ActionResult> Resolve([FromBody] PlayResolveRequestDto body)
    {
        if (body == null || body.tmdbId <= 0 || string.IsNullOrWhiteSpace(body.provider))
            return ErrorEnvelope("invalid_request", "tmdbId and provider are required", 400);

        string media = NormalizeMedia(body.media);

        try
        {
            var auth = new AuthContext(body.account_email, body.uid, body.token, body.nws_id, body.profile_id);
            var api = new InternalApiClient(host, HttpContext.Request.Scheme, auth);
            var catalog = new CatalogService(api);
            var discovery = new ProviderDiscoveryService(api);
            var normalizer = new ProviderResponseNormalizer();
            var optionsSvc = new ProviderOptionsService(api, catalog, normalizer);
            var resolver = new PlaybackResolveService(play =>
            {
                var wrapped = WrapPlayStrict(play, out _);
                return wrapped;
            });

            var (context, detail) = await catalog.GetTitleContext(media, body.tmdbId, body.lang ?? "en-US");
            if (context == null)
                return ErrorEnvelope("title_not_found", "Title is not found", 404);

            var providers = await discovery.Discover(context);
            string provider = body.provider.ToLowerInvariant();
            if (!providers.provider_urls.TryGetValue(provider, out string providerUrl))
                return ErrorEnvelope("provider_not_found", "Provider is not available for this title", 404);

            var snapshot = await optionsSvc.Build(context, provider, providerUrl, body.season, body.translation, body.quality, body.lang ?? "en-US", detail);
            var resolved = resolver.Resolve(body with { media = media }, snapshot);
            if (resolved == null)
            {
                bool hadPlayable = snapshot.Response?.episodes?.Any(e => e.status == "available" && e.play != null) == true
                    || snapshot.Response?.movie_streams?.Any(m => m.play != null) == true;
                if (hadPlayable)
                    return ErrorEnvelope("proxy_wrap_failed", "Failed to proxy resolved stream URL", 502);
                return ErrorEnvelope("resolve_failed", "No playable stream found", 404);
            }

            return OkEnvelope(resolved);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tvclient_resolve");
            return ErrorEnvelope("resolve_failed", "Failed to resolve playback", 502);
        }
    }
}
