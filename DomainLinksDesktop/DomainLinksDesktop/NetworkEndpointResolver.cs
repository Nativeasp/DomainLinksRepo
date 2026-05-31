using System.Net.Http;

namespace DomainLinksDesktop;

internal static class NetworkEndpointResolver
{
    public static async Task<string> ResolveHttpBaseUrlAsync(
        string configuredUrl,
        IEnumerable<string> fallbackUrls,
        string healthPath,
        CancellationToken cancellationToken = default)
    {
        foreach (var baseUrl in BuildCandidateUrls(configuredUrl, fallbackUrls))
        {
            if (await CanReachAsync(baseUrl, healthPath, cancellationToken))
            {
                return baseUrl;
            }
        }

        return NormalizeUrl(configuredUrl);
    }

    private static IEnumerable<string> BuildCandidateUrls(string configuredUrl, IEnumerable<string> fallbackUrls)
    {
        return new[] { configuredUrl }
            .Concat(fallbackUrls)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> CanReachAsync(string baseUrl, string healthPath, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromMilliseconds(1200),
            };
            using var response = await client.GetAsync(healthPath, linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeUrl(string value) => value.Trim().TrimEnd('/');
}
