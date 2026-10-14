# Alloy API Readme

The **Alloy API** project is a restful api for Alloy functionality in the Crucible ecosystem. By default, `alloy.api` is available at `localhost:4402`, with the Swagger page at `localhost:4402/api`.

## postgresql migrations

From the **Alloy.Api** folder, with `Database:Provider` set to `PostgreSQL` (for example `Database__Provider=PostgreSQL`), because the migrations assembly is chosen by the provider name:

```bash
dotnet ef migrations add <MigrationName> --project ../Alloy.Api.Migrations.PostgreSQL/Alloy.Api.Migrations.PostgreSQL.csproj
```

`dotnet-ef` is not in the repository's tool manifest; install it as a global tool if needed. Migrations are applied at startup on PostgreSQL; the default provider in `appsettings.json` is `Sqlite`, which does not run them.

## Alloy API Data

### EventTemplate

- `Guid Id`
- `Guid? ViewId` (Player, a view eventTemplate)
- `Guid? DirectoryId` (Caster)
- `Guid? ScenarioTemplateId` (Steamfitter)
- `string Name`
- `string Description`
- `int DurationHours`
- `bool UseDynamicHost` (Caster)
- `bool IsPublished`

### Event

- `Guid Id`
- `Guid? EventTemplateId` (Alloy)
- `Guid UserId`
- `string Username`
- `Guid? ViewId` (Player)
- `Guid? WorkspaceId` (Caster)
- `Guid? RunId` (Caster)
- `Guid? ScenarioId` (Steamfitter)
- `string Name`
- `string Description`
- `string ShareCode`
- `EventStatus Status`
- `InternalEventStatus InternalStatus`
- `DateTime StatusDate`
- `DateTime? LaunchDate`
- `DateTime? EndDate`
- `DateTime? EndRequestedAt`
- `DateTime? ExpirationDate`
- failure tracking: `int FailureCount`, `LastLaunchStatus`/`LastLaunchInternalStatus`, `LastEndStatus`/`LastEndInternalStatus`, `string ErrorMessage`, `string ErrorDetail` (not on the API's `Event` model; read through `GET /api/events/{id}/error-detail`)

## Caster Interface

Alloy will deploy a lab/odx as a Caster workspace in the template's Caster directory, including a workspace specific variables file (`<workspace name>.auto.tfvars`). Variables include:
- `view_id`: the Player View ID (also written as the older `exercise_id`)
- `user_id` and `username`: the user who launched the event
- one variable per Player team, named from the team name, holding the Team ID

Then, Alloy will:
- Create a run
- Create a plan
- Apply the plan

Caster will write the `ViewId` and the `TeamId` from Player to each VM's guest info variables.

Alloy will get all authorized directories from `caster.api`.

## Player Interface

Alloy will get the views the current user can access from `player.api`. On launch, Alloy clones the event template's view with the `CloneView` endpoint and adds the event's users to the cloned view's default team.

## Steamfitter Interface

Alloy will get all authorized scenarioTemplates from `steamfitter.api`. Alloy will call the `CreateScenarioFromScenarioTemplate` endpoint on launch, `StartScenario` once the Caster workspace is applied, and `EndScenario` when the event ends.

## Preparing for Launch

Determine if an event already exists that can be rejoined or if a new event needs to be created.
   - `GET /api/eventTemplates/{eventTemplateId}/events/mine`
   - check for `Active` status

If there is an `Active` event, then present option to rejoin and skip launch.

## Launch

1. Create a new event from the eventTemplate.
   - `POST /api/eventTemplates/{eventTemplateId}/events2` with a `CreateEventCommand` body (the older `POST /api/eventTemplates/{eventTemplateId}/events`, with `userId` and `username` query parameters, is kept for backwards compatibility)
2. Clone the Player view.
3. Create a Steamfitter scenario from the scenarioTemplate.
4. Deploy a new Caster workspace in the Caster directory.
   - Create the workspace and its variables file
   - Create a `Run` for the workspace
   - Wait for the `Run` to be `Planned` status
   - Apply the `Run`
   - Wait for the `Run` to be `Applied` status
5. Start the Steamfitter scenario.
6. Set the Event status to `Active`, and its `ExpirationDate` to the launch time plus the template's `DurationHours`.

Each step is skipped when the event template has no view, scenario template or directory for it.

## End

An event ends when it is requested (`DELETE /api/events/{id}/end`) or when its `ExpirationDate` passes.

1. Settle any launch `Run` still in progress on the workspace.
2. Destroy the Caster workspace.
   - Create an `isDestroy` `Run` for the workspace (skipped when the workspace has no resources)
   - Wait for the `Run` to be `Planned` status
   - Apply the `Run`
   - Wait for the `Run` to be `Applied` status
   - Delete the workspace
3. Delete the Player view.
4. End the Steamfitter scenario.
5. Set the Event status to `Ended` (or `Failed`, when the launch had failed).

## Background Tasks

The entire **Launch** and **End** processes will be single background tasks that poll the Caster API for each status change, update the event database record appropriately, and then move on to the next step in the process.

## Event request models

Events are created only by launching an Event Template, which uses
`CreateEventCommand`. There is no direct event creation endpoint. Editing an
event (`PUT /events/{id}`) accepts `UpdateEventRequest`, containing only `Name`,
`Description`, and `ExpirationDate`, and returns `Event`.

Existing JSON payloads may still include additional response fields; those fields
are ignored when reading requests. Audit fields, end-request tracking, errors,
retry history, and workspace/run/scenario IDs remain server-owned.
Regenerated clients use `UpdateEventRequest` for `updateEvent`.

## Testing

The tests are in `Alloy.Api.Tests` and run against a PostgreSQL database that Testcontainers starts in a container, so Docker must be running:

```bash
dotnet test Alloy.Api.Tests
```

See [docs/Testing.md](docs/Testing.md) for how the suite is built and how to add a test.

## Reporting bugs and requesting features

Think you found a bug? Please report all Crucible bugs - including bugs for the individual Crucible apps - in the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include as much detail as possible including steps to reproduce, specific app involved, and any error messages you may have received.

Have a good idea for a new feature? Submit all new feature requests through the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include the reasons why you're requesting the new feature and how it might benefit other Crucible users.

## License

Copyright 2021 Carnegie Mellon University. See the [LICENSE.md](./LICENSE.md) files for details.
