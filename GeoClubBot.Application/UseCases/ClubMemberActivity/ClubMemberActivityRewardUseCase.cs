using System.Text;
using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity;

public class ClubMemberActivityRewardUseCase(IGeoGuessrUserIdsToDiscordUserIdsUseCase geoGuessrUserIdsToDiscordUserIdsUseCase,
    IServerRolesAccess serverRolesAccess, 
    IMessageSender messageSender, 
    IConfiguration config, 
    ILogger<ClubMemberActivityRewardUseCase> logger) : IClubMemberActivityRewardUseCase
{
    public async Task RewardMemberActivityAsync(List<ClubMemberActivityStatus> statuses)
    {
        logger.LogDebug("Starting reward member activity");
        
        // Group statuses by xp and then sort by xp to get the leaderboard
        var leaderboard = statuses
            .Where(s => s.XpSinceLastUpdate > 0)
            .GroupBy(s => s.XpSinceLastUpdate)
            .OrderByDescending(g => g.Key)
            .Take(3)
            .ToList();
        
        // If there are no players to mention
        if (leaderboard.Count == 0)
        {
            return;
        }
        
        // The builder for the message
        var msgBuilder = new StringBuilder("# The new MVP's of the club are here! :partying_face:\n");
        msgBuilder.AppendLine("The club members that achieved the most club XP since last time are:");

        var place = 1;
        IEnumerable<string> mvpPlayerUserIds = [];
        foreach (var group in leaderboard)
        {
            msgBuilder.Append("**#");
            msgBuilder.Append(place);
            msgBuilder.Append(":** With ");
            msgBuilder.Append(group.Key);
            msgBuilder.Append("XP: ");
            msgBuilder.Append(string.Join(", ", group.Select(s => s.Nickname)));

            if (place == 1)
            {
                msgBuilder.Append(group.Count() > 1 
                    ? " (Our new MVP's) :clap: " 
                    : " (Our new MVP) :clap: ");
                
                mvpPlayerUserIds = group.Select(s => s.UserId);
            }
            
            msgBuilder.AppendLine();
            
            place++;
        }
        
        // Build the message
        var msg = msgBuilder.ToString();
        
        // Send the message
        await messageSender.SendMessageAsync(msg, _channelId).ConfigureAwait(false);
        
        // Get the discord user ids for the mvps
        var discordUserIds = await geoGuessrUserIdsToDiscordUserIdsUseCase.GetDiscordUserIdsAsync(mvpPlayerUserIds).ConfigureAwait(false);
        
        // Distribute the roles
        await serverRolesAccess.AddRoleToMembersByUserIdsAsync(discordUserIds, _mvpRoleId).ConfigureAwait(false);
    }
    
    private readonly string _channelId = config.GetValue<string>(ConfigKeys.ActivityRewardTextChannelIdConfigurationKey)!;
    private readonly ulong _mvpRoleId = config.GetValue<ulong>(ConfigKeys.ActivityRewardMvpRoleIdConfigurationKey);
}