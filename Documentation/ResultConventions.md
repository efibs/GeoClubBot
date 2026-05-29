# Result<T> conventions

`Result<T>` (and its unit-bearing sibling `Result`) is the canonical way to surface
expected failure modes from application-layer handlers. This note pins the conventions
that the Phase-2 rollout settled on, so future contributors don't re-litigate them.

## When to use `Result<T>` vs `T?`

Use `Result<T>` when a handler can fail for a **named, meaningful reason** that callers
must be able to react to differently — a not-found, a conflict, a validation rejection,
a sync failure with an upstream API. Adopt it whenever you'd otherwise want to:

- Throw and `catch` to map to a different user-facing message
- Return a custom tuple/record with a `Successful` bool + a payload
- Communicate "the upstream said no" vs. "the upstream had a structured error"

Use `T?` only when `null` (or an empty list) represents a **legitimately optional read**:

- "No reminder is set for this user" — `GetDailyMissionReminderStatusQuery` returns
  `DomainDailyMissionReminder?` because absence is the steady-state for most users
- "This club's average XP rollup is empty" — `List<T>` with zero items already
  communicates that without needing a wrapper
- Static-shape queries that just project storage (`ClubStatistics?` where `null`
  means "main club row absent"). Borderline; lean toward `Result<T>` if a caller
  would ever need to distinguish "doesn't exist" from "exists but empty"

If you find yourself writing `if (result is null) return GenericError;` at the call
site, you've probably picked the wrong shape — that's the case `Result<T>` is for.

## Canonical error-code namespacing

Error codes are dot-separated, **slice-prefixed**, kebab-cased between dots:

```
{slice}.{kind}
```

Examples in use:

| Code | Used by | Type |
|---|---|---|
| `account_linking.request_not_found` | `CompleteAccountLinkingCommand` | `NotFound` |
| `account_linking.otp_mismatch` | `CompleteAccountLinkingCommand` | `Validation` |
| `account_linking.user_sync_failed` | `CompleteAccountLinkingCommand` | `Unexpected` |
| `account_linking.in_progress` | `StartAccountLinkingCommand` | `Conflict` |
| `account_linking.not_linked` | `UnlinkAccountsCommand`, `AccountLinkingQueries` | `NotFound` |
| `excuse.not_found` | `RemoveExcuseCommand`, `UpdateExcuseCommand` | `NotFound` |
| `strike.not_found` | `RevokeStrikeCommand`, `UnrevokeStrikeCommand` | `NotFound` |
| `club_member.not_found` | `CheckGeoGuessrPlayerActivityHandler`, `ReadOrSyncClubMember` | `NotFound` |
| `daily_mission_reminder.not_found` | `StopDailyMissionReminderCommand` | `NotFound` |
| `member_private_channel.not_found` | `DeleteMemberPrivateChannelCommand` | `NotFound` |
| `member_private_channel.delete_failed` | `DeleteMemberPrivateChannelCommand` | `Unexpected` |

Rules:

- `{slice}` matches the directory under `Application/UseCases/` in snake_case
  (e.g. `GeoGuessrAccountLinking` → `account_linking`,
  `MemberPrivateChannels` → `member_private_channel`)
- `{kind}` is short, snake_case, and verb-y when the error is about a failed
  operation (`otp_mismatch`, `user_sync_failed`, `delete_failed`); noun-y when the
  error is about an absent thing (`not_found`, `in_progress`, `not_linked`)
- The `Error.Message` is the human-friendly text. The code is the stable handle for
  routing decisions (Discord followup branches, HTTP status mapping). **Do not parse
  message strings.**

## Surfacing failures to the user

Two helpers in the Discord layer already exist for this — use them instead of writing
new mapping code:

- `ClubBotInteractionModule.FollowupFailureAsync(Error error, bool ephemeral = true)`
  follows up with the friendly text derived from the error's `Type`
- `ClubBotInteractionModule.FriendlyMessageFor(Error error)` is the same mapper as a
  pure function — use it when you need to inline the text into a larger message

For the HTTP path, `GlobalExceptionHandler` maps `FluentValidationException` →
`ValidationProblemDetails`. Result-typed handlers don't throw, so HTTP controllers
that consume them branch on `IsSuccess` directly.

## Helpers

Two extension methods live in `Utilities/ResultExtensions.cs`:

- `result.Match(onSuccess, onFailure)` — branch on the outcome, return the result of
  the chosen branch. Pure: nothing else fires
- `result.Map(value => transformed)` — apply a transformation to the success value;
  the failure passes through unchanged

These exist to kill the per-callsite `if (result.IsSuccess) … else …` repetition.
Reach for `Map` when you're projecting the success value into a new shape (e.g. a
DTO), and `Match` when both branches need to produce the same return type
(e.g. building a user-facing message).
