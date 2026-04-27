using System.Text.RegularExpressions;

namespace PriceWatcher.Price;

public sealed class PrinzipCatalogClient : IPrinzipCatalogClient
{
    private static readonly Regex AbsoluteUrlRegex =
        new(@"https://prinzip\.su/apartments/[a-zA-Z0-9_/-]+/\d+/?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RelativeUrlRegex =
        new(@"/apartments/[a-zA-Z0-9_/-]+/\d+/?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _http;
    private readonly ILogger<PrinzipCatalogClient> _logger;

    public PrinzipCatalogClient(HttpClient http, ILogger<PrinzipCatalogClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetApartmentUrlsAsync(int maxCount, CancellationToken ct)
    {
        maxCount = Math.Clamp(maxCount, 1, 1000);
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await CollectFromSitemapAsync("https://prinzip.su/sitemap.xml", urls, ct);
        await CollectFromSitemapAsync("https://prinzip.su/sitemap-apartments.xml", urls, ct);
        await CollectFromApartmentsPageAsync("https://prinzip.su/apartments/", urls, ct);

        if (urls.Count == 0)
        {
            _logger.LogWarning("No apartment URLs found in sitemap");
            return [];
        }

        return urls.Take(maxCount).ToArray();
    }

    private async Task CollectFromSitemapAsync(string sitemapUrl, HashSet<string> urls, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, sitemapUrl);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PriceWatcher/1.0");
            req.Headers.Accept.ParseAdd("application/xml,text/xml,*/*");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return;

            var xml = await resp.Content.ReadAsStringAsync(ct);
            var matches = AbsoluteUrlRegex.Matches(xml);
            foreach (Match match in matches)
            {
                if (match.Success)
                    urls.Add(match.Value.Trim());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to collect apartment URLs from {SitemapUrl}", sitemapUrl);
        }
    }

    private async Task CollectFromApartmentsPageAsync(string pageUrl, HashSet<string> urls, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PriceWatcher/1.0");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return;

            var html = await resp.Content.ReadAsStringAsync(ct);

            foreach (Match absolute in AbsoluteUrlRegex.Matches(html))
            {
                if (absolute.Success)
                    urls.Add(absolute.Value.Trim());
            }

            foreach (Match relative in RelativeUrlRegex.Matches(html))
            {
                if (!relative.Success)
                    continue;

                urls.Add($"https://prinzip.su{relative.Value.Trim()}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to collect apartment URLs from page {PageUrl}", pageUrl);
        }
    }
}

