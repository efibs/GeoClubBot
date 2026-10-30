using Microsoft.Extensions.DependencyInjection;

namespace GeoClubBot.Tests.TestBuilders;

/// <summary>
/// A real DI scope factory, for handlers that fan out into one scope per club. Register the
/// substitutes the scope should hand out; a real container also proves the handler disposes
/// and resolves its scopes the way production does.
/// </summary>
public static class TestScopes
{
    public static IServiceScopeFactory Serving(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
