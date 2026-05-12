namespace TvClient.Models;

public enum ProviderPayloadType
{
    Unknown = 0,
    Movie = 1,
    Season = 2,
    Episode = 3
}

public record NormalizedProviderPayload(
    ProviderPayloadType Type,
    string ParseSource,
    IReadOnlyList<NormalizedVoice> Voices,
    IReadOnlyList<NormalizedSeason> Seasons,
    IReadOnlyList<NormalizedPlayableItem> Movies,
    IReadOnlyList<NormalizedEpisode> Episodes
);

public record NormalizedVoice(string id, string name, bool active, string url);

public record NormalizedSeason(int id, string name, string url);

public record NormalizedPlayableItem(
    string translate,
    string translate_id,
    string url,
    string stream,
    string method,
    Dictionary<string, string> headers,
    Dictionary<string, string> quality,
    List<SubtitleOptionDto> subtitles,
    string maxquality,
    string details,
    string title
);

public record NormalizedEpisode(
    int season,
    int episode,
    string name,
    string title,
    string details,
    string url,
    string stream,
    string method,
    Dictionary<string, string> headers,
    Dictionary<string, string> quality,
    List<SubtitleOptionDto> subtitles,
    string translation,
    string translation_id
);

public record ProviderContext(
    string media,
    long tmdb_id,
    string source,
    string rchtype,
    string title,
    string original_title,
    string original_language,
    int year,
    string imdb_id,
    string kinopoisk_id,
    bool serial
);

public record ProviderDiscoveryResult(
    string default_provider,
    IReadOnlyList<ProviderItemDto> items,
    Dictionary<string, string> provider_urls
);
