using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Users;

public sealed record ReadOrSyncGeoGuessrUserByUserIdQuery(string UserId) : IQuery<GeoGuessrUser?>;

public sealed class ReadOrSyncGeoGuessrUserByUserIdHandler(
    IGeoGuessrUserRepository users,
    IGeoGuessrClientFactory geoGuessrClientFactory)
    : IRequestHandler<ReadOrSyncGeoGuessrUserByUserIdQuery, GeoGuessrUser?>
{
    public async Task<GeoGuessrUser?> Handle(ReadOrSyncGeoGuessrUserByUserIdQuery request, CancellationToken cancellationToken)
    {
        var existing = await users.ReadUserByUserIdAsync(request.UserId).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var client = geoGuessrClientFactory.CreateUserProfileClient();
            var dto = await client.ReadUserAsync(request.UserId).ConfigureAwait(false);

            var created = GeoGuessrUser.Create(dto.Id, dto.Nick);
            users.AddUser(created);
            return created;
        }
        catch
        {
            // GeoGuessr API surfaces missing users as exceptions; swallow and return null.
            return null;
        }
    }
}
