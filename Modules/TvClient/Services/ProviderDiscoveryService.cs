using TvClient.Models;

namespace TvClient.Services;

public class ProviderDiscoveryService
{
    readonly InternalApiClient _api;

    public ProviderDiscoveryService(InternalApiClient api)
    {
        _api = api;
    }

    public async Task<ProviderDiscoveryResult> Discover(ProviderContext context)
    {
        var empty = new ProviderDiscoveryResult("", Array.Empty<ProviderItemDto>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        if (context == null)
            return empty;

        string auth = _api.BuildAuthQuery();
        string q =
            $"id={context.tmdb_id}" +
            $"&imdb_id={HttpUtility.UrlEncode(context.imdb_id ?? string.Empty)}" +
            $"&kinopoisk_id={HttpUtility.UrlEncode(context.kinopoisk_id ?? "0")}" +
            $"&title={HttpUtility.UrlEncode(context.title ?? string.Empty)}" +
            $"&original_title={HttpUtility.UrlEncode(context.original_title ?? string.Empty)}" +
            $"&original_language={HttpUtility.UrlEncode(context.original_language ?? string.Empty)}" +
            $"&year={context.year}" +
            $"&source=tmdb" +
            $"&serial={(context.serial ? 1 : 0)}" +
            "&islite=true";

        if (!string.IsNullOrWhiteSpace(auth))
            q += "&" + auth;

        var token = await _api.GetJsonToken($"/lite/events?{q}", timeoutSec: ModInit.conf.providers_timeout_sec, statusCodeOK: false);
        if (token is not JArray arr || arr.Count == 0)
            return empty;

        var order = (ModInit.conf.provider_order ?? Array.Empty<string>())
            .Select((v, i) => (v: v.ToLowerInvariant(), i))
            .ToDictionary(x => x.v, x => x.i);

        // Context params appended to each provider URL so provider controllers
        // know which title to serve (bare event URLs lack these).
        string contextSuffix =
            $"id={context.tmdb_id}" +
            $"&imdb_id={HttpUtility.UrlEncode(context.imdb_id ?? string.Empty)}" +
            $"&kinopoisk_id={HttpUtility.UrlEncode(context.kinopoisk_id ?? "0")}" +
            $"&title={HttpUtility.UrlEncode(context.title ?? string.Empty)}" +
            $"&original_title={HttpUtility.UrlEncode(context.original_title ?? string.Empty)}" +
            $"&original_language={HttpUtility.UrlEncode(context.original_language ?? string.Empty)}" +
            $"&year={context.year}" +
            $"&serial={(context.serial ? 1 : 0)}";

        if (!string.IsNullOrWhiteSpace(auth))
            contextSuffix += "&" + auth;

        var providerUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var providers = new List<ProviderItemDto>(arr.Count);

        foreach (var item in arr.Children<JObject>())
        {
            string code = item.Value<string>("balanser")?.ToLowerInvariant() ?? string.Empty;
            string name = item.Value<string>("name") ?? code;
            string url = item.Value<string>("url") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(url))
                continue;

            // Use path-only URL so InternalApiClient routes via LocalBase (direct to lampac:9118),
            // bypassing Caddy and its IP allow-list.
            string pathAndQuery = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? uri.PathAndQuery
                : url;
            string internalUrl = pathAndQuery.Contains('?')
                ? $"{pathAndQuery}&{contextSuffix}"
                : $"{pathAndQuery}?{contextSuffix}";
            providerUrls[code] = internalUrl;

            int priority = order.TryGetValue(code, out int idx)
                ? idx
                : 1000 + providers.Count;

            providers.Add(new ProviderItemDto(
                code,
                name,
                url,   // bare URL for client display; fullUrl is used internally
                priority,
                true,
                context.serial,
                !context.serial
            ));
        }

        var sorted = providers.OrderBy(i => i.priority).ThenBy(i => i.code).ToArray();
        string defaultProvider = sorted.FirstOrDefault()?.code ?? string.Empty;

        return new ProviderDiscoveryResult(defaultProvider, sorted, providerUrls);
    }
}
