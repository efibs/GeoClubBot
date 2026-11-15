using Utilities;

namespace UseCases.OutputPorts.AI;

/// <summary>
/// Names the plant in a picture, from an image-recognition service (Pl@ntNet).
///
/// The free chat models guess a screenshot's plant from look-alike guide pictures and are often
/// wrong, and cannot say how sure they are. A service trained on plant photographs names candidates
/// with a score, and the species it names can then be looked up where it grows.
/// </summary>
public interface IPlantIdentifier
{
    /// <summary>Whether identifications can be made at all: the feature is on and a key is configured.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The candidates for the plant in the pictures at <paramref name="imageUrls"/> — up to five views of
    /// one plant, judged together for one identification — best first. Fails with
    /// <see cref="ErrorType.NotFound"/> (<see cref="PlantIdentificationErrorCodes.NotRecognised"/>) when
    /// the service sees no plant it knows, with <see cref="ErrorType.Conflict"/>
    /// (<see cref="PlantIdentificationErrorCodes.QuotaExceeded"/>) when the day's allowance is spent,
    /// with <see cref="ErrorType.Validation"/> when no picture can be sent, and with
    /// <see cref="ErrorType.Unexpected"/> when the service could not be reached. Never throws for any of them.
    /// </summary>
    Task<Result<PlantIdentification>> IdentifyAsync(IReadOnlyList<string> imageUrls, CancellationToken cancellationToken = default);
}

/// <param name="Candidates">What the plant may be, best first; never empty on success.</param>
/// <param name="RemainingRequests">What the service says is left of the day's allowance, when it says.</param>
public sealed record PlantIdentification(IReadOnlyList<PlantCandidate> Candidates, int? RemainingRequests)
{
    public PlantCandidate Best => Candidates[0];

    /// <summary>
    /// The genera the plant may belong to, best first, scored by the service on their own; may be empty.
    /// A Street View frame rarely shows a species, but often shows its genus plainly: 13 % for the best
    /// pine of a Bulgarian screenshot, 83 % for the genus Pinus (2026-11-15).
    /// </summary>
    public IReadOnlyList<PlantGenusCandidate> Genera { get; init; } = [];
}

/// <param name="Name">"Pinus".</param>
/// <param name="Family">"Pinaceae", when given.</param>
/// <param name="CommonNames">In the configured language, as the service lists them; may be empty.</param>
/// <param name="Score">The service's confidence, 0 to 1.</param>
public sealed record PlantGenusCandidate(string Name, string? Family, IReadOnlyList<string> CommonNames, double Score);

/// <param name="ScientificName">Without its author: "Pinus kesiya".</param>
/// <param name="Author">The name's authorship, when given: "Royle ex Gordon".</param>
/// <param name="Genus">"Pinus".</param>
/// <param name="Family">"Pinaceae".</param>
/// <param name="CommonNames">In the configured language, as the service lists them; may be empty.</param>
/// <param name="Score">The service's confidence, 0 to 1.</param>
/// <param name="GbifTaxonKey">The species' key in GBIF's backbone, when the service gives one.</param>
public sealed record PlantCandidate(
    string ScientificName,
    string? Author,
    string? Genus,
    string? Family,
    IReadOnlyList<string> CommonNames,
    double Score,
    long? GbifTaxonKey);

public static class PlantIdentificationErrorCodes
{
    /// <summary>The service sees no plant it knows in the picture.</summary>
    public const string NotRecognised = "ai.plant_not_recognised";

    /// <summary>The day's identifications are spent, by this bot's own cap or the service's.</summary>
    public const string QuotaExceeded = "ai.plant_identification_quota_exceeded";

    /// <summary>The picture could not be sent: not fetchable, too large, or in a format the service does not take.</summary>
    public const string ImageUnusable = "ai.plant_image_unusable";

    /// <summary>The feature is off, or no key is configured.</summary>
    public const string Unavailable = "ai.plant_identification_unavailable";

    /// <summary>The service could not be reached or did not answer.</summary>
    public const string ServiceFailed = "ai.plant_identification_failed";
}
