using HamQso.Spots.Application.Access;
using HamQso.Spots.Application.Dedup;
using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ingestion;
using HamQso.Spots.Application.Proxy;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Application.Reading;
using HamQso.Spots.Application.Sending;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Auth;
using HamQso.Spots.Infrastructure.Mqtt;
using HamQso.Spots.Infrastructure.Postgres;
using HamQso.Spots.Infrastructure.Profiles;
using HamQso.Spots.Infrastructure.Proxy;
using HamQso.Spots.Infrastructure.Rbn;
using HamQso.Spots.Infrastructure.Sending;
using HamQso.Spots.Infrastructure.Sources;
using HamQso.Spots.Infrastructure.Sources.Telnet;
using Microsoft.Extensions.Options;
using System.Net;
using Npgsql;

namespace HamQso.Spots.Host;

/// <summary>The composition root: every port gets its adapter here and nowhere else.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpotsServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<ServerOptions>().BindConfiguration(ServerOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<TelnetOptions>().BindConfiguration(TelnetOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<MqttOptions>().BindConfiguration(MqttOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SupabaseOptions>().BindConfiguration(SupabaseOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<DxSendOptions>().BindConfiguration(DxSendOptions.Section).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(NpgsqlDataSource.Create(configuration.GetConnectionString("Spots")
            ?? throw new InvalidOperationException("ConnectionStrings:Spots is missing")));
        services.AddSingleton<PostgresSpotStore>();
        services.AddSingleton<ISpotStore>(sp => sp.GetRequiredService<PostgresSpotStore>());
        services.AddSingleton<ISpotReader>(sp => sp.GetRequiredService<PostgresSpotStore>());
        services.AddSingleton<ISpotArchiveSchema>(sp => sp.GetRequiredService<PostgresSpotStore>());
        services.AddSingleton<GetSpots>();
        services.AddSingleton<ISourceHealth>(sp => sp.GetRequiredService<SourceHealthRegistry>());
        services.AddSingleton<ISpotPublisher, MqttSpotPublisher>();
        // Without the secret key (a local run) the alerts are off: see FeatureSwitches.
        if (!string.IsNullOrWhiteSpace(configuration[$"{SupabaseOptions.Section}:SecretKey"])) services.AddHttpClient<IAlertNotifier, SupabaseAlertNotifier>();
        else services.AddSingleton<IAlertNotifier, NullAlertNotifier>();
        services.AddHttpClient<ITokenValidator, SupabaseJwtValidator>();
        services.AddSingleton<ISpotDeduplicator, InMemorySpotDeduplicator>();
        services.AddSingleton<IngestSpots>();
        services.AddSingleton<SourceHealthRegistry>();

        services.AddHttpClient<IProfileReader, SupabaseProfileReader>();
        services.AddSingleton<IClusterSender, TelnetClusterSender>();
        services.AddSingleton<SpotRateLimiter>();
        services.AddSingleton(sp => FeatureSwitches.Sending(
            sp.GetRequiredService<IOptions<SupabaseOptions>>().Value.HasSecretKey,
            sp.GetRequiredService<IOptions<DxSendOptions>>().Value,
            sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));
        services.AddSingleton<SendDxSpot>();

        services.AddApiCors();
        services.AddClientAddress(configuration);
        services.AddOptions<ProxyOptions>().BindConfiguration(ProxyOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(ProxyAllowlist.Production);
        // No cookies, no automatic redirects (only same-host ones, by hand) and no
        // HttpClient logging: its request lines would carry query strings with keys.
        services.AddHttpClient<IUpstreamProxy, UpstreamProxy>(UpstreamProxy.ClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
            })
            .RemoveAllLoggers();
        services.AddSingleton<ProxyRateLimiter>();
        services.AddTransient<ForwardToUpstream>();

        services.AddOptions<RbnOptions>().BindConfiguration(RbnOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<RbnTelnetFeed>();
        services.AddSingleton<IRbnFeed>(sp => sp.GetRequiredService<RbnTelnetFeed>());
        services.AddSingleton<WatchRbn>();

        services.AddSingleton(sp =>
        {
            var mqtt = sp.GetRequiredService<IOptions<MqttOptions>>().Value;
            return new MqttAccess(sp.GetRequiredService<ITokenValidator>(), mqtt.Username, mqtt.Password);
        });

        foreach (var (feed, _, _) in SpotSourceFactory.Feeds)
        {
            services.AddHttpClient(feed.Name, SpotSourceFactory.ConfigureClient);
        }
        services.AddSingleton<IEnumerable<ISpotSource>>(sp =>
        {
            var feeds = configuration.GetSection(FeedOptions.Section).Get<Dictionary<string, FeedOptions>>() ?? [];
            var http = sp.GetRequiredService<IHttpClientFactory>();
            return SpotSourceFactory.Create(
                sp.GetRequiredService<IOptions<TelnetOptions>>().Value,
                feeds,
                http.CreateClient,
                sp.GetRequiredService<TimeProvider>());
        });

        services.AddHostedService<IngestionWorker>();
        services.AddHostedService<RetentionWorker>();
        services.AddHealthChecks().AddCheck<SourcesHealthCheck>("sources");
        return services;
    }
}
