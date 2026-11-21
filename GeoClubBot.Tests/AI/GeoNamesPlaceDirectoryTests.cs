using System.IO.Compression;
using System.Text;
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

    [Fact]
    public async Task ReadsTheTownsArchive_AndKeepsIt()
    {
        var path = WriteArchive(Rows, damage: false);
        try
        {
            var towns = await GeoNamesPlaceDirectory.ReadTownsArchiveAsync(path);

            towns.Select(town => town.Name).Should().Equal("Boston", "Ho Chi Minh City", "Braintree");
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DeletesADamagedArchive_SoTheNextTryDownloadsItAgain()
    {
        // One byte of the stored towns changed: the zip's structure is intact, only the entry's CRC-32
        // no longer matches, which .NET 11 checks as the entry is read.
        var path = WriteArchive(Rows, damage: true);
        try
        {
            var read = () => GeoNamesPlaceDirectory.ReadTownsArchiveAsync(path);

            await read.Should().ThrowAsync<InvalidDataException>();
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteArchive(string rows, bool damage)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("cities15000.txt", CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(rows));
        }

        var bytes = buffer.ToArray();
        if (damage)
        {
            var at = bytes.AsSpan().IndexOf("Braintree"u8);
            at.Should().BePositive("the towns are stored uncompressed");
            bytes[at] = (byte)'X';
        }

        var path = Path.Combine(Path.GetTempPath(), $"towns-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
