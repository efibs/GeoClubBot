namespace UseCases.OutputPorts.AI;

public enum AiChatRole
{
    System = 0,
    User,
    Assistant
}

/// <summary>One piece of a message. Messages are multimodal, so a single turn mixes text and images.</summary>
public abstract record AiContentPart;

public sealed record AiTextPart(string Text) : AiContentPart;

/// <summary>
/// An image referenced by URL. Remote <c>https</c> URLs are preferred over <c>data:</c> URIs — the
/// provider fetches them itself, which avoids base64-inflating every request by ~33%.
/// </summary>
public sealed record AiImagePart(string Url) : AiContentPart;

public sealed record AiChatMessage(AiChatRole Role, IReadOnlyList<AiContentPart> Parts)
{
    public static AiChatMessage System(string text) =>
        new(AiChatRole.System, [new AiTextPart(text)]);

    public static AiChatMessage Assistant(string text) =>
        new(AiChatRole.Assistant, [new AiTextPart(text)]);

    public static AiChatMessage User(string text, IEnumerable<string>? imageUrls = null)
    {
        var parts = new List<AiContentPart> { new AiTextPart(text) };
        if (imageUrls is not null)
        {
            parts.AddRange(imageUrls.Select(url => new AiImagePart(url)));
        }

        return new AiChatMessage(AiChatRole.User, parts);
    }

    /// <summary>Plain-text projection, used for length budgeting and logging.</summary>
    public string ToPlainText() =>
        string.Join(" ", Parts.OfType<AiTextPart>().Select(part => part.Text));
}

/// <summary>
/// A completion request. <paramref name="ModelChain"/> is ordered: the first entry is the preferred
/// model and the rest are server-side fallbacks, so one HTTP call survives a model being down,
/// rate-limited, or refusing on moderation grounds.
/// </summary>
public sealed record AiChatRequest(
    IReadOnlyList<string> ModelChain,
    IReadOnlyList<AiChatMessage> Messages,
    double? Temperature = null,
    int? MaxTokens = null);

public sealed record AiTokenUsage(int PromptTokens, int CompletionTokens);

/// <summary>
/// <paramref name="ModelUsed"/> is the model that actually answered, which may be any entry from the
/// chain. Recording it is what makes "the bot got worse today" diagnosable.
/// </summary>
public sealed record AiChatResponse(string Text, string ModelUsed, AiTokenUsage Usage);
