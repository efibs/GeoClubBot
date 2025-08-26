using Constants;
using GeoClubBot;
using GeoClubBot.Hubs;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using QuartzExtensions;

var builder = WebApplication.CreateBuilder(args);

// Set the configuration to the configured cron job
ConfiguredCronJobAttribute.Config = builder.Configuration;

// Add services to the container.
builder.Services.AddControllers();
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

// Add all the necessary services
builder.Services.AddClubBotServices(builder.Configuration);

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
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.MapControllers();
app.MapHub<ClubNotificationHub>("/clubNotificationHub");
app.Run();