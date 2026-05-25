using Infrastructure.OutputAdapters.Rendering;
using UseCases.OutputPorts.Rendering;

namespace GeoClubBot.DependencyInjection.Modules;

public static class RenderingModule
{
    public static IServiceCollection AddRenderingModule(this IServiceCollection services)
    {
        services.AddTransient<IHistoryRenderer, SkiaSharpHistoryRenderer>();

        return services;
    }
}
