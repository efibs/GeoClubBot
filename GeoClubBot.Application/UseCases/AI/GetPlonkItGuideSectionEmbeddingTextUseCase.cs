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
        $"Identifying {CountryPlaceholder}",
        "Google car and coverage",
        "Regional and state-specific clues",
        "Landscape and vegetation",
        "Agriculture",
        "Infrastructure",
        "Architecture",
        "Language",
        "Flags",
        "Store chains",
        "Miscellaneous"
    ];

    private const string ClassifyPrompt = @"
You are a text classifier. 
Classify text into one of the following categories:
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
        
        var llmEndpoint = config.GetConnectionString(ConfigKeys.LlmInferenceEndpointConnectionString)!;
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