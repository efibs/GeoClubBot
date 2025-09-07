using Entities;

namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface ICompleteAccountLinkingUseCase
{
    Task<(bool Successful, GeoGuessrUser? User)> CompleteLinkingAsync(ulong discordUserId, string geoGuessrUserId, string oneTimePassword);
}