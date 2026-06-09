using Configuration;
using FluentValidation;
using Infrastructure.OutputAdapters.DataAccess;
using Infrastructure.OutputAdapters.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.Behaviors;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Builds a real <see cref="IServiceProvider"/> that mirrors the production MediatR composition
/// so use-case handlers can be exercised end-to-end against the shared Postgres test container:
/// <list type="bullet">
/// <item>MediatR handlers + the <see cref="ValidationBehavior{TRequest,TResponse}"/> and
/// <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/> pipeline (so commands really persist and
/// dispatch domain events).</item>
/// <item>The real EF Core <see cref="GeoClubBotDbContext"/>, repositories and unit of work pointing
/// at the test container.</item>
/// <item>NSubstitute fakes for every external output port (Discord, GeoGuessr, rendering, …) so no
/// network or Discord connection is required. Tests can grab a fake via <see cref="Mock{T}"/> to
/// arrange/verify it.</item>
/// </list>
/// External-system output ports are discovered by reflection so the host keeps working as new ports
/// are added. Tests that need real configuration values pass a <c>configure</c> callback to override
/// the defaulted <see cref="IOptions{TOptions}"/> registrations.
/// </summary>
public sealed class MediatorTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public MediatorTestHost(
        string connectionString,
        Action<IServiceCollection>? configure = null,
        IReadOnlyDictionary<string, string?>? configurationValues = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // A handful of handlers inject IConfiguration directly (role ids, channel ids, …).
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues ?? new Dictionary<string, string?>())
            .Build();
        services.AddSingleton(configuration);

        services.AddDbContext<GeoClubBotDbContext>(options => options.UseNpgsql(connectionString));

        var applicationAssembly = typeof(IUnitOfWork).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
        });

        RegisterValidators(services, applicationAssembly);
        RegisterPersistence(services);
        RegisterInternalApplicationServices(services);
        RegisterConfigurationOptions(services, configuration);
        RegisterExternalPortSubstitutes(services, applicationAssembly);

        configure?.Invoke(services);

        _provider = services.BuildServiceProvider();
    }

    /// <summary>Resolves the singleton NSubstitute fake registered for an external port.</summary>
    public T Mock<T>() where T : class => _provider.GetRequiredService<T>();

    /// <summary>Sends a request through the full MediatR pipeline in its own scope.</summary>
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(request, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _provider.Dispose();

    private static void RegisterValidators(IServiceCollection services, System.Reflection.Assembly applicationAssembly)
    {
        foreach (var type in applicationAssembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var @interface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddTransient(@interface, type);
            }
        }
    }

    private static void RegisterPersistence(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, DbUnitOfWork>();

        var infrastructureAssembly = typeof(DbUnitOfWork).Assembly;
        foreach (var impl in infrastructureAssembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false }
                                 && t.Name.StartsWith("Ef", StringComparison.Ordinal)
                                 && t.Name.EndsWith("Repository", StringComparison.Ordinal)))
        {
            foreach (var @interface in impl.GetInterfaces()
                         .Where(i => i.Name.EndsWith("Repository", StringComparison.Ordinal)))
            {
                services.AddScoped(@interface, impl);
            }
        }
    }

    /// <summary>
    /// Internal application services that are concrete (not output ports) and so are not picked up
    /// by the substitute scan. Mirrors the production registrations in <c>ClubMembersModule</c> so
    /// the GeoGuessr-orchestration handlers (club level / activity checks) resolve their collaborators.
    /// </summary>
    private static void RegisterInternalApplicationServices(IServiceCollection services)
    {
        services.AddSingleton<IClubLevelTracker, ClubLevelTracker>();
        services.AddSingleton<IActivityReportPublishGate, ActivityReportPublishGate>();
        services.AddTransient<ActivityCheckSyncStep>();
        services.AddTransient<ActivityStatusCalculator>();
        services.AddTransient<ActivityAverageXpRollupStep>();
    }

    private static void RegisterConfigurationOptions(IServiceCollection services, IConfiguration configuration)
    {
        var configurationAssembly = typeof(ActivityCheckerConfiguration).Assembly;
        var createMethod = typeof(Options).GetMethod(nameof(Options.Create))!;

        foreach (var type in configurationAssembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false }
                                 && t.Name.EndsWith("Configuration", StringComparison.Ordinal)))
        {
            // Bind each options class from its section so any `configurationValues` a test supplies
            // (e.g. "SelfRoles:TextChannelId") flow into the matching IOptions<T>. Tests that need
            // richer values still override via the `configure` callback, which runs afterwards.
            if (type.GetField(nameof(ActivityCheckerConfiguration.SectionName),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?.GetValue(null) is not string sectionName)
            {
                continue;
            }

            object? instance;
            try
            {
                instance = configuration.GetSection(sectionName).Get(type) ?? Activator.CreateInstance(type);
            }
            catch
            {
                continue;
            }

            if (instance is null)
            {
                continue;
            }

            var optionsType = typeof(IOptions<>).MakeGenericType(type);
            var optionsInstance = createMethod.MakeGenericMethod(type).Invoke(null, [instance])!;
            services.AddSingleton(optionsType, optionsInstance);
        }
    }

    private static void RegisterExternalPortSubstitutes(IServiceCollection services, System.Reflection.Assembly applicationAssembly)
    {
        var alreadyRegistered = services.Select(d => d.ServiceType).ToHashSet();

        foreach (var @interface in applicationAssembly.GetTypes()
                     .Where(t => t is { IsInterface: true }
                                 && !t.IsGenericTypeDefinition
                                 && t.Namespace is not null
                                 && t.Namespace.Contains("OutputPorts", StringComparison.Ordinal)))
        {
            // Real repositories / unit of work are wired to EF above; everything else is external.
            if (@interface.Name.EndsWith("Repository", StringComparison.Ordinal)
                || @interface == typeof(IUnitOfWork)
                || alreadyRegistered.Contains(@interface))
            {
                continue;
            }

            var substitute = Substitute.For([@interface], []);
            services.AddSingleton(@interface, substitute);
        }
    }
}
