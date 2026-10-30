using System.Text.Json;
using HamQso.Spots.Host;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var ports = builder.Configuration.GetSection(ServerOptions.Section).Get<ServerOptions>() ?? new ServerOptions();
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(ports.PublicPort);
    kestrel.ListenAnyIP(ports.InternalPort);
});
builder.Services.AddSpotsServer(builder.Configuration);

var app = builder.Build();
FeatureSwitches.LogAtStart(app.Services, app.Logger);
app.UseForwardedHeaders();
app.UseCors();
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealth });
app.MapMqttAuth(ports.InternalPort);
app.MapSpotsApi();
app.MapReferenceCatalogApi();
app.MapSendSpotApi();
app.MapProxyApi();
app.MapRbnApi();
await app.RunAsync();

static Task WriteHealth(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";
    // F53.3: the reference catalog the apps download (null before the first one).
    var catalog = context.RequestServices.GetRequiredService<HamQso.Spots.Application.Catalog.CurrentReferenceCatalog>().File;
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        sources = report.Entries.SelectMany(e => e.Value.Data).ToDictionary(d => d.Key, d => d.Value),
        catalog = catalog is null ? null : new
        {
            references = catalog.Count,
            built = catalog.GeneratedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        },
    }));
}

/// <summary>Entry point; public for WebApplicationFactory in the Host tests.</summary>
public partial class Program;
