using HamQso.Spots.Application.Access;
using HamQso.Spots.Application.Dedup;
using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ingestion;
using HamQso.Spots.Application.Reading;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Auth;
using HamQso.Spots.Infrastructure.Mqtt;
using HamQso.Spots.Infrastructure.Postgres;
using HamQso.Spots.Infrastructure.Sources;
using HamQso.Spots.Infrastructure.Sources.Telnet;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HamQso.Spots.Host;

/// <summary>The composition root: every port gets its adapter here and nowhere else.</summary>
public static class ServiceCollectionExtensions
{
    private const string UserAgent = "HamQSO-Spots/1.0 (+https://hamqso.app)";

    public static IServiceCollection AddSpotsServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<ServerOptions>().BindConfiguration(ServerOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<TelnetOptions>().BindConfiguration(TelnetOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<MqttOptions>().BindConfiguration(MqttOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SupabaseOptions>().BindConfiguration(SupabaseOptions.Section).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(NpgsqlDataSource.Create(configuration.GetConnectionString("Spots")
            ?? throw new InvalidOperationException("ConnectionStrings:Spots is missing")));
        services.AddSingleton<PostgresSpotStore>();
        services.AddSingleton<ISpotStore>(sp => sp.GetRequiredService<PostgresSpotStore>());
        services.AddSingleton<ISpotReader>(sp => sp.GetRequiredService<PostgresSpotStore>());
        services.AddSingleton<ISpotArchiveSchema>(sp => sp.GetRequiredService<PostgresSpotStore>());
        services.AddSingleton<GetSpots>();
        services.AddSingleton<ISourceHealth>(sp => sp.GetRequiredService<SourceHealthRegistry>());
        services.AddSingleton<ISpotPublisher, MqttSpotPublisher>();
        services.AddHttpClient<IAlertNotifier, SupabaseAlertNotifier>();
        services.AddHttpClient<ITokenValidator, SupabaseJwtValidator>();
        services.AddSingleton<ISpotDeduplicator, InMemorySpotDeduplicator>();
        services.AddSingleton<IngestSpots>();
        services.AddSingleton<SourceHealthRegistry>();

        services.AddSingleton(sp =>
        {
            var mqtt = sp.GetRequiredService<IOptions<MqttOptions>>().Value;
            return new MqttAccess(sp.GetRequiredService<ITokenValidator>(), mqtt.Username, mqtt.Password);
        });

        foreach (var (feed, _) in SpotSourceFactory.Feeds)
        {
            services.AddHttpClient(feed.Name, c =>
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                c.Timeout = TimeSpan.FromSeconds(10);
            });
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
