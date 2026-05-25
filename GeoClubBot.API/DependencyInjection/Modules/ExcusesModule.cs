namespace GeoClubBot.DependencyInjection.Modules;

public static class ExcusesModule
{
    public static IServiceCollection AddExcusesModule(this IServiceCollection services)
    {
        // Excuse handlers are auto-registered via MediatR's assembly scan in Program.cs.
        return services;
    }
}
