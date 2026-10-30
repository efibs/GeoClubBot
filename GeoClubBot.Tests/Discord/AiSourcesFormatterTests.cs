using System.Text;
using Discord;
using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Ingestion;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// The admin views of the guide-source registry. Snapshot-tested like the other formatters, plus the one
/// limit that would otherwise only show on a live command: the listing must fit an embed however long
/// the stored reasons are.
/// </summary>
public sealed class AiSourcesFormatterTests
{
    // Fixed so the <t:...:R> timestamps in the snapshots stay deterministic.
    private static readonly DateTimeOffset Tried = new(2026, 10, 23, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task BuildListEmbed_ShowsEachSourcesReasonAndSchedule()
    {
        var listing = new KnowledgeSourceListing(
        [
            Summary("gdoc", "1Vcun64ZzIAE4tGyj7jL2jncjzRWokChomnMQ3-gUYTw", "Pakistan bollards",
                KnowledgeSourceStatus.Unavailable, "This document no longer exists (HTTP 410).", failures: 1,
                next: Tried.AddDays(30)),
            Summary("rmrg", "greece", "Greece",
                KnowledgeSourceStatus.Skipped, "This page carries no guide content.", failures: 0, next: null),
            Summary("plonkit", "tunisia", "Tunisia",
                KnowledgeSourceStatus.Failed, "Could not fetch this guide page (HTTP 503).", failures: 3,
                next: null, isDue: true)
        ], Total: 3);

        return Verify(RenderEmbed(AiSourcesFormatter.BuildListEmbed(listing, KnowledgeSourceListKind.Problems, null)));
    }

    [Fact]
    public Task BuildListEmbed_SaysSo_WhenNothingNeedsALook()
    {
        var listing = new KnowledgeSourceListing([], Total: 0);

        return Verify(RenderEmbed(AiSourcesFormatter.BuildListEmbed(listing, KnowledgeSourceListKind.Problems, "gdoc")));
    }

    [Fact]
    public void BuildListEmbed_StaysInsideDiscordsLimit_AndCountsWhatDidNotFit()
    {
        // A reason is the extractor's own message, stored up to 512 characters. Twenty-five of those would
        // be far past Discord's 4096 and fail the whole reply.
        var longReason = new string('x', 512);
        var sources = Enumerable.Range(0, 25)
            .Select(index => Summary("gdoc", $"doc-{index}", new string('T', 200),
                KnowledgeSourceStatus.Failed, longReason, failures: 2, next: Tried.AddDays(1)))
            .ToList();

        var embed = AiSourcesFormatter.BuildListEmbed(new KnowledgeSourceListing(sources, Total: 40),
            KnowledgeSourceListKind.Failed, null);

        embed.Description.Length.Should().BeLessThanOrEqualTo(4096);
        embed.Description.Should().Contain("of 40");
    }

    [Fact]
    public Task DescribeRun_NamesEachKindOfTrouble()
    {
        var report = new IngestionReport(
            Attempted: 12, Ingested: 2, Unchanged: 1, Failed: 1, Skipped: 1, ChunksWritten: 40,
            BudgetExhausted: false, RateLimited: false,
            Gone: 6, NotPublic: 1, TooLarge: 1, HostRefused: 3, Removed: 2);

        return Verify(AiSourcesFormatter.DescribeRun(report));
    }

    [Fact]
    public void DescribeCounts_MentionsUnlistedSourcesOnlyWhenThereAreAny()
    {
        AiSourcesFormatter.DescribeCounts(new KnowledgeSourceCounts(10, 1, 6, 1, 1, 1))
            .Should().Be("6 indexed · 1 pending · 1 failed · 1 skipped · 1 unavailable");

        AiSourcesFormatter.DescribeCounts(new KnowledgeSourceCounts(12, 1, 6, 1, 1, 1, Removed: 2))
            .Should().EndWith("· 2 no longer listed");
    }

    private static KnowledgeSourceSummary Summary(
        string type,
        string key,
        string title,
        KnowledgeSourceStatus status,
        string reason,
        int failures,
        DateTimeOffset? next,
        bool isDue = false) =>
        new(type, key, $"https://example.org/{type}/{key}", title, status, reason, failures, Tried, next, isDue,
            RemovedFromSyncAtUtc: null);

    private static string RenderEmbed(Embed embed) =>
        new StringBuilder()
            .AppendLine($"Title: {embed.Title}")
            .AppendLine("Description:")
            .AppendLine(embed.Description)
            .ToString();
}
