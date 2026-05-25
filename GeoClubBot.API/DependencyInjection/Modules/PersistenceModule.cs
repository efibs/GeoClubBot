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

        // Per-slice repositories are registered here so MediatR handlers can inject them
        // directly. Repositories not yet injected this way are still constructed inline by
        // DbUnitOfWork; they migrate slice by slice alongside the use-case migration.
        services.AddTransient<IStrikesRepository, EfStrikesRepository>();

        var connectionString = configuration.GetConnectionString(ConfigKeys.PostgresConnectionString)!;
        services.AddDbContext<GeoClubBotDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
