// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Alloy.Api.Controllers;

public class GroupPermissionsController : BaseController
{
    private readonly IAlloyAuthorizationService _authorizationService;

    public GroupPermissionsController(IAlloyAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Get all GroupPermissions for the calling User.
    /// </summary>
    /// <returns></returns>
    [HttpGet("permissions/group/mine")]
    [ProducesResponseType(typeof(IEnumerable<GroupPermissionsClaim>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetMyGroupPermissions")]
    public Task<IActionResult> GetMine([FromQuery] Guid? groupId)
    {
        var result = _authorizationService.GetGroupPermissions(groupId);
        return Task.FromResult<IActionResult>(Ok(result));
    }
}
