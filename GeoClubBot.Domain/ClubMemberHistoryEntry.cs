using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Entities;

public record ClubMemberHistoryEntry(
    [property: Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    DateTimeOffset Timestamp,
    [property: ForeignKey(nameof(ClubMember))]
    string UserId,
    int Xp,
    ClubMember? ClubMember = null);