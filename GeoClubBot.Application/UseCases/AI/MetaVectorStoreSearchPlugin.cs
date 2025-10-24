using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using UseCases.InputPorts.AI;

namespace UseCases.UseCases.AI;

public class MetaVectorStoreSearchPlugin(MetaVectorStore vectorStore, ILogger<MetaVectorStoreSearchPlugin> logger) : IMetaVectorStoreSearchPlugin
{
    [KernelFunction]
    [Description("Search for information using semantic search based on a query")]
    public async Task<string> SearchInformation(
        [Description("The search query to find relevant information")] string query,
        [Description("Maximum number of results to return")] int limit = 7)
    {
        logger.LogDebug($"Running search query '{query}' with limit {limit}");

        try
        {
            var sections = await vectorStore.SearchSectionsAsync(query, limit).ConfigureAwait(false);

            if (sections.Count == 0)
                return "No sections found matching the query.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {sections.Count} relevant sections:\n");

            for (int i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                sb.AppendLine($"--- Section {i + 1} ---");
                sb.AppendLine($"ID: {section.Id}");
                sb.AppendLine($"Country: {section.Country}");
                sb.AppendLine($"Source: {section.Source}");
                sb.AppendLine($"Text: {section.Text}");
                sb.AppendLine($"Hash: {section.Hash}");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while trying to find relevant information");
            throw;
        }
    }

    [KernelFunction]
    [Description("Get all unique countries that have information in the database")]
    public async Task<string> GetCountries()
    {
        logger.LogDebug("Running get all countries");
        
        var countries = await vectorStore.GetUniqueCountriesAsync().ConfigureAwait(false);
        
        if (countries.Count == 0)
            return "No countries found in the database.";

        return $"Available countries ({countries.Count}):\n" + string.Join("\n", countries);
    }

    [KernelFunction]
    [Description("Get all teh information for a specific country")]
    public async Task<string> GetInformationByCountry(
        [Description("The name of the country to retrieve the information for")] string country)
    {
        logger.LogDebug("Running get all sections for country {country}", country);
        
        var sections = await vectorStore.GetSectionsByCountryAsync(country).ConfigureAwait(false);
        
        if (sections.Count == 0)
            return $"No sections found for country: {country}";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {sections.Count} sections for {country}:\n");

        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            sb.AppendLine($"--- Section {i + 1} ---");
            sb.AppendLine($"ID: {section.Id}");
            sb.AppendLine($"Country: {section.Country}");
            sb.AppendLine($"Source: {section.Source}");
            sb.AppendLine($"Text: {section.Text}");
            sb.AppendLine($"Hash: {section.Hash}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}