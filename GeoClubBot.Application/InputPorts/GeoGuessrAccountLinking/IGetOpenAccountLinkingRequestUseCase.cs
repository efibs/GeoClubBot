using Entities;

namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IGetOpenAccountLinkingRequestUseCase
{
    Task<GeoGuessrAccountLinkingRequest?> GetOpenAccountLinkingRequestForDiscordUserIdAsync(ulong discordUserId);
}