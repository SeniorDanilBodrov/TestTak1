using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceWatcher.Price;

public sealed class PrinzipHtmlPriceClient : IListingPriceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PrinzipHtmlPriceClient> _logger;

    private static readonly Regex JsonPriceRegex =
        new("\"(?:price|full_price|base_price)\"\\s*:\\s*(\\d{6,})", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FlatIdFromUrlRegex =
        new("/(\\d+)/?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FlatDataRegex =
        new("data-flat-id\\s*=\\s*\"(?<id>\\d+)\"[^>]*data-price\\s*=\\s*\"(?<price>\\d+(?:\\.\\d+)?)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AnyDataPriceRegex =
        new("data-price\\s*=\\s*\"(?<price>\\d+(?:\\.\\d+)?)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RubPriceRegex =
        new("(?<!\\d)(\\d{1,3}(?:[\\s\\u00A0\\u202F]\\d{3})+|\\d{6,})\\s*(?:₽|руб\\.?|RUB)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public PrinzipHtmlPriceClient(HttpClient http, ILogger<PrinzipHtmlPriceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<long?> TryGetPriceRubAsync(string listingUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, listingUrl);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PriceWatcher/1.0");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Non-success status {StatusCode} for {Url}", (int)resp.StatusCode, listingUrl);
                return null;
            }

            var html = await resp.Content.ReadAsStringAsync(ct);
            return TryExtractPriceRub(html, listingUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch/parse price for {Url}", listingUrl);
            return null;
        }
    }

    private long? TryExtractPriceRub(string html, string listingUrl)
    {
        var normalized = System.Net.WebUtility.HtmlDecode(html)
            .Replace('\u00A0', ' ')
            .Replace('\u202F', ' ');

        var flatId = ExtractFlatIdFromUrl(listingUrl);
        if (!string.IsNullOrWhiteSpace(flatId))
        {
            foreach (Match m in FlatDataRegex.Matches(normalized))
            {
                if (!m.Success || !string.Equals(m.Groups["id"].Value, flatId, StringComparison.Ordinal))
                    continue;

                if (TryParsePrice(m.Groups["price"].Value, out var matchedFlatPrice))
                    return matchedFlatPrice;
            }
        }

        var anyDataPrice = AnyDataPriceRegex.Match(normalized);
        if (anyDataPrice.Success && TryParsePrice(anyDataPrice.Groups["price"].Value, out var anyDataPriceValue))
            return anyDataPriceValue;

        var jsonMatch = JsonPriceRegex.Match(normalized);
        if (jsonMatch.Success && TryParsePrice(jsonMatch.Groups[1].Value, out var jsonPrice))
            return jsonPrice;

        var focusIndex = normalized.IndexOf("кв. №", StringComparison.OrdinalIgnoreCase);
        if (focusIndex >= 0)
        {
            var start = Math.Max(0, focusIndex - 800);
            var len = Math.Min(normalized.Length - start, 4500);
            var focusedChunk = normalized.Substring(start, len);

            var focused = ExtractRubCandidates(focusedChunk).ToList();
            if (focused.Count > 0)
                return focused.Max();
        }

        var allCandidates = ExtractRubCandidates(normalized)
            .Where(x => x is > 500_000 and < 300_000_000)
            .ToList();

        if (allCandidates.Count > 0)
            return allCandidates.Min();

        _logger.LogWarning("Price not found on page {Url}", listingUrl);
        return null;
    }

    private static IEnumerable<long> ExtractRubCandidates(string content)
    {
        var matches = RubPriceRegex.Matches(content);
        foreach (Match match in matches)
        {
            if (!match.Success)
                continue;

            if (TryParsePrice(match.Groups[1].Value, out var price))
                yield return price;
        }
    }

    private static bool TryParsePrice(string raw, out long price)
    {
        var normalized = raw.Trim().Replace(',', '.');
        if (normalized.Contains('.'))
        {
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalPrice))
            {
                price = (long)Math.Round(decimalPrice, MidpointRounding.AwayFromZero);
                return price >= 100_000;
            }
        }

        var digitsOnly = new string(raw.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length < 6)
        {
            price = 0;
            return false;
        }

        return long.TryParse(digitsOnly, NumberStyles.None, CultureInfo.InvariantCulture, out price);
    }

    private static string? ExtractFlatIdFromUrl(string listingUrl)
    {
        var m = FlatIdFromUrlRegex.Match(listingUrl);
        return m.Success ? m.Groups[1].Value : null;
    }
}

