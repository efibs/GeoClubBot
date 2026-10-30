using Configuration;
using Entities;
using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What a question is searched by. The cases are the rated follow-ups of the 2026-10-23 feedback
/// export, every one of which was offered the wrong guides because it was searched by its own words.
/// </summary>
public sealed class RetrievalQueryComposerTests
{
    private static readonly CountryGazetteer Gazetteer = CountryGazetteer.Build(
        ["philippines", "malaysia", "brazil", "ecuador", "peru", "czechia", "czech republic", "slovakia",
         "norway", "sweden", "finland", "denmark", "iceland", "usa", "united states", "australia", "bolivia", "india"]);

    [Fact]
    public void Compose_SearchesAFreshQuestion_ByItsOwnWords()
    {
        var plan = Compose("where can I find sugar cane in the Philippines?");

        plan.Lead.Should().Be(new RetrievalWording(RetrievalQueryComposer.QuestionWording,
            "where can I find sugar cane in the Philippines?", 1d));
        plan.Others.Should().BeEmpty();
        plan.Countries.Select(country => country.Name).Should().Equal("philippines");
        plan.Texts.Should().ContainSingle();
    }

    [Fact]
    public void Compose_SearchesAFollowUp_ByItsBranch_AndKeepsItsOwnWordsWeaker()
    {
        // "What about corn?" found the Philippine corn tip 10th by its own words, 1st with the
        // question before it.
        var plan = Compose("What about corn?", Branch("where can I find sugar cane in the Philippenes?", "On Negros [1]."));

        plan.Lead.Should().Be(new RetrievalWording(RetrievalQueryComposer.ConversationWording,
            "where can I find sugar cane in the Philippenes? What about corn?", 1d));
        plan.Others.Should().Equal(new RetrievalWording(RetrievalQueryComposer.QuestionWording, "What about corn?", 0.5));
        plan.Countries.Select(country => country.Name).Should().Equal(["philippines"],
            "a follow-up keeps the country its branch was about");
    }

    [Fact]
    public void Compose_DoesNotKeepTheBranchesCountries_WhenTheFollowUpAsksAboutCountriesInGeneral()
    {
        var plan = Compose("In wich countries can you see the stubby antenna?",
            Branch("what is the difference in the Gen3 google cars in Ecuador vs Peru?", "The antenna [1]."));

        plan.Lead.Name.Should().Be(RetrievalQueryComposer.ConversationWording);
        plan.Countries.Should().BeEmpty("the question is no longer about Ecuador and Peru");
    }

    [Fact]
    public void Compose_JoinsTheLatestQuestionsOfTheBranch_NotTheAnswers()
    {
        var turns = new List<ConversationTurnView>
        {
            new(AiTurnRole.User, 1, "first question", []),
            new(AiTurnRole.Assistant, 2, "first answer", []),
            new(AiTurnRole.User, 1, "second question", []),
            new(AiTurnRole.Assistant, 2, "second answer", [])
        };

        Compose("third?", turns).Lead.Text.Should().Be("second question third?");
        Compose("third?", turns, settings => settings.ContextUserTurns = 2).Lead.Text
            .Should().Be("first question second question third?");
    }

    [Fact]
    public void Compose_KeepsTheEndOfALongBranch_WhichTheFollowUpContinues()
    {
        var plan = Compose("and here?", Branch(new string('a', 2000) + " Sabah lamps", "answer"));

        plan.Lead.Text.Should().EndWith("Sabah lamps and here?");
        plan.Lead.Text.Length.Should().BeLessThan(700);
    }

    [Fact]
    public void Compose_SearchesAFollowUpByItsOwnWords_WhenContextualQueriesAreOff()
    {
        var plan = Compose("What about corn?", Branch("sugar cane in the Philippines?", "Negros."),
            settings => settings.ContextualQuery = false);

        plan.Lead.Should().Be(new RetrievalWording(RetrievalQueryComposer.QuestionWording, "What about corn?", 1d));
        plan.Others.Should().BeEmpty();
    }

    [Fact]
    public void Compose_LeadsWithThePlannersRewrite_AndAddsItsPhrasings()
    {
        var planned = new RetrievalPlan(
            "What are the differences between snow poles in the Nordic countries?",
            ["snow poles Norway Sweden Finland", "snow poles design", "snow poles Norway Sweden Finland", "fourth"],
            ["Norway", "Sweden", "Finland"],
            ["snow poles"]);

        var plan = Compose("which are the differences between the snow bollards in the nordic countries?", plan: planned,
            configure: settings => settings.CountryBoost.MaxCountries = 5);

        plan.Lead.Should().Be(new RetrievalWording(RetrievalQueryComposer.RewrittenWording,
            "What are the differences between snow poles in the Nordic countries?", 1d));
        plan.Others.Select(other => (other.Name, other.Text, other.Weight)).Should().Equal(
            (RetrievalQueryComposer.QuestionWording, "which are the differences between the snow bollards in the nordic countries?", 1d),
            ("phrasing 1", "snow poles Norway Sweden Finland", 0.5),
            ("phrasing 2", "snow poles design", 0.5));
        // "Nordic" names all five; the planner's three are among them.
        plan.Countries.Select(country => country.Name).Should().BeEquivalentTo(["norway", "sweden", "finland", "denmark", "iceland"]);
        plan.KeyPhrases.Should().Equal("snow poles");
    }

    [Fact]
    public void Compose_TakesThePlannersCountries_OverTheBranches()
    {
        // The planner knows Sabah is in Malaysia, and that a question about countries in general is
        // about none of them.
        var planned = new RetrievalPlan("How can I identify Sabah?", [], ["Malaysia (Sabah)"], []);

        var plan = Compose("How can I clearly identify it?", Branch("is red soil in Brazil regional?", "No."), plan: planned);

        plan.Countries.Select(country => country.Name).Should().Equal("malaysia");
    }

    [Theory]
    [InlineData("")]
    [InlineData("where is this?")]
    public void Compose_DoesNotLendTheBranchesCountry_ToAScreenshotFollowUp(string question)
    {
        // A new screenshot under a question about Brazilian bollards asks where *it* is. Boosting
        // Brazil, and halving the screenshot's own pixel search for it, offered almost only Brazilian
        // excerpts whatever the picture showed.
        var branch = Branch("what do Brazilian bollards look like?", "Black with a white band [1].");

        var plan = Compose(question, branch, hasScreenshot: true);
        var query = plan.ToQuery([.. plan.Texts.Select(_ => (ReadOnlyMemory<float>)new float[] { 1f })], new float[] { 2f }, Settings());

        plan.Lead.Name.Should().Be(RetrievalQueryComposer.ConversationWording, "the text side still follows the branch");
        plan.Lead.Text.Should().Be(question.Length == 0
            ? "what do Brazilian bollards look like?"
            : "what do Brazilian bollards look like? where is this?");
        plan.Countries.Should().BeEmpty();
        query.BoostCountries.Should().BeEmpty();
        query.ImageWeight.Should().Be(1d, "no country the question names outweighs its screenshot");
    }

    [Fact]
    public void Compose_LendsTheBranchesCountry_ToAScreenshotFollowUpAskingSomethingElse_WithoutLettingItOutweighThePicture()
    {
        // From the 2026-10-23 export: the script on the screenshot, in the branch's country.
        var plan = Compose("Is this Bengali-Assamese?", Branch(", what writing system does Tripura, India use?", "Bengali [1]."),
            hasScreenshot: true);
        var query = plan.ToQuery([.. plan.Texts.Select(_ => (ReadOnlyMemory<float>)new float[] { 1f })], new float[] { 2f }, Settings());

        plan.Countries.Select(country => country.Name).Should().Equal("india");
        plan.QuestionNamesCountries.Should().BeFalse();
        query.ImageWeight.Should().Be(1d, "the question does not name India; its branch does");
    }

    [Fact]
    public void Compose_DropsTheBranchesCountry_ThatThePlannerHandsBackForAScreenshotFollowUp()
    {
        // The planner reads the whole conversation and names its countries.
        var planned = new RetrievalPlan("Where was this screenshot taken?", [], ["Brazil"], []);

        var plan = Compose("where is this?", Branch("what do Brazilian bollards look like?", "Black [1]."),
            plan: planned, hasScreenshot: true);

        plan.Countries.Should().BeEmpty();
        plan.ToQuery([.. plan.Texts.Select(_ => (ReadOnlyMemory<float>)new float[] { 1f })], new float[] { 2f }, Settings())
            .ImageWeight.Should().Be(1d);
    }

    [Fact]
    public void Compose_LetsAScreenshotFollowUpThatNamesACountry_OutweighItsLookAlikes()
    {
        var plan = Compose("Is this bollard a meta in Australia?",
            Branch("what do Brazilian bollards look like?", "Black [1]."), hasScreenshot: true);

        plan.Countries.Select(country => country.Name).Should().Equal("australia");
        plan.ToQuery([.. plan.Texts.Select(_ => (ReadOnlyMemory<float>)new float[] { 1f })], new float[] { 2f }, Settings())
            .ImageWeight.Should().Be(Settings().CountryBoost.ScreenshotWeightWhenNamed);
    }

    [Fact]
    public void Compose_TakesThePlannersReadingOfTheQuestionsOwnPlace_ForAScreenshot()
    {
        // Tripura is no name the gazetteer knows; the planner knows it is in India.
        var planned = new RetrievalPlan("Is this a meta in Tripura?", [], ["India", "Brazil"], []);

        var plan = Compose("Is this a meta in Tripura?", Branch("what do Brazilian bollards look like?", "Black [1]."),
            plan: planned, hasScreenshot: true);

        plan.Countries.Select(country => country.Name).Should().Equal("india");
        plan.QuestionNamesCountries.Should().BeTrue();
    }

    [Fact]
    public void Compose_BoostsOnlyTheCountriesAFollowUpNames_NotTheBranches()
    {
        var branch = Branch("what do Brazilian bollards look like?", "Black [1].");

        Compose("How do I tell Peru from Bolivia?", branch).Countries.Select(country => country.Name)
            .Should().BeEquivalentTo(["peru", "bolivia"]);

        // Nor when the planner, reading the whole conversation, names the branch's country too.
        var planned = new RetrievalPlan("How do I tell Peru from Bolivia?", [], ["Peru", "Bolivia", "Brazil"], []);
        Compose("How do I tell Peru from Bolivia?", branch, plan: planned).Countries.Select(country => country.Name)
            .Should().BeEquivalentTo(["peru", "bolivia"]);
    }

    [Fact]
    public void Compose_KeepsTheBranchesCountry_BesideTheFollowUpsOwn_WhenTheFollowUpRefersBack()
    {
        // Both guardrail tips the feedback named, one Czech and one Slovak, answer it.
        var branch = Branch("can you see A-type guardrails in Slovakia?", "Yes [1].");

        Compose("Can I use this to differentiate it from Czechia?", branch).Countries.Select(country => country.Name)
            .Should().BeEquivalentTo(["czechia", "slovakia"]);

        var planned = new RetrievalPlan("Can A-type guardrails tell Slovakia from Czechia?", [], ["Slovakia", "Czechia"], []);
        Compose("Can I use this to differentiate it from Czechia?", branch, plan: planned).Countries.Select(country => country.Name)
            .Should().BeEquivalentTo(["czechia", "slovakia"]);
    }

    [Fact]
    public void Compose_KeepsTheBranchesCountry_ForAPlannedFollowUp_WithoutCallingItNamed()
    {
        var planned = new RetrievalPlan("Where in the Philippines is corn grown?", [], ["Philippines"], []);

        var plan = Compose("What about corn?", Branch("where can I find sugar cane in the Philippines?", "On Negros [1]."), plan: planned);

        plan.Countries.Select(country => country.Name).Should().Equal("philippines");
        plan.QuestionNamesCountries.Should().BeFalse("the branch lent it");
    }

    [Fact]
    public void Compose_DropsTheBranchesCountries_ThatThePlannerHandsBackForAQuestionAboutCountriesInGeneral()
    {
        var planned = new RetrievalPlan("In which countries can you see the stubby antenna?", [], ["Ecuador", "Peru"], []);

        var plan = Compose("In wich countries can you see the stubby antenna?",
            Branch("what is the difference in the Gen3 google cars in Ecuador vs Peru?", "The antenna [1]."), plan: planned);

        plan.Countries.Should().BeEmpty();
    }

    [Fact]
    public void Compose_BoostsNoCountry_WhenTheQuestionIsAboutTooMany()
    {
        var plan = Compose("snow poles in Norway, Sweden, Finland, Denmark and Iceland",
            configure: settings => settings.CountryBoost.MaxCountries = 3);

        plan.Countries.Should().BeEmpty();
    }

    [Fact]
    public void Compose_LeavesOutWhatIsSwitchedOff()
    {
        var planned = new RetrievalPlan(null, ["a phrasing"], ["Brazil"], ["no supports"]);

        var plan = Compose("which states in Brazil don't do pole supports?", plan: planned, configure: settings =>
        {
            settings.CountryBoost.Enabled = false;
            settings.KeyPhraseWeight = 0;
            settings.QueryPlanner.PhrasingWeight = 0;
        });

        plan.Countries.Should().BeEmpty();
        plan.KeyPhrases.Should().BeEmpty();
        plan.Others.Should().BeEmpty("a wording that counts for nothing is not worth embedding");
    }

    [Fact]
    public void ToQuery_CarriesEveryWording_WithItsWeight()
    {
        var plan = Compose("What about corn?", Branch("sugar cane in the Philippines?", "Negros."));
        float[] lead = [1f], own = [2f], screenshot = [3f];

        var query = plan.ToQuery([lead, own], screenshot, Settings());

        query.TextVector!.Value.ToArray().Should().Equal(lead);
        query.TextWeight.Should().Be(1d);
        query.ExtraTextVectors.Should().ContainSingle();
        query.ExtraTextVectors[0].Name.Should().Be(RetrievalQueryComposer.QuestionWording);
        query.ExtraTextVectors[0].Vector.ToArray().Should().Equal(own);
        query.ExtraTextVectors[0].Weight.Should().Be(0.5);
        query.BoostCountries.Should().Equal("philippines");
        query.Country.Should().BeNull("the boost filters nothing; only /ai search filters by country");
        query.Limit.Should().Be(Settings().ExcerptLimit);
    }

    [Fact]
    public void ToQuery_LetsANamedCountryOutweighAScreenshotsLookAlikes()
    {
        // "Is this bollard a meta in Australia?" with a screenshot: its pixels found US bollards.
        var named = Compose("Is this bollard a meta in Australia?", hasScreenshot: true)
            .ToQuery([new float[] { 1f }], new float[] { 2f }, Settings());
        var unnamed = Compose("Where is this and why?", hasScreenshot: true)
            .ToQuery([new float[] { 1f }], new float[] { 2f }, Settings());

        named.ImageWeight.Should().Be(Settings().CountryBoost.ScreenshotWeightWhenNamed);
        unnamed.ImageWeight.Should().Be(1d);
    }

    [Fact]
    public void ToQuery_DemotesHeadingOnlyPictures_ExceptForAScreenshot()
    {
        // For "where is this?" a look-alike captioned "Svalbard — Identifying Svalbard" says which
        // country it looks like, which is the whole question.
        var settings = Settings();
        settings.HeadingOnlyPictureWeight = 0.25;

        Compose("is red soil in malaysia regional").ToQuery([new float[] { 1f }], null, settings)
            .HeadingOnlyPictureWeight.Should().Be(0.25);
        Compose("Where is this and why?", hasScreenshot: true).ToQuery([new float[] { 1f }], new float[] { 2f }, settings)
            .HeadingOnlyPictureWeight.Should().Be(1d);
    }

    [Fact]
    public void ToQuery_RefusesVectorsThatDoNotMatchTheWordings()
    {
        var plan = Compose("What about corn?", Branch("sugar cane?", "Negros."));

        var act = () => plan.ToQuery([new float[] { 1f }], null, Settings());

        act.Should().Throw<ArgumentException>();
    }

    private static RetrievalSearchPlan Compose(
        string question,
        IReadOnlyList<ConversationTurnView>? turns = null,
        Action<AiRetrievalConfiguration>? configure = null,
        RetrievalPlan? plan = null,
        bool hasScreenshot = false)
    {
        var settings = Settings();
        configure?.Invoke(settings);
        return RetrievalQueryComposer.Compose(question, turns ?? [], hasScreenshot, plan, Gazetteer, settings);
    }

    private static AiRetrievalConfiguration Settings() => new() { FollowUpQuestionWeight = 0.5 };

    private static IReadOnlyList<ConversationTurnView> Branch(string question, string answer) =>
    [
        new(AiTurnRole.User, 1, question, []),
        new(AiTurnRole.Assistant, 2, answer, [])
    ];
}
