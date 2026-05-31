# Interactions (Discord slash commands)

One **subfolder per feature**, mirroring `GeoClubBot.Application/UseCases/`
(`AccountLinking/`, `Activity/`, `Club/`, `DailyMissionReminder/`, `SelfRoles/`,
`Users/`, `AI/`). Add a new command's module in the matching subfolder.

A module:

- extends `Base/ClubBotInteractionModule(mediator, logger)`,
- declares a `[Group(...)]` and `[SlashCommand(...)]` methods,
- calls `Mediator.Send(new XxxQuery/Command(...), ct)` inside the `ExecuteAsync(...)` helper.

Modules are **auto-discovered** (`InteractionService.AddModulesAsync` via
`InteractionsAssemblyMarker`) — no manual registration. Model new ones on
`Users/UserInfoModule.cs`. If you only need another subcommand on an existing group,
add a method to that group's existing module rather than a new file.

`Base/` (the shared base module) and `InteractionsAssemblyMarker.cs` stay at the root.

Full recipe: [`Documentation/DeveloperGuide.md`](../../../Documentation/DeveloperGuide.md).
