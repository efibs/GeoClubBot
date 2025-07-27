using Constants;
using GeoClubBot;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add all the necessary services
builder.Services.AddClubBotServices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
app.Run();