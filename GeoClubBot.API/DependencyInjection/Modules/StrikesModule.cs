namespace GeoClubBot.DependencyInjection.Modules;

public static class StrikesModule
{
    public static IServiceCollection AddStrikesModule(this IServiceCollection services)
    {
        // Strike handlers are auto-registered via MediatR's assembly scan in Program.cs.
        return services;
    }
}
