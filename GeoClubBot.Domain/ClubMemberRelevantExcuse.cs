using Utilities;

namespace Entities;

public record ClubMemberRelevantExcuse(string MemberNickname, TimeRange ExcuseTimeRange, bool IsUpcoming)
{
    public override string ToString()
    {
        return IsUpcoming
            ? $"{MemberNickname} ({ExcuseTimeRange.ToString("yyyy-MM-dd")})"
            : $"{MemberNickname} until {ExcuseTimeRange.To:yyyy-MM-dd}";
    }
}
