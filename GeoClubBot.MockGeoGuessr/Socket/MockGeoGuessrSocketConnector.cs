using Configuration;
using Infrastructure.InputAdapters.GeoGuessrSocket;

namespace GeoClubBot.MockGeoGuessr.Socket;

/// <summary>Replaces the real connector when <c>GeoGuessr:UseMock</c> is on: sessions open on <see cref="MockClubSocketHub"/>.</summary>
public sealed class MockGeoGuessrSocketConnector(MockClubSocketHub hub) : IGeoGuessrSocketConnector
{
    public Task<IGeoGuessrSocketSession> ConnectAsync(GeoGuessrClubEntry club, bool afterServerRestart, CancellationToken cancellationToken) =>
        Task.FromResult<IGeoGuessrSocketSession>(hub.Open());
}
