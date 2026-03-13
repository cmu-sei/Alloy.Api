# Alloy.Api.Tests.Integration

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

## Purpose

Integration tests for the Alloy API that validate the full HTTP request pipeline, database interactions, and controller behaviors using a real PostgreSQL database via Testcontainers. Tests exercise the complete application stack from HTTP endpoints through to database persistence.

## Files

### Fixtures/AlloyTestContext.cs

`WebApplicationFactory<Program>` implementation that configures the test host environment:

**Key Features:**
- Spins up a PostgreSQL container using Testcontainers.PostgreSql
- Configures test database with credentials (username/password: foundry)
- Replaces production DbContext with test database connection
- Substitutes authentication with `TestAuthenticationHandler` (no real OIDC required)
- Replaces `IAlloyAuthorizationService` with `TestAlloyAuthorizationService` (permissive authorization)
- Removes hosted background services that depend on external integrations
- Uses `[ClassDataSource<T>(Shared = SharedType.PerTestSession)]` for container lifecycle management

**Container Configuration:**
- Image: `postgres:latest`
- Hostname: localhost
- Auto-remove and cleanup enabled for test isolation

### Fixtures/TestAlloyAuthorizationService.cs

Permissive authorization service for integration tests that allows all operations:
- `AuthorizeAsync` methods always return `true`
- `GetSystemPermissions` returns all SystemPermission enum values
- `GetAuthorizedEventIds`, `GetEventPermissions`, `GetEventTemplatePermissions` return empty collections

Enables testing controller logic without enforcing real permission checks.

### Controllers/HealthCheckTests.cs

Health endpoint integration tests:
- `GetReadiness_ReturnsSuccessStatusCode` - Validates `/api/health/ready` endpoint returns HTTP 200
- `GetLiveliness_ReturnsSuccessStatusCode` - Validates `/api/health/live` endpoint returns HTTP 200

### Controllers/UserControllerTests.cs

User API endpoint integration tests demonstrating full CRUD lifecycle:
- `GetUsers_ReturnsSuccessStatusCode` - GET `/api/users` returns HTTP 200
- `CreateUser_ReturnsCreatedStatusCode` - POST `/api/users` creates user and returns HTTP 201 with created entity
- `GetUser_AfterCreate_ReturnsCorrectUser` - Tests full create → retrieve cycle, validates entity persistence and retrieval

## Dependencies

- **TUnit 1.19.22** - Test framework
- **Microsoft.AspNetCore.Mvc.Testing 10.0.1** - WebApplicationFactory for in-process API hosting
- **Testcontainers.PostgreSql 4.0.0** - PostgreSQL Docker container management
- **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0** - PostgreSQL EF Core provider
- **AutoFixture 4.18.1** - Test data generation
- **Alloy.Api.Tests.Shared** - Shared fixtures
- **Crucible.Common.Testing** - Test authentication handlers and extensions

## Running Tests

```bash
# Navigate to the Alloy API directory
cd /mnt/data/crucible/alloy/alloy.api

# Run all integration tests (requires Docker)
dotnet test Alloy.Api.Tests.Integration

# Run with detailed output
dotnet test Alloy.Api.Tests.Integration --logger "console;verbosity=detailed"

# Run specific test class
dotnet test Alloy.Api.Tests.Integration --filter FullyQualifiedName~UserControllerTests

# Run with code coverage
dotnet test Alloy.Api.Tests.Integration --collect:"XPlat Code Coverage"
```

**Prerequisites:**
- Docker must be running (Testcontainers starts PostgreSQL automatically)
- Sufficient Docker resources for PostgreSQL container
- Network access to pull `postgres:latest` image

## Key Patterns

### WebApplicationFactory Pattern

```csharp
[ClassDataSource<AlloyTestContext>(Shared = SharedType.PerTestSession)]
public class ControllerTests(AlloyTestContext context)
{
    [Test]
    public async Task TestEndpoint()
    {
        var client = context.CreateClient();
        var response = await client.GetAsync("/api/endpoint");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
```

TUnit's `[ClassDataSource<T>(Shared = SharedType.PerTestSession)]` ensures AlloyTestContext (and its PostgreSQL container) is shared across all tests in the class, then disposed once.

### Testcontainers Lifecycle

```csharp
public class AlloyTestContext : WebApplicationFactory<Program>, IAsyncInitializer, IAsyncDisposable
{
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:latest")
            .WithAutoRemove(true)
            .Build();
        await _container.StartAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
```

`IAsyncInitializer` and `IAsyncDisposable` hooks ensure container starts before any tests run and stops after all tests complete.

### HTTP Client Testing

```csharp
// GET request
var response = await client.GetAsync("/api/users");
await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

// POST request with JSON body
var user = new User { Id = Guid.NewGuid(), Name = "Test" };
var response = await client.PostAsJsonAsync("/api/users", user);
await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

// Read response body
var createdUser = await response.Content.ReadFromJsonAsync<User>();
await Assert.That(createdUser).IsNotNull();
```

Uses `HttpClient` extension methods from `System.Net.Http.Json` for serialization.

### Test Database Access

```csharp
// Access DbContext for setup or verification
var dbContext = _context.GetDbContext();
dbContext.Users.Add(new UserEntity { Id = userId, Name = "Setup User" });
await dbContext.SaveChangesAsync();
```

Useful for seeding data before tests or verifying side effects after API calls.

## Test Isolation

Each test class receives a fresh `AlloyTestContext`, but tests within a class share the same PostgreSQL container and database state. For true test isolation:

1. Use unique GUIDs for all test data
2. Consider cleanup logic in test teardown if needed
3. Use transactions that rollback (advanced pattern, not currently implemented)

## Test Coverage Goals

- All controller endpoints tested with successful path
- HTTP status codes validated (200 OK, 201 Created, 404 Not Found, etc.)
- Request/response body serialization verified
- Authentication and authorization integration confirmed (via test handlers)
- Database persistence validated through retrieve-after-create patterns
