using HamQso.Voacap.Core;
using HamQso.Voacap.Host;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<VoacapOptions>().BindConfiguration(VoacapOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IVoacapEngine, ProcessVoacapEngine>();
builder.Services.AddSingleton<Predict>();
builder.Services.AddSingleton(sp => new EffectiveSsn(sp.GetRequiredService<IVoacapEngine>(), sp.GetRequiredService<TimeProvider>()));

var app = builder.Build();
app.MapVoacapApi();
await app.RunAsync();

/// <summary>Entry point; public for WebApplicationFactory in the tests.</summary>
public partial class Program;
