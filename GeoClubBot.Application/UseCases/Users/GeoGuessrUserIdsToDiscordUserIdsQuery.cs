using MediatR;
using UseCases.Abstractions;

namespace UseCases.UseCases.Users;

public sealed record GeoGuessrUserIdsToDiscordUserIdsQuery(IEnumerable<string> GeoGuessrUserIds) : IQuery<List<ulong>>;

public sealed class GeoGuessrUserIdsToDiscordUserIdsHandler(ISender mediator)
    : IRequestHandler<GeoGuessrUserIdsToDiscordUserIdsQuery, List<ulong>>
{
    public async Task<List<ulong>> Handle(GeoGuessrUserIdsToDiscordUserIdsQuery request, CancellationToken cancellationToken)
    {
        var discordUserIds = new List<ulong>();

        foreach (var userId in request.GeoGuessrUserIds)
        {
            var result = await mediator
                .Send(new ReadOrSyncGeoGuessrUserByUserIdQuery(userId), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess && result.Value.DiscordUserId is not null)
            {
                discordUserIds.Add(result.Value.DiscordUserId.Value);
            }
        }

        return discordUserIds;
    }
}
