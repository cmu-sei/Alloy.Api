# Alloy.Api.Tests.Unit

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

## Purpose

Unit tests for the Alloy API event orchestration and on-demand simulation launcher. Tests service layer business logic, AutoMapper configurations, and data access patterns using in-memory databases and mocks. Validates core functionality without external dependencies.

## Files

### Fixtures/AlloyAutoDataAttribute.cs

Custom xUnit `AutoDataAttribute` that combines AutoFixture with FakeItEasy auto-mocking and Alloy-specific entity customizations. Enables parameterized tests with auto-generated mocks and domain entities.

### MappingConfigurationTests.cs

AutoMapper profile validation tests:
- `AllMappingProfiles_ShouldHaveValidConfiguration` - Verifies all mapping profiles can create a mapper instance (weaker than `AssertConfigurationIsValid` due to unmapped permission properties populated by controllers)
- `EventProfile_ShouldMapEventEntityToEvent` - Validates EventEntity → Event view model mapping
- `UserProfile_ShouldMapUserEntityToUser` - Validates UserEntity → User view model mapping

### Services/EventServiceTests.cs

EventService unit tests using TestDbContextFactory InMemory EF Core provider:
- `GetAsync_ReturnsAllEvents` - Verifies retrieval of all events from database
- `GetEventTemplateEventsAsync_FiltersCorrectly` - Tests filtering events by EventTemplateId
- `DeleteAsync_RemovesEvent_ReturnsTrue` - Validates event deletion and return value

### Services/EventTemplateServiceTests.cs

EventTemplateService unit tests with InMemory database:
- `GetAsync_ReturnsAllEventTemplates` - Retrieves all event templates
- `GetPublishedAsync_ReturnsOnlyPublishedTemplates` - Filters templates by IsPublished flag
- `DeleteAsync_RemovesTemplate_ReturnsTrue` - Tests template deletion

### Services/UserServiceTests.cs

UserService unit tests with ClaimsPrincipal identity:
- `GetAsync_ReturnsAllUsers` - Retrieves all users
- `GetByIdAsync_ReturnsCorrectUser` - Fetches specific user by ID
- `DeleteAsync_WhenDeletingSelf_ThrowsForbiddenException` - Validates business rule preventing self-deletion

## Dependencies

- **xUnit 2.9.3** - Test framework
- **FakeItEasy 8.3.0** - Mocking framework
- **AutoFixture 4.18.1** - Test data generation
- **AutoFixture.AutoFakeItEasy 4.18.1** - Auto-mocking integration
- **Microsoft.EntityFrameworkCore.InMemory 10.0.1** - In-memory database provider
- **Shouldly 4.2.1** - Assertion library
- **MockQueryable.FakeItEasy 7.0.3** - IQueryable mocking support
- **Alloy.Api.Tests.Shared** - Shared fixtures and customizations
- **Crucible.Common.Testing** - TestDbContextFactory for InMemory EF contexts

## Running Tests

```bash
# Navigate to the Alloy API directory
cd /mnt/data/crucible/alloy/alloy.api

# Run all unit tests
dotnet test Alloy.Api.Tests.Unit

# Run with detailed output
dotnet test Alloy.Api.Tests.Unit --logger "console;verbosity=detailed"

# Run specific test class
dotnet test Alloy.Api.Tests.Unit --filter FullyQualifiedName~EventServiceTests

# Run with code coverage
dotnet test Alloy.Api.Tests.Unit --collect:"XPlat Code Coverage"
```

## Key Patterns

### TestDbContextFactory Pattern

```csharp
var context = TestDbContextFactory.Create<AlloyContext>();
context.Events.AddRange(events);
context.SaveChanges();

var sut = BuildEventService(context, mapper);
var result = await sut.GetAsync(CancellationToken.None);
```

Uses EF Core InMemory provider for fast, isolated database tests without PostgreSQL dependency.

### FakeItEasy Service Mocking

```csharp
var mapper = A.Fake<IMapper>();
A.CallTo(() => mapper.Map<IEnumerable<ViewModels.Event>>(A<object>._))
    .Returns(expectedResult);

// Verify call occurred with specific arguments
A.CallTo(() => mapper.Map<IEnumerable<ViewModels.Event>>(
    A<List<EventEntity>>.That.Matches(l => l.Count == 1)))
    .MustHaveHappenedOnceExactly();
```

### FakeBuilder Pattern

```csharp
private static EventService BuildEventService(AlloyContext context, IMapper? mapper = null)
{
    var resolvedMapper = mapper ?? A.Fake<IMapper>();
    return FakeBuilder.BuildMeA<EventService>(context, resolvedMapper);
}
```

Uses `Crucible.Common.Testing.Fixtures.FakeBuilder` to construct services with dependency injection while allowing mock overrides for specific dependencies.

### Shouldly Assertions

```csharp
result.ShouldNotBeNull();
result.Count().ShouldBe(2);
context.Events.FirstOrDefault(e => e.Id == eventId).ShouldBeNull();
await Should.ThrowAsync<ForbiddenException>(() => sut.DeleteAsync(id, ct));
```

Fluent assertion syntax that produces readable failure messages.

## Test Coverage Goals

- Minimum 80% code coverage across all service methods
- All public service methods tested with happy path and edge cases
- Business rules validated (e.g., user cannot delete self)
- AutoMapper profiles verified for all entity-to-view model mappings
