using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace UseCases.UseCases.AI;

public partial class PlonkItGuidePlugin(PlonkItGuideVectorStore vectorStore, ILogger<PlonkItGuidePlugin> logger)
{
    [KernelFunction]
    [Description("Search for information in the PlonkIt Guide using semantic search based on a query")]
    public async Task<string> SearchInformation(
        [Description("The search query to find relevant information")] string query,
        [Description("Maximum number of results to return")] int limit = 7)
    {
        LogRunningSearchQueryWithLimit(logger, query, limit);

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
                sb.AppendLine($"Country: {section.Country}");
                sb.AppendLine($"Source: {section.Source}");
                sb.AppendLine($"Text: {section.Text}");
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
    [Description("Get all unique countries that have information in the PlonkIt Guide")]
    public async Task<string> GetCountries()
    {
        logger.LogDebug("Running get all countries");
        
        var countries = await vectorStore.GetUniqueCountriesAsync().ConfigureAwait(false);
        
        if (countries.Count == 0)
            return "No countries found in the database.";

        return JsonSerializer.Serialize(countries);
    }

    [KernelFunction]
    [Description("Get all the information for a specific country that is available in the PlonkIt Guide")]
    public async Task<string> GetInformationByCountry(
        [Description("The name of the country to retrieve the information for")] string country)
    {
        LogRunningGetAllSectionsForCountry(logger, country);
        
        var sections = await vectorStore.GetSectionsByCountryAsync(country).ConfigureAwait(false);
        
        if (sections.Count == 0)
            return $"No sections found for country: {country}";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {sections.Count} sections for {country}:\n");

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            sb.AppendLine($"--- Section {i + 1} ---");
            sb.AppendLine($"Country: {section.Country}");
            sb.AppendLine($"Source: {section.Source}");
            sb.AppendLine($"Text: {section.Text}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
    
    public SemaphoreSlim RebuildStoreLock => vectorStore.RebuildStoreLock;
    
    [LoggerMessage(LogLevel.Debug, "Running search query '{query}' with limit {limit}")]
    static partial void LogRunningSearchQueryWithLimit(ILogger<PlonkItGuidePlugin> logger, string query, int limit);

    [LoggerMessage(LogLevel.Debug, "Running get all sections for country {country}")]
    static partial void LogRunningGetAllSectionsForCountry(ILogger<PlonkItGuidePlugin> logger, string country);
}