using TvClient.Models;

namespace TvClient.Services;

public class ProviderOptionsSnapshot
{
    public ProviderOptionsResponseDto Response { get; init; }
    public string ProviderStatus { get; init; } = "failed";
    public string ParseSource { get; init; } = "none";

    public IReadOnlyList<NormalizedEpisode> AllEpisodes { get; init; } = Array.Empty<NormalizedEpisode>();

    public IReadOnlyList<SeasonOptionDto> AllSeasons { get; init; } = Array.Empty<SeasonOptionDto>();

    public ProviderContext Context { get; init; }

    public string Provider { get; init; }
}

public class ProviderOptionsService
{
    readonly InternalApiClient _api;
    readonly CatalogService _catalog;
    readonly ProviderResponseNormalizer _normalizer;

    public ProviderOptionsService(InternalApiClient api, CatalogService catalog, ProviderResponseNormalizer normalizer)
    {
        _api = api;
        _catalog = catalog;
        _normalizer = normalizer;
    }

    static string EnsureRjson(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (Regex.IsMatch(url, "[?&]rjson=", RegexOptions.IgnoreCase))
            return url;

        return url + (url.Contains('?') ? "&" : "?") + "rjson=true";
    }

    static string SelectTranslation(string requested, IReadOnlyList<TranslationOptionDto> translations)
    {
        if (translations == null || translations.Count == 0)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = translations.FirstOrDefault(t =>
                requested.Equals(t.id, StringComparison.OrdinalIgnoreCase)
                || requested.Equals(t.name, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match.id;
        }

        var active = translations.FirstOrDefault(i => i.active);
        if (active != null)
            return active.id;

        return translations[0].id;
    }

    async Task<(IReadOnlyList<SeasonOptionDto> seasons, Dictionary<int, JObject> tmdbSeasons)> LoadTmdbSeasons(ProviderContext context, JObject preloadedDetail, string lang, int selectedSeason)
    {
        var seasons = new List<SeasonOptionDto>();
        var cache = new Dictionary<int, JObject>();

        if (context?.serial != true)
            return (seasons, cache);

        JObject detail = preloadedDetail;
        if (detail == null)
        {
            var (_, fetched) = await _catalog.GetTitleContext("tv", context.tmdb_id, lang);
            detail = fetched;
        }

        var tmdbSeasons = detail?["seasons"] as JArray;

        if (tmdbSeasons != null)
        {
            foreach (var s in tmdbSeasons.Children<JObject>())
            {
                int sn = s.Value<int?>("season_number") ?? 0;
                if (sn < 1)
                    continue;

                seasons.Add(new SeasonOptionDto(sn, $"S{sn}", sn == selectedSeason));
            }
        }

        if (selectedSeason > 0)
            cache[selectedSeason] = await _catalog.GetTvSeason(context.tmdb_id, selectedSeason, lang);

        return (seasons, cache);
    }

    public async Task<ProviderOptionsSnapshot> Build(ProviderContext context, string provider, string providerUrl, int? requestedSeason, string requestedTranslation, string requestedQuality, string lang, JObject preloadedDetail = null)
    {
        if (context == null || string.IsNullOrWhiteSpace(providerUrl))
            return new ProviderOptionsSnapshot
            {
                Response = new ProviderOptionsResponseDto(
                    context?.media ?? "movie",
                    context?.tmdb_id ?? 0,
                    provider ?? string.Empty,
                    "failed",
                    "none",
                    "proxy",
                    1,
                    new SelectionDto(1, 0, string.Empty, string.Empty, string.Empty),
                    Array.Empty<SeasonOptionDto>(),
                    Array.Empty<TranslationOptionDto>(),
                    Array.Empty<string>(),
                    Array.Empty<EpisodeOptionDto>(),
                    Array.Empty<PlannedEpisodeDto>(),
                    Array.Empty<SubtitleOptionDto>(),
                    Array.Empty<MovieStreamOptionDto>()
                ),
                Context = context,
                Provider = provider ?? string.Empty,
                ProviderStatus = "failed",
                ParseSource = "none"
            };

        string rootRaw = await _api.GetRaw(EnsureRjson(providerUrl), timeoutSec: ModInit.conf.providers_timeout_sec, statusCodeOK: false);
        var root = _normalizer.Parse(rootRaw);

        if (!context.serial)
            return BuildMovie(context, provider, root, requestedTranslation, requestedQuality);

        return await BuildSeries(context, provider, root, requestedSeason, requestedTranslation, requestedQuality, lang, preloadedDetail);
    }

    ProviderOptionsSnapshot BuildMovie(ProviderContext context, string provider, NormalizedProviderPayload payload, string requestedTranslation, string requestedQuality)
    {
        var movieItems = payload.Movies ?? Array.Empty<NormalizedPlayableItem>();

        var translations = new List<TranslationOptionDto>();
        if (payload.Voices?.Count > 0)
        {
            translations.AddRange(payload.Voices.Select(v => new TranslationOptionDto(v.id, v.name, v.active)));
        }
        else
        {
            translations.AddRange(movieItems
                .Select(i => i.translate_id)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id =>
                {
                    var src = movieItems.FirstOrDefault(m => id.Equals(m.translate_id, StringComparison.OrdinalIgnoreCase));
                    return new TranslationOptionDto(id, src?.translate ?? id, false);
                }));
        }

        string selectedTranslationId = SelectTranslation(requestedTranslation, translations);

        if (translations.Count > 0)
        {
            for (int i = 0; i < translations.Count; i++)
            {
                if (translations[i].id.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase))
                    translations[i] = translations[i] with { active = true };
            }
        }

        var filtered = movieItems
            .Where(i => translations.Count == 0 || i.translate_id.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase) || i.translate.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
            filtered = movieItems.ToList();

        var streamOptions = filtered.Select(i =>
        {
            var candidate = new PlayCandidateDto(i.url, i.stream, i.method, i.headers, i.quality, i.subtitles);
            var play = PlaybackSelection.ToPlayResult(candidate, requestedQuality, out string selectedQuality);
            var qlist = (i.quality?.Keys?.ToArray() ?? Array.Empty<string>()).Select(PlaybackSelection.NormalizeQuality).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(PlaybackSelection.QualityRank).ToArray();

            return new MovieStreamOptionDto(
                i.translate,
                i.translate_id,
                string.IsNullOrWhiteSpace(selectedQuality) ? i.maxquality : selectedQuality,
                qlist,
                i.subtitles?.ToArray() ?? Array.Empty<SubtitleOptionDto>(),
                new PlayCandidateDto(play.url, i.stream, i.method, i.headers, i.quality, i.subtitles, play.stream_type)
            );
        }).ToArray();

        var qualities = streamOptions.SelectMany(i => i.available_qualities).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(PlaybackSelection.QualityRank).ToArray();
        string selectedQualityGlobal = PlaybackSelection.PickQuality(qualities, requestedQuality);

        var subtitles = streamOptions.SelectMany(i => i.subtitles).GroupBy(i => i.url).Select(i => i.First()).ToArray();
        string selectedTranslationName = translations.FirstOrDefault(i => i.id.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase))?.name ?? string.Empty;

        var response = new ProviderOptionsResponseDto(
            "movie",
            context.tmdb_id,
            provider,
            streamOptions.Length > 0 ? "ok" : "no_items",
            payload.ParseSource ?? "none",
            "proxy",
            1,
            new SelectionDto(1, 0, selectedTranslationName, selectedTranslationId, selectedQualityGlobal),
            Array.Empty<SeasonOptionDto>(),
            translations,
            qualities,
            Array.Empty<EpisodeOptionDto>(),
            Array.Empty<PlannedEpisodeDto>(),
            subtitles,
            streamOptions
        );

        return new ProviderOptionsSnapshot
        {
            Response = response,
            Context = context,
            Provider = provider,
            ProviderStatus = streamOptions.Length > 0 ? "ok" : "no_items",
            ParseSource = payload.ParseSource ?? "none"
        };
    }

    async Task<ProviderOptionsSnapshot> BuildSeries(ProviderContext context, string provider, NormalizedProviderPayload root, int? requestedSeason, string requestedTranslation, string requestedQuality, string lang, JObject preloadedDetail = null)
    {
        var availableSeasons = new List<NormalizedSeason>();
        var episodesBySeason = new Dictionary<int, List<NormalizedEpisode>>();

        if (root.Type == ProviderPayloadType.Season)
        {
            availableSeasons.AddRange(root.Seasons.Where(s => s.id > 0));
        }
        else if (root.Type == ProviderPayloadType.Episode)
        {
            foreach (var ep in root.Episodes)
            {
                int bucket = ep.season > 0 ? ep.season : 1;
                if (!episodesBySeason.ContainsKey(bucket))
                    episodesBySeason[bucket] = new List<NormalizedEpisode>();
                episodesBySeason[bucket].Add(ep.season == 0 ? ep with { season = bucket } : ep);
            }

            availableSeasons.AddRange(episodesBySeason.Keys.OrderBy(i => i).Select(s => new NormalizedSeason(s, $"S{s}", string.Empty)));
        }

        int selectedSeason = requestedSeason.GetValueOrDefault(0);
        if (selectedSeason <= 0)
            selectedSeason = availableSeasons.Select(s => s.id).Where(i => i > 0).DefaultIfEmpty(1).First();

        if (availableSeasons.Count > 0 && !availableSeasons.Any(s => s.id == selectedSeason))
            selectedSeason = availableSeasons.Select(s => s.id).OrderBy(i => i).First();

        List<NormalizedEpisode> selectedSeasonEpisodes = null;
        var seasonEntry = availableSeasons.FirstOrDefault(s => s.id == selectedSeason);

        if (episodesBySeason.TryGetValue(selectedSeason, out var cachedEpisodes))
        {
            selectedSeasonEpisodes = cachedEpisodes;
        }
        else if (seasonEntry != null && !string.IsNullOrWhiteSpace(seasonEntry.url))
        {
            string seasonRaw = await _api.GetRaw(EnsureRjson(seasonEntry.url), timeoutSec: ModInit.conf.providers_timeout_sec, statusCodeOK: false);
            var seasonPayload = _normalizer.Parse(seasonRaw);
            if (seasonPayload.Type == ProviderPayloadType.Episode)
            {
                selectedSeasonEpisodes = seasonPayload.Episodes.Where(e => e.season == 0 || e.season == selectedSeason).ToList();

                if (selectedSeasonEpisodes.Any(e => e.season == 0))
                {
                    selectedSeasonEpisodes = selectedSeasonEpisodes
                        .Select(e => e with { season = selectedSeason })
                        .ToList();
                }

                episodesBySeason[selectedSeason] = selectedSeasonEpisodes;

                if (seasonPayload.Voices?.Count > 0)
                {
                    // Preserve translation hints from voice links if episode data lacks them.
                    var translationMap = seasonPayload.Voices.ToDictionary(v => v.id, v => v.name, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < selectedSeasonEpisodes.Count; i++)
                    {
                        var ep = selectedSeasonEpisodes[i];
                        if (string.IsNullOrWhiteSpace(ep.translation) && translationMap.TryGetValue(ep.translation_id ?? string.Empty, out string name))
                            selectedSeasonEpisodes[i] = ep with { translation = name };
                    }
                }
            }
        }

        selectedSeasonEpisodes ??= new List<NormalizedEpisode>();

        var allVoices = new List<NormalizedVoice>();
        allVoices.AddRange(root.Voices ?? Array.Empty<NormalizedVoice>());

        if (allVoices.Count == 0)
        {
            foreach (var g in selectedSeasonEpisodes
                .Where(e => !string.IsNullOrWhiteSpace(e.translation_id))
                .GroupBy(e => e.translation_id, StringComparer.OrdinalIgnoreCase))
            {
                var sample = g.First();
                allVoices.Add(new NormalizedVoice(sample.translation_id, sample.translation ?? sample.translation_id, false, string.Empty));
            }
        }

        var translations = allVoices
            .GroupBy(v => v.id, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new TranslationOptionDto(first.id, first.name, g.Any(i => i.active));
            })
            .ToList();

        string selectedTranslationId = SelectTranslation(requestedTranslation, translations);
        if (translations.Count > 0)
        {
            for (int i = 0; i < translations.Count; i++)
            {
                if (translations[i].id.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase))
                    translations[i] = translations[i] with { active = true };
            }
        }

        var filteredEpisodes = selectedSeasonEpisodes
            .Where(e => translations.Count == 0
                || e.translation_id.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase)
                || e.translation.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.episode)
            .ToList();

        if (filteredEpisodes.Count == 0)
            filteredEpisodes = selectedSeasonEpisodes.OrderBy(e => e.episode).ToList();

        var (tmdbSeasonOptions, tmdbSeasonsCache) = await LoadTmdbSeasons(context, preloadedDetail, lang, selectedSeason);
        var tmdbSeason = tmdbSeasonsCache.TryGetValue(selectedSeason, out var seasonData) ? seasonData : null;

        var episodeByNumber = filteredEpisodes.Where(e => e.episode > 0).GroupBy(e => e.episode).ToDictionary(g => g.Key, g => g.First());
        var episodeOptions = new List<EpisodeOptionDto>();
        var planned = new List<PlannedEpisodeDto>();

        var tmdbEpisodes = tmdbSeason?["episodes"]?.Children<JObject>().ToList() ?? new List<JObject>();
        if (tmdbEpisodes.Count > 0)
        {
            foreach (var tmdbEp in tmdbEpisodes)
            {
                int epNum = tmdbEp.Value<int?>("episode_number") ?? 0;
                if (epNum <= 0)
                    continue;

                string airDate = tmdbEp.Value<string>("air_date") ?? string.Empty;
                int? daysUntil = DaysUntil(airDate);

                if (episodeByNumber.TryGetValue(epNum, out var providerEp))
                {
                    episodeOptions.Add(ToEpisodeOption(providerEp, "available", airDate, daysUntil));
                }
                else
                {
                    string status = (daysUntil ?? 0) > 0 ? "upcoming" : "unavailable";
                    string name = tmdbEp.Value<string>("name") ?? $"Episode {epNum}";
                    // Keep TMDB-only entries in planned list; do not expose as
                    // playable episode options to avoid false-positive selection.
                    planned.Add(new PlannedEpisodeDto(selectedSeason, epNum, name, airDate, daysUntil, status));
                }
            }
        }
        else
        {
            episodeOptions.AddRange(filteredEpisodes.Select(ep => ToEpisodeOption(ep, "available", string.Empty, null)));
        }

        episodeOptions = episodeOptions.OrderBy(i => i.episode).ToList();

        var qualities = episodeOptions
            .SelectMany(e => e.available_qualities)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(PlaybackSelection.QualityRank)
            .ToArray();

        int selectedEpisode = episodeOptions.FirstOrDefault(i => i.status == "available")?.episode ?? episodeOptions.FirstOrDefault()?.episode ?? 0;
        string selectedQuality = PlaybackSelection.PickQuality(qualities, requestedQuality);
        string selectedTranslationName = translations.FirstOrDefault(t => t.id.Equals(selectedTranslationId, StringComparison.OrdinalIgnoreCase))?.name ?? string.Empty;

        var mergedSeasons = tmdbSeasonOptions.Count > 0
            ? tmdbSeasonOptions
            : availableSeasons.Select(s => new SeasonOptionDto(s.id, s.name, s.id == selectedSeason)).OrderBy(s => s.season).ToArray();

        var subtitles = episodeOptions
            .SelectMany(i => i.subtitles)
            .GroupBy(i => i.url)
            .Select(i => i.First())
            .ToArray();

        var response = new ProviderOptionsResponseDto(
            "tv",
            context.tmdb_id,
            provider,
            ComputeProviderStatus(episodeOptions, planned),
            root.ParseSource ?? "none",
            "proxy",
            selectedSeason,
            new SelectionDto(selectedSeason, selectedEpisode, selectedTranslationName, selectedTranslationId, selectedQuality),
            mergedSeasons,
            translations,
            qualities,
            episodeOptions,
            planned,
            subtitles,
            Array.Empty<MovieStreamOptionDto>()
        );

        var allEpisodes = episodesBySeason.Values.SelectMany(i => i).ToList();
        if (allEpisodes.Count == 0)
            allEpisodes = selectedSeasonEpisodes;

        return new ProviderOptionsSnapshot
        {
            Response = response,
            AllEpisodes = allEpisodes,
            AllSeasons = mergedSeasons,
            Context = context,
            Provider = provider,
            ProviderStatus = ComputeProviderStatus(episodeOptions, planned),
            ParseSource = root.ParseSource ?? "none"
        };
    }

    static string ComputeProviderStatus(IReadOnlyList<EpisodeOptionDto> episodeOptions, IReadOnlyList<PlannedEpisodeDto> planned)
    {
        bool hasAvailable = episodeOptions?.Any(e => e.status == "available" && e.play != null) == true;
        if (hasAvailable)
            return "ok";

        bool hasUpcoming = (episodeOptions?.Any(e => e.status == "upcoming" || e.status == "unavailable") == true)
            || (planned?.Count > 0);
        if (hasUpcoming)
            return "upcoming_only";

        return "no_items";
    }

    static string providerUrlFromEpisode(IReadOnlyList<NormalizedEpisode> episodes, int season)
    {
        var sample = episodes?.FirstOrDefault(e => e.season == season);
        if (sample == null)
            return string.Empty;

        return sample.url;
    }

    static EpisodeOptionDto ToEpisodeOption(NormalizedEpisode ep, string status, string airDate, int? daysUntil)
    {
        var candidate = new PlayCandidateDto(ep.url, ep.stream, ep.method, ep.headers, ep.quality, ep.subtitles);
        var play = PlaybackSelection.ToPlayResult(candidate, null, out _);
        var qualities = (ep.quality?.Keys?.ToArray() ?? Array.Empty<string>())
            .Select(PlaybackSelection.NormalizeQuality)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(PlaybackSelection.QualityRank)
            .ToArray();

        return new EpisodeOptionDto(
            ep.season,
            ep.episode,
            ep.name,
            ep.title,
            status,
            airDate,
            daysUntil,
            qualities,
            ep.translation,
            ep.translation_id,
            ep.subtitles?.ToArray() ?? Array.Empty<SubtitleOptionDto>(),
            candidate with { stream_type = play.stream_type }
        );
    }

    static int? DaysUntil(string airDate)
    {
        if (string.IsNullOrWhiteSpace(airDate))
            return null;

        if (!DateTime.TryParseExact(airDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime dt))
            return null;

        int days = (int)Math.Ceiling((dt.Date - DateTime.UtcNow.Date).TotalDays);
        return days;
    }
}
