namespace UseCases.InputPorts.AI;

public interface IGetPlonkItGuideSectionEmbeddingTextUseCase
{
    Task<string> GetEmbeddingTextAsync(string country, string sectionContent, ICollection<string> continents);
}