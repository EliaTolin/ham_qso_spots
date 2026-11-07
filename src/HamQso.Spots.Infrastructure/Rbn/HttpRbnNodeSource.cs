using HamQso.Spots.Application.Ports;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>Downloads the RBN node list page (<see cref="RbnOptions.NodesUrl"/>), at most <see cref="MaxBytes"/>.</summary>
public sealed class HttpRbnNodeSource(HttpClient http, IOptions<RbnOptions> options, TimeProvider time) : IRbnNodeSource
{
    /// <summary>The page is about 313 KB (350 nodes, 07/11/2026): ten times that is not the node list.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    public async Task<RbnNodeList> FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(options.Value.NodesUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"node list: HTTP {(int)response.StatusCode}");
        if (response.Content.Headers.ContentLength > MaxBytes) throw new HttpRequestException("node list: too large");
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var limited = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (limited.Length + read > MaxBytes) throw new HttpRequestException("node list: too large");
            limited.Write(buffer, 0, read);
        }
        // The site serves Latin-1; the fields we read are ASCII anyway.
        return RbnNodeListParser.Parse(System.Text.Encoding.Latin1.GetString(limited.GetBuffer(), 0, (int)limited.Length), time.GetUtcNow());
    }
}
