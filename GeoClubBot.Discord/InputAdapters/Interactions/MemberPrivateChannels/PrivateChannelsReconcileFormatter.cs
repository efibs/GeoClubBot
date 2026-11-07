using System.Text;
using GeoClubBot.Discord.InputAdapters.Interactions.CountryChallenges;
using UseCases.UseCases.MemberPrivateChannels;

namespace GeoClubBot.Discord.InputAdapters.Interactions.MemberPrivateChannels;

public static class PrivateChannelsReconcileFormatter
{
    public static IReadOnlyList<string> Report(PrivateChannelReconcileReport report)
    {
        var builder = new StringBuilder(report.DryRun
            ? "## Private channels — dry run (nothing was changed)\n"
            : "## Private channels reconciled\n");

        if (report.NothingToDo)
        {
            builder.AppendLine("Every private channel is where it belongs, with the right permissions.");
            return CountryChallengesFormatter.Split([builder.ToString()]);
        }

        var verb = report.DryRun ? "Would" : "Did";

        foreach (var (repair, nicknames) in report.Repairs)
        {
            builder.AppendLine($"- {verb} {Describe(repair)} ({nicknames.Count}): {string.Join(", ", nicknames)}");
        }

        if (report.AccessFixed.Count > 0)
        {
            builder.AppendLine(
                $"- {verb} fix the permissions of {report.AccessFixed.Count} channel(s): {string.Join(", ", report.AccessFixed)}");
        }

        foreach (var (club, count) in report.CategoriesToCreate)
        {
            builder.AppendLine(report.DryRun
                ? $"- Would create {Categories(count)} for {club}."
                : $"- Created {Categories(count)} for {club}.");
        }

        if (report.CategoryAccessFixed > 0)
        {
            builder.AppendLine($"- Fixed the permissions of {Categories(report.CategoryAccessFixed)}.");
        }

        if (report.CategoriesPruned > 0)
        {
            builder.AppendLine($"- Deleted {Categories(report.CategoriesPruned)} that were empty.");
        }

        foreach (var emptied in report.EmptiedCategoryIds)
        {
            builder.AppendLine($":information_source: <#{emptied}> is empty now; delete it by hand if it is no longer needed.");
        }

        if (report.Failures.Count > 0)
        {
            builder.AppendLine($":x: {report.Failures.Count} failed (retried at the next reconcile):");
            foreach (var failure in report.Failures)
            {
                builder.AppendLine($"  - {failure}");
            }
        }

        return CountryChallengesFormatter.Split([builder.ToString()]);
    }

    private static string Categories(int count) => count == 1 ? "1 category" : $"{count} categories";

    private static string Describe(PrivateChannelRepair repair) => repair switch
    {
        PrivateChannelRepair.Create => "create the missing channel",
        PrivateChannelRepair.Restore => "restore the archived channel",
        PrivateChannelRepair.MoveToClub => "move the channel into the club's category",
        PrivateChannelRepair.Archive => "archive the channel of a member in no club",
        PrivateChannelRepair.MoveToArchive => "move the archived channel back into the archive",
        PrivateChannelRepair.Forget => "forget the deleted channel",
        _ => repair.ToString()
    };
}
