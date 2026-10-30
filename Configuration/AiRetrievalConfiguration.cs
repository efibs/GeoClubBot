using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// How a question becomes searches of the guide index, and how much of what comes back is offered to
/// the model.
///
/// Every default here was measured by replaying the rated answers of the 2026-10-23 feedback export
/// through the bot's own search against the production index (<c>Tools/GeoClubBot.RetrievalProbe</c>,
/// <c>compare</c>). Change one by re-running that measurement rather than by reasoning about it.
/// </summary>
public class AiRetrievalConfiguration : IValidatableObject
{
    /// <summary>Nested under the AI section so the whole feature stays configurable from one place.</summary>
    public const string SectionName = "AI:Retrieval";

    /// <summary>
    /// Guide excerpts offered per question. Every one the answer cites is listed under it, so this also
    /// bounds the length of that list.
    ///
    /// Ten rather than the eight the bot offered before: replayed with the planner, the two extra places
    /// were on target in 37 of 98 cases and brought two more of the excerpts the feedback named (Sabah's
    /// parallel pole tops, the Brazilian Gen 4 antenna slide). Twelve brought one more, but the share of
    /// on-target excerpts kept falling (57 % at eight, 53 % at ten, 52 % at twelve), and chunks run to
    /// 2,000 characters each. Without the planner, the two extra places were on target in 30 of 98 cases
    /// and brought no more of the named excerpts.
    /// </summary>
    [Range(1, 20)]
    public int ExcerptLimit { get; set; } = 10;

    /// <summary>
    /// Searches a follow-up by its branch as well as by its own words. "What about corn?" says nothing
    /// a guide could match; with the question before it, it finds the Philippine corn tip first (8th
    /// before), the A-type guardrail tips 1st and 2nd (28th and unfound), a single sideways insulator
    /// tips 1st and 8th (both unfound).
    /// </summary>
    public bool ContextualQuery { get; set; } = true;

    /// <summary>
    /// How many of the branch's latest questions are joined to a follow-up for its contextual search.
    /// Two measured no better than one: the question just before is the one a follow-up continues.
    /// </summary>
    [Range(1, 6)]
    public int ContextUserTurns { get; set; } = 1;

    /// <summary>
    /// How much a follow-up's own words count beside the search that carries its context. Below one,
    /// because a follow-up's own words often match nothing on the topic at all; 0.25 and 1 both measured
    /// worse.
    /// </summary>
    [Range(0d, 4d)]
    public double FollowUpQuestionWeight { get; set; } = 0.5;

    /// <summary>
    /// How many of the excerpts the previous answer cited a follow-up is offered again, read back from
    /// the index by id and numbered first, ahead of the <see cref="ExcerptLimit"/> found for it. "Can you
    /// show me an image of the distribution?" meant the map the answer before had cited as [1], and was
    /// answered "I don't have the actual distribution image": the search for the follow-up's own words
    /// offered one Mongolian excerpt of eight, and not that one. Answers in the feedback cited one to
    /// three excerpts, rarely six or eight. Zero offers none again.
    /// </summary>
    [Range(0, 20)]
    public int MaxCarriedExcerpts { get; set; } = 6;

    /// <summary>The model call that rewrites a question for searching. See <see cref="AiQueryPlannerConfiguration"/>.</summary>
    public AiQueryPlannerConfiguration QueryPlanner { get; set; } = new();

    /// <summary>Keeping the countries a question is about in view. See <see cref="AiCountryBoostConfiguration"/>.</summary>
    public AiCountryBoostConfiguration CountryBoost { get; set; } = new();

    /// <summary>
    /// How much a picture whose only text is a guide heading ("Svalbard — Identifying Svalbard",
    /// "Distribution") counts, relative to its fused rank. The model knows a picture only by its text,
    /// so it can say nothing about one of these, yet they took 29 of 392 offered slots in the replay.
    /// At a quarter they took 6 (at a half 17), one more excerpt was on target, and one more of those
    /// the feedback named was offered, none lost. One keeps them as they rank. Never applied to a screenshot
    /// question, where such a picture's heading names the country its look-alike comes from.
    /// </summary>
    [Range(0d, 1d)]
    public double HeadingOnlyPictureWeight { get; set; } = 0.25;

    /// <summary>
    /// Searches for the planner's literal key phrases — "no supports" — among chunks whose text contains
    /// them, as one more fused list. A phrase that a similarity search misses entirely is found this way:
    /// the Brazilian tip with "no supports" rose from 32nd to 23rd. A small effect, because the phrase
    /// has to be the guide's own words, which a model guessing them seldom hits. Zero searches none.
    /// </summary>
    [Range(0d, 4d)]
    public double KeyPhraseWeight { get; set; } = 0.5;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Nested sections are not validated by data annotations on their own.
        foreach (var (section, name) in new (object Section, string Name)[]
                 {
                     (QueryPlanner, nameof(QueryPlanner)),
                     (CountryBoost, nameof(CountryBoost))
                 })
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(section, new ValidationContext(section), results, validateAllProperties: true);

            foreach (var result in results)
            {
                yield return new ValidationResult(
                    $"{name}: {result.ErrorMessage}",
                    [.. result.MemberNames.Select(member => $"{name}:{member}")]);
            }
        }

        foreach (var (alias, countries) in CountryBoost.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || countries.Count == 0 || countries.Any(string.IsNullOrWhiteSpace))
            {
                yield return new ValidationResult(
                    $"{nameof(CountryBoost)}:{nameof(AiCountryBoostConfiguration.Aliases)} entry '{alias}' must name at least one country.");
            }
        }
    }
}

/// <summary>
/// One cheap model call before searching that rewrites the question the way the guides would put it:
/// a follow-up made standalone, "snow bollards" searched as the guides' "snow poles", and the
/// countries it is about named. It costs one more request from the daily allowance, and is skipped —
/// never failing the question — when that allowance is short, the call fails, outlasts its timeout or
/// its reply cannot be read; retrieval then uses the question and its branch alone.
///
/// Replayed with <c>nemotron-3-ultra</c> planning, it offered 16 of the 24 excerpts the feedback named
/// where the same settings without it offered 14: the Finnish snow poles for "snow bollards" (unfound
/// before), and Sabah's A-road and JKR-sign tips (12th and 8th before, 4th and 7th with it). Overall
/// on-target excerpts were level (261 against 258 of 490). How much it costs in waiting depends on the
/// model heading the chain: ultra planned in 4.4 s (median), <c>dots-3-note-preview</c> in 34 s — which
/// is what <see cref="TimeoutSeconds"/> and <see cref="PauseAfterTimeoutMinutes"/> are for.
/// </summary>
public class AiQueryPlannerConfiguration
{
    /// <summary>
    /// Off by default, because on the free allowance it costs more answers than it gains. A planned
    /// question costs three requests instead of two: on the 45 a day that
    /// <c>AI:OpenRouter:DailyRequestBudget</c> ships with, planning up to <see cref="MaxDailyBudgetPercent"/>
    /// (11 questions, 33 requests) leaves room for 17 answers a day instead of 22. And the gain was
    /// measured with <c>nemotron-3-ultra</c> picked by hand: <c>dots-3-note-preview</c>, which the bot's
    /// selector put at the head of the chain that day, planned within <see cref="TimeoutSeconds"/> for
    /// 11 of 49 questions. Worth turning on with the 1,000 a day of a key with credits, where the extra
    /// request costs no answers — after replaying with the chain the selector picks
    /// (<c>Tools/GeoClubBot.RetrievalProbe compare --allow-chat</c>).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Longest the planner may take before the question is searched without it. Short, because the
    /// answer still has to be generated afterwards. 48 of 49 replayed plans by ultra came within it, 11
    /// of 49 by dots-3-note-preview.
    /// </summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Minutes the planner is skipped after it ran out of time, so a slow model at the head of the chain
    /// does not make every question wait <see cref="TimeoutSeconds"/> for nothing. Zero never skips it.
    /// </summary>
    [Range(0, 1440)]
    public int PauseAfterTimeoutMinutes { get; set; } = 30;

    /// <summary>Extra search phrasings taken from the planner's reply.</summary>
    [Range(0, 5)]
    public int MaxPhrasings { get; set; } = 3;

    /// <summary>
    /// How much each of the planner's phrasings counts, against its standalone question at one. At one
    /// they crowded out on-target excerpts (213 against 223 of 392); at zero Sabah's A-road tip fell
    /// from 4th to 10th, and with dots-3-note-preview's plans the Finnish snow poles were no longer
    /// found for "snow bollards".
    /// </summary>
    [Range(0d, 4d)]
    public double PhrasingWeight { get; set; } = 0.5;

    /// <summary>
    /// How much a follow-up searched with its branch still counts once the planner has rewritten it,
    /// against the rewrite at one. Zero searches by the rewrite alone.
    /// </summary>
    [Range(0d, 4d)]
    public double ConversationWeight { get; set; } = 1;

    /// <summary>
    /// Share of the daily allowance the planner may spend up to. Its request goes on the counter that
    /// answers and indexing share, and it is the first thing given up: once the day's requests pass
    /// this share, questions are searched without it and the rest of the allowance goes to answers.
    /// On the 50-a-day tier a planned question costs three requests instead of two.
    /// </summary>
    [Range(1, 100)]
    public int MaxDailyBudgetPercent { get; set; } = 75;
}

/// <summary>
/// Countries a question names are searched again within those countries, and the lists fused with the
/// unfiltered ones. A soft boost rather than a filter: "which countries have…" and "Scandinavia" span
/// several, and a question about one country can still be answered by another's guide.
/// </summary>
public class AiCountryBoostConfiguration
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How much a place in a country-filtered list counts, against the same search unfiltered. Replayed
    /// without the planner, the share of excerpts from the question's country was 73 % before; at one it
    /// was 88 %, at two 94 % with eight more excerpts on target, at three 95 % with one more. Three
    /// comes close to the filter this is meant not to be: a strong match from another country's guide
    /// still makes the first five at two.
    /// </summary>
    [Range(0d, 4d)]
    public double Weight { get; set; } = 2;

    /// <summary>
    /// More countries than this reads as a question about many places — "which countries…", "northern
    /// European pines against the Pacific Northwest's" — and is not boosted at all. Five, so the Nordic
    /// countries or four Balkan ones are still boosted together: against three, the share from the
    /// question's countries rose from 93 % to 94 %, and "snow bollards in the nordic countries" found
    /// the Finnish snow poles without the planner (16th; unfound at three).
    /// </summary>
    [Range(1, 10)]
    public int MaxCountries { get; set; } = 5;

    /// <summary>
    /// How much a screenshot's own pixel search counts when the question names a country. A screenshot
    /// of an Australian bollard finds look-alike bollards from everywhere; the named country is the
    /// stronger signal. Replayed with a Dutch bollard standing in for the expired screenshot, "Is this
    /// bollard a meta in Australia?" was offered 2 Australian excerpts before, 7 at full weight and 8 at
    /// a half; lower changed nothing more.
    /// </summary>
    [Range(0d, 1d)]
    public double ScreenshotWeightWhenNamed { get; set; } = 0.5;

    /// <summary>Hours the index's own country names are cached, which a country is matched against.</summary>
    [Range(1, 168)]
    public int CountryListCacheHours { get; set; } = 6;

    /// <summary>
    /// More names for a country, or a region for several: <c>{ "sabah": ["Malaysia"] }</c>. Added to
    /// the built-in names; values are countries as the index or the built-in names know them.
    /// </summary>
    public Dictionary<string, List<string>> Aliases { get; set; } = [];
}
