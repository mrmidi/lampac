using TvClient.Models;

namespace TvClient.Services;

public class PlaybackResolveService
{
    readonly Func<PlayResultDto, PlayResultDto> _wrapPlay;

    public PlaybackResolveService(Func<PlayResultDto, PlayResultDto> wrapPlay = null)
    {
        _wrapPlay = wrapPlay;
    }

    public PlayResolveResponseDto Resolve(PlayResolveRequestDto request, ProviderOptionsSnapshot snapshot)
    {
        if (snapshot?.Response == null)
            return null;

        var options = snapshot.Response;
        string media = string.Equals(request.media, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";

        if (media == "movie")
            return ResolveMovie(request, snapshot, options);

        return ResolveSeries(request, snapshot, options);
    }

    PlayResolveResponseDto ResolveMovie(PlayResolveRequestDto request, ProviderOptionsSnapshot snapshot, ProviderOptionsResponseDto options)
    {
        var streams = options.movie_streams?.ToList() ?? new List<MovieStreamOptionDto>();
        if (streams.Count == 0)
            return null;

        string selectedTranslationId = SelectTranslation(request.translation, options.translations);
        var filtered = streams
            .Where(s => string.IsNullOrWhiteSpace(selectedTranslationId)
                || selectedTranslationId.Equals(s.translation_id, StringComparison.OrdinalIgnoreCase)
                || selectedTranslationId.Equals(s.translation, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
            filtered = streams;

        var candidate = filtered.FirstOrDefault()?.play;
        if (candidate == null)
            return null;

        var play = PlaybackSelection.ToPlayResult(candidate, request.quality, out string selectedQuality);
        if (_wrapPlay != null)
            play = _wrapPlay(play);
        if (play == null || string.IsNullOrWhiteSpace(play.url))
            return null;
        if (string.IsNullOrWhiteSpace(selectedQuality))
            selectedQuality = PlaybackSelection.PickQuality(options.qualities, request.quality);

        var selectedTranslation = options.translations?.FirstOrDefault(t => t.id.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase));

        return new PlayResolveResponseDto(
            "movie",
            request.tmdbId,
            request.provider,
            snapshot.ProviderStatus,
            snapshot.ParseSource,
            "proxy",
            new SelectionDto(1, 0, selectedTranslation?.name ?? options.selected.translation, selectedTranslation?.id ?? options.selected.translation_id, selectedQuality),
            play,
            Array.Empty<QueueItemDto>()
        );
    }

    PlayResolveResponseDto ResolveSeries(PlayResolveRequestDto request, ProviderOptionsSnapshot snapshot, ProviderOptionsResponseDto options)
    {
        int season = request.season.GetValueOrDefault(options.selected_season > 0 ? options.selected_season : 1);
        string selectedTranslationId = SelectTranslation(request.translation, options.translations);

        var candidates = (options.episodes ?? Array.Empty<EpisodeOptionDto>())
            .Where(e => e.season == season && e.status == "available" && e.play != null)
            .ToList();

        if (candidates.Count == 0)
            return null;

        var byTranslation = candidates
            .Where(e => string.IsNullOrWhiteSpace(selectedTranslationId)
                || selectedTranslationId.Equals(e.translation_id, StringComparison.OrdinalIgnoreCase)
                || selectedTranslationId.Equals(e.translation, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byTranslation.Count > 0)
            candidates = byTranslation;

        EpisodeOptionDto selectedEpisode = null;

        if (request.episode.HasValue)
            selectedEpisode = candidates.FirstOrDefault(e => e.episode == request.episode.Value);

        selectedEpisode ??= candidates.OrderBy(e => e.episode).FirstOrDefault();
        if (selectedEpisode == null)
            return null;

        var play = PlaybackSelection.ToPlayResult(selectedEpisode.play, request.quality, out string selectedQuality);
        if (_wrapPlay != null)
            play = _wrapPlay(play);
        if (play == null || string.IsNullOrWhiteSpace(play.url))
            return null;
        if (string.IsNullOrWhiteSpace(selectedQuality))
            selectedQuality = PlaybackSelection.PickQuality(selectedEpisode.available_qualities, request.quality);

        var translation = options.translations?.FirstOrDefault(t => t.id.Equals(selectedEpisode.translation_id, StringComparison.OrdinalIgnoreCase));

        int queueLimit = Math.Max(1, ModInit.conf?.queue_limit ?? 1);

        var upNext = candidates
            .Where(e => e.episode > selectedEpisode.episode)
            .OrderBy(e => e.episode)
            .Take(queueLimit)
            .Select(e =>
            {
                var qPlay = PlaybackSelection.ToPlayResult(e.play, selectedQuality, out _);
                if (_wrapPlay != null)
                    qPlay = _wrapPlay(qPlay);
                if (qPlay == null || string.IsNullOrWhiteSpace(qPlay.url))
                    return null;
                return new QueueItemDto(e.season, e.episode, e.name, e.title, qPlay);
            })
            .Where(i => i != null)
            .ToArray();

        return new PlayResolveResponseDto(
            "tv",
            request.tmdbId,
            request.provider,
            snapshot.ProviderStatus,
            snapshot.ParseSource,
            "proxy",
            new SelectionDto(
                season,
                selectedEpisode.episode,
                translation?.name ?? selectedEpisode.translation,
                translation?.id ?? selectedEpisode.translation_id,
                selectedQuality
            ),
            play,
            upNext
        );
    }

    static string SelectTranslation(string requested, IReadOnlyList<TranslationOptionDto> translations)
    {
        if (translations == null || translations.Count == 0)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var exact = translations.FirstOrDefault(t =>
                requested.Equals(t.id, StringComparison.OrdinalIgnoreCase)
                || requested.Equals(t.name, StringComparison.OrdinalIgnoreCase));

            if (exact != null)
                return exact.id;
        }

        var active = translations.FirstOrDefault(t => t.active);
        if (active != null)
            return active.id;

        return translations[0].id;
    }
}
