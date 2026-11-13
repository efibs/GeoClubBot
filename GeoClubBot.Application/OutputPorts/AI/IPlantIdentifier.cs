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
    /// The candidates for the plant in the picture at <paramref name="imageUrl"/>, best first. Fails
    /// with <see cref="ErrorType.NotFound"/> (<see cref="PlantIdentificationErrorCodes.NotRecognised"/>)
    /// when the service sees no plant it knows, with <see cref="ErrorType.Conflict"/>
    /// (<see cref="PlantIdentificationErrorCodes.QuotaExceeded"/>) when the day's allowance is spent,
    /// with <see cref="ErrorType.Validation"/> when the picture cannot be sent, and with
    /// <see cref="ErrorType.Unexpected"/> when the service could not be reached. Never throws for any of them.
    /// </summary>
    Task<Result<PlantIdentification>> IdentifyAsync(string imageUrl, CancellationToken cancellationToken = default);
}

/// <param name="Candidates">What the plant may be, best first; never empty on success.</param>
/// <param name="RemainingRequests">What the service says is left of the day's allowance, when it says.</param>
public sealed record PlantIdentification(IReadOnlyList<PlantCandidate> Candidates, int? RemainingRequests)
{
    public PlantCandidate Best => Candidates[0];
}

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
