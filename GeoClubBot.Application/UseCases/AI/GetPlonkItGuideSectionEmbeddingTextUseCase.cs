using System.Text;
using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using UseCases.InputPorts.AI;

namespace UseCases.UseCases.AI;

public class GetPlonkItGuideSectionEmbeddingTextUseCase : IGetPlonkItGuideSectionEmbeddingTextUseCase
{
    private const string CountryPlaceholder = "{{country}}";
    private static readonly List<string> Categories = [
        $"Identifying {CountryPlaceholder} (Clues that help you to identify the country or differentiate it from other countries)",
        "Google car and coverage (Clues related to the google car that was used to take the panoramas or general coverage metas like copyright, season or weather)",
        $"Regional and state-specific clues (Clues that help you to identify specific states or regions INSIDE the country. If the clue is related to differentiating the country from other countries, it belongs to the Identifying {CountryPlaceholder} category.)",
        "Landscape and vegetation (Clues related to the landscape and vegetation in specific regions of the country)",
        "Agriculture (Clues related to agriculture, most likely which crops are grown where)",
        "Infrastructure (Clues related to the infrastructure such as road lines, power poles or bollards)",
        "Architecture (Clues related to the general architecture being used)",
        "Language (Clues related to regional languages such as catalan or basque in spain)",
        "Flags (Clues related to special flags that are not just the country flag)",
        "Store chains (Clues related to regional store chains)",
        "Miscellaneous (Everything that didn't fit into the other categories)"
    ];

    private const string ClassifyPrompt = @"
You are a text classifier. You will be given a Text that describes a clue for GeoGuessr. 
Classify text into one of the following categories and use the description for each category:
{{$categories}}

Examples:
Text: 'The standard Spanish bollards have a yellow-orange reflector on the front and two white dots on the back (though the back can also be blank). They are typically hollow.', Category: 'Identifying Spain'
Text: 'Mongolian licence plates are mostly white with a hint of red on the left side from the Soyombo symbol.', Category: 'Identifying Mongolia'
Text: 'The German Alps is the most mountainous region in Germany, it is mostly concentrated on the Austrian Border.', Category: 'Landscape and vegetation'
Text: 'Some states also use wooden poles. They are most commonly seen in Rio Grande do Sul, Roraima, Amazonas, and Rio de Janeiro.', Category: 'Infrastructure'
Text: 'Here is a map of German area codes, which you can practise by playing this map.', Category: 'Regional and state-specific clues
Text: 'Autumn Generation 3 coverage is common in Kaliningrad, Bryansk, Tula, and Belgorod in western Russia, but Kaliningrad only has autumn coverage.', Category: 'Google car and coverage'

Now classify:
Text: {{$input}}
Return only the category.
";
    
    public GetPlonkItGuideSectionEmbeddingTextUseCase(ILogger<GetPlonkItGuideSectionEmbeddingTextUseCase> logger, IConfiguration config)
    {
        _logger = logger;   
        
        var llmEndpoint = config.GetConnectionString(ConfigKeys.CategorizationEndpoint)!;
        var llmModelName = config.GetValue<string>(ConfigKeys.CategorizeModelNameConfigurationKey)!;
        var llmApiKey = config.GetValue<string>(ConfigKeys.LlmApiKeyConfigurationKey);

        _categorizeKernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: llmModelName, apiKey: llmApiKey, endpoint: new Uri(llmEndpoint))
            .Build();

        _categorizeFunction = _categorizeKernel.CreateFunctionFromPrompt(ClassifyPrompt, new OpenAIPromptExecutionSettings
        {
            Temperature = 0
        });
    }
    
    public async Task<string> GetEmbeddingTextAsync(string country, string sectionContent, ICollection<string> continents)
    {
        // Get the available categories
        var availableCategories = string.Join(", ", Categories.Select(c => c.Replace(CountryPlaceholder, country)));
        
        string? category = null;
        try
        {
            // Let the llm categorize
            var result = await _categorizeKernel.InvokeAsync(
                _categorizeFunction,
                new()
                {
                    ["input"] = sectionContent,
                    ["categories"] = availableCategories
                }
            ).ConfigureAwait(false);

            var resultString = result.ToString();

            if (resultString.StartsWith("Category:"))
            {
                resultString = resultString.Substring(9);
            }
            else if (resultString.StartsWith("Category"))
            {
                resultString = resultString.Substring(8);
            }
            
            category = resultString.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Categorization of PlonkIt section failed.");
        }
        
        // Build the text
        var textBuilder = new StringBuilder("Country: ");
        textBuilder.AppendLine(country);
        textBuilder.Append("Continent(s): ");
        textBuilder.AppendLine(string.Join(", ", continents));
        if (category != null)
        {
            textBuilder.Append("Category: ");
            textBuilder.AppendLine(category);
        }
        textBuilder.AppendLine();
        textBuilder.Append(sectionContent);
        
        var text =  textBuilder.ToString();
        return text;
    }
    
    private readonly Kernel _categorizeKernel;
    private readonly KernelFunction _categorizeFunction;
    private readonly ILogger<GetPlonkItGuideSectionEmbeddingTextUseCase> _logger;
}