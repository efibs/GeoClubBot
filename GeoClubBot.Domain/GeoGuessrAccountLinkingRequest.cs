using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[PrimaryKey(nameof(DiscordUserId), nameof(GeoGuessrUserId))]
public class GeoGuessrAccountLinkingRequest
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong DiscordUserId { get; set; }
    
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    public string GeoGuessrUserId { get; set; } = string.Empty;

    [MaxLength(StringLengthConstants.AccountLinkingRequestOneTimePasswordLength)]
    public string OneTimePassword { get; set; } = string.Empty;
}