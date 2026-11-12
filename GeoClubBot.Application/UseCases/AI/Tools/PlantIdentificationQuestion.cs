using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Reads whether a question with a picture asks what plant the picture shows: "what tree is this?",
/// "which cactus is that?", "can you identify this plant", "what kind of palm trees are these", "is
/// this a eucalyptus?". Only the question's shape is read, never the picture; the caller knows whether
/// there is one. A question that names a country and asks about its bollards is not sent to a plant
/// service because a tree stands in the picture.
///
/// Pure, so every rule is pinned by a test.
/// </summary>
public static partial class PlantIdentificationQuestion
{
    /// <summary>
    /// Words that say the thing asked about is a plant. Kinds, not species — "eucalyptus" and "yucca"
    /// are here because members ask "is this a yucca?", not because the list could ever name them all.
    /// </summary>
    private static readonly string[] PlantWords =
    [
        "plant", "plants", "tree", "trees", "flower", "flowers", "bush", "bushes", "shrub", "shrubs",
        "palm", "palms", "grass", "grasses", "cactus", "cacti", "cactuses", "succulent", "succulents",
        "vegetation", "flora", "vine", "vines", "fern", "ferns", "moss", "crop", "crops", "weed", "weeds",
        "pine", "pines", "conifer", "conifers", "oak", "oaks", "eucalyptus", "yucca", "yuccas", "agave",
        "agaves", "cholla", "baobab", "baobabs", "acacia", "acacias", "banana", "bananas", "bamboo",
        "hedge", "hedges", "foliage", "leaves", "species", "shrubbery", "cypress", "poplar", "poplars",
        "birch", "birches", "spruce", "fir", "firs", "willow", "willows", "maple", "maples", "olive",
        "olives", "cedar", "cedars", "juniper", "junipers", "sugarcane", "sugar cane", "rice", "maize",
        "corn", "wheat", "tea", "coffee", "cocoa", "rubber", "teak", "mango", "coconut", "coconuts", "date palm"
    ];

    /// <summary>Whether <paramref name="question"/>, asked with a picture, asks what plant the picture shows.</summary>
    public static bool Asks(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        if (!HasPlantWord(question))
        {
            return false;
        }

        // Asks what it is, which it is, or to name or identify it — about this picture.
        return Identify().IsMatch(question) || WhatIsThis().IsMatch(question) || IsThisA().IsMatch(question);
    }

    private static bool HasPlantWord(string question)
    {
        var words = question.ToLowerInvariant();
        return PlantWords.Any(word => Regex.IsMatch(words, $@"\b{Regex.Escape(word)}\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>"identify this tree", "can you name the plant", "ID this cactus", "what species".</summary>
    [GeneratedRegex(@"\b(identify|identification|name|id|recogni[sz]e|tell\s+me\s+what|what\s+species|which\s+species|what\s+kind\s+of|what\s+type\s+of|what\s+sort\s+of)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Identify();

    /// <summary>"what tree is this", "which cactus is that", "what plant is in the picture", "what are these trees".</summary>
    [GeneratedRegex(@"\b(what|which)\b[^?.!]{0,60}\b(is|are|am|was|were|could|might|would|do|does)\b[^?.!]{0,40}\b(this|that|these|those|here|it|the\s+(picture|screenshot|image|photo|pic|round|location|background|left|right))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WhatIsThis();

    /// <summary>"is this a eucalyptus?", "are these palm trees?", "is that an acacia or a mesquite".</summary>
    [GeneratedRegex(@"^\s*(is|are|could|might|would)\s+(this|that|these|those|it)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IsThisA();
}
