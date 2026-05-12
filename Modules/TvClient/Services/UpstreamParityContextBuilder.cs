using TvClient.Models;
using Shared.Services.Utilities;

namespace TvClient.Services;

public class UpstreamParityContextBuilder
{
    public string NormalizeSource(string source)
        => string.IsNullOrWhiteSpace(source) ? "tmdb" : source.Trim().ToLowerInvariant();

    public IReadOnlyDictionary<string, bool> AuthFlags(AuthContext auth)
        => new Dictionary<string, bool>
        {
            ["account_email"] = !string.IsNullOrWhiteSpace(auth?.account_email),
            ["uid"] = !string.IsNullOrWhiteSpace(auth?.uid),
            ["token"] = !string.IsNullOrWhiteSpace(auth?.token),
            ["nws_id"] = !string.IsNullOrWhiteSpace(auth?.nws_id),
            ["profile_id"] = !string.IsNullOrWhiteSpace(auth?.profile_id),
            ["cub_id"] = !string.IsNullOrWhiteSpace(auth?.account_email),
        };

    public Dictionary<string, string> BuildContextMap(ProviderContext context, AuthContext auth)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = context.tmdb_id.ToString(),
            ["tmdb_id"] = context.tmdb_id.ToString(),
            ["imdb_id"] = context.imdb_id ?? string.Empty,
            ["kinopoisk_id"] = string.IsNullOrWhiteSpace(context.kinopoisk_id) ? "0" : context.kinopoisk_id,
            ["title"] = context.title ?? string.Empty,
            ["original_title"] = context.original_title ?? string.Empty,
            ["original_language"] = context.original_language ?? string.Empty,
            ["year"] = context.year.ToString(),
            ["source"] = NormalizeSource(context.source),
            ["serial"] = context.serial ? "1" : "0",
            ["islite"] = "true",
            ["rchtype"] = context.rchtype ?? string.Empty,
        };

        AddAuth(map, auth, includeCubId: true);
        return map;
    }

    public string BuildEventsQuery(ProviderContext context, AuthContext auth)
        => ToQueryString(BuildContextMap(context, auth));

    public string AppendMissingParams(string urlOrPath, IReadOnlyDictionary<string, string> paramsMap)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath))
            return string.Empty;

        string result = urlOrPath;
        foreach (var kv in paramsMap)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            if (Regex.IsMatch(result, $"[?&]{Regex.Escape(kv.Key)}=", RegexOptions.IgnoreCase))
                continue;

            result += (result.Contains('?') ? "&" : "?") + $"{kv.Key}={HttpUtility.UrlEncode(kv.Value)}";
        }

        return result;
    }

    public string EnsureRjson(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (Regex.IsMatch(url, "[?&]rjson=", RegexOptions.IgnoreCase))
            return url;

        return url + (url.Contains('?') ? "&" : "?") + "rjson=true";
    }

    static void AddAuth(Dictionary<string, string> map, AuthContext auth, bool includeCubId)
    {
        if (auth == null)
            return;

        void add(string k, string v)
        {
            if (!string.IsNullOrWhiteSpace(v))
                map[k] = v;
        }

        add("account_email", auth.account_email);
        add("uid", auth.uid);
        add("token", auth.token);
        add("nws_id", auth.nws_id);
        add("profile_id", auth.profile_id);

        if (includeCubId && !string.IsNullOrWhiteSpace(auth.account_email))
            add("cub_id", CrypTo.md5(auth.account_email));
    }

    static string ToQueryString(IReadOnlyDictionary<string, string> map)
    {
        var parts = new List<string>(map.Count);
        foreach (var kv in map)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;
            parts.Add($"{kv.Key}={HttpUtility.UrlEncode(kv.Value)}");
        }
        return string.Join("&", parts);
    }
}
