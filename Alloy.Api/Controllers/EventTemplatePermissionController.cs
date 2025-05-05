// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System;

namespace Alloy.Api.Controllers;

public class EventTemplatePermissionsController : BaseController
{
    private readonly IAlloyAuthorizationService _authorizationService;

    public EventTemplatePermissionsController(IAlloyAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Get all SystemPermissions for the calling User.
    /// </summary>
    /// <returns></returns>
    [HttpGet("eventTemplates/{id}/me/permissions")]
    [ProducesResponseType(typeof(IEnumerable<EventTemplatePermissionClaim>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetMyEventTemplatePermissions")]
    public async Task<IActionResult> GetMine(Guid id)
    {
        var result = _authorizationService.GetEventTemplatePermissions();
        return Ok(result);
    }
}