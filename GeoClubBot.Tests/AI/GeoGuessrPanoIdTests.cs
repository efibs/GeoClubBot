using FluentAssertions;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>GeoGuessr lists panorama ids hex-encoded (highscores, 2026-11-13); Google knows them decoded.</summary>
public sealed class GeoGuessrPanoIdTests
{
    [Theory]
    [InlineData("7470683379453331676366314D436E61756671565567", "tph3yE31gcf1MCnaufqVUg")]
    [InlineData("6B67594C57526C435672714E5773736C334833557077", "kgYLWRlCVrqNWssl3H3Upw")]
    [InlineData("tph3yE31gcf1MCnaufqVUg", "tph3yE31gcf1MCnaufqVUg")]
    [InlineData("CAoSLEFGMVFpcE5", "CAoSLEFGMVFpcE5")]
    [InlineData("00ff", "00ff")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void Decode_GivesGooglesId_AndLeavesWhatIsNoEncodingAlone(string? listed, string? expected) =>
        GeoGuessrPanoId.Decode(listed).Should().Be(expected);
}
