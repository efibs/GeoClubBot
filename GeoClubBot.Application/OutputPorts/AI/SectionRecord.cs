namespace UseCases.OutputPorts.AI;

/// <summary>
/// A flat read-projection of a single PlonkIt section as it lives in the vector store.
/// Returned by search and lookup queries; <see cref="Text"/> is the original HTML content.
/// </summary>
public sealed class SectionRecord
{
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
