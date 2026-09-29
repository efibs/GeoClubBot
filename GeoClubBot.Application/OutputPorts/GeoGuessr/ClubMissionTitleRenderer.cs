namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// Renders a board mission's title the way GeoGuessr's web client does: the plural title unless the
/// target is exactly one, with <c>{0}</c> replaced by the target, <c>{1}</c> by the map name and
/// <c>{2}</c> by the threshold (e.g. the minimum score per game).
/// </summary>
public static class ClubMissionTitleRenderer
{
    public static string Render(ClubMissionTileDto tile, ClubMissionTemplateDto? template)
    {
        if (template is null)
        {
            // GeoGuessr added a template the snapshot does not carry. Better an approximate title
            // than none.
            return $"{tile.Type ?? tile.TemplateId} ({tile.TargetProgress})";
        }

        var format = tile.TargetProgress != 1 && !string.IsNullOrWhiteSpace(template.TitlePlural)
            ? template.TitlePlural
            : template.Title;

        return format
            .Replace("{0}", tile.TargetProgress.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{1}", template.MapName ?? tile.MapSlug ?? "any map")
            .Replace("{2}", tile.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
