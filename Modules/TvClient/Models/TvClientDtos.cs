namespace TvClient.Models;

public record AuthContext(
    string account_email,
    string uid,
    string token,
    string nws_id,
    string profile_id
);

public record MediaCardDto(
    long tmdb_id,
    string media,
    string title,
    string original_title,
    string overview,
    string poster,
    string backdrop,
    string release_date,
    double vote_average,
    string original_language,
    int year
);

public record HomeResponseDto(
    string lang,
    int page,
    IReadOnlyList<MediaCardDto> trending_movies,
    IReadOnlyList<MediaCardDto> trending_tv,
    WatchingNowDto watching_now
);

public record WatchingNowDto(
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] bool enabled,
    IReadOnlyList<object> items
);

public record PagedMediaResponseDto(
    string feed,
    string media,
    int page,
    int total_pages,
    IReadOnlyList<MediaCardDto> results
);

public record TitleDetailResponseDto(
    string media,
    long tmdb_id,
    string title,
    string original_title,
    string overview,
    string poster,
    string backdrop,
    string release_date,
    double vote_average,
    string original_language,
    int year,
    object credits,
    IReadOnlyList<MediaCardDto> recommendations,
    IReadOnlyList<MediaCardDto> similar,
    ProvidersSummaryDto providers
);

public record ProvidersSummaryDto(
    string default_provider,
    IReadOnlyList<ProviderItemDto> items
);

public record ProviderItemDto(
    string code,
    string name,
    string url,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] int priority,
    bool available,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] bool supports_series,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] bool supports_movie
);

public record ProviderOptionsResponseDto(
    string media,
    long tmdb_id,
    string provider,
    string provider_status,
    string parse_source,
    string proxy_mode,
    int selected_season,
    SelectionDto selected,
    IReadOnlyList<SeasonOptionDto> seasons,
    IReadOnlyList<TranslationOptionDto> translations,
    IReadOnlyList<string> qualities,
    IReadOnlyList<EpisodeOptionDto> episodes,
    IReadOnlyList<PlannedEpisodeDto> planned_episodes,
    IReadOnlyList<SubtitleOptionDto> subtitles,
    IReadOnlyList<MovieStreamOptionDto> movie_streams
);

public record SeasonOptionDto(int season, string name, bool active);

public record TranslationOptionDto(string id, string name, bool active);

public record SubtitleOptionDto(string label, string url);

public record MovieStreamOptionDto(
    string translation,
    string translation_id,
    string display_quality,
    IReadOnlyList<string> available_qualities,
    IReadOnlyList<SubtitleOptionDto> subtitles,
    PlayCandidateDto play
);

public record EpisodeOptionDto(
    int season,
    int episode,
    string name,
    string title,
    string status,
    string air_date,
    int? days_until,
    IReadOnlyList<string> available_qualities,
    string translation,
    string translation_id,
    IReadOnlyList<SubtitleOptionDto> subtitles,
    PlayCandidateDto play
);

public record PlannedEpisodeDto(
    int season,
    int episode,
    string name,
    string air_date,
    int? days_until,
    string status
);

public record SelectionDto(
    int season,
    int episode,
    string translation,
    string translation_id,
    string quality
);

public record PlayResolveRequestDto(
    string media,
    long tmdbId,
    string provider,
    int? season,
    int? episode,
    string translation,
    string quality,
    string lang,
    string account_email,
    string uid,
    string token,
    string nws_id,
    string profile_id
);

public record PlayResolveResponseDto(
    string media,
    long tmdb_id,
    string provider,
    string provider_status,
    string parse_source,
    string proxy_mode,
    SelectionDto selected,
    PlayResultDto play,
    IReadOnlyList<QueueItemDto> up_next
);

public record PlayResultDto(
    string url,
    string stream_type,
    IReadOnlyDictionary<string, string> headers,
    IReadOnlyList<SubtitleOptionDto> subtitles
);

public record PlayCandidateDto(
    string url,
    string stream,
    string method,
    IReadOnlyDictionary<string, string> headers,
    IReadOnlyDictionary<string, string> quality,
    IReadOnlyList<SubtitleOptionDto> subtitles,
    string stream_type = "unknown"
);

public record QueueItemDto(
    int season,
    int episode,
    string name,
    string title,
    PlayResultDto play
);

public record ApiErrorDto(string code, string message);

public record ApiEnvelope<T>(T data, ApiErrorDto error = null);
