using TvClient.Models;

namespace TvClient.Services;

public static class PlaybackSelection
{
    public static string NormalizeQuality(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            return string.Empty;

        quality = quality.Trim().ToLowerInvariant();
        if (!quality.EndsWith("p", StringComparison.Ordinal))
            quality += "p";

        return quality;
    }

    public static int QualityRank(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            return -1;

        var m = Regex.Match(quality, "([0-9]{3,4})", RegexOptions.IgnoreCase);
        if (!m.Success)
            return -1;

        if (int.TryParse(m.Groups[1].Value, out int n))
            return n;

        return -1;
    }

    public static string PickQuality(IEnumerable<string> available, string requested = null)
    {
        var list = (available ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeQuality)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
            return string.Empty;

        string normalizedRequested = NormalizeQuality(requested);
        if (!string.IsNullOrWhiteSpace(normalizedRequested))
        {
            var exact = list.FirstOrDefault(x => x.Equals(normalizedRequested, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact))
                return exact;
        }

        return list.OrderByDescending(QualityRank).ThenByDescending(x => x, StringComparer.OrdinalIgnoreCase).First();
    }

    public static string PickUrlForQuality(PlayCandidateDto candidate, string requestedQuality, out string selectedQuality)
    {
        selectedQuality = string.Empty;

        if (candidate?.quality != null && candidate.quality.Count > 0)
        {
            selectedQuality = PickQuality(candidate.quality.Keys, requestedQuality);
            if (!string.IsNullOrWhiteSpace(selectedQuality) && candidate.quality.TryGetValue(selectedQuality, out string qurl) && !string.IsNullOrWhiteSpace(qurl))
                return qurl;

            var fallback = candidate.quality
                .OrderByDescending(k => QualityRank(k.Key))
                .Select(k => k.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (!string.IsNullOrWhiteSpace(fallback))
                return fallback;
        }

        if (!string.IsNullOrWhiteSpace(candidate?.stream))
            return candidate.stream;

        return candidate?.url ?? string.Empty;
    }

    public static string DetectStreamType(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "unknown";

        string lower = url.ToLowerInvariant();
        if (lower.Contains(".m3u8"))
            return "hls";

        if (lower.Contains(".mpd"))
            return "dash";

        if (lower.Contains(".mp4") || lower.Contains(".mkv") || lower.Contains(".avi"))
            return "file";

        return "unknown";
    }

    public static PlayResultDto ToPlayResult(PlayCandidateDto candidate, string requestedQuality, out string selectedQuality)
    {
        string url = PickUrlForQuality(candidate, requestedQuality, out selectedQuality);

        return new PlayResultDto(
            url,
            DetectStreamType(url),
            candidate?.headers ?? new Dictionary<string, string>(),
            candidate?.subtitles ?? Array.Empty<SubtitleOptionDto>()
        );
    }
}
