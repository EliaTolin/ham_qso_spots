using HamQso.Spots.Application.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Host.Tests;

/// <summary>
/// The real Host with no background service, the archive reader and the token
/// check faked. The test server has no ports: a request picks its local port
/// with the <see cref="PortHeader"/> header (8080 public by default, 8081 internal).
/// </summary>
public sealed class TestHost : WebApplicationFactory<Program>
{
    public const string ValidToken = "valid.supabase.token";

    /// <summary>A valid token of a reader project (HamQRG).</summary>
    public const string ReaderToken = "valid.reader.token";

    /// <summary>A valid token of an anonymous HamQSO user (no signed-in account).</summary>
    public const string AnonymousToken = "valid.anonymous.token";

    /// <summary>The user of <see cref="ValidToken"/>.</summary>
    public const string UserId = "4b1e2a9c-0d3f-4e5a-8b6c-7d8e9f0a1b2c";

    public const string PortHeader = "X-Test-Local-Port";

    /// <summary>The address the test connection comes from (default 127.0.0.1).</summary>
    public const string RemoteHeader = "X-Test-Remote-Ip";

    /// <summary>A valid token of the HamQSO user whose id follows the prefix.</summary>
    public const string UserTokenPrefix = "valid.user.";

    public FakeReader Reader { get; } = new();

    public FakeProfiles Profiles { get; } = new();

    public FakeCluster Cluster { get; } = new();

    public FakeRbnFeed Rbn { get; } = new();

    /// <summary>The proxy allowlist of the test (empty: no service).</summary>
    public Dictionary<string, Uri> Upstreams { get; } = new(StringComparer.Ordinal);

    /// <summary>The environment of the Host (its appsettings.{Environment}.json).</summary>
    public string Environment { get; set; } = "Development";

    /// <summary>Extra configuration, set before the first request.</summary>
    public Dictionary<string, string> Settings { get; } = [];

    /// <summary>Every log line the Host writes, filtered by its real logging configuration.</summary>
    public LogSink Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Spots", "Host=localhost;Database=none");
        builder.UseSetting("Mqtt:Password", "ingestor-password");
        builder.UseSetting("Supabase:SecretKey", "test");
        builder.UseSetting("Feeds:WWFF:Enabled", "false");
        builder.UseSetting("Feeds:POTA:Interval", "00:02:00");
        // Sending on, to a node on this machine (the cluster itself is faked).
        builder.UseSetting("DxSend:Enabled", "true");
        builder.UseSetting("DxSend:Host", "127.0.0.1");
        builder.UseEnvironment(Environment);
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IHostedService>();
            s.AddSingleton<ISpotReader>(Reader);
            s.AddSingleton<ITokenValidator>(new FakeTokens());
            s.AddSingleton<IProfileReader>(Profiles);
            s.AddSingleton<IClusterSender>(Cluster);
            s.AddSingleton<IRbnFeed>(Rbn);
            s.AddSingleton(new Infrastructure.Proxy.ProxyAllowlist(Upstreams));
            s.AddSingleton<IStartupFilter, LocalPortFilter>();
        });
    }

    public HttpClient Client(int port = 8080, string? token = ValidToken)
    {
        var http = CreateClient();
        http.DefaultRequestHeaders.Add(PortHeader, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (token is not null) http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private sealed class FakeTokens : ITokenValidator
    {
        public Task<TokenUser?> ValidateAsync(string token, CancellationToken cancellationToken) => Task.FromResult(token switch
        {
            ValidToken => new TokenUser(UserId, false, false),
            ReaderToken => new TokenUser(UserId, true, false),
            AnonymousToken => new TokenUser(UserId, false, true),
            _ when token.StartsWith(UserTokenPrefix, StringComparison.Ordinal) => new TokenUser(token[UserTokenPrefix.Length..], false, true),
            _ => null,
        });
    }

    public sealed class FakeProfiles : IProfileReader
    {
        public Dictionary<string, string?> Callsigns { get; } = new() { [UserId] = "IU4VRB" };

        public Task<string?> CallsignAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(Callsigns.GetValueOrDefault(userId));
    }

    public sealed class FakeCluster : IClusterSender
    {
        public List<(string Login, string Line)> Sent { get; } = [];

        /// <summary>What the node does instead of sending: Unavailable or Busy.</summary>
        public ClusterSendResult? Failure { get; set; }

        public Task<ClusterSendResult> SendAsync(string userId, string login, string line, CancellationToken cancellationToken)
        {
            if (Failure is not null) return Task.FromResult(Failure);
            Sent.Add((login, line));
            return Task.FromResult(ClusterSendResult.Sent(["Spot sent"]));
        }
    }

    private sealed class LocalPortFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.LocalPort = int.TryParse(context.Request.Headers[PortHeader], out var port) ? port : 8080;
                context.Connection.RemoteIpAddress = System.Net.IPAddress.TryParse(context.Request.Headers[RemoteHeader], out var remote) ? remote : System.Net.IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    public sealed class FakeReader : ISpotReader
    {
        public SpotFilter? LastFilter { get; private set; }

        private static readonly Domain.Spots.Spot Spot = new()
        {
            Program = Domain.Spots.SpotProgram.Dx,
            SpotKey = "k",
            Callsign = "JA1ABC",
            FrequencyKhz = 14025,
            SpotTime = DateTimeOffset.UnixEpoch,
            Feeds = ["DXFun"],
            Seq = 99,
        };

        public Task<SpotPage> LatestAsync(SpotFilter filter, int limit, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult(new SpotPage(99, false, [Spot]));
        }

        public Task<SpotPage> ChangedSinceAsync(SpotFilter filter, long sinceSeq, int limit, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult(new SpotPage(99, false, []));
        }
    }

    /// <summary>The RBN without telnet: <see cref="Push"/> sends a line to everyone reading.</summary>
    public sealed class FakeRbnFeed : IRbnFeed
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<System.Threading.Channels.Channel<RbnLine>, bool> readers = new();

        public int Readers => readers.Count;

        public void Push(RbnLine line)
        {
            foreach (var reader in readers.Keys) reader.Writer.TryWrite(line);
        }

        public async IAsyncEnumerable<RbnLine> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<RbnLine>();
            readers[channel] = true;
            try
            {
                await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken)) yield return line;
            }
            finally
            {
                readers.TryRemove(channel, out _);
            }
        }
    }

    /// <summary>Keeps the formatted log lines (with exceptions) of every category.</summary>
    public sealed class LogSink : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(LogSink sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Lines.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
