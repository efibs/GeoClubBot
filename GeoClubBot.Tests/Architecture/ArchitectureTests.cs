using System.Reflection;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.DataAccess;
using NetArchTest.Rules;
using Xunit;
using ApplicationMarker = UseCases.UseCases.IUseCasesAssemblyMarker;
using DiscordMarker = GeoClubBot.Discord.InputAdapters.Interactions.INteractionsAssemblyMarker;

namespace GeoClubBot.Tests.Architecture;

/// <summary>
/// Codifies the Clean Architecture dependency rules so violations fail the build instead of
/// being caught (or missed) in review. Dependencies are matched by the referenced type's
/// namespace; note the projects' assembly names (<c>GeoClubBot.*</c>) differ from their root
/// namespaces (Domain → <c>Entities</c>, Application → <c>UseCases</c>, Infrastructure →
/// <c>Infrastructure</c>), while Discord/API types live under <c>GeoClubBot.*</c>.
///
/// Forbidden namespaces are spelled out fully (e.g. <c>GeoClubBot.Discord</c> rather than a bare
/// <c>GeoClubBot</c>): NetArchTest also matches string constants, and several Application types
/// hold a <c>"GeoClubBot.Application"</c> meter/source-name literal that a broad token false-flags.
/// Depending on the API is structurally impossible from Domain/Application anyway (it would be a
/// reference cycle), so the meaningful boundaries are the layers below + the Discord adapter.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(BaseEntity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationMarker).Assembly;
    private static readonly Assembly DiscordAssembly = typeof(DiscordMarker).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "UseCases",                       // Application
                "Infrastructure",
                "GeoClubBot.Discord",             // Discord adapter
                "Configuration",
                "Microsoft.EntityFrameworkCore")  // persistence concern
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the Domain layer is the core and must stay free of outward dependencies, " +
            "but these types violate it: {0}",
            FailingTypes(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure_or_adapters()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Infrastructure",
                "GeoClubBot.Discord",             // Discord adapter
                "Microsoft.EntityFrameworkCore")  // EF belongs to Infrastructure, behind a port
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the Application layer must depend only on the Domain and its own ports, " +
            "but these types violate it: {0}",
            FailingTypes(result));
    }

    [Fact]
    public void Discord_adapters_should_not_depend_on_infrastructure()
    {
        var result = Types.InAssembly(DiscordAssembly)
            .ShouldNot()
            .HaveDependencyOn("Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Discord is an input/output adapter over Application ports and must not reach into " +
            "Infrastructure directly, but these types violate it: {0}",
            FailingTypes(result));
    }

    private static string FailingTypes(TestResult result) =>
        string.Join(", ", result.FailingTypeNames ?? []);
}
