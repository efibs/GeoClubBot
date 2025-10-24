namespace UseCases.InputPorts.AI;

public interface IMetaVectorStoreSearchPlugin
{
    Task<string> SearchInformation(string query, int limit = 7);
    Task<string> GetCountries();

    Task<string> GetInformationByCountry(string country);
}