using FluentAssertions;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Utilities;

public sealed class DeterministicGuidTests
{
    [Fact]
    public void FromName_IsStableAcrossCalls()
    {
        // The whole point: re-ingesting unchanged content must land on the same id so the upsert
        // updates in place instead of appending a duplicate.
        var first = DeterministicGuid.FromName("point", "plonkit|tunisia|text|0/1chu");
        var second = DeterministicGuid.FromName("point", "plonkit|tunisia|text|0/1chu");

        first.Should().Be(second);
    }

    [Fact]
    public void FromName_IsPinnedToAKnownValue()
    {
        // Golden value: if this changes, every previously indexed id is orphaned and the store needs
        // a full rebuild. Failing here should prompt a deliberate decision, not a silent update.
        DeterministicGuid.FromName("point", "plonkit|tunisia|text|0/1chu")
            .ToString()
            .Should().Be("6facf95d-fb1b-57b6-99e2-cd5b9b285c80");
    }

    [Fact]
    public void FromName_DistinguishesScopeFromName()
    {
        // Without scope in the hash, "a"+"bc" and "ab"+"c" would collide.
        DeterministicGuid.FromName("a", "bc").Should().NotBe(DeterministicGuid.FromName("ab", "c"));
    }

    [Fact]
    public void FromName_ProducesAWellFormedVersion5Uuid()
    {
        var value = DeterministicGuid.FromName("point", "anything").ToString();

        value[14].Should().Be('5', "the version nibble must be stamped");
        "89ab".Should().Contain(value[19].ToString(), "the variant bits must be stamped");
    }

    [Fact]
    public void FromName_DiffersForDifferentNames()
    {
        DeterministicGuid.FromName("point", "one").Should().NotBe(DeterministicGuid.FromName("point", "two"));
    }
}
