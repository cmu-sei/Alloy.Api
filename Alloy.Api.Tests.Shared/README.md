# Alloy.Api.Tests.Shared

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

## Purpose

Shared test infrastructure and fixtures used by both Alloy.Api.Tests.Unit and Alloy.Api.Tests.Integration projects. Provides AutoFixture customizations and entity builders to create consistent test data across all Alloy API test suites.

## Files

### Fixtures/AlloyCustomization.cs

AutoFixture customization that registers entity type builders and services for Alloy domain entities:

**Entity Registrations:**
- `EventEntity` - Active events with InternalEventStatus.Launched
- `EventTemplateEntity` - Event templates (unpublished by default)
- `UserEntity` - System users
- `GroupEntity` - User groups
- `GroupMembershipEntity` - Group membership associations
- `SystemRoleEntity` - System-level roles with permissions
- `EventTemplateRoleEntity` - Event template-scoped roles
- `EventRoleEntity` - Event-scoped roles
- `EventMembershipEntity` - Event membership with default EventMember role
- `EventTemplateMembershipEntity` - Event template membership with default role
- `EventUserEntity` - Event user associations
- `PermissionEntity` - Permission definitions

**Service Registrations:**
- AutoMapper with all Alloy mapping profiles (Event, EventTemplate, User, Group, SystemRole, EventRole, EventTemplateRole, Membership profiles)

**Behaviors:**
- `OmitOnRecursionBehavior` - Prevents circular reference errors in entity graphs
- `GuidIdBuilder` - Generates valid GUIDs for ID properties
- `DateTimeOffsetBuilder` - Creates consistent DateTimeOffset values

## Dependencies

- **AutoFixture 4.18.1** - Test data generation framework
- **Alloy.Api** - Main API project reference
- **Alloy.Api.Data** - Data access layer with entity models
- **Crucible.Common.Testing** - Shared Crucible test utilities (GuidIdBuilder, DateTimeOffsetBuilder)

## Usage

This project does not contain runnable tests. It provides shared infrastructure consumed by:

- **Alloy.Api.Tests.Unit** - References AlloyCustomization via CrucibleFixtureFactory
- **Alloy.Api.Tests.Integration** - Can reference for entity building in integration scenarios

### In Unit Tests

```csharp
using Alloy.Api.Tests.Shared.Fixtures;

public class CrucibleFixtureFactory : ITestDataSource
{
    public IEnumerable<Func<object?[]>> GetTestData(TestContext testContext)
    {
        var fixture = new Fixture().Customize(new AlloyCustomization());
        // Generate test data using fixture
        yield return () => new object?[] { /* test parameters */ };
    }
}

// Use in tests:
[Test]
public async Task TestMethod(EventEntity entity, IMapper mapper)
{
    // entity and mapper can be generated using fixture in test body
}
```

## Key Patterns

- **Entity Factories** - Each entity registration provides realistic default values (names prefixed with entity type, valid status enums, UTC timestamps)
- **Service Mocking Foundation** - Pre-configured AutoMapper with all production profiles for accurate mapping tests
- **Reusable Behaviors** - OmitOnRecursion prevents stack overflows when testing entities with navigation properties
