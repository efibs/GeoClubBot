namespace GeoClubBot.ApiProbe;

/// <summary>Command line of the probe. Every command is a GET; there is no verb to choose.</summary>
public sealed record ProbeArguments(
    string Command,
    string? Target,
    Guid? ClubId,
    int Limit,
    int Pages,
    string? OutputPath,
    bool NoCensus)
{
    public static ProbeArguments? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var command = args[0];
        string? target = null;
        Guid? clubId = null;
        var limit = 100;
        var pages = 1;
        string? outputPath = null;
        var noCensus = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--club":
                    if (!TryTakeValue(args, ref i, out var clubText) || !Guid.TryParse(clubText, out var parsedClub))
                    {
                        Console.Error.WriteLine("--club needs a GUID.");
                        return null;
                    }

                    clubId = parsedClub;
                    break;

                case "--limit":
                    if (!TryTakeValue(args, ref i, out var limitText) || !int.TryParse(limitText, out limit) || limit <= 0)
                    {
                        Console.Error.WriteLine("--limit needs a positive integer.");
                        return null;
                    }

                    break;

                case "--pages":
                    if (!TryTakeValue(args, ref i, out var pagesText) || !int.TryParse(pagesText, out pages) || pages <= 0)
                    {
                        Console.Error.WriteLine("--pages needs a positive integer.");
                        return null;
                    }

                    break;

                case "--out":
                    if (!TryTakeValue(args, ref i, out outputPath))
                    {
                        Console.Error.WriteLine("--out needs a file path.");
                        return null;
                    }

                    break;

                case "--no-census":
                    noCensus = true;
                    break;

                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                        return null;
                    }

                    target ??= args[i];
                    break;
            }
        }

        return new ProbeArguments(command, target, clubId, limit, pages, outputPath, noCensus);
    }

    public static void PrintUsage() =>
        Console.Error.WriteLine(
            """
            GeoClubBot.ApiProbe - read-only GeoGuessr API inspector.

            Usage:
              dotnet run --project Tools/GeoClubBot.ApiProbe -- <command> [options]

            Commands:
              activities            GET /v4/clubs/{club}/activities   (the club XP feed)
              missions              GET /v4/missions                  (today's daily missions)
              members               GET /v4/clubs/{club}/members
              club                  GET /v4/clubs/{club}
              user <userId>         GET /v3/users/{userId}
              raw <path>            GET https://www.geoguessr.com/api<path>

            Options:
              --club <guid>         Club to target (default: GEOGUESSR_CLUB_ID / appsettings.Local.json)
              --limit <n>           Page size for 'activities' (default 100)
              --pages <n>           Pages to follow for 'activities' (default 1)
              --out <file>          Also write the output to a file
              --no-census           Print raw JSON only, skip the field summary

            Credentials:
              GEOGUESSR_NCFA_TOKEN environment variable, or appsettings.Local.json beside the
              project. See README.md.

            This tool only ever issues GET requests - see README.md, "Read-only by construction".
            """);

    private static bool TryTakeValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        value = args[++index];
        return true;
    }
}
