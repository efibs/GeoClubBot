using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[Index(nameof(Nickname))]
public record ClubMember(
    [property: Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    string UserId,
    [property: ForeignKey(nameof(Club))]
    Guid ClubId,
    string Nickname,
    Club? Club = null,
    List<ClubMemberHistoryEntry>? History = null,
    List<ClubMemberStrike>? Strikes = null,
    List<ClubMemberExcuse>? Excuses = null);