using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HamQso.Voacap.Core;
using Microsoft.Extensions.Options;

namespace HamQso.Voacap.Host;

/// <summary>
/// The HTTP API (plan F71, 3.3): POST /v1/predict and /v1/effective-ssn with
/// "Authorization: Bearer &lt;VOACAP_TOKEN&gt;", GET /health without. snake_case
/// JSON; a missing field is an invalid request. Positions are never logged (they
/// are where the users are).
/// </summary>
public static partial class VoacapEndpoints
{
    /// <summary>The body is at most this long (600 receivers are about 40 KB).</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static void MapVoacapApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", (IVoacapEngine engine) => Results.Json(new { status = "ok", engine = engine.Name }));

        app.MapPost("/v1/predict", async (HttpContext http, Predict predict, IOptions<VoacapOptions> options, ILoggerFactory loggers) =>
        {
            var logger = loggers.CreateLogger(typeof(VoacapEndpoints));
            if (!Authorized(http, options.Value.Token)) return Results.Unauthorized();
            var request = await ReadAsync<PredictRequest>(http);
            if (request is null) return Error(StatusCodes.Status400BadRequest, RequestRules.Invalid);
            switch (RequestRules.Check(request))
            {
                case RequestRules.TooManyReceivers: return Error(StatusCodes.Status422UnprocessableEntity, RequestRules.TooManyReceivers);
                case { } error: return Error(StatusCodes.Status400BadRequest, error);
            }
            try
            {
                var result = await predict.ExecuteAsync(request, http.RequestAborted);
                LogPredict(logger, request.Scenarios.Count, request.Receivers.Count, result.ElapsedMs);
                return Results.Json(new
                {
                    engine = result.Engine,
                    elapsed_ms = result.ElapsedMs,
                    results = result.Results.ToDictionary(r => r.Key, r => r.Value.Select(x => new { id = x.Id, rel = x.Rel, snr_dbhz = x.SnrDbhz, muf_mhz = x.MufMhz, mode = x.Mode })),
                });
            }
            catch (VoacapEngineException e)
            {
                LogFailed(logger, "predict", e.Message);
                return EngineFailed(e);
            }
        });

        app.MapPost("/v1/effective-ssn", async (HttpContext http, EffectiveSsn search, IOptions<VoacapOptions> options, ILoggerFactory loggers) =>
        {
            var logger = loggers.CreateLogger(typeof(VoacapEndpoints));
            if (!Authorized(http, options.Value.Token)) return Results.Unauthorized();
            var request = await ReadAsync<EffectiveSsnRequest>(http);
            if (request is null || RequestRules.Check(request) is not null) return Error(StatusCodes.Status400BadRequest, RequestRules.Invalid);
            try
            {
                var r = await search.ExecuteAsync(request, http.RequestAborted);
                LogEffectiveSsn(logger, r.SsnEffective, r.Iterations);
                return Results.Json(new { engine = r.Engine, ssn_effective = r.SsnEffective, predicted_mufd_mhz = r.PredictedMufdMhz, iterations = r.Iterations });
            }
            catch (VoacapEngineException e)
            {
                LogFailed(logger, "effective-ssn", e.Message);
                return EngineFailed(e);
            }
        });
    }

    private static IResult Error(int status, string error) => Results.Json(new { error }, statusCode: status);

    private static IResult EngineFailed(VoacapEngineException e) =>
        Results.Json(new { error = "engine_failed", message = e.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>"Bearer &lt;token&gt;", compared in constant time.</summary>
    private static bool Authorized(HttpContext http, string token)
    {
        var header = http.Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (!header.StartsWith(bearer, StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[bearer.Length..]), Encoding.UTF8.GetBytes(token));
    }

    /// <summary>The body as <typeparamref name="T"/>, or null when it is not one (bad JSON, a field missing or null, too long).</summary>
    private static async Task<T?> ReadAsync<T>(HttpContext http)
        where T : class
    {
        if (http.Request.ContentLength > MaxBodyBytes) return null;
        try
        {
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body, http.RequestAborted);
            if (body.Length > MaxBodyBytes) return null;
            body.Position = 0;
            return await JsonSerializer.DeserializeAsync<T>(body, Json, http.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "predict: {Scenarios} scenarios, {Receivers} receivers, {Ms} ms")]
    private static partial void LogPredict(ILogger logger, int scenarios, int receivers, long ms);

    [LoggerMessage(Level = LogLevel.Information, Message = "effective-ssn: {Ssn} in {Iterations} runs")]
    private static partial void LogEffectiveSsn(ILogger logger, int ssn, int iterations);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Endpoint}: voacapl failed: {Message}")]
    private static partial void LogFailed(ILogger logger, string endpoint, string message);
}
