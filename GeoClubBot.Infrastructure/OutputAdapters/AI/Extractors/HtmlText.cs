using System.Net;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Decodes the HTML entities in text and attributes read from a guide.
///
/// Not HtmlAgilityPack's <c>HtmlEntity.DeEntitize</c>, which cannot decode a character outside the Basic
/// Multilingual Plane: the emoji guide authors scatter through their prose arrive as numeric entities,
/// and <c>&amp;#128128;</c> came out as <c>&amp;##128128;</c> instead of 💀. On production that debris sat in
/// 88 chunks across 17 guides — in the text the model reads, in the vectors retrieval compares, and in
/// the source labels shown under an answer ("Research by Yaron&amp;##128016;"). An architecture test keeps
/// <c>HtmlEntity</c> out of this assembly.
/// </summary>
internal static class HtmlText
{
    public static string Decode(string? value) => WebUtility.HtmlDecode(value ?? string.Empty);
}
