namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// A mission's text. <c>{0}</c> in the titles is the target progress and <c>{1}</c> the map name.
/// </summary>
public class ClubMissionTemplateDto
{
    public required string Id { get; set; }

    public required string Title { get; set; }

    public string? TitlePlural { get; set; }

    public string? Description { get; set; }

    public string? MapName { get; set; }

    public string? IconPath { get; set; }

    public string? ArtPath { get; set; }
}
