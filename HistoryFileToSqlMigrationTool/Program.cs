using Constants;
using HistoryFileToSqlMigrationTool;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using QuartzExtensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Get the connection string
var connectionString = builder.Configuration.GetConnectionString(ConfigKeys.PostgresConnectionString);

// Add the db context
builder.Services.AddDbContext<GeoClubBotDbContext>(options => 
    options.UseNpgsql(connectionString));

builder.Services.AddHostedService<MigrateService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();
