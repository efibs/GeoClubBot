using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[Index(nameof(Timestamp), IsUnique = true)]
public record ClubMemberStrike(
    [property: Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    Guid StrikeId,
    [property: ForeignKey(nameof(ClubMember))]
    string UserId,
    DateTimeOffset Timestamp,
    bool Revoked,
    ClubMember? ClubMember = null);