using Configuration;
using Constants;
using FluentValidation;
using GeoClubBot.DependencyInjection;
using GeoClubBot.Discord.DependencyInjection;
using GeoClubBot.Discord.Services;
using GeoClubBot.Middleware;
using GeoClubBot.MockGeoGuessr.DependencyInjection;
using GeoClubBot.MockGeoGuessr.Endpoints;
using Infrastructure.OutputAdapters.DataAccess;
using Infrastructure.OutputAdapters.Hubs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuartzExtensions;
using UseCases.Behaviors;
using UseCases.Observability;
using UseCases.UseCases;

var builder = WebApplication.CreateBuilder(args);

// Set the configuration to the configured cron job
ConfiguredCronJobAttribute.Config = builder.Configuration;

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

const string ConfiguredCorsPolicy = "ConfiguredCors";
var allowedOrigins = builder.Configuration
    .GetSection(CorsConfiguration.SectionName)
    .Get<CorsConfiguration>()?.AllowedOrigins ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(ConfiguredCorsPolicy, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            // No origins configured — keep cross-origin requests disabled.
            policy.DisallowCredentials();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add the options
builder.Services.AddClubBotOptions(builder.Configuration);

// Add the discord services
builder.Services.AddDiscordServices();

// Add GeoGuessr client (mock or real)
var useMockGeoGuessr = builder.Configuration.GetValue("GeoGuessr:UseMock", false);
if (useMockGeoGuessr)
    builder.Services.AddMockGeoGuessrServices();
else
    builder.Services.AddGeoGuessrHttpClients(builder.Configuration);

// Add all the necessary services
builder.Services.AddClubBotServices(builder.Configuration);

// Add the MediatR services with pipeline behaviors. Order matters: outermost first.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<IUseCasesAssemblyMarker>();
    cfg.AddOpenBehavior(typeof(TracingBehavior<,>));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
});

// Register FluentValidation validators from the use cases assembly.
builder.Services.AddValidatorsFromAssemblyContaining<IUseCasesAssemblyMarker>();

// OpenTelemetry: custom handler / job / cache meters and activity sources from the
// Application + Discord layers, plus stock instrumentation for ASP.NET Core, outbound HTTP,
// EF Core, and the process runtime. Traces, metrics and logs are all exported.
// The OTLP exporter is opt-in via the OpenTelemetry:Endpoint config key — if absent,
// instruments are still produced (in-process listeners work) but nothing is shipped out.
var otelEndpoint = builder.Configuration.GetValue<string?>("OpenTelemetry:Endpoint");
const string ServiceName = "GeoClubBot.API";

// Duration histograms (ms) — explicit buckets so p95/p99 are meaningful for our workloads.
var durationBucketsMs = new[] { 5d, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000 };

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(ServiceName, serviceVersion: "1.0.0")
        .AddAttributes([new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)]))
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(HandlerMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddView("geoclubbot.handler.duration", new ExplicitBucketHistogramConfiguration { Boundaries = durationBucketsMs })
            .AddView("geoclubbot.job.duration", new ExplicitBucketHistogramConfiguration { Boundaries = durationBucketsMs });

        if (!string.IsNullOrWhiteSpace(otelEndpoint))
        {
            metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otelEndpoint));
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(ApplicationDiagnostics.ActivitySourceName)
            .AddSource(DiscordDiagnostics.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            // SQL text is captured in Development only — it can contain sensitive parameter values.
            .AddEntityFrameworkCoreInstrumentation(ef => ef.SetDbStatementForText = builder.Environment.IsDevelopment());

        if (!string.IsNullOrWhiteSpace(otelEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otelEndpoint));
        }
    })
    .WithLogging(
        logging =>
        {
            if (!string.IsNullOrWhiteSpace(otelEndpoint))
            {
                logging.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otelEndpoint));
            }
        },
        // Ship the rendered message + logging scopes so logs are useful next to the traces
        // they correlate with (by TraceId/SpanId).
        options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // HSTS is intentionally off in Development so it can't poison localhost over plain HTTP.
    app.UseHsts();
}

// If the db migrations should be applied
if (app.Configuration.GetValue<bool>(ConfigKeys.SqlMigrateConfigurationKey))
{
    // Apply the database migrations
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeoClubBotDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

app.UseExceptionHandler();

// Baseline security response headers on every response.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next().ConfigureAwait(false);
});

app.UseHttpsRedirection();
app.UseCors(ConfiguredCorsPolicy);

if (useMockGeoGuessr)
{
    app.UseStaticFiles();
    app.MapMockGeoGuessrEndpoints();
}

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<ClubNotificationHub>("/api/clubNotificationHub");

if (useMockGeoGuessr)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var baseUrl = addresses?.Addresses.FirstOrDefault() ?? "http://localhost:5194";
        var mockUrl = $"{baseUrl}/mock";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mockUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            app.Logger.LogInformation("Mock GeoGuessr UI available at: {Url}", mockUrl);
        }
    });
}

app.Run();

// Exposes the implicit Program class (top-level statements) so the test project can
// boot the real app in-process via WebApplicationFactory<Program>.
public partial class Program;
