using Configuration;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Tests.TestBuilders;

public sealed class GeoGuessrConfigurationBuilder
{
    private readonly List<GeoGuessrClubEntry> _clubs = [];

    public GeoGuessrConfigurationBuilder WithClub(
        Guid clubId,
        bool isMain = true,
        int? minXp = null,
        int? gracePeriodDays = null,
        int? maxNumStrikes = null,
        ulong? roleId = null)
    {
        _clubs.Add(new GeoGuessrClubEntry
        {
            ClubId = clubId,
            NcfaToken = "ncfa-test",
            IsMain = isMain,
            MinXP = minXp,
            GracePeriodDays = gracePeriodDays,
            MaxNumStrikes = maxNumStrikes,
            RoleId = roleId
        });
        return this;
    }

    public IOptions<GeoGuessrConfiguration> BuildOptions()
    {
        var config = new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 * * * ?",
            ActivityNcfaToken = "activity-token",
            MissionsNcfaToken = "missions-token",
            UserProfileNcfaToken = "userprofile-token",
            Clubs = _clubs
        };
        return Options.Create(config);
    }
}

public sealed class ActivityCheckerConfigurationBuilder
{
    private int _minXp = 100;
    private int _gracePeriodDays = 7;
    private int _maxNumStrikes = 3;
    private int? _averageXpTopN;
    private int? _averageXpBottomN;
    private int _averageXpHistoryDepth = 4;

    public ActivityCheckerConfigurationBuilder WithMinXp(int minXp)
    {
        _minXp = minXp;
        return this;
    }

    public ActivityCheckerConfigurationBuilder WithGracePeriodDays(int days)
    {
        _gracePeriodDays = days;
        return this;
    }

    public ActivityCheckerConfigurationBuilder WithMaxNumStrikes(int maxNumStrikes)
    {
        _maxNumStrikes = maxNumStrikes;
        return this;
    }

    public ActivityCheckerConfigurationBuilder WithAverageXpTopN(int topN, int historyDepth = 4)
    {
        _averageXpTopN = topN;
        _averageXpHistoryDepth = historyDepth;
        return this;
    }

    public ActivityCheckerConfigurationBuilder WithAverageXpBottomN(int bottomN, int historyDepth = 4)
    {
        _averageXpBottomN = bottomN;
        _averageXpHistoryDepth = historyDepth;
        return this;
    }

    public IOptions<ActivityCheckerConfiguration> BuildOptions()
    {
        var config = new ActivityCheckerConfiguration
        {
            Schedule = "0 0 * * * ?",
            TextChannelId = 1234UL,
            MinXP = _minXp,
            GracePeriodDays = _gracePeriodDays,
            MaxNumStrikes = _maxNumStrikes,
            HistoryKeepTimeSpan = "180.00:00:00",
            StrikeDecayTimeSpan = "60.00:00:00",
            AverageXpTopN = _averageXpTopN,
            AverageXpBottomN = _averageXpBottomN,
            AverageXpHistoryDepth = _averageXpHistoryDepth
        };
        return Options.Create(config);
    }
}
