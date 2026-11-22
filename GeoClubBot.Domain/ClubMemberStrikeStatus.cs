namespace Entities;

public record ClubMemberStrikeStatus(string MemberNickname, int NumActiveStrikes, List<ClubMemberStrike> Strikes);
