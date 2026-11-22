using System.Collections.Frozen;
using System.Reflection;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Help;

/// <summary>
/// The line under a failed command's reply that points to its page of <c>/help</c>, read from the
/// <see cref="HelpFeatureAttribute"/> of the module (or the module it is nested in).
/// </summary>
public static class HelpHint
{
    /// <summary>E.g. <c>-# `/help` → ⏰ Daily reminder explains these commands.</c>; null for a module of no feature.</summary>
    public static string? For(Type moduleType)
    {
        for (var type = moduleType; type is not null; type = type.DeclaringType)
        {
            if (type.GetCustomAttribute<HelpFeatureAttribute>() is { } declaration
                && Described.Value.TryGetValue(declaration.Key, out var feature))
            {
                return $"-# `/help` → {feature.Emoji} {feature.Title} explains these commands.";
            }
        }

        return null;
    }

    private static readonly Lazy<FrozenDictionary<string, HelpFeatureAttribute>> Described = new(() =>
        typeof(HelpHint).Assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<HelpFeatureAttribute>())
            .OfType<HelpFeatureAttribute>()
            .Where(declaration => declaration.Describes)
            .GroupBy(declaration => declaration.Key, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal));
}
