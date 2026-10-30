using FluentAssertions;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Every id, name and description below is verbatim from OpenRouter's free roster of 2026-10-23 —
/// descriptions as the API truncates them — so the phrasings pinned are the ones providers actually use.
/// </summary>
public sealed class ModelParameterCountParserTests
{
    [Theory]
    // Sizes in the id, a mixture of experts' active count included.
    [InlineData("nvidia/nemotron-3-ultra-550b-a55b:free", "NVIDIA: Nemotron 3 Ultra (free)",
        "NVIDIA Nemotron 3 Ultra is an open frontier-reasoning and orchestration model from NVIDIA, with 55B active parameters out of 550B total (MoE).",
        550d, 55d)]
    [InlineData("nvidia/nemotron-3-super-120b-a12b:free", "NVIDIA: Nemotron 3 Super (free)",
        "NVIDIA Nemotron 3 Super is a 120B-parameter open hybrid MoE model, activating just 12B parameters for maximum compute efficiency and accuracy in complex multi-agent applications.",
        120d, 12d)]
    [InlineData("google/gemma-4-31b-it:free", "Google: Gemma 4 31B (free)",
        "Gemma 4 31B Instruct is Google DeepMind's 30.7B dense multimodal model supporting text and image input with text output.",
        31d, null)]
    [InlineData("liquid/lfm-2.5-2.6b:free", "LiquidAI: LFM2.5-2.6B (free)",
        "LFM2.5-2.6B is a compact reasoning model from Liquid AI. It is suited for agent workflows, data extraction, RAG, and long-context processing.",
        2.6d, null)]
    [InlineData("xingchen-agi/xing4.0-29b-a4b:free", "XingChen: Xing4.0 29B A4B (free)",
        "Xing4.0-29B-A4B is an open-weight mixture-of-experts language model from China Telecom's AI team (TeleAI) and the successor to the TeleChat series.",
        29d, 4d)]
    [InlineData("google/gemma-4-26b-a4b-it:free", "Google: Gemma 4 26B A4B  (free)",
        "Gemma 4 26B A4B IT is an instruction-tuned Mixture-of-Experts (MoE) model from Google DeepMind. Despite 25.2B total parameters, only 3.8B activate per token during inference — delivering near-31B quality at...",
        26d, 4d)]
    [InlineData("nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free", "NVIDIA: Nemotron 3 Nano Omni (free)",
        "NVIDIA Nemotron™ 3 Nano Omni is a 30B-A3B open multimodal model designed to function as a perception and context sub-agent in enterprise agent systems.",
        30d, 3d)]
    // Sizes only in the prose.
    [InlineData("dots-studio/dots-3-note-preview:free", "Dots Studio: Dots3-Note Preview (free)",
        "Dots3-Note Preview is an open-weight mixture-of-experts model from Dots Studio, with 16B active parameters out of 280B total. It is the lightest model in the Dots 3 family and is...",
        280d, 16d)]
    [InlineData("thinkingmachines/inkling:free", "Thinking Machines: Inkling (free)",
        "Inkling is an open-weight multimodal mixture-of-experts model from Thinking Machines Lab, with 41B active parameters out of 975B total.",
        975d, 41d)]
    [InlineData("nvidia/nemotron-3.5-super-vl:free", "NVIDIA: Nemotron 3.5 Super VL (free)",
        "NVIDIA Nemotron 3.5 Super VL is an open multimodal mixture-of-experts model from NVIDIA, with 12B active parameters out of 120B total. It takes text, images, documents, GUI screenshots, and video...",
        120d, 12d)]
    [InlineData("recursive/flint-v1.0:free", "Recursive: Flint (free)",
        "Flint is an open-weight mixture-of-experts model from Recursive, with about 2.5B active parameters out of 40B total, built by Recursive's autoresearch system Eureka Machine for coding agents.",
        40d, 2.5d)]
    [InlineData("poolside/laguna-s-2.1:free", "Poolside: Laguna S 2.1 (free)",
        "Laguna S 2.1 is the latest coding agent model from [Poolside](<https://poolside.ai/>). Laguna S 2.1 is a 118B total parameter model with 8B active parameters, scoring 70.2% on Terminal-Bench 2.1 and...",
        118d, 8d)]
    [InlineData("poolside/laguna-xs-2.1:free", "Poolside: Laguna XS 2.1 (free)",
        "Laguna XS 2.1 is the latest coding agent model in the 33B-A3B category from [Poolside](https://poolside.ai/) and a step forward from their Laguna XS.2 model (released in April 2026).",
        33d, 3d)]
    [InlineData("cohere/north-mini-code:free", "Cohere: North Mini Code (free)",
        "North Mini Code is Cohere's first agentic coding model and the debut of its North family. A sparse mixture-of-experts model with 30B total parameters and 3B active, it is optimized...",
        30d, 3d)]
    [InlineData("nvidia/nemotron-3.5-content-safety:free", "NVIDIA: Nemotron 3.5 Content Safety (free)",
        "NVIDIA Nemotron 3.5 Content Safety is a compact 4B-parameter multimodal guardrail model from NVIDIA, fine-tuned from Google Gemma-3-4B.",
        4d, null)]
    [InlineData("nvidia/nemotron-3.5-lightning:free", "NVIDIA: Nemotron 3.5 Lightning (free)",
        "NVIDIA Nemotron 3.5 Lightning is an open mixture-of-experts model from NVIDIA, with 3B active parameters out of 30B total.",
        30d, 3d)]
    public void Parse_ReadsTheSizeEachProviderStates(string id, string name, string description, double total, double? active)
    {
        var size = ModelParameterCountParser.Parse(id, name, description);

        size.TotalBillions.Should().Be(total);
        size.ActiveBillions.Should().Be(active);
    }

    [Theory]
    [InlineData("google/lyria-3-pro-preview", "Google: Lyria 3 Pro Preview",
        "Full-length songs are priced at $0.08 per song. Lyria 3 is Google's family of music generation models, available through the Gemini API. With Lyria 3, you can generate high-quality, 48kHz...")]
    [InlineData("openrouter/free", "Free Models Router",
        "The simplest way to get free inference. openrouter/free is a router that selects free models at random from the models available on OpenRouter.")]
    public void Parse_LeavesTheSizeUnknown_WhenNothingStatesIt(string id, string name, string description)
    {
        // Unknown ranks in the middle; a misread size would rank the model wrongly.
        var size = ModelParameterCountParser.Parse(id, name, description);

        size.TotalBillions.Should().BeNull();
        size.ActiveBillions.Should().BeNull();
    }

    [Theory]
    [InlineData("mistralai/mixtral-8x7b-instruct")]
    [InlineData("some/model-v2.5b")]
    [InlineData("acme1b/model:free")]
    [InlineData("acme/model:beta-7b")]
    public void Parse_IgnoresWhatOnlyLooksLikeASize(string id) =>
        // Expert counts, versions, an author or a variant that happens to end in a digit and a "b".
        ModelParameterCountParser.Parse(id, name: null, description: null).TotalBillions.Should().BeNull();

    [Fact]
    public void Parse_PrefersTheIdOverComparisonsInTheProse()
    {
        // Prose names other models too — "near-31B quality", "outperforms 70B dense models" — while the id
        // is written for exactly this.
        var size = ModelParameterCountParser.Parse(
            "acme/widget-12b:free", "Acme: Widget", "Widget beats many 70B dense models at a fraction of their size.");

        size.TotalBillions.Should().Be(12);
    }

    [Fact]
    public void Parse_CompletesAnIdsTotal_WithTheActiveCountTheProseStates()
    {
        var size = ModelParameterCountParser.Parse(
            "openai/gpt-oss-120b:free", "OpenAI: gpt-oss-120b", "A 117B-parameter mixture of experts that activates 5.1B parameters per token.");

        size.TotalBillions.Should().Be(120);
        size.ActiveBillions.Should().Be(5.1);
    }

    [Theory]
    // A dense id, and prose that compares with a mixture of experts. Read as 3B active, the 14B model
    // would count as 6.5B and fall below the selector's 8B bar.
    [InlineData("acme/foo-14b-instruct:free", "Acme: Foo 14B (free)",
        "Foo 14B is a dense model that outperforms Qwen3-30B-A3B on most benchmarks.", 14d)]
    [InlineData("acme/foo-24b:free", "Acme: Foo 24B (free)",
        "Foo 24B rivals MoE models with 3B active parameters at a fraction of their latency.", 24d)]
    // A comparison stated like a size of its own, but for a different total.
    [InlineData("acme/foo-14b:free", "Acme: Foo 14B (free)",
        "Foo 14B beats every model in the 30B-A3B class.", 14d)]
    // A comparison close to the model's own size, named as a model.
    [InlineData("acme/foo-32b:free", "Acme: Foo 32B (free)",
        "Foo 32B outperforms Qwen3-30B-A3B and Gemma-3-27B.", 32d)]
    // No size in the id: the prose's own total, then the comparison.
    [InlineData("acme/foo:free", "Acme: Foo (free)",
        "Foo is a 14B-parameter model that outperforms Qwen3-30B-A3B.", 14d)]
    [InlineData("acme/foo:free", "Acme: Foo (free)",
        "Foo is a 14B-parameter dense model that beats MoE models with 3B active parameters.", 14d)]
    public void Parse_TakesNoActiveCount_FromAComparisonWithAnotherModel(
        string id, string name, string description, double total)
    {
        var size = ModelParameterCountParser.Parse(id, name, description);

        size.TotalBillions.Should().Be(total);
        size.ActiveBillions.Should().BeNull();
    }

    [Fact]
    public void Parse_LeavesTheSizeUnknown_WhenTheProseStatesAnActiveCountWithoutATotal()
    {
        // "Activating 3B" with no total beside it reads the same as "rivals models activating 3B".
        var size = ModelParameterCountParser.Parse(
            "acme/foo:free", "Acme: Foo (free)", "Foo matches models activating just 3B parameters per token.");

        size.TotalBillions.Should().BeNull();
        size.ActiveBillions.Should().BeNull();
    }

    [Fact]
    public void Parse_ReadsAnActiveCountStatedBeforeTheWordParameters_AsActive()
    {
        var size = ModelParameterCountParser.Parse(
            "acme/foo:free", "Acme: Foo (free)", "Foo is a mixture of experts with 12B parameters active out of 120B total.");

        size.TotalBillions.Should().Be(120);
        size.ActiveBillions.Should().Be(12);
    }

    [Theory]
    [InlineData("A 1 trillion total parameter model with 32 billion active parameters.", 1000d, 32d)]
    [InlineData("A 1T total parameter model with 32B active parameters.", 1000d, 32d)]
    // Hyphenated, as in "a 30-billion-parameter model". Missed, the 3B active count stood alone and held
    // the model to the size bar as a 3B one.
    [InlineData("Acme MoE is a 30-billion-parameter mixture-of-experts model with 3 billion active parameters.", 30d, 3d)]
    public void Parse_ReadsTrillionsAndSpelledOutUnits(string description, double total, double active)
    {
        var size = ModelParameterCountParser.Parse("moonshotai/kimi-k2:free", "MoonshotAI: Kimi K2", description);

        size.TotalBillions.Should().Be(total);
        size.ActiveBillions.Should().Be(active);
    }

    [Fact]
    public void Parse_ReadsMillionsInAnId() =>
        ModelParameterCountParser.Parse("liquid/lfm2-350m:free", null, null).TotalBillions.Should().Be(0.35);
}
