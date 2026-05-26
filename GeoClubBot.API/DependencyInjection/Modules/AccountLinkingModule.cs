namespace GeoClubBot.DependencyInjection.Modules;

public static class AccountLinkingModule
{
    public static IServiceCollection AddAccountLinkingModule(this IServiceCollection services)
    {
        // Account-linking handlers are auto-registered via MediatR's assembly scan in Program.cs.
        return services;
    }
}
