using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[Index(nameof(Nickname))]
public class GeoGuessrUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    public string UserId { get; set; } = string.Empty;
    
    [MaxLength(StringLengthConstants.GeoGuessrPlayerNicknameMaxLength)]
    public string Nickname { get; set; } = string.Empty;

    public ulong? DiscordUserId { get; set; }
    
    public override string ToString()
    {
        return Nickname;
    }
}