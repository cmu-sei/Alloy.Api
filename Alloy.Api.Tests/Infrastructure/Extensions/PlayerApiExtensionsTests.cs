// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure;
using Alloy.Api.Infrastructure.Extensions;
using Crucible.Api.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Player.Api.Client;

namespace Alloy.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// Cloning a template's View for an event, over the real generated client and a scripted Player: the
/// participants join the View's participant team, and the configured service accounts, such as Caster's
/// Terraform provider, join its admin team so they can manage the copy.
/// </summary>
public class PlayerApiExtensionsTests
{
    private const string Player = "https://player.test";

    private static readonly Guid AdminRoleId = Guid.NewGuid();
    private static readonly Guid MemberRoleId = Guid.NewGuid();
    private static readonly Guid AutomationRoleId = Guid.NewGuid();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Guid _templateViewId = Guid.NewGuid();
    private readonly Guid _cloneId = Guid.NewGuid();
    private readonly Guid _adminTeamId = Guid.NewGuid();
    private readonly Guid _memberTeamId = Guid.NewGuid();
    private readonly Guid _automationTeamId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _serviceUserId = Guid.NewGuid();

    [Fact]
    public async Task Without_service_users_only_the_participants_join_the_clone()
    {
        var player = ScriptedPlayer(withAutomationTeam: false);

        var result = await Create(player, serviceUserIds: [], serviceTeamRoleId: null);

        Assert.True(result.IsSuccess, $"{result.Summary}: {result.Detail}");
        Assert.Equal([$"api/teams/{_memberTeamId}/users/{_ownerId}"], Joins(player));
    }

    [Fact]
    public async Task A_service_user_joins_the_admin_team_of_the_clone()
    {
        var player = ScriptedPlayer(withAutomationTeam: false);

        var result = await Create(player, serviceUserIds: [_serviceUserId, _serviceUserId], serviceTeamRoleId: null);

        Assert.True(result.IsSuccess, $"{result.Summary}: {result.Detail}");
        Assert.Equal(
            [$"api/teams/{_memberTeamId}/users/{_ownerId}", $"api/teams/{_adminTeamId}/users/{_serviceUserId}"],
            Joins(player));
    }

    [Fact]
    public async Task A_configured_team_role_picks_the_team_the_service_user_joins()
    {
        var player = ScriptedPlayer(withAutomationTeam: true);

        var result = await Create(player, serviceUserIds: [_serviceUserId], serviceTeamRoleId: AutomationRoleId);

        Assert.True(result.IsSuccess, $"{result.Summary}: {result.Detail}");
        Assert.Contains($"api/teams/{_automationTeamId}/users/{_serviceUserId}", Joins(player));
        Assert.DoesNotContain($"api/teams/{_adminTeamId}/users/{_serviceUserId}", Joins(player));
    }

    [Fact]
    public async Task A_clone_with_no_team_for_the_service_user_fails_and_is_deleted()
    {
        var player = ScriptedPlayer(withAutomationTeam: false);

        var result = await Create(player, serviceUserIds: [_serviceUserId], serviceTeamRoleId: AutomationRoleId);

        Assert.True(result.IsPermanent);
        Assert.DoesNotContain(Joins(player), x => x.EndsWith($"/users/{_serviceUserId}", StringComparison.Ordinal));
        Assert.Contains(player.Sent, x => x.Method == HttpMethod.Delete && x.Path == $"api/views/{_cloneId}");
    }

    [Fact]
    public void Without_a_team_role_the_service_team_is_the_one_whose_role_grants_all_permissions()
    {
        var teams = new[]
        {
            new Team { Id = _memberTeamId, RoleId = MemberRoleId },
            new Team { Id = Guid.NewGuid() },
            new Team { Id = _adminTeamId, RoleId = AdminRoleId },
        };

        Assert.Equal(_adminTeamId, PlayerApiExtensions.GetServiceTeamId(teams, Roles(), teamRoleId: null));
        Assert.Null(PlayerApiExtensions.GetServiceTeamId([.. teams.Take(2)], Roles(), teamRoleId: null));
    }

    [Fact]
    public void Without_an_all_permissions_role_a_team_granted_ManageView_is_the_service_team()
    {
        var managerTeamId = Guid.NewGuid();
        var teams = new[]
        {
            new Team { Id = _memberTeamId, RoleId = MemberRoleId, Permissions = [] },
            new Team { Id = managerTeamId, Permissions = [new TeamPermissionModel { Name = "ManageView" }] },
        };

        Assert.Equal(managerTeamId, PlayerApiExtensions.GetServiceTeamId(teams, Roles(), teamRoleId: null));
    }

    [Fact]
    public async Task A_service_user_already_on_the_service_team_as_the_owner_is_not_added_twice()
    {
        var player = ScriptedPlayer(withAutomationTeam: false);

        var result = await Create(player, serviceUserIds: [_ownerId], serviceTeamRoleId: MemberRoleId);

        Assert.True(result.IsSuccess, $"{result.Summary}: {result.Detail}");
        Assert.Equal([$"api/teams/{_memberTeamId}/users/{_ownerId}"], Joins(player));
    }

    private Task<ApiCallResult<Guid>> Create(StubHttpMessageHandler player, IReadOnlyCollection<Guid> serviceUserIds, Guid? serviceTeamRoleId) =>
        PlayerApiExtensions.CreatePlayerViewAsync(
            Client(player),
            new EventEntity { Id = Guid.NewGuid(), UserId = _ownerId, Username = "owner" },
            new EventTemplateEntity { Id = Guid.NewGuid(), Name = "template", ViewId = _templateViewId },
            [],
            serviceUserIds,
            serviceTeamRoleId,
            NullLogger.Instance,
            Ct);

    private StubHttpMessageHandler ScriptedPlayer(bool withAutomationTeam)
    {
        var teams = new List<Team>
        {
            new() { Id = _adminTeamId, Name = "Admin", RoleId = AdminRoleId, Permissions = [] },
            new() { Id = _memberTeamId, Name = "Blue", RoleId = MemberRoleId, Permissions = [] },
        };
        if (withAutomationTeam)
            teams.Add(new Team { Id = _automationTeamId, Name = "Automation", RoleId = AutomationRoleId, Permissions = [] });

        return new StubHttpMessageHandler()
            .AnswersJson($"POST api/views/{_templateViewId}/*", JsonSerializer.Serialize(new View { Id = _cloneId }, Json), HttpStatusCode.Created)
            .Answers($"GET api/views/{_cloneId}/teams", teams)
            .Answers("GET api/team-roles", Roles())
            .Answers("GET api/users/*", new User())
            .Answers("POST api/teams/*/users/*", HttpStatusCode.OK)
            .Answers($"DELETE api/views/{_cloneId}", HttpStatusCode.NoContent)
            .RefusesUnmatched();
    }

    private static List<TeamRole> Roles() =>
    [
        new() { Id = AdminRoleId, Name = "View Admin", AllPermissions = true, Permissions = [] },
        new() { Id = MemberRoleId, Name = "View Member", AllPermissions = false, Permissions = [] },
        new() { Id = AutomationRoleId, Name = "Automation", AllPermissions = false, Permissions = [] },
    ];

    private static List<string> Joins(StubHttpMessageHandler player) =>
        [.. player.Sent.Where(x => x.Method == HttpMethod.Post && x.Path.StartsWith("api/teams/", StringComparison.Ordinal)).Select(x => x.Path)];

    private static PlayerApiClient Client(StubHttpMessageHandler player) =>
        new(new HttpClient(player) { BaseAddress = new Uri(Player) });
}
