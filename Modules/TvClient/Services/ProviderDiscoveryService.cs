using TvClient.Models;

namespace TvClient.Services;

public class ProviderDiscoveryService
{
    readonly InternalApiClient _api;
    readonly UpstreamParityContextBuilder _parity = new();

    public ProviderDiscoveryService(InternalApiClient api)
    {
        _api = api;
    }

    public async Task<ProviderDiscoveryResult> Discover(ProviderContext context)
    {
        var empty = new ProviderDiscoveryResult("", Array.Empty<ProviderItemDto>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        if (context == null)
            return empty;

        var conf = ModInit.conf;
        int timeoutSec = conf?.providers_timeout_sec ?? 15;
        string q = _parity.BuildEventsQuery(context, _api.Auth);
        string eventsPath = $"/lite/events?{q}";
        Serilog.Log.Information("TvClient upstream events [source: {Source}] [serial: {Serial}] [tmdb_id: {TmdbId}] [has_auth: {HasAuth}] [path: {Path}]",
            _parity.NormalizeSource(context.source), context.serial ? 1 : 0, context.tmdb_id, _parity.AuthFlags(_api.Auth).Values.Any(v => v), eventsPath);

        var token = await _api.GetJsonToken(eventsPath, timeoutSec: timeoutSec, statusCodeOK: false);
        if (token is not JArray arr || arr.Count == 0)
            return empty;

        var order = (conf?.provider_order ?? Array.Empty<string>())
            .Select((v, i) => (v: v.ToLowerInvariant(), i))
            .ToDictionary(x => x.v, x => x.i);

        var contextMap = _parity.BuildContextMap(context, _api.Auth);

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
            string internalUrl = _parity.AppendMissingParams(pathAndQuery, contextMap);
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
