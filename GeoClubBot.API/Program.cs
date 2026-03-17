using Configuration;
using Constants;
using GeoClubBot.DependencyInjection;
using GeoClubBot.Discord.DependencyInjection;
using GeoClubBot.MockGeoGuessr.DependencyInjection;
using GeoClubBot.MockGeoGuessr.Endpoints;
using Infrastructure.OutputAdapters.DataAccess;
using Infrastructure.OutputAdapters.Hubs;
using Microsoft.EntityFrameworkCore;
using QuartzExtensions;
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
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

// Add the MediatR services
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IUseCasesAssemblyMarker>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// If the db migrations should be applied
if (app.Configuration.GetValue<bool>(ConfigKeys.SqlMigrateConfigurationKey))
{
    // Apply the database migrations
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeoClubBotDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

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