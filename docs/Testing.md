Alloy.Api has an automated test suite in the `Alloy.Api.Tests` project. This document says how to run it, how the harness is built, and what is particular to Alloy. The parts every Crucible API shares are described once, in the Crucible API test standard (`agent-docs/api-testing/` of the workspace: `README.md` for the harness, `CONVENTIONS.md` for how tests are written); this document points there rather than repeating them.

# Testing

The suite is built on xUnit v3 and NSubstitute and runs against a real PostgreSQL instance started in a container, with Alloy's real migrations. These are not isolated unit tests. A typical test sends an HTTP request to the application hosted in process, through the real `Startup`, the real MVC filters, the real claims transformer, the real authorization service and handlers, the real AutoMapper profiles and a real database, then asserts on the response, on what changed in the database, and on what was broadcast through `EngineHub`. Only the collaborators that leave the process are replaced: the identity provider, Player, Caster and Steamfitter.

# Running the tests

```bash
dotnet test Alloy.Api.Tests
dotnet test Alloy.Api.Tests -- xUnit.DiagnosticMessages=true    # prints the database provider banner
dotnet test Alloy.Api.Tests --filter "FullyQualifiedName~.EventControllerTests"
```

Docker must be running. The suite starts and disposes its own PostgreSQL container (`postgres:16-alpine`) through Testcontainers when the first test asks for a database, so nothing is installed and no development database is touched; tests that take no database (the Caster run helpers, the queue, the mapping and handler tests) run without Docker.

# Coverage

```bash
dotnet test Alloy.Api.Tests --collect:"XPlat Code Coverage"
```

`coverlet.runsettings` (the standard's) is applied by `RunSettingsFilePath` in the project file, with its collector off by default, so only a run that asks collects, and it always excludes `Alloy.Api.Migrations.PostgreSQL`, the test assembly, the `Crucible.Common.EntityEvents` content files and generated code. Read cobertura totals from the root element's `lines-covered` and `lines-valid` (the standard's README, "Known pitfalls"). Coverage is a local diagnostic; CI neither collects nor gates on it.

# Build settings

- `Alloy.Api.Tests/Directory.Build.props` (the standard's) turns on `TreatWarningsAsErrors` for the test project only, so the xUnit analyzers fail the build (xUnit1051 makes every awaited call take `Ct`), and keeps the NuGet audit codes as warnings. The repository has no root `Directory.Build.props`, so the application projects keep their own settings.
- The root `.editorconfig` raises xUnit1004, so `[Fact(Skip = ...)]` fails the build.
- There is no central package management in this repository, so the test packages carry the standard's pinned versions (`agent-docs/api-testing/test-packages.props`) as `Version=` attributes in `Alloy.Api.Tests.csproj`; `sync.sh` checks them there.
- `ImplicitUsings` stays off, as in the API projects; each file names its usings. Nullable is off.
- A build of the test project rebuilds `Alloy.Api`, which reports CS1573 for documented actions whose `CancellationToken` has no `<param>` tag. The test project's own build is warning-free apart from the NuGet audit.
- The suites run in VSTest mode, for coverlet; see the standard's README.

# How the harness works

The harness is the standard's, in `Alloy.Api.Tests/Support/`. The shared files are in `Support/Shared/` (namespace `Crucible.Api.Testing`), copied by `agent-docs/api-testing/sync.sh` and never edited here; the base classes, the fixtures, the recorder rule and the isolation model are described in the standard's README. What is Alloy's own:

## Fixtures and the host

- `DatabaseFixture` wraps `PostgresTestDatabase<AlloyContext>` with the database name `alloy`, the migrations assembly `Alloy.Api.Migrations.PostgreSQL` and `AlloyContextFactory`. Each test's database is a clone of a template migrated once.
- `AlloyAppFactory` is the run-wide factory (an assembly fixture, one host for the run). `Program.Main` runs `InitializeDatabase` with no switch that skips it, so the factory takes the template's **step 1B**: the host gets a throwaway clone of its own (`DatabaseFixture.HostDatabase()`), passed as the `Database:Provider` and `ConnectionStrings:PostgreSQL` host settings, where migrating is a no-op. The shared `TestDatabaseScope.ReplaceRegistration` routes a request to the database of the test that sent it; its two-argument overload, `() => _started ? null : DatabaseFixture.HostDatabase()`, lets `InitializeDatabase` (which resolves the context outside any request) reach the host's database, over that session's own services so the seed's entity events reach no real handler or recorder, until the host has started, and throws afterwards. `CreateHost` builds the one host under a lock, because `WebApplicationFactory.StartServer` takes none and two tests asking for their first client at once would otherwise each run `Program.Main`.
- The host runs in Production, so `JsonExceptionFilter` answers as deployed: a handled exception's message in `Title`, a 500's in `Detail` under the title "A server error occurred.", never the stack trace.
- `TestConfiguration` turns off the claims cache (one host serves the run, and the cache is keyed on user id), and gives `ResourceOwnerAuthorization` a user name and password, which IdentityModel requires before Alloy can ask for its own token. Roles and groups from the token stay on as shipped: `TestAuthHandler` mints no `realm_access` or `groups` claim, so permissions come only from seeded rows; `UserClaimsServiceTests` covers the token path.

## What is replaced

- **Token validation**: the shared `TestAuthHandler`, registered under its own scheme name (the default) and under `Bearer`, which `EngineHub`'s `[Authorize(AuthenticationSchemes = "Bearer")]` names; Startup's JWT bearer registration is dropped with the `IConfigureOptions<AuthenticationOptions>` that carries it. Actor clients also carry an `Authorization: Bearer token-of-<actor id>` header, which the handler ignores: production requests always carry one, `Add{Player,Caster,Steamfitter}ApiClient` forward it to the sibling APIs (the Caster and Steamfitter clients cannot be built without it), and a test can assert the caller's token reached the sibling (`BearerToken(actor)`).
- **`IHubContext<EngineHub>`**: the shared `HubRecorder<EngineHub>` (`Factory.Hub`). Alloy's handlers broadcast with `Clients.Groups(id, admin group)`, which the recorder files under each group: read it with `Factory.Hub.ToGroup(id)` for an id the test seeded, and `ToGroups(id, admin group)` for the call itself.
- **`IHttpClientFactory`**: the shared `StubHttpClientFactory` over `Factory.OutboundHttp`. `TestIdentity` fixes the identity provider's discovery, keys and token answers on it at construction, (the shipped `http://localhost:5000` authority); `OutboundHttp.RespondJson(url, body)` arranges a JSON answer. Player, Caster and Steamfitter answer at the shipped `ClientSettings:urls` (`http://localhost:4300/`, `:4309/`, `:4400/`); arrange a url keyed on an id the test minted (`http://localhost:4300/api/views/{viewId}`), and every other url answers 404.
- **The hosted services** (`AlloyBackgroundService`, `AlloyQueryService`) are removed. The singleton `IAlloyEventQueue` stays real; nothing takes from it in the host.

## Actors

`TestActor` mirrors `UserClaimsService.GetPermissionClaims`. `Root` holds the seeded Administrator role. `Actor()` seeds:

- `WithSystemPermissions(...)` (a role minted for the actor) or `WithRole(id)` (a seeded role such as `TestData.Roles.ContentDeveloper`).
- `OnEvent(eventId, permissions | roleId, throughGroup)` and `OnEventTemplate(...)`: a membership that always names its role, because the membership tables default `role_id` to the seeded Member role (view and edit). `throughGroup: true` puts the membership on a group the actor joins, the transformer's group path.
- `InGroup(groupId, role)`: a group membership; a `Manager` holds `ManageMembership` on the group.
- `OnNewEvent(templateId, permissions)`, `OnNewEventTemplate(permissions)`, `OnNewGroup(role)`: mint the resource too, for a denied test's near miss (the right permission on another event, template or group). A minted template is unpublished, because a published template grants `ViewEventTemplate` to every user. The minted ids are on `NewEvents`, `NewEventTemplates`, `NewGroups`.

A denied test names what the caller holds: `Update_is_forbidden_for_a_member_holding_only_ViewEvent`, `Get_is_forbidden_for_a_caller_holding_ViewEvent_only_on_another_event`.

## Reading responses

- `ReadAsync<T>` in Alloy's `ApiTestBase` hides the shared one: Alloy writes a null `Guid?` as `""` (`JsonNullableGuidConverter`), which the shared `TestJson.Options` cannot read, so it calls the shared `ReadAsync<T>(response, AlloyJson)` (the shared options plus Alloy's converters). Send request bodies with `AlloyJson` too.
- The shared `AssertJsonError(status, response)` asserts the body `JsonExceptionFilter` answers an exception with: a `ProblemDetails` sent as `application/json`, and returns it; a characterized 500 asserts its `Detail`. The shared `AssertProblem` (`application/problem+json`) fits only the `[ApiController]` answer to a body or route value that does not bind.

## Service tests

`ServiceTestBase` and `ApiTestHost` construct services with options a test chose, without the web host: the launch and end worker (`AlloyBackgroundService.ProcessEventAsync`, which reads `ClientOptions` through `IOptionsMonitor` and resolves a context and the resource-owner options per scope) and `EventService` where a request's own stale context is the subject. The host registers the context per scope over the test's database, the real authorization service and handlers, the Startup client registrations, and a `FakeSiblingApis` per host. `ApiTestHostOptions` defaults to two retries, one-minute Caster waits and a zero poll interval. `ApiTestHostTests` constructs what the host offers.

`FakeSiblingApis` is an app extra: Player, Caster and Steamfitter answered by a script (`Handle`), and the identity provider answered by itself. The worker's subject is a conversation (a run that reads `Planning` and then `Applying`, a save-state conflict followed by a terminal status, an end request arriving while a poll is in flight), and its scripts pick the status per request, throw after changing state (a response lost after the call took effect), read the request body and await an end request mid-call. The shared stub's route rules answer with a fixed status, and `AnswersJson(route, Func<string>)` computes only a 200 body synchronously, so they cannot express those. It is owned by one test, never registered in the run-wide host. `CasterApiExtensionsTests` uses one directly.

## Self-tests

`DatabaseHarnessTests` (isolation probed on `system_roles.name`, the uniquely indexed column), `HttpHarnessTests` (on `api/system-roles`), `TestActorTests` (each actor shape through the real `UserClaimsService`) and `ApiTestHostTests`. When the harness breaks, these fail first.

# Adding a test

Follow `agent-docs/api-testing/CONVENTIONS.md`: sentences for names, `Ct` everywhere, re-read through `NewContext()`, mothers from `TestData`, callers from `Actor()`, near misses for every denied case, recorders keyed on ids the test owns. Derive from `ApiTestBase` to send a request, `ServiceTestBase` to construct the worker or a service with options, `DatabaseTestBase` for the database alone, or nothing.

A defect the tests find is characterized by a passing test of the current behaviour and described in `agent-docs/api-test-bugs/alloy.api.md` of the workspace, never in a test comment (CONVENTIONS.md section 3).

# Layout

```
Alloy.Api.Tests/
  Controllers/          every controller over HTTP: gates, persistence, broadcasts
  Services/             the worker, EventService, EventLifecycle, the queue, UserClaimsService
  Hubs/                 EngineHub through HubHarness, and its [Authorize] over the negotiate request
  Infrastructure/       authorization handlers, the exception filter, the Caster run helpers, mapping
  Data/                 the migrations (EndRequestedAt up and down) and the model snapshot
  Support/              the harness: Alloy's files, the self-tests, the extras (FakeSiblingApis,
                        TestIdentity)
    Shared/             the standard's shared files, copied by sync.sh, never edited here
```

# Continuous integration

`.github/workflows/build-and-test.yml` is the standard's workflow with this repository's test project filled in; `sync.sh` keeps it identical. It runs on pull requests and pushes to `main`, builds, runs the suite with the provider banner on, greps `[Alloy.Api.Tests] database provider: PostgreSQL` from the log, and uploads the TRX as `test-results`. Testcontainers starts its own PostgreSQL, so the job has no `services:` block.
