namespace GeoClubBot.DependencyInjection.Modules;

public static class SelfRolesModule
{
    public static IServiceCollection AddSelfRolesModule(this IServiceCollection services)
    {
        // The self-roles handler is auto-registered via MediatR's assembly scan in Program.cs.
        return services;
    }
}
