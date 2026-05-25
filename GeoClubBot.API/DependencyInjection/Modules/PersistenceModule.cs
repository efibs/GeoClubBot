using Constants;
using Infrastructure.OutputAdapters;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace GeoClubBot.DependencyInjection.Modules;

public static class PersistenceModule
{
    public static IServiceCollection AddPersistenceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IUnitOfWork, DbUnitOfWork>();
        services.AddMemoryCache();

        // IClubRepository is decorated with a memory-cache layer; the concrete EF impl
        // is registered separately so the decorator can resolve it as its inner instance.
        services.AddTransient<EfClubRepository>();
        services.AddTransient<IClubRepository, CachingClubRepository>();

        var connectionString = configuration.GetConnectionString(ConfigKeys.PostgresConnectionString)!;
        services.AddDbContext<GeoClubBotDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
