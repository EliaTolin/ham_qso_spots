using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HamQso.Voacap.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HamQso.Voacap.Tests;

/// <summary>The HTTP service (plan F71, 3.3) with voacapl faked; the contract is the shared fixtures.</summary>
public sealed class ApiTests : IDisposable
{
    private const string Token = "test-token-0123456789abcdef";

    private readonly Factory factory = new();

    public void Dispose() => factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonNode Fixture(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)))!;

    private HttpClient Client(string? token = Token)
    {
        var http = factory.CreateClient();
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private Task<HttpResponseMessage> Post(string path, string json, string? token = Token) =>
        Client(token).PostAsync(new Uri(path, UriKind.Relative), new StringContent(json, Encoding.UTF8, "application/json"), Ct);

    private static async Task<JsonNode> Body(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync(Ct))!;

    [Fact]
    public async Task Predict_answers_the_shared_fixture()
    {
        var fixture = Fixture("voacap_predict_v1.json");
        var expected = fixture["response"]!;
        factory.Engine.Answer = (_, ssn) => ssn switch
        {
            118 => new CircuitResult(0.83, 41, 19.6, "1F2"),
            _ => new CircuitResult(0.88, 43, 21.0, "1F2"),
        };
        // The station scenario has the yagi: told apart by its antenna file.
        factory.Engine.AnswerFor = antennas => antennas.Any(a => a.Content?.Contains("  24    [ 2]", StringComparison.Ordinal) == true) ? new CircuitResult(0.95, 49, 21.0, "1F2") : null;

        using var response = await Post("/v1/predict", fixture["request"]!.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.True(body["elapsed_ms"]!.GetValue<long>() >= 0);
        body.AsObject()["elapsed_ms"] = expected["elapsed_ms"]!.GetValue<long>();
        Assert.True(JsonNode.DeepEquals(expected, body), body.ToJsonString());
    }

    [Fact]
    public async Task Effective_ssn_answers_the_shared_fixture()
    {
        var fixture = Fixture("voacap_effective_ssn_v1.json");
        // Coarse: 140 is nearest (30 MHz against 5 below 131); fine: 131 gives 17.1.
        factory.Engine.Answer = (_, ssn) => new CircuitResult(0.5, 30, ssn == 131 ? 17.1 : ssn < 131 ? 5 : 30, "1F2");

        using var response = await Post("/v1/effective-ssn", fixture["request"]!.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(131, body["ssn_effective"]!.GetValue<int>());
        Assert.Equal(17.1, body["predicted_mufd_mhz"]!.GetValue<double>());
        Assert.Equal("voacapl 0.7.6", body["engine"]!.GetValue<string>());
        Assert.Equal(
            fixture["response"]!.AsObject().Select(p => p.Key).Order(),
            body.AsObject().Select(p => p.Key).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token")]
    public async Task Without_the_token_it_is_a_401(string? token)
    {
        using var response = await Post("/v1/predict", Fixture("voacap_predict_v1.json")["request"]!.ToJsonString(), token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Engine.Runs);
    }

    [Fact]
    public async Task Health_needs_no_token_and_names_the_engine()
    {
        using var response = await Client(null).GetAsync(new Uri("/health", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("voacapl 0.7.6", (await Body(response))["engine"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"month": 11}""")]
    public async Task A_body_that_is_not_a_request_is_invalid_request(string json)
    {
        using var response = await Post("/v1/predict", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", (await Body(response))["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_value_out_of_range_is_invalid_request()
    {
        var request = Fixture("voacap_predict_v1.json")["request"]!;
        request["month"] = 13;

        using var response = await Post("/v1/predict", request.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task More_than_600_receivers_is_a_422()
    {
        var request = Fixture("voacap_predict_v1.json")["request"]!;
        request["receivers"] = new JsonArray([.. Enumerable.Range(0, 601).Select(i => (JsonNode)new JsonObject { ["id"] = $"R{i}", ["lat"] = 50.0, ["lon"] = 14.0 })]);

        using var response = await Post("/v1/predict", request.ToJsonString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("too_many_receivers", (await Body(response))["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_engine_that_fails_is_a_503_with_its_message()
    {
        factory.Engine.Failure = new VoacapEngineException("PAUSE error in readant");

        using var response = await Post("/v1/predict", Fixture("voacap_predict_v1.json")["request"]!.ToJsonString());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(("engine_failed", "PAUSE error in readant"), (body["error"]!.GetValue<string>(), body["message"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Positions_never_reach_the_log()
    {
        using var response = await Post("/v1/predict", Fixture("voacap_predict_v1.json")["request"]!.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(factory.Logs, l => l.Contains("45.6", StringComparison.Ordinal) || l.Contains("49.98", StringComparison.Ordinal));
        Assert.Contains(factory.Logs, l => l.Contains("predict", StringComparison.Ordinal) && l.Contains("1 receivers", StringComparison.Ordinal));
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public SwitchEngine Engine { get; } = new();

        public System.Collections.Concurrent.ConcurrentQueue<string> Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Voacap:Token", Token);
            builder.UseSetting("Voacap:Engine", "voacapl 0.7.6");
            builder.ConfigureLogging(l => l.AddProvider(new Sink(Logs)));
            builder.ConfigureTestServices(s =>
            {
                s.RemoveAll<IVoacapEngine>();
                s.AddSingleton<IVoacapEngine>(Engine);
            });
        }
    }

    /// <summary>The fake engine, with an answer chosen by the whole deck when <see cref="AnswerFor"/> gives one.</summary>
    public sealed class SwitchEngine : IVoacapEngine
    {
        private readonly FakeEngine inner = new();

        public string Name => inner.Name;

        public List<(string Deck, IReadOnlyList<AntennaFile> Antennas)> Runs => inner.Runs;

        public Func<GeoPoint, double, CircuitResult> Answer { get => inner.Answer; set => inner.Answer = value; }

        public Func<IReadOnlyList<AntennaFile>, CircuitResult?> AnswerFor { get; set; } = _ => null;

        public Exception? Failure { get => inner.Failure; set => inner.Failure = value; }

        public Task<string> RunAsync(string deck, IReadOnlyList<AntennaFile> antennas, CancellationToken cancellationToken)
        {
            if (AnswerFor(antennas) is { } fixedAnswer)
            {
                var output = new FakeEngine { Answer = (_, _) => fixedAnswer }.RunAsync(deck, antennas, cancellationToken);
                lock (Runs) Runs.Add((deck, antennas));
                return output;
            }
            return inner.RunAsync(deck, antennas, cancellationToken);
        }
    }

    private sealed class Sink(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(lines);

        public void Dispose()
        {
        }

        private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }
}
