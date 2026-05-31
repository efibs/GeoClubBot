using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.GeoGuessr;
using Utilities;

namespace UseCases.UseCases.Users;

public sealed record ReadOrSyncGeoGuessrUserByUserIdQuery(string UserId) : IQuery<Result<GeoGuessrUser>>;

public sealed partial class ReadOrSyncGeoGuessrUserByUserIdHandler(
    IGeoGuessrUserRepository users,
    IGeoGuessrClientFactory geoGuessrClientFactory,
    ILogger<ReadOrSyncGeoGuessrUserByUserIdHandler> logger)
    : IRequestHandler<ReadOrSyncGeoGuessrUserByUserIdQuery, Result<GeoGuessrUser>>
{
    public async Task<Result<GeoGuessrUser>> Handle(ReadOrSyncGeoGuessrUserByUserIdQuery request, CancellationToken cancellationToken)
    {
        var existing = await users.ReadUserByUserIdAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var client = geoGuessrClientFactory.CreateUserProfileClient();
            var dto = await client.ReadUserAsync(request.UserId, cancellationToken).ConfigureAwait(false);

            var created = GeoGuessrUser.Create(dto.Id, dto.Nick);
            users.AddUser(created);
            return created;
        }
        catch (Exception ex)
        {
            // GeoGuessr API surfaces missing users as exceptions. Log the underlying cause and
            // return a typed NotFound so callers can distinguish "doesn't exist" from genuine errors.
            LogUserLookupFailed(logger, ex, request.UserId);
            return Error.NotFound(
                "geoguessr_user.not_found",
                $"GeoGuessr user '{request.UserId}' could not be found.");
        }
    }

    [LoggerMessage(LogLevel.Debug, "GeoGuessr user lookup for id '{userId}' failed; treating as not-found.")]
    static partial void LogUserLookupFailed(
        ILogger<ReadOrSyncGeoGuessrUserByUserIdHandler> logger,
        Exception exception,
        string userId);
}
