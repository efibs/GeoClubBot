using System.Text.RegularExpressions;
using Entities;
using UseCases.UseCases.AI.Conversations;

namespace UseCases.UseCases.AI.Retrieval;

/// <summary>
/// Whether a question asks for something that is not common knowledge: "tell me a reliable clue I
/// don't know yet", "a good USA regionguessing clue that is not on PlonkIt", "any lesser-known metas
/// for Brazil?". To a player that means not on PlonkIt, the guide everybody has read.
///
/// A follow-up keeps asking it: "Please give me another one, maybe outside India" says nothing of
/// what "another one" has to be, and was answered with two more clues PlonkIt carries (2026-11-09). So
/// the questions before it on its branch count too, the nearest few.
///
/// Read from the shape of the question, never from a model, so it costs nothing and is pinned by tests.
/// </summary>
public static partial class LesserKnownQuestion
{
    /// <summary>How many of the branch's earlier questions still say what a follow-up is after.</summary>
    private const int BranchQuestionsRead = 3;

    /// <summary>Whether <paramref name="question"/> itself asks for something new.</summary>
    public static bool Asks(string? question) =>
        !string.IsNullOrWhiteSpace(question) && Pattern().IsMatch(question);

    /// <summary>Whether the question does, or the questions before it on its branch did.</summary>
    public static bool Asks(string? question, IReadOnlyList<ConversationTurnView> branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        return Asks(question)
               || branch
                   .Where(turn => turn.Role == AiTurnRole.User)
                   .TakeLast(BranchQuestionsRead)
                   .Any(turn => Asks(turn.Content));
    }

    /// <summary>
    /// Whether a question that asks for something new names nothing to look for: "tell me a reliable
    /// clue I don't know yet", "a good meta that's not on PlonkIt, please". A search has only its
    /// wording to go by then (<see cref="ClueSampler"/>).
    ///
    /// Read by what is left once the words such a request is made of are taken away. Any other word
    /// is a subject — "bollard", "Brazil", "Gen 4" — and so is a number, so a question is only ever
    /// taken for open when it plainly is; "a lesser-known bollard clue" is searched like any other.
    /// A follow-up inherits its branch's subject: "another one please" after "any lesser-known bollard
    /// clues?" is still about bollards, and is open only when the questions before it were.
    /// </summary>
    public static bool NamesNoSubject(string? question, IReadOnlyList<ConversationTurnView> branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        return NamesNoSubject(question)
               && branch
                   .Where(turn => turn.Role == AiTurnRole.User)
                   .TakeLast(BranchQuestionsRead)
                   .All(turn => NamesNoSubject(turn.Content));
    }

    /// <summary>Whether <paramref name="question"/> is made of nothing but the words of a request for a clue.</summary>
    public static bool NamesNoSubject(string? question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Any(char.IsDigit))
        {
            return false;
        }

        var words = Word().Matches(question);
        return words.Count > 0
               && words.All(word => RequestWords.Contains(word.Value.Replace("'", string.Empty, StringComparison.Ordinal)
                   .Replace("’", string.Empty, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The words a request for a clue is made of, apostrophes dropped. None of them says what the clue
    /// should be about; a word not listed here is taken to.
    /// </summary>
    // Stryker disable once String : lookup data; a test per word would only repeat the table.
    private static readonly HashSet<string> RequestWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Asking.
        "tell", "give", "show", "share", "teach", "surprise", "name", "suggest", "recommend", "throw", "hit", "got", "get",
        "have", "has", "had", "know", "knew", "known", "knowing", "heard", "seen", "learn", "learned", "learnt", "want", "need",
        "like", "love", "think", "bet", "can", "could", "would", "will", "may", "might", "should", "do", "does", "did", "is",
        "are", "was", "were", "be", "been", "please", "pls", "plz", "thanks", "thank", "thx", "hey", "hi", "hello", "ok", "okay",

        // A clue, any clue.
        "clue", "clues", "meta", "metas", "tip", "tips", "trick", "tricks", "hint", "hints", "fact", "facts", "thing", "things",
        "stuff", "something", "anything", "everything", "nothing", "idea", "ideas", "knowledge", "info", "trivia", "one", "ones",
        "another", "other", "others", "more", "else", "example", "examples",

        // What kind: good, and new to the asker.
        "reliable", "good", "great", "useful", "helpful", "nice", "cool", "interesting", "fun", "neat", "solid", "strong",
        "consistent", "best", "better", "new", "newer", "fresh", "different", "niche", "obscure", "secret", "hidden",
        "underrated", "unknown", "lesser", "less", "little", "well", "widely", "commonly", "common", "rare", "random", "under",
        "radar",

        // Who knows it, and where it is not: PlonkIt, players.
        "plonkit", "plonk", "guide", "guides", "people", "players", "player", "geoguessr", "geoguessrs", "everyone", "everybody",
        "nobody", "anyone", "most", "many", "few", "hardly", "barely", "game", "world", "worldwide", "anywhere", "somewhere",
        "regionguessing", "region", "regions", "regional", "guessing", "country", "countries",

        // The small words between.
        "a", "an", "the", "some", "any", "me", "us", "i", "im", "ive", "we", "you", "your", "my", "our", "it", "its", "that",
        "thats", "this", "those", "these", "which", "what", "whats", "who", "not", "no", "isnt", "arent", "dont", "doesnt",
        "didnt", "havent", "wont", "wouldnt", "really", "very", "quite", "pretty", "actually", "probably", "maybe", "perhaps",
        "likely", "just", "also", "too", "again", "already", "yet", "before", "still", "ever", "never", "now", "then", "so", "and",
        "or", "but", "if", "with", "without", "about", "of", "for", "to", "on", "in", "from", "at", "by", "as", "than", "beyond",
        "outside", "besides", "apart", "there", "here", "out", "up", "time", "today", "all", "only", "even", "much", "lot", "lots"
    };

    [GeneratedRegex(@"\p{L}+(?:['’]\p{L}+)*", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    // "not on PlonkIt", "isn't in the plonk it guide", "beyond PlonkIt"; "I don't know yet", "few people
    // know", "new to me"; "lesser-known", "obscure", "niche", "secret", "hidden", "underrated".
    [GeneratedRegex(
        @"\b(?:not|isn'?t|aren'?t|n'?t|never|nowhere|beyond|outside(?:\s+of)?|other\s+than|apart\s+from|besides|missing\s+from)\s+(?:(?:found|listed|mentioned|covered|included)\s+)?(?:on|in|from)?\s*(?:the\s+)?plonk\s?-?it\b"
        // "a clue I don't know", "something we might not know": what is not known is the clue, so the
        // words follow one. "I don't know where this is" asks something else entirely.
        + @"|\b(?:clue|meta|tip|trick|thing|fact|one|something|anything|stuff)s?\s+(?:that\s+|which\s+)?(?:i|we)\s+(?:(?:probably|maybe|likely)\s+)?"
        + @"(?:don'?t|do\s+not|didn'?t|did\s+not|might\s+not|may\s+not|wouldn'?t|won'?t|haven'?t|have\s+not)\s+(?:already\s+|yet\s+)?(?:know|heard(?:\s+of)?|seen)\b"
        + @"|\b(?:i|we)\s+(?:don'?t|do\s+not|didn'?t|haven'?t)\s+(?:already\s+)?(?:know|heard\s+of|seen)\s+(?:yet|already|before)\b"
        + @"|\b(?:i|we)\s+(?:don'?t|do\s+not)\s+already\s+know\b"
        + @"|\b(?:few|not\s+many|hardly\s+any|barely\s+any)\s+(?:people|players|geoguessrs?)\s+know\b"
        + @"|\bnot\s+(?:(?:very|that|so)\s+)?(?:well|widely|commonly)[\s-]known\b"
        + @"|\b(?:lesser|less|little)[\s-]known\b|\bunknown\b|\bobscure\b|\bniche\b|\bsecret\b|\bhidden\b|\bunderrated\b|\bunder[\s-]the[\s-]radar\b"
        + @"|\bnew\s+to\s+me\b|\b(?:surprise|teach)\s+me\b|\bsomething\s+(?:i\s+)?(?:new|don'?t\s+know)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
