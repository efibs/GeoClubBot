using System.Text.RegularExpressions;
using Configuration;
using Entities;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace UseCases.UseCases.AI.Retrieval;

/// <summary>One wording of the question to embed and search by.</summary>
/// <param name="Name">What it is — "question", "conversation", "rewritten", "phrasing 1" — for anyone explaining a result.</param>
/// <param name="Weight">How much a place in its result list counts, against the lead wording at one.</param>
public sealed record RetrievalWording(string Name, string Text, double Weight);

/// <summary>
/// Everything one question is searched by: its wordings, the countries it is about and the phrases it
/// turns on. Composed before anything is embedded, so all the wordings go out in one embedding request.
/// </summary>
/// <param name="Lead">The wording that best stands for the question; the country and phrase searches rank by it.</param>
/// <param name="Others">The other wordings, each searched as a list of its own.</param>
/// <param name="QuestionNamesCountries">
/// The question's own words name at least one of <paramref name="Countries"/>, rather than its branch
/// lending them all. Only then do they outweigh a screenshot's look-alikes.
/// </param>
public sealed record RetrievalSearchPlan(
    RetrievalWording Lead,
    IReadOnlyList<RetrievalWording> Others,
    IReadOnlyList<CountryMention> Countries,
    IReadOnlyList<string> KeyPhrases,
    bool HasScreenshot,
    bool QuestionNamesCountries)
{
    /// <summary>Every wording to embed, the lead first.</summary>
    public IReadOnlyList<string> Texts => [Lead.Text, .. Others.Select(other => other.Text)];

    /// <summary>
    /// The index query for these wordings once embedded.
    /// </summary>
    /// <param name="textVectors">One vector per <see cref="Texts"/> entry, in the same order.</param>
    /// <param name="imageVector">The screenshot's vector, when the question carries one.</param>
    public KnowledgeQuery ToQuery(
        IReadOnlyList<ReadOnlyMemory<float>> textVectors,
        ReadOnlyMemory<float>? imageVector,
        AiRetrievalConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(textVectors);
        ArgumentNullException.ThrowIfNull(settings);

        if (textVectors.Count != Texts.Count)
        {
            throw new ArgumentException(
                $"Expected {Texts.Count} text vectors, one per wording, but got {textVectors.Count}.", nameof(textVectors));
        }

        // Only a country the question itself names: one its branch lent says nothing about the
        // screenshot, and lowering the screenshot's own search for it would bury the picture under
        // the branch's country.
        var named = QuestionNamesCountries && Countries.Count > 0;

        return new KnowledgeQuery
        {
            TextVector = textVectors[0],
            TextWeight = Lead.Weight,
            ExtraTextVectors = [.. Others.Select((other, index) =>
                new WeightedQueryVector(other.Name, textVectors[index + 1], other.Weight))],
            ImageVector = imageVector,
            // A named country outweighs a screenshot's look-alikes from everywhere else.
            ImageWeight = named ? settings.CountryBoost.ScreenshotWeightWhenNamed : 1d,
            BoostCountries = [.. Countries.SelectMany(country => country.IndexNames).Distinct(StringComparer.Ordinal)],
            CountryBoostWeight = settings.CountryBoost.Weight,
            KeyPhrases = KeyPhrases,
            KeyPhraseWeight = settings.KeyPhraseWeight,
            // Not for a screenshot: there a look-alike picture captioned only "Svalbard — Identifying
            // Svalbard" says the one thing "where is this?" asks — which country it looks like.
            HeadingOnlyPictureWeight = HasScreenshot ? 1d : settings.HeadingOnlyPictureWeight,
            Limit = settings.ExcerptLimit
        };
    }
}

/// <summary>
/// Turns a question, the branch it belongs to and the planner's reading of it into what the index is
/// searched by.
///
/// A follow-up is searched by its branch, not only its own words. In the 2026-10-23 feedback every bad
/// follow-up failed this way: "What about corn?" found its answer 10th, the A-type guardrails 24th and
/// 46th, a single sideways insulator 46th and 151st — and with the question before it joined on, 1st,
/// 1st and 2nd, 1st and 6th. The follow-up's own words are kept as a weaker list beside it, because a
/// follow-up that already stands on its own ("In which countries can you see the stubby antenna?")
/// loses nothing that way.
///
/// Pure, so what a question is searched by is pinned by tests and replayed by the retrieval probe
/// exactly as the bot composes it.
/// </summary>
public static partial class RetrievalQueryComposer
{
    public const string QuestionWording = "question";
    public const string ConversationWording = "conversation";
    public const string RewrittenWording = "rewritten";
    public const string PhrasingWording = "phrasing";

    /// <summary>
    /// Characters of earlier questions joined to a follow-up. A long one would drown the follow-up's
    /// own subject in the one before it.
    /// </summary>
    private const int MaxContextCharacters = 600;

    /// <param name="question">The message being answered.</param>
    /// <param name="turns">The branch it replies to, oldest first; empty for a fresh question.</param>
    /// <param name="hasScreenshot">The question carries a picture, searched by its pixels too.</param>
    /// <param name="plan">The planner's reading of the question, or null when there is none.</param>
    /// <param name="gazetteer">The index's countries, to find the ones the question is about.</param>
    public static RetrievalSearchPlan Compose(
        string question,
        IReadOnlyList<ConversationTurnView> turns,
        bool hasScreenshot,
        RetrievalPlan? plan,
        CountryGazetteer gazetteer,
        AiRetrievalConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(gazetteer);
        ArgumentNullException.ThrowIfNull(settings);

        var bare = Collapse(question);
        var earlier = settings.ContextualQuery ? EarlierQuestions(turns, settings.ContextUserTurns) : null;
        // A screenshot sent without a word is searched by its branch alone, not by the branch and a space.
        var contextual = earlier is null ? null : bare.Length > 0 ? $"{earlier} {bare}" : earlier;
        var isFollowUp = contextual is not null;

        // The lead is the wording that best stands for the question on its own: the planner's rewrite,
        // else the follow-up with its branch, else the question as asked.
        var wordings = new List<RetrievalWording>();
        if (plan?.StandaloneQuestion is { Length: > 0 } rewritten)
        {
            wordings.Add(new RetrievalWording(RewrittenWording, rewritten, 1d));

            // The branch is kept beside the rewrite rather than replaced by it: a rewrite can lose what
            // the branch was about, and the branch is what found the corn, guardrail and insulator tips.
            if (contextual is not null)
            {
                wordings.Add(new RetrievalWording(ConversationWording, contextual, settings.QueryPlanner.ConversationWeight));
            }

            wordings.Add(new RetrievalWording(QuestionWording, bare, isFollowUp ? settings.FollowUpQuestionWeight : 1d));
        }
        else if (contextual is not null)
        {
            wordings.Add(new RetrievalWording(ConversationWording, contextual, 1d));
            wordings.Add(new RetrievalWording(QuestionWording, bare, settings.FollowUpQuestionWeight));
        }
        else
        {
            wordings.Add(new RetrievalWording(QuestionWording, bare, 1d));
        }

        foreach (var (phrasing, index) in (plan?.SearchPhrasings ?? []).Take(settings.QueryPlanner.MaxPhrasings).Select((text, index) => (text, index)))
        {
            wordings.Add(new RetrievalWording($"{PhrasingWording} {index + 1}", phrasing, settings.QueryPlanner.PhrasingWeight));
        }

        // The same text twice would only be searched twice. Weightless wordings are not searched at all.
        var distinct = wordings
            .Where(wording => wording.Text.Length > 0)
            .DistinctBy(wording => wording.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var lead = distinct.Count > 0 ? distinct[0] : new RetrievalWording(QuestionWording, bare, 1d);
        var others = distinct.Skip(1).Where(wording => wording.Weight > 0).ToList();

        var (countries, named) = settings.CountryBoost.Enabled
            ? FindCountries(bare, earlier, turns, hasScreenshot, plan, gazetteer, settings.CountryBoost.MaxCountries)
            : ([], false);

        return new RetrievalSearchPlan(
            lead,
            others,
            countries,
            settings.KeyPhraseWeight > 0 ? plan?.KeyPhrases ?? [] : [],
            hasScreenshot,
            named);
    }

    /// <summary>
    /// The countries the question is about, and whether its own words name any of them.
    ///
    /// Its own words count first: the countries the gazetteer finds in it, and those the planner reads
    /// from its regions and cities (Kyushu: Japan). The planner reads the whole conversation and names
    /// the branch's countries too, so a planned country the branch names counts as the question's own
    /// only when the question names it as well.
    ///
    /// A follow-up keeps its branch's countries — from the planner when there is a plan, else from the
    /// earlier questions — when it names none ("What about corn?" keeps its Philippines) or refers back
    /// to the branch ("Can I use this to differentiate it from Czechia?" after A-type guardrails in
    /// Slovakia is about both). Not when it stands on its own beside its branch: "How do I tell Peru
    /// from Bolivia?" after a question about Brazil is about Peru and Bolivia. Not when it asks about
    /// countries in general: "In which countries can you see the stubby antenna?" after one about
    /// Ecuador and Peru is no longer about those two.
    ///
    /// A screenshot follow-up keeps them only when it names no place and asks something other than
    /// where the picture was taken — "Is this Bengali-Assamese?" after a question about Tripura's
    /// script. "Where is this?", or a screenshot sent without a word, under a question about Brazilian
    /// bollards asks where the picture is, and a boost of Brazil would answer "Brazil" whatever it
    /// shows. "This" there is the picture, so it says nothing about the branch.
    ///
    /// More than <paramref name="maxCountries"/> reads as a question about many places and boosts none.
    /// </summary>
    private static (IReadOnlyList<CountryMention> Countries, bool Named) FindCountries(
        string question,
        string? earlier,
        IReadOnlyList<ConversationTurnView> turns,
        bool hasScreenshot,
        RetrievalPlan? plan,
        CountryGazetteer gazetteer,
        int maxCountries)
    {
        var planned = plan?.Countries.SelectMany(gazetteer.Resolve).ToList() ?? [];
        var branch = turns.SelectMany(turn => gazetteer.FindMentions(turn.Content)).ToHashSet();
        var own = gazetteer.FindMentions(question).Concat(planned.Where(country => !branch.Contains(country))).Distinct().ToList();

        var keepsBranch = !AsksAboutCountriesInGeneral().IsMatch(question)
                          && (hasScreenshot
                              ? own.Count == 0 && question.Length > 0 && !AsksWhere().IsMatch(question)
                              : own.Count == 0 || RefersBack().IsMatch(question));
        var countries = keepsBranch
            ? own.Concat(plan is not null ? planned : gazetteer.FindMentions(earlier)).Distinct().ToList()
            : own;

        return countries.Count > 0 && countries.Count <= maxCountries ? (countries, own.Count > 0) : ([], false);
    }

    /// <summary>
    /// Words by which a follow-up leans on its branch: "differentiate <em>it</em> from Czechia", "is it
    /// the <em>same</em> in Peru?". Broad on purpose: a false match only keeps the branch's countries
    /// beside the question's own, which a follow-up naming countries of its own always did before.
    /// </summary>
    [GeneratedRegex(@"\b(?:it|its|this|that|these|those|they|them|their|there|here|same|too|also|either|both|ones)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RefersBack();

    /// <summary>"Where is this?", "where was this taken?": a screenshot question asking for its place.</summary>
    [GeneratedRegex(@"\bwhere\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AsksWhere();

    /// <summary>"Which countries…", "what country…", "where else…": a question about places in general.</summary>
    [GeneratedRegex(@"\b(?:countries|(?:which|what|other|any) country|where else|anywhere else|elsewhere)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AsksAboutCountriesInGeneral();

    /// <summary>The branch's latest questions, oldest first, or null when it has none.</summary>
    private static string? EarlierQuestions(IReadOnlyList<ConversationTurnView> turns, int count)
    {
        var questions = turns
            .Where(turn => turn.Role == AiTurnRole.User && !string.IsNullOrWhiteSpace(turn.Content))
            .TakeLast(Math.Max(1, count))
            .Select(turn => Collapse(turn.Content))
            .ToList();

        if (questions.Count == 0)
        {
            return null;
        }

        var joined = string.Join(' ', questions);

        // Kept from the end: the latest question is the one the follow-up continues.
        return joined.Length <= MaxContextCharacters ? joined : joined[^MaxContextCharacters..].TrimStart();
    }

    private static string Collapse(string? text) =>
        text is null ? string.Empty : string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
}
