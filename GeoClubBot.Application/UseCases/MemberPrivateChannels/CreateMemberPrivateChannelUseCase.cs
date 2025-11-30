using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

public class CreateMemberPrivateChannelUseCase(
    ICreateOrUpdateClubMemberUseCase createOrUpdateClubMemberUseCase,
    IDiscordTextChannelAccess discordTextChannelAccess, 
    IDiscordMessageAccess discordMessageAccess,
    IConfiguration config,
    ILogger<CreateMemberPrivateChannelUseCase> logger) 
    : ICreateMemberPrivateChannelUseCase
{
    public async Task<ulong?> CreatePrivateChannelAsync(ClubMember clubMember)
    {
        // Get the text channel name
        var textChannelName = $"{clubMember.User.Nickname.ToLowerInvariant()}-private-channel";
        
        // Create the text channel
        var textChannelId = await discordTextChannelAccess.CreatePrivateTextChannelAsync(_privateTextChannelCategoryId, 
                textChannelName, 
                _privateChannelsDescription,
                [clubMember.User.DiscordUserId!.Value],
                null)
            .ConfigureAwait(false);
        
        // If the creation failed
        if (textChannelId == null)
        {
            // Log warning
            logger.LogWarning($"Private text channel could not be created for club member '{clubMember.User.Nickname}'");
            return null;
        }
        
        // Send the welcome message
        await _sendWelcomeMessageAsync(clubMember, textChannelId.Value).ConfigureAwait(false);
        
        // Set the text channel id on the club member
        clubMember.PrivateTextChannelId = textChannelId;
        
        // Save the club member to the database
        await createOrUpdateClubMemberUseCase.CreateOrUpdateClubMemberAsync(clubMember).ConfigureAwait(false);
        
        return textChannelId;
    }

    private async Task _sendWelcomeMessageAsync(ClubMember clubMember, ulong textChannelId)
    {
        // Build the message body
        var messageBody = $"Welcome <@{clubMember.User.DiscordUserId!.Value}>! This is your " +
                          "private space to talk to our admins. Only you and the admins can see the messages in this " +
                          "text channel. Use this channel for example to talk about when you need an excuse for the " +
                          "club XP rule or any other concerns you might have.";
        
        // Send the message
        await discordMessageAccess.SendMessageAsync(messageBody, textChannelId).ConfigureAwait(false);
    }
    
    private readonly ulong _privateTextChannelCategoryId =
        config.GetValue<ulong>(ConfigKeys.MemberPrivateChannelsCategoryIdConfigurationKey);
    private readonly string _privateChannelsDescription = 
        config.GetValue<string>(ConfigKeys.MemberPrivateChannelsDescriptionConfigurationKey)!;
}