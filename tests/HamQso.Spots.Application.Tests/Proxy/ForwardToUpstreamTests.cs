using System.Text;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Proxy;

public class ForwardToUpstreamTests
{
    private const string User = "4b1e2a9c-0d3f-4e5a-8b6c-7d8e9f0a1b2c";

    private readonly FakeTimeProvider time = new();
    private readonly FakeUpstream upstream = new();
    private readonly RecordingLogger logger = new();
    private readonly ForwardToUpstream forward;

    public ForwardToUpstreamTests() => forward = new ForwardToUpstream(upstream, new ProxyRateLimiter(time), time, logger);

    private static ProxyCall Call() => new(
        "qrz-logbook",
        "POST",
        "api/secret-path",
        "?KEY=QRZ-API-KEY-1234",
        new Dictionary<string, string> { ["Authorization"] = "Bearer SOTA-TOKEN-5678" },
        Encoding.UTF8.GetBytes("password=EQSL-PASSWORD-9999"),
        "application/x-www-form-urlencoded");

    private Task<ForwardResult> Forward(string user = User, string address = "203.0.113.7") => forward.ExecuteAsync(new TokenUser(user, false, false), address, Call(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_call_goes_to_the_service_and_its_answer_comes_back()
    {
        var result = await Forward();

        Assert.Same(upstream.Reply, result.Reply);
        Assert.Equal(Call() with { Headers = upstream.Received!.Headers, Body = upstream.Received.Body }, upstream.Received);
    }

    [Fact]
    public async Task The_log_has_user_service_method_status_duration_and_sizes_and_nothing_else()
    {
        time.AutoAdvanceAmount = TimeSpan.FromMilliseconds(40);

        await Forward();

        var line = Assert.Single(logger.Messages);
        Assert.StartsWith($"Proxy {User} qrz-logbook POST -> 200 in ", line, StringComparison.Ordinal);
        Assert.EndsWith(" ms, 27 B up, 5 B down", line, StringComparison.Ordinal);
        foreach (var secret in new[] { "QRZ-API-KEY", "SOTA-TOKEN", "EQSL-PASSWORD", "secret-path", "KEY=", "Bearer", "x-www-form", "203.0.113.7" })
        {
            Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task After_120_calls_in_a_minute_the_user_waits_and_nothing_goes_out()
    {
        for (var i = 0; i < 120; i++) Assert.NotNull((await Forward()).Reply);

        var limited = await Forward();

        Assert.Null(limited.Reply);
        Assert.Equal((ProxyAdmission.RateLimited, TimeSpan.FromMinutes(1)), (limited.Admission, limited.RetryAfter));
        Assert.Equal(120, upstream.Calls);
        Assert.EndsWith("-> rate_limited in 0 ms, 27 B up, 0 B down", logger.Messages[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_the_whole_server_is_over_its_budget_the_call_is_busy_and_logged_so()
    {
        for (var i = 0; i < 2000; i++) await Forward($"user-{i}", $"addr-{i}");

        var busy = await Forward("someone", "elsewhere");

        Assert.Equal((null, ProxyAdmission.Busy), (busy.Reply, busy.Admission));
        Assert.Equal(2000, upstream.Calls);
        Assert.Contains("-> busy in 0 ms", logger.Messages[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_is_logged_by_its_kind_not_by_its_message()
    {
        upstream.Reply = ProxyReply.Failed(ProxyOutcome.UpstreamTimeout);

        await Forward();

        Assert.Contains("-> UpstreamTimeout in ", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    private sealed class FakeUpstream : IUpstreamProxy
    {
        public ProxyReply Reply { get; set; } = new(ProxyOutcome.Answered, 200, "text/plain", Encoding.UTF8.GetBytes("QSO=1"));

        public ProxyCall? Received { get; private set; }

        public int Calls { get; private set; }

        public bool Knows(string service) => service == "qrz-logbook";

        public Task<ProxyReply> SendAsync(ProxyCall request, CancellationToken cancellationToken)
        {
            Received = request;
            Calls++;
            return Task.FromResult(Reply);
        }
    }

    private sealed class RecordingLogger : ILogger<ForwardToUpstream>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
