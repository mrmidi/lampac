using HtmlAgilityPack;
using TvClient.Models;

namespace TvClient.Services;

public class ProviderResponseNormalizer
{
    public NormalizedProviderPayload Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Empty();

        string trimmed = raw.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            var fromJson = ParseJsonPayload(trimmed);
            if (fromJson.Type != ProviderPayloadType.Unknown)
                return fromJson;
        }

        return ParseHtmlFallback(raw);
    }

    static NormalizedProviderPayload Empty()
        => new(ProviderPayloadType.Unknown, Array.Empty<NormalizedVoice>(), Array.Empty<NormalizedSeason>(), Array.Empty<NormalizedPlayableItem>(), Array.Empty<NormalizedEpisode>());

    NormalizedProviderPayload ParseJsonPayload(string json)
    {
        JToken token;

        try
        {
            token = JToken.Parse(json);
        }
        catch
        {
            return Empty();
        }

        if (token is not JObject root)
            return Empty();

        string type = root.Value<string>("type")?.ToLowerInvariant();

        var voices = ParseVoices(root["voice"]);
        if (type == "movie")
        {
            var data = root["data"]?.Children().Select(ParseMovieItem).Where(i => i != null).ToArray() ?? Array.Empty<NormalizedPlayableItem>();
            return new NormalizedProviderPayload(ProviderPayloadType.Movie, voices, Array.Empty<NormalizedSeason>(), data, Array.Empty<NormalizedEpisode>());
        }

        if (type == "season")
        {
            var seasons = root["data"]?.Children().Select(ParseSeasonItem).Where(i => i != null).ToArray() ?? Array.Empty<NormalizedSeason>();
            return new NormalizedProviderPayload(ProviderPayloadType.Season, voices, seasons, Array.Empty<NormalizedPlayableItem>(), Array.Empty<NormalizedEpisode>());
        }

        if (type == "episode")
        {
            var episodes = root["data"]?.Children().Select(ParseEpisodeItem).Where(i => i != null).ToArray() ?? Array.Empty<NormalizedEpisode>();
            return new NormalizedProviderPayload(ProviderPayloadType.Episode, voices, Array.Empty<NormalizedSeason>(), Array.Empty<NormalizedPlayableItem>(), episodes);
        }

        return Empty();
    }

    static IReadOnlyList<NormalizedVoice> ParseVoices(JToken token)
    {
        if (token == null)
            return Array.Empty<NormalizedVoice>();

        var list = new List<NormalizedVoice>();
        foreach (var v in token.Children())
        {
            string url = v.Value<string>("url") ?? string.Empty;
            string name = v.Value<string>("name") ?? string.Empty;
            bool active = v.Value<bool?>("active") == true;
            string id = ExtractTranslationId(url, name);

            if (!string.IsNullOrWhiteSpace(name))
                list.Add(new NormalizedVoice(id, name, active, url));
        }

        return list;
    }

    static NormalizedSeason ParseSeasonItem(JToken token)
    {
        if (token == null)
            return null;

        int id = token.Value<int?>("id") ?? 0;
        string name = token.Value<string>("name") ?? $"Season {id}";
        string url = token.Value<string>("url") ?? string.Empty;

        if (id == 0)
            id = ExtractNumber(name);

        return new NormalizedSeason(id, name, url);
    }

    static NormalizedPlayableItem ParseMovieItem(JToken token)
    {
        if (token == null)
            return null;

        string translate = token.Value<string>("translate") ?? token.Value<string>("voice_name") ?? "default";
        string url = token.Value<string>("url") ?? string.Empty;
        string stream = token.Value<string>("stream") ?? string.Empty;
        string method = token.Value<string>("method") ?? "play";
        string maxquality = token.Value<string>("maxquality") ?? string.Empty;
        string details = token.Value<string>("details") ?? string.Empty;
        string title = token.Value<string>("title") ?? string.Empty;

        var headers = token["headers"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        var quality = token["quality"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        var subtitles = token["subtitles"]?.Children().Select(s => new SubtitleOptionDto(
            s.Value<string>("label") ?? "",
            s.Value<string>("url") ?? ""
        )).Where(s => !string.IsNullOrWhiteSpace(s.url)).ToList() ?? new List<SubtitleOptionDto>();

        string translateId = ExtractTranslationId(url, translate);

        return new NormalizedPlayableItem(translate, translateId, url, stream, method, headers, quality, subtitles, maxquality, details, title);
    }

    static NormalizedEpisode ParseEpisodeItem(JToken token)
    {
        if (token == null)
            return null;

        int s = token.Value<int?>("s") ?? 0;
        int e = token.Value<int?>("e") ?? 0;

        string details = token.Value<string>("details") ?? string.Empty;
        string translation = details;
        string translationId = ExtractTranslationId(token.Value<string>("url") ?? string.Empty, details);

        var headers = token["headers"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        var quality = token["quality"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        var subtitles = token["subtitles"]?.Children().Select(x => new SubtitleOptionDto(
            x.Value<string>("label") ?? "",
            x.Value<string>("url") ?? ""
        )).Where(x => !string.IsNullOrWhiteSpace(x.url)).ToList() ?? new List<SubtitleOptionDto>();

        return new NormalizedEpisode(
            s,
            e,
            token.Value<string>("name") ?? $"Episode {e}",
            token.Value<string>("title") ?? string.Empty,
            details,
            token.Value<string>("url") ?? string.Empty,
            token.Value<string>("stream") ?? string.Empty,
            token.Value<string>("method") ?? "play",
            headers,
            quality,
            subtitles,
            translation,
            translationId
        );
    }

    NormalizedProviderPayload ParseHtmlFallback(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var voices = ParseVoicesFromHtml(doc);
        var items = ParseDataJsonItems(doc);

        if (items.Count == 0)
            return Empty();

        bool hasSeason = items.Any(i => i.Value<string>("method") == "link" && Regex.IsMatch(i.Value<string>("url") ?? string.Empty, "[?&]s=", RegexOptions.IgnoreCase) && !Regex.IsMatch(i.Value<string>("url") ?? string.Empty, "[?&]e=", RegexOptions.IgnoreCase));
        bool hasEpisode = items.Any(i => i.Value<int?>("s") > 0 || i.Value<int?>("e") > 0 || Regex.IsMatch(i.Value<string>("title") ?? string.Empty, "серия|episode", RegexOptions.IgnoreCase));

        if (hasSeason)
        {
            var seasons = items.Select(ParseSeasonItem).Where(i => i != null).ToArray();
            return new NormalizedProviderPayload(ProviderPayloadType.Season, voices, seasons, Array.Empty<NormalizedPlayableItem>(), Array.Empty<NormalizedEpisode>());
        }

        if (hasEpisode)
        {
            var episodes = items.Select(ParseEpisodeItem).Where(i => i != null).ToArray();
            return new NormalizedProviderPayload(ProviderPayloadType.Episode, voices, Array.Empty<NormalizedSeason>(), Array.Empty<NormalizedPlayableItem>(), episodes);
        }

        var movies = items.Select(ParseMovieItem).Where(i => i != null).ToArray();
        return new NormalizedProviderPayload(ProviderPayloadType.Movie, voices, Array.Empty<NormalizedSeason>(), movies, Array.Empty<NormalizedEpisode>());
    }

    static List<JObject> ParseDataJsonItems(HtmlDocument doc)
    {
        var list = new List<JObject>();
        var nodes = doc.DocumentNode.SelectNodes("//*[@data-json]");

        if (nodes == null)
            return list;

        foreach (var node in nodes)
        {
            string json = node.GetAttributeValue("data-json", string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                continue;

            json = HttpUtility.HtmlDecode(json);
            try
            {
                if (JToken.Parse(json) is JObject jo)
                {
                    string title = node.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(title) && !jo.ContainsKey("name"))
                        jo["name"] = title;

                    if (node.Attributes["s"] != null && short.TryParse(node.Attributes["s"].Value, out short s))
                        jo["s"] = s;

                    if (node.Attributes["e"] != null && short.TryParse(node.Attributes["e"].Value, out short e))
                        jo["e"] = e;

                    list.Add(jo);
                }
            }
            catch
            {
                // ignore malformed nodes
            }
        }

        return list;
    }

    static IReadOnlyList<NormalizedVoice> ParseVoicesFromHtml(HtmlDocument doc)
    {
        var list = new List<NormalizedVoice>();
        var nodes = doc.DocumentNode.SelectNodes("//*[contains(@class,'videos__button') and @data-json]");

        if (nodes == null)
            return list;

        foreach (var node in nodes)
        {
            string raw = HttpUtility.HtmlDecode(node.GetAttributeValue("data-json", string.Empty));
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            try
            {
                if (JToken.Parse(raw) is not JObject jo)
                    continue;

                string url = jo.Value<string>("url") ?? string.Empty;
                string name = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty).Trim();
                bool active = (node.GetAttributeValue("class", string.Empty) ?? string.Empty).Contains("active", StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(new NormalizedVoice(ExtractTranslationId(url, name), name, active, url));
            }
            catch
            {
                // ignore malformed voice
            }
        }

        return list;
    }

    public static string ExtractTranslationId(string url, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            foreach (var key in new[] { "t", "voice", "translator", "tr" })
            {
                var m = Regex.Match(url, $"[?&]{key}=([^&]+)", RegexOptions.IgnoreCase);
                if (m.Success)
                    return HttpUtility.UrlDecode(m.Groups[1].Value);
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? "default" : fallback.Trim();
    }

    static int ExtractNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var m = Regex.Match(value, "([0-9]+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
            return n;

        return 0;
    }
}
