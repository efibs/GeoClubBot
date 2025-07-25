using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Entities;

public record Club(
    [property: Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    Guid ClubId,
    string Name,
    int Level,
    DateTimeOffset? LatestActivityCheckTime = null,
    List<ClubMember>? Members = null);