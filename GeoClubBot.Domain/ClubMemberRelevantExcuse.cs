using Utilities;

namespace Entities;

public record ClubMemberRelevantExcuse(string MemberNickname, TimeRange ExcuseTimeRange, bool IsUpcoming, bool IsPrevious = false)
{
    public override string ToString()
    {
        if (IsPrevious)
            return $"{MemberNickname} ({ExcuseTimeRange.ToString("yyyy-MM-dd")})";
        return IsUpcoming
            ? $"{MemberNickname} ({ExcuseTimeRange.ToString("yyyy-MM-dd")})"
            : $"{MemberNickname} until {ExcuseTimeRange.To:yyyy-MM-dd}";
    }
}
