namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Internal helper used by the PlonkIt-guide vector-store rebuild path to turn a raw section
/// into an embeddable text (with a classification prepended). This lives inside the AI
/// adapter because it's a SemanticKernel/LLM implementation detail.
/// </summary>
public interface IPlonkItGuideEmbeddingTextProvider
{
    Task<string> GetEmbeddingTextAsync(string country, string sectionContent, ICollection<string> continents);

    Task<bool> TestConnectionAsync();
}
