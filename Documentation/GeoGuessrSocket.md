# GeoGuessr club socket

The bot holds GeoGuessr's WebSocket (`wss://api.geoguessr.com/ws`) once per configured club, with
that club's `NcfaToken`, and subscribes to `club:<clubId>`. Joins, leaves, club updates and mission
board changes then arrive within seconds instead of at the next scheduled job. Settings:
`GeoGuessrSocket` in `appsettings.json`; per club `UseSocket` and `SocketPollWhileDisconnected`.

## What is known, and how

The protocol was first observed by GeoGuessrAccountShadowBot (its `Documentation/Protocol.md`).
**The club frames have not been seen live**; they come from GeoGuessr's client code:

| Code | Payload (from the client) | The bot |
|---|---|---|
| `ClubMemberJoined` | an item of `/v4/clubs/{id}/members` | stores the join from it (needs `user.userId`, `user.nick`, `xp`, `joinedAt`) |
| `ClubMemberLeft` | `{userId}` | clears the member if stored in that club |
| `ClubUpdated` | partial club | applies `level` / `name` if present, else nothing |
| `ClubMissionBoardUpdated` | none | drops the cached board, records that club's claims |
| role change, presence, chat | — | ignored |

A member payload lacking any needed field becomes a full roster sync (warning once per code,
metric `geoclubbot.geoguessr_socket.payload_fallbacks`). Every distinct payload field set is logged
once at Information. Verify with `dotnet run --project Tools/GeoClubBot.ApiProbe -- socket` (see its
README). `UsePayloads=false` turns every member event into a full sync.

## How it fits together

- `Infrastructure/InputAdapters/GeoGuessrSocket/`: `ClientWebSocketConnector`/`Session` (ported),
  `SocketConnectionPolicy` (pure: backoff, 1012/4100/4200/1001/401/403/429), `ClubFrameDecoder`,
  `ClubConnectionAlerter`, `ClubSocketSupervisor` (one per club), `ClubEventBatcher`,
  `GeoGuessrSocketService` (hosted; stripped by the E2E host).
- `Application/UseCases/ClubEvents/`: `ClubEventPlanner` (pure) folds a batch; `ApplyClubEventsCommand`
  applies it.
- **Batching**: events wait `BatchWindow` (10 s) from the first one. A switch arrives as a leave on
  one club's socket and a join on the other's, in either order; per user the planner keeps where
  they end up (a leave of a club they are no longer in is the older half of a switch). Any refresh
  or unusable payload makes the batch a full `SyncClubsCommand` (all clubs, so switches still merge).
- **Catch-up**: every (re)connect asks for a refresh of the club (roster, board, level). A club
  without a live socket — down or switched off — is refreshed every `PollWhileDisconnected` (1 h).
  The cron jobs (roster sync, level check, claim recorder) run every 6 h as the safety net.
- **Serialisation**: `IClubMemberSyncGate` is held from a roster read to the end of its
  `SaveClubMembersCommand` by every membership writer; it refuses re-entry. Claim recording and
  level changes lock per club.
- **Alerting is logging**: warnings reach the Discord log channel. Reconnects are Information; an
  outage longer than `AlertAfter` a Warning; a rejected token or denied topic an Error; each kind
  repeats at most once per its interval.
- **Mock**: with `GeoGuessr:UseMock` the sockets open on `MockClubSocketHub`; the mock UI's changes
  publish the frames above, and its club view can drop the socket or send any frame.
