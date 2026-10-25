using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// The tools the answering model may call, as it is told of them.
///
/// Each description says when to call the tool and when not to, because that is all a model reads
/// about it: the question already comes with excerpts, and a model that searches again for what they
/// say spends a request and the reader's time for nothing. Each points at a cause of a bad answer in
/// the 2026-10-23 feedback export:
/// <list type="bullet">
/// <item><see cref="SearchGuides"/>: a player's words are not the guides' — "snow bollards" found the
/// Finnish snow-pole tip 43rd, "snow poles" 4th — and a country the first search missed.</item>
/// <item><see cref="ReadGuide"/>: the answer was in a guide that was offered, in a part that was not
/// (Baja California's plants, under "Sonora and BCS" and "El Vizcaíno, BCS").</item>
/// <item><see cref="SpeciesRange"/>: no guide says where a tree grows ("I am pretty sure you can also
/// find Khasi pines in other countries such as probably Thailand").</item>
/// </list>
/// </summary>
public static class AnswerTools
{
    public const string SearchGuides = "search_guides";
    public const string ReadGuide = "read_guide";
    public const string SpeciesRange = "species_range";

    /// <summary>
    /// Added to the system prompt only when tools are offered — a model told of tools it cannot call
    /// tries to call them in prose. It closes the main prompt (<c>AiPromptBuilder.SystemPrompt</c>) and
    /// leans on it: the citing, the pictures and the rules for answering are said there once, so this
    /// says only when a tool is worth a call — including for a picture the user asked to see, or the
    /// thing they named when the excerpts describe something related but different.
    /// </summary>
    public const string SystemPrompt = """
        When the excerpts are not enough you can call tools; their results are numbered excerpts too,
        cited the same way. search_guides searches the guides in your own words: use it when the
        excerpts use other terms than the question, describe only something related to what was
        asked, miss a place it names, or have no picture of what the user asks to see. read_guide
        shows more of the guide an excerpt came from, when that guide clearly says more. species_range
        says where a plant, tree or crop species is recorded. Don't call a tool when the excerpts
        already answer the question.
        """;

    /// <summary><see cref="SystemPrompt"/> for when <see cref="SpeciesRange"/> is turned off.</summary>
    public const string SystemPromptWithoutSpeciesRange = """
        When the excerpts are not enough you can call tools; their results are numbered excerpts too,
        cited the same way. search_guides searches the guides in your own words: use it when the
        excerpts use other terms than the question, describe only something related to what was
        asked, miss a place it names, or have no picture of what the user asks to see. read_guide
        shows more of the guide an excerpt came from, when that guide clearly says more. Don't call a
        tool when the excerpts already answer the question.
        """;

    private static readonly AiToolDefinition SearchGuidesTool = new(
        SearchGuides,
        "Search the community GeoGuessr guides again, in your own words. Use it when the excerpts you "
        + "were given do not answer the question: the question uses other terms than the guides (they say "
        + "\"snow poles\" where a player says \"snow bollards\"), or it is about a country or region the "
        + "excerpts miss. Returns up to 6 new numbered excerpts, cited like the others. Don't use it when "
        + "the excerpts already answer the question.",
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "What to look for, in the words a guide would use, e.g. \"pole supports\"." },
            "country": { "type": "string", "description": "Optional. A country to prefer, e.g. \"Brazil\"; guides of other countries can still come back." },
            "pictures_only": { "type": "boolean", "description": "Optional. True to find only excerpts that come with a picture." }
          },
          "required": ["query"]
        }
        """);

    private static readonly AiToolDefinition ReadGuideTool = new(
        ReadGuide,
        "Read more of the guide one of the numbered excerpts came from: the sections around it. Use it "
        + "when that guide clearly covers the question in more detail than its excerpt shows, such as a list "
        + "of regions cut short. Returns up to 8 new numbered excerpts from that guide. Don't use it when the "
        + "excerpt already answers the question.",
        """
        {
          "type": "object",
          "properties": {
            "excerpt": { "type": "integer", "description": "The number of the excerpt whose guide to read, e.g. 3." }
          },
          "required": ["excerpt"]
        }
        """);

    private static readonly AiToolDefinition SpeciesRangeTool = new(
        SpeciesRange,
        "Where a plant, tree or crop species is recorded, by country, or by region within one country, "
        + "from the GBIF biodiversity database. Use it for questions about where a plant, tree or crop grows "
        + "when the guides don't settle it. Returns one numbered excerpt, cited like the others. Don't use it "
        + "for anything but a plant, or when the excerpts already say where it grows.",
        """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Scientific or common name, e.g. \"Pinus kesiya\" or \"khasi pine\"." },
            "country": { "type": "string", "description": "Optional. A country whose regions to break the records down by, e.g. \"Mexico\"." }
          },
          "required": ["name"]
        }
        """);

    /// <summary>The tools to declare, <see cref="SpeciesRange"/> only when it is on.</summary>
    public static IReadOnlyList<AiToolDefinition> Definitions(bool speciesRange) =>
        speciesRange ? [SearchGuidesTool, ReadGuideTool, SpeciesRangeTool] : [SearchGuidesTool, ReadGuideTool];

    /// <summary>The system prompt's paragraph for the tools <see cref="Definitions"/> declares.</summary>
    public static string SystemPromptFor(bool speciesRange) =>
        speciesRange ? SystemPrompt : SystemPromptWithoutSpeciesRange;
}
