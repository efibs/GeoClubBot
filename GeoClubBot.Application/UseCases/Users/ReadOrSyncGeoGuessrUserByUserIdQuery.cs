using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
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
        // Tracker-aware lookup: a caller may sync several users in one unit of work (the daily
        // challenge resolves the podium of every difficulty), and a database-only read would miss a
        // user this same unit of work already synced. Adding them a second time throws an identity
        // conflict that leaves a detached zombie entry behind and poisons the whole SaveChanges.
        var existing = await users.ReadForUpdateByUserIdAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        UserDto dto;
        try
        {
            var client = geoGuessrClientFactory.CreateUserProfileClient();
            dto = await client.ReadUserAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // GeoGuessr API surfaces missing users as exceptions. Log the underlying cause and
            // return a typed NotFound so callers can distinguish "doesn't exist" from genuine errors.
            // Only the API call is guarded: a persistence failure must not be reported as not-found.
            LogUserLookupFailed(logger, ex, request.UserId);
            return Error.NotFound(
                "geoguessr_user.not_found",
                $"GeoGuessr user '{request.UserId}' could not be found.");
        }

        var created = GeoGuessrUser.Create(dto.Id, dto.Nick);
        users.AddUser(created);
        return created;
    }

    [LoggerMessage(LogLevel.Debug, "GeoGuessr user lookup for id '{userId}' failed; treating as not-found.")]
    static partial void LogUserLookupFailed(
        ILogger<ReadOrSyncGeoGuessrUserByUserIdHandler> logger,
        Exception exception,
        string userId);
}
