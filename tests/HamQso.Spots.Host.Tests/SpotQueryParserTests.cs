using HamQso.Spots.Domain.Spots;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace HamQso.Spots.Host.Tests;

public class SpotQueryParserTests
{
    private static SpotQuery? Parse(string query, out string? error)
    {
        var values = QueryHelpersParse(query);
        return SpotQueryParser.Parse(name => values.TryGetValue(name, out var v) ? v : StringValues.Empty, out error);
    }

    private static Dictionary<string, StringValues> QueryHelpersParse(string query) =>
        new QueryString(query).HasValue ? new Dictionary<string, StringValues>(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query)) : [];

    [Fact]
    public void Program_band_mode_limit_and_since_seq_are_read()
    {
        var q = Parse("?program=dx&band=20m&band=40m,other&mode=cw&limit=50&since_seq=1041", out var error)!;

        Assert.Null(error);
        Assert.Equal(SpotProgram.Dx, q.Filter.Program);
        Assert.Equal(["20m", "40m", "other"], q.Filter.Bands);
        Assert.Equal(["cw"], q.Filter.Modes);
        Assert.Equal((50, 1041L), (q.Limit, q.SinceSeq));
    }

    [Fact]
    public void Only_the_program_is_required()
    {
        var q = Parse("?program=pota", out _)!;

        Assert.Equal((SpotProgram.Pota, 0, 0, (int?)null, (long?)null), (q.Filter.Program, q.Filter.Bands.Count, q.Filter.Modes.Count, q.Limit, q.SinceSeq));
    }

    [Theory]
    [InlineData("", "program")]
    [InlineData("?program=rbn", "program")]
    [InlineData("?program=dx&band=11m", "band")]
    [InlineData("?program=dx&mode=ssb", "mode")]
    [InlineData("?program=dx&limit=-1", "limit")]
    [InlineData("?program=dx&since_seq=abc", "since_seq")]
    public void A_wrong_query_says_what_is_wrong(string query, string about)
    {
        Assert.Null(Parse(query, out var error));
        Assert.Contains(about, error, StringComparison.Ordinal);
    }
}
