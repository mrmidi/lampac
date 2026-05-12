using TvClient.Models;

namespace TvClient.Services;

public class CatalogService
{
    readonly InternalApiClient _api;

    public CatalogService(InternalApiClient api)
    {
        _api = api;
    }

    static string DefaultLang(string lang)
        => string.IsNullOrWhiteSpace(lang) ? (ModInit.conf?.default_lang ?? "en-US") : lang;

    static string MapFeedEndpoint(string media, string feed)
    {
        string f = (feed ?? string.Empty).Trim().ToLowerInvariant();
        string m = (media ?? string.Empty).Trim().ToLowerInvariant();

        if (m == "movie")
        {
            return f switch
            {
                "trending" => "trending/movie/week",
                "now_playing" => "movie/now_playing",
                "popular" => "movie/popular",
                _ => "trending/movie/week"
            };
        }

        return f switch
        {
            "trending" => "trending/tv/week",
            "popular" => "tv/popular",
            "on_the_air" => "tv/on_the_air",
            "airing_today" => "tv/airing_today",
            _ => "trending/tv/week"
        };
    }

    static MediaCardDto MapCard(JToken item, string forcedMedia = null)
    {
        if (item == null)
            return null;

        string media = forcedMedia;
        if (string.IsNullOrEmpty(media))
        {
            string mt = item.Value<string>("media_type");
            media = mt == "tv" ? "tv" : "movie";
        }

        string title = media == "tv"
            ? item.Value<string>("name")
            : item.Value<string>("title");

        string original = media == "tv"
            ? item.Value<string>("original_name")
            : item.Value<string>("original_title");

        string date = media == "tv"
            ? item.Value<string>("first_air_date")
            : item.Value<string>("release_date");

        string posterPath = item.Value<string>("poster_path");
        string backdropPath = item.Value<string>("backdrop_path");

        int year = 0;
        if (!string.IsNullOrWhiteSpace(date) && date.Length >= 4)
            int.TryParse(date[..4], out year);

        string poster = string.IsNullOrWhiteSpace(posterPath) ? string.Empty : $"/tmdb/img/t/p/w500{posterPath}";
        string backdrop = string.IsNullOrWhiteSpace(backdropPath) ? string.Empty : $"/tmdb/img/t/p/w1280{backdropPath}";

        return new MediaCardDto(
            item.Value<long?>("id") ?? 0,
            media,
            title ?? original ?? string.Empty,
            original ?? title ?? string.Empty,
            item.Value<string>("overview") ?? string.Empty,
            poster,
            backdrop,
            date ?? string.Empty,
            item.Value<double?>("vote_average") ?? 0,
            item.Value<string>("original_language") ?? string.Empty,
            year
        );
    }

    async Task<JObject> CallTmdb(string endpoint, string lang, int page)
    {
        string apiKey = CoreInit.conf?.cub?.api_key;
        string language = DefaultLang(lang);

        string query = $"api_key={HttpUtility.UrlEncode(apiKey)}&language={HttpUtility.UrlEncode(language)}&page={page}";
        var token = await _api.GetJsonToken($"/tmdb/api/3/{endpoint}?{query}", timeoutSec: ModInit.conf.tmdb_timeout_sec);

        return token as JObject;
    }

    public async Task<HomeResponseDto> GetHome(string lang, int page)
    {
        var movie = await CallTmdb("trending/movie/week", lang, page);
        var tv = await CallTmdb("trending/tv/week", lang, page);

        var trendingMovies = movie?["results"]?.Children().Select(i => MapCard(i, "movie")).Where(i => i != null).ToArray()
            ?? Array.Empty<MediaCardDto>();

        var trendingTv = tv?["results"]?.Children().Select(i => MapCard(i, "tv")).Where(i => i != null).ToArray()
            ?? Array.Empty<MediaCardDto>();

        return new HomeResponseDto(
            DefaultLang(lang),
            page,
            trendingMovies,
            trendingTv,
            new WatchingNowDto(false, Array.Empty<object>())
        );
    }

    public async Task<PagedMediaResponseDto> GetFeed(string media, string feed, int page, string lang)
    {
        string m = string.Equals(media, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
        string endpoint = MapFeedEndpoint(m, feed);

        var token = await CallTmdb(endpoint, lang, page);
        var results = token?["results"]?.Children().Select(i => MapCard(i, m)).Where(i => i != null).ToArray()
            ?? Array.Empty<MediaCardDto>();

        return new PagedMediaResponseDto(
            feed?.ToLowerInvariant() ?? "trending",
            m,
            token?.Value<int?>("page") ?? page,
            token?.Value<int?>("total_pages") ?? 1,
            results
        );
    }

    public async Task<PagedMediaResponseDto> Search(string query, string media, int page, string lang)
    {
        string m = (media ?? "all").Trim().ToLowerInvariant();
        string endpoint = m switch
        {
            "movie" => "search/movie",
            "tv" => "search/tv",
            _ => "search/multi"
        };

        string apiKey = CoreInit.conf?.cub?.api_key;
        string language = DefaultLang(lang);

        string qs = $"api_key={HttpUtility.UrlEncode(apiKey)}&language={HttpUtility.UrlEncode(language)}&query={HttpUtility.UrlEncode(query ?? string.Empty)}&page={page}";
        var token = await _api.GetJsonToken($"/tmdb/api/3/{endpoint}?{qs}", timeoutSec: ModInit.conf.tmdb_timeout_sec) as JObject;

        var results = token?["results"]?.Children()
            .Select(i =>
            {
                string mt = m == "all" ? i.Value<string>("media_type") : m;
                if (m == "all" && mt is not ("movie" or "tv"))
                    return null;
                return MapCard(i, mt == "tv" ? "tv" : "movie");
            })
            .Where(i => i != null)
            .ToArray() ?? Array.Empty<MediaCardDto>();

        return new PagedMediaResponseDto(
            "search",
            m,
            token?.Value<int?>("page") ?? page,
            token?.Value<int?>("total_pages") ?? 1,
            results
        );
    }

    public virtual async Task<(ProviderContext context, JObject detail)> GetTitleContext(string media, long tmdbId, string lang)
    {
        string m = string.Equals(media, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
        string apiKey = CoreInit.conf?.cub?.api_key;
        string language = DefaultLang(lang);

        string append = "external_ids,credits,recommendations,similar";
        string path = $"/tmdb/api/3/{m}/{tmdbId}?api_key={HttpUtility.UrlEncode(apiKey)}&language={HttpUtility.UrlEncode(language)}&append_to_response={append}";

        var detail = await _api.GetJsonToken(path, timeoutSec: ModInit.conf.tmdb_timeout_sec) as JObject;
        if (detail == null)
            return (null, null);

        string title = m == "tv" ? detail.Value<string>("name") : detail.Value<string>("title");
        string originalTitle = m == "tv" ? detail.Value<string>("original_name") : detail.Value<string>("original_title");
        string date = m == "tv" ? detail.Value<string>("first_air_date") : detail.Value<string>("release_date");

        int year = 0;
        if (!string.IsNullOrWhiteSpace(date) && date.Length >= 4)
            int.TryParse(date[..4], out year);

        string imdb = detail["external_ids"]?.Value<string>("imdb_id") ?? detail.Value<string>("imdb_id") ?? string.Empty;

        string extPath = $"/externalids?id={tmdbId}&imdb_id={HttpUtility.UrlEncode(imdb)}&serial={(m == "tv" ? 1 : 0)}";
        var external = await _api.GetJsonToken(extPath, timeoutSec: 8, statusCodeOK: false) as JObject;

        string kinopoisk = external?.Value<string>("kinopoisk_id") ?? "0";
        if (string.IsNullOrWhiteSpace(kinopoisk))
            kinopoisk = "0";

        var ctx = new ProviderContext(
            m,
            tmdbId,
            title ?? originalTitle ?? string.Empty,
            originalTitle ?? title ?? string.Empty,
            detail.Value<string>("original_language") ?? string.Empty,
            year,
            imdb,
            kinopoisk,
            m == "tv"
        );

        return (ctx, detail);
    }

    public TitleDetailResponseDto ToTitleDetailDto(string media, long tmdbId, JObject detail, ProvidersSummaryDto providers)
    {
        if (detail == null)
            return null;

        string m = string.Equals(media, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";

        string title = m == "tv" ? detail.Value<string>("name") : detail.Value<string>("title");
        string originalTitle = m == "tv" ? detail.Value<string>("original_name") : detail.Value<string>("original_title");
        string date = m == "tv" ? detail.Value<string>("first_air_date") : detail.Value<string>("release_date");

        int year = 0;
        if (!string.IsNullOrWhiteSpace(date) && date.Length >= 4)
            int.TryParse(date[..4], out year);

        string posterPath = detail.Value<string>("poster_path");
        string backdropPath = detail.Value<string>("backdrop_path");

        var recs = detail["recommendations"]?[
            "results"]?.Children().Select(i => MapCard(i)).Where(i => i != null).ToArray() ?? Array.Empty<MediaCardDto>();

        var sim = detail["similar"]?["results"]?.Children().Select(i => MapCard(i)).Where(i => i != null).ToArray() ?? Array.Empty<MediaCardDto>();

        return new TitleDetailResponseDto(
            m,
            tmdbId,
            title ?? originalTitle ?? string.Empty,
            originalTitle ?? title ?? string.Empty,
            detail.Value<string>("overview") ?? string.Empty,
            string.IsNullOrWhiteSpace(posterPath) ? string.Empty : $"/tmdb/img/t/p/w500{posterPath}",
            string.IsNullOrWhiteSpace(backdropPath) ? string.Empty : $"/tmdb/img/t/p/w1280{backdropPath}",
            date ?? string.Empty,
            detail.Value<double?>("vote_average") ?? 0,
            detail.Value<string>("original_language") ?? string.Empty,
            year,
            detail["credits"],
            recs,
            sim,
            providers
        );
    }

    public virtual async Task<JObject> GetTvSeason(long tmdbId, int season, string lang)
    {
        string apiKey = CoreInit.conf?.cub?.api_key;
        string language = DefaultLang(lang);

        string path = $"/tmdb/api/3/tv/{tmdbId}/season/{season}?api_key={HttpUtility.UrlEncode(apiKey)}&language={HttpUtility.UrlEncode(language)}";
        return await _api.GetJsonToken(path, timeoutSec: ModInit.conf.tmdb_timeout_sec) as JObject;
    }
}
