// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Tests.Controllers;

/// <summary><c>EventRolesController</c> and <c>EventTemplateRolesController</c> over HTTP.</summary>
public class EventRoleControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A role stored without permissions is returned with an empty list, not null.</summary>
    [Fact]
    public async Task Get_returns_an_event_role_stored_without_permissions_with_an_empty_list()
    {
        var role = TestData.EventRole();
        role.Permissions = null;
        await Seed(role);

        var read = await ReadAsync<EventRole>(await RootClient.GetAsync($"api/event-roles/{role.Id}", Ct));

        Assert.Equal([], read.Permissions);
    }

    /// <summary>A role stored without permissions is returned with an empty list, not null.</summary>
    [Fact]
    public async Task Get_returns_an_event_template_role_stored_without_permissions_with_an_empty_list()
    {
        var role = TestData.EventTemplateRole();
        role.Permissions = null;
        await Seed(role);

        var read = await ReadAsync<EventTemplateRole>(await RootClient.GetAsync($"api/eventTemplate-roles/{role.Id}", Ct));

        Assert.Equal([], read.Permissions);
    }
}
