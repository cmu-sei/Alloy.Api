// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Alloy.Api.Infrastructure.Exceptions;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using System.Threading;

namespace Alloy.Api.Controllers;

public class EventRolesController : BaseController
{
    private readonly IAlloyAuthorizationService _authorizationService;
    private readonly IEventRoleService _scenarioRoleService;

    public EventRolesController(IAlloyAuthorizationService authorizationService, IEventRoleService scenarioRoleService)
    {
        _authorizationService = authorizationService;
        _scenarioRoleService = scenarioRoleService;
    }

    /// <summary>
    /// Get a single EventRole.
    /// </summary>
    /// <param name="id">ID of a EventRole.</param>
    /// <returns></returns>
    [HttpGet("scenario-roles/{id}")]
    [ProducesResponseType(typeof(EventRole), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetEventRole")]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
    {
        if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewRoles], ct))
            throw new ForbiddenException();

        var result = await _scenarioRoleService.GetAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get all EventRoles.
    /// </summary>
    /// <returns></returns>
    [HttpGet("scenario-roles")]
    [ProducesResponseType(typeof(IEnumerable<EventRole>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetAllEventRoles")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _scenarioRoleService.GetAsync(ct);
        return Ok(result);
    }
}