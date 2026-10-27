using HamQso.Spots.Application.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HamQso.Spots.Host.Tests;

/// <summary>
/// The real Host with no background service, the archive reader and the token
/// check faked. The test server has no ports: a request picks its local port
/// with the <see cref="PortHeader"/> header (8080 public by default, 8081 internal).
/// </summary>
public sealed class TestHost : WebApplicationFactory<Program>
{
    public const string ValidToken = "valid.supabase.token";

    public const string PortHeader = "X-Test-Local-Port";

    public FakeReader Reader { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Spots", "Host=localhost;Database=none");
        builder.UseSetting("Mqtt:Password", "ingestor-password");
        builder.UseSetting("Supabase:SecretKey", "test");
        builder.UseSetting("Feeds:WWFF:Enabled", "false");
        builder.UseSetting("Feeds:POTA:Interval", "00:02:00");
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IHostedService>();
            s.AddSingleton<ISpotReader>(Reader);
            s.AddSingleton<ITokenValidator>(new FakeTokens());
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
        public Task<bool> IsValidAsync(string token, CancellationToken cancellationToken) => Task.FromResult(token == ValidToken);
    }

    private sealed class LocalPortFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.LocalPort = int.TryParse(context.Request.Headers[PortHeader], out var port) ? port : 8080;
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
}
