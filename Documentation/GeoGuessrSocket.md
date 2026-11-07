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
| `ChatMessage` on `chat:Club:TextMessages:<clubId>` | a chat message (`textPayload`, `id`, `sourceId`, …) | confirms a forward (see below) |
| role change, presence, other chat | — | ignored |

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

## Forwarding Discord messages into the club chat

`ClubChatForwarding` (off by default; per club `ForwardChat`) adds the message command
"📢 Send to club chat". The bot writes as each club's `NcfaToken` account with one frame:

```json
{"code":"ChatMessage","topic":"chat:Club:TextMessages:<clubId>","payload":"<text>","client":"web"}
```

The payload is the bare text, not a JSON document. GeoGuessrAccountShadowBot observed this frame
for direct messages (`chat:Friend:TextMessages:<peerId>`); **the club topic comes from GeoGuessr's
client code and has not been seen live**. It also observed that GeoGuessr **silently cuts a message
to 300 characters** and stores everything else unchanged, and that every connection subscribed to a
chat gets its new messages back within about 40 ms, the sender's own included.

That echo is what confirms a forward:

- `GeoGuessrClubChatSender` subscribes the chat topic (once per socket, lazily: a club nobody forwards
  to is never subscribed), sends the first part and waits up to `EchoTimeout` for it to come back
  before sending the next. A part not echoed stops that club's forward and is reported as "may have
  arrived": nothing is ever resent, since a second copy is worse than a report.
- It uses the club's live socket when `ClubChatChannels` has one (the supervisor attaches it while
  connected and hands it the chat frames it reads). Otherwise, the socket feature off for that club
  included, it opens a connection only for the forward and closes it afterwards.
- A `SubscribeDenied` for the chat topic only fails the forward; it never counts as the club's own
  topic being denied, so the club's socket stays up.
- The text is split by `ClubChatTextSplitter` into numbered parts (`(1/3) …`, at paragraphs, lines,
  sentences, then words) of at most `MaxMessageLength`, up to `MaxParts`. Each club's forward is
  stored in `ClubChatForwards`, which the form reads to warn before the same message goes out again.
- Metric `geoclubbot.geoguessr_socket.chat_forwards` (by `status` and `socket` = `live`/`own`).

`Tools/GeoClubBot.ApiProbe`'s `ReadOnlySocketGuard` deliberately still refuses chat frames: the probe
never acts in a club. The first real forward to one club is the live check of the topic: if GeoGuessr
denies it or never echoes, the admin's report says so and the log has a warning.

In the mock, the club view's *Chat* card lists what was written into the club chat (cut at 300) and can
drop the echoes to show an unconfirmed forward.
