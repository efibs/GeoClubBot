using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Regions;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// GeoNames' towns file, read as the place directory reads it: rows as GeoNames writes them (trimmed
/// from cities15000.txt, 2026-11-15), other names kept for large towns only.
/// </summary>
public sealed class GeoNamesPlaceDirectoryTests
{
    private const string Rows =
        "4930956\tBoston\tBoston\tBOS,Baston,Bostonas,Bostono,Bostun,Boston\t42.35843\t-71.05977\tP\tPPLA\tUS\t\tMA\t025\t\t\t653833\t43\t14\tAmerica/New_York\t2024-01-01\n"
        + "1566083\tHo Chi Minh City\tHo Chi Minh City\tHCM City,HCMC,Saigon,Sai Gon,Thanh pho Ho Chi Minh,胡志明市\t10.82302\t106.62965\tP\tPPLA\tVN\t\t20\t\t\t\t3467331\t\t9\tAsia/Ho_Chi_Minh\t2024-01-01\n"
        + "4945283\tBraintree\tBraintree\tBraintree Town,Brejntri\t42.20384\t-71.00215\tP\tPPL\tUS\t\tMA\t021\t\t\t37297\t\t30\tAmerica/New_York\t2024-01-01\n"
        + "1\tBroken\trow\n"
        + "2\tNowhere\tNowhere\t\tnot-a-number\t0\tP\tPPL\tXX\t\t\t\t\t\t20000\t\t\t\t\n";

    [Fact]
    public void ReadsTheTowns_WithTheirStateAndPopulation_SkippingMalformedRows()
    {
        var towns = GeoNamesPlaceDirectory.ParseTowns(new StringReader(Rows));

        towns.Select(town => town.Name).Should().Equal("Boston", "Ho Chi Minh City", "Braintree");
        var boston = towns[0];
        boston.CountryCode.Should().Be("US");
        boston.Admin1.Should().Be("MA");
        boston.Latitude.Should().Be(42.35843);
        boston.Population.Should().Be(653_833);
    }

    [Fact]
    public void KeepsOtherNames_OfLargeTownsOnly_InLatinLetters_AndNotCodes()
    {
        var towns = GeoNamesPlaceDirectory.ParseTowns(new StringReader(Rows));

        towns[0].Aliases.Should().BeEquivalentTo(["Baston", "Bostonas", "Bostono", "Bostun"], "BOS is a code, Boston the name itself");
        towns[1].Aliases.Should().Contain(["Saigon", "Sai Gon", "HCM City"]).And.NotContain("胡志明市").And.NotContain("HCMC").And.NotContain("Thanh pho Ho Chi Minh", "five words");
        towns[2].Aliases.Should().BeEmpty("a town of 37,297 keeps its own name alone");
    }
}
