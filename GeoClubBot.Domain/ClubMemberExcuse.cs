using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[Index(nameof(To))]
public record ClubMemberExcuse(
    [property: Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    Guid Id,
    [property: ForeignKey(nameof(ClubMember))]
    string UserId,
    DateTimeOffset From, 
    DateTimeOffset To,
    ClubMember? ClubMember = null);