using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>
/// The RBN node list (https://www.reversebeacon.net/cont_includes/status.php?t=skt,
/// the piece of HTML behind reversebeacon.net/nodes): one &lt;tr&gt; per node, cells
/// call, bands, locator, DXCC, continent, ITU, CQ, first seen, last seen (each
/// "seen" with its seconds ago in a hidden &lt;b&gt;); the row class has "online"
/// when the node is. Not an API: it can change without notice. The text is not
/// ours, so every value is checked and a row that does not fit is left out; a
/// page with no good row has changed format.
/// </summary>
public static partial class RbnNodeListParser
{
    private const int Cells = 9;

    public static RbnNodeList Parse(string html, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(html);
        var nodes = new List<RbnNode>();
        foreach (Match row in Row().Matches(html))
        {
            if (Node(row.Groups["class"].Value, row.Groups["cells"].Value, fetchedAt) is { } node) nodes.Add(node);
        }
        return new RbnNodeList(nodes, FormatChanged: nodes.Count == 0);
    }

    private static RbnNode? Node(string rowClass, string body, DateTimeOffset fetchedAt)
    {
        var cells = Cell().Matches(body).Select(m => m.Groups["text"].Value).ToArray();
        if (cells.Length < Cells) return null;
        var call = Text(cells[0]).ToUpperInvariant();
        if (!Call().IsMatch(call)) return null;
        var bands = Text(cells[1]).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(b => Bands.Labels.Contains(b, StringComparer.Ordinal))
            .ToArray();
        var locator = Text(cells[2]);
        var online = rowClass.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("online", StringComparer.Ordinal);
        return new RbnNode(call, LocatorLike().IsMatch(locator) ? locator : null, bands, online, LastSeen(cells[8], fetchedAt));
    }

    /// <summary>The hidden "seconds ago" of the last-seen cell, as a moment.</summary>
    private static DateTimeOffset? LastSeen(string cell, DateTimeOffset fetchedAt)
    {
        var m = SecondsAgo().Match(cell);
        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s < 20L * 365 * 86400
            ? fetchedAt.AddSeconds(-s)
            : null;
    }

    /// <summary>The visible text of a cell: tags removed, entities decoded, spaces trimmed.</summary>
    private static string Text(string cell) => WebUtility.HtmlDecode(Tag().Replace(cell, " ")).Trim();

    [GeneratedRegex(@"<tr\s+class=""(?<class>[^""]*)""\s*>(?<cells>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Row();

    [GeneratedRegex(@"<td[^>]*>(?<text>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Cell();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"<b\s+class=""hide"">\s*(\d{1,12})\s*</b>")]
    private static partial Regex SecondsAgo();

    // A skimmer's call as the node list writes it: "OK4YL", "PA5KT-4", "4X1ABC/P".
    [GeneratedRegex(@"^[A-Z0-9]{1,4}[0-9][A-Z0-9/]{0,10}(-\d{1,2})?$")]
    private static partial Regex Call();

    // Something that looks like a locator; whether it is a valid 6-character one is SkimmerPosition's rule.
    [GeneratedRegex(@"^[A-Za-z0-9]{2,10}$")]
    private static partial Regex LocatorLike();
}
