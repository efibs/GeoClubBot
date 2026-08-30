namespace UseCases.OutputPorts.AI;

/// <summary>
/// A raw section scraped from a PlonkIt country page. Carries everything the embedder
/// needs to produce the embedding text (guide title + continents) and the source/country
/// metadata that's written into the vector-store payload.
/// </summary>
public sealed record PlonkItSection(
    string Country,
    string GuideTitle,
    string Source,
    string InnerHtml,
    string InnerText,
    IReadOnlyList<string> Continents);

/// <summary>
/// Event emitted by <see cref="IPlonkItPageFetcher"/> — either a parsed section or a
/// human-readable status update for the rebuild progress stream.
/// </summary>
public abstract record PlonkItFetchEvent;

public sealed record PlonkItStatusEvent(string Message) : PlonkItFetchEvent;

public sealed record PlonkItSectionEvent(PlonkItSection Section) : PlonkItFetchEvent;
