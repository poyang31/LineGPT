using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace LineGPT;

public sealed record SearchSource(string Title, string Url, string Snippet);

public sealed class WebSearchService(HttpClient http, IConfiguration configuration, ILogger<WebSearchService> logger)
{
    public async Task<IReadOnlyList<SearchSource>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("WebSearch:TimeoutSeconds", 8), 1, 15)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://search.yahoo.com/search?p=" + Uri.EscapeDataString(query[..Math.Min(query.Length, 500)]));
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LineGPT/1.0)");
            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseResults(html, Math.Clamp(configuration.GetValue("WebSearch:MaxResults", 3), 1, 5));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Web search failed ({ExceptionType}).", exception.GetType().Name);
            return [];
        }
    }

    internal static IReadOnlyList<SearchSource> ParseResults(string html, int maxResults)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var results = new List<SearchSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in document.QuerySelectorAll("div.relsrch"))
        {
            var link = result.QuerySelector("div[class*='Title'] a[href]");
            var url = link?.GetAttribute("href")?.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            if (IsRedirectHost(uri.Host))
            {
                var match = Regex.Match(url!, @"/RU=(.*?)(?:/RK=|/RS=|$)");
                if (!match.Success || !Uri.TryCreate(Uri.UnescapeDataString(match.Groups[1].Value), UriKind.Absolute, out uri)) continue;
            }
            if (uri.Scheme is not ("https" or "http") || IsRedirectHost(uri.Host) ||
                (uri.Host == "www.bing.com" && uri.AbsolutePath == "/aclick")) continue;
            var snippet = Normalize(string.Join(" ", result.QuerySelectorAll("div[class*='Text']").Select(n => n.TextContent)));
            if (snippet.Length == 0 || !seen.Add(uri.AbsoluteUri)) continue;
            var title = Normalize(result.QuerySelector("h3")?.TextContent ?? link?.TextContent ?? "");
            results.Add(new(title[..Math.Min(title.Length, 200)], uri.AbsoluteUri, snippet[..Math.Min(snippet.Length, 1000)]));
            if (results.Count >= maxResults) break;
        }
        return results;
    }

    private static bool IsRedirectHost(string host) => host is "r.search.yahoo.com" or "rd.search.yahoo.com";
    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
