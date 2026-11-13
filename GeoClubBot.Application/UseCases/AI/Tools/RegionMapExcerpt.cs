using System.Globalization;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <summary>The words that go with a region map: its title, what the model is told of it, and where its source line leads.</summary>
public static class RegionMapExcerpt
{
    /// <summary>The source's title, which the reply's source list names it by.</summary>
    public const string SourceTitle = "Region map";

    /// <summary>Where the source line leads: the boundary data's home, which is the credit it asks for (it asks for none).</summary>
    public const string SourceUrl = "https://www.naturalearthdata.com/downloads/10m-cultural-vectors/10m-admin-1-states-provinces/";

    /// <summary>"Lạng Sơn and Quảng Ninh in Vietnam", "Sabah, Sarawak and Johor in Malaysia".</summary>
    public static string Title(string countryName, IReadOnlyList<string> regionLabels)
    {
        ArgumentNullException.ThrowIfNull(regionLabels);

        var names = regionLabels.Where(label => !string.IsNullOrWhiteSpace(label)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var joined = names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
        };

        return joined.Length == 0 ? $"Regions of {countryName}" : $"{joined} in {countryName}";
    }

    /// <summary>The excerpt a <c>region_map</c> call returns: the picture, and what it shows, cited like any other.</summary>
    public static KnowledgeHit ToHit(string countryCode, string countryName, IReadOnlyList<RegionHighlight> regions, string imageUrl, bool shaded)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var labels = regions.Select(region => region.Label).ToList();
        var title = Title(countryName, labels);

        var text = shaded
            ? $"A map of {countryName} with {string.Join(", ", regions.Select(region => $"{region.Label} ({Percent(region.Weight)})"))} shaded red, darker where the share is higher."
            : $"A map of {countryName} with {string.Join(", ", labels)} highlighted in red.";

        return new KnowledgeHit(
            DeterministicGuid.FromName("region-map", $"{countryCode}|{string.Join("|", regions.Select(region => $"{region.Id}:{region.Weight.ToString("F2", CultureInfo.InvariantCulture)}"))}"),
            Score: 0f,
            KnowledgeChunkKind.Image,
            text,
            SourceUrl,
            imageUrl,
            SourceTitle,
            Country: null,
            SectionPath: title,
            Author: null,
            Priority: 0);
    }

    private static string Percent(double weight) =>
        $"{Math.Round(Math.Clamp(weight, 0, 1) * 100).ToString(CultureInfo.InvariantCulture)} %";
}
