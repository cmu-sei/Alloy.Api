# Alloy API tests

Requires the repository's .NET SDK and a running Docker daemon.

```sh
dotnet test Alloy.Api.Tests/Alloy.Api.Tests.csproj
```

Database and worker tests use PostgreSQL Testcontainers (`postgres:17.6`, matching
the development AppHost). One container serves the test collection, with a
separate database for each test. Testcontainers removes the container afterward.
No connection to the development Alloy database is used.

The migration test applies the real PostgreSQL migration history through PR #72,
inserts historical events, upgrades, and rolls back the new migration. Other
tests exercise the event services and worker with separate database contexts.
Caster, Player, Steamfitter, and identity HTTP responses are controlled in-process;
the tests use the actual generated clients without requiring those services.
