namespace Entities;

public record ClubMemberRelevantStrike(string MemberNickname, int NumActiveStrikes)
{
    public override string ToString()
    {
        return $"{MemberNickname}: {NumActiveStrikes} strike{(NumActiveStrikes > 1 ? 's' : "")}";
    }
}