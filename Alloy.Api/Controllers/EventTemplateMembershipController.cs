// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
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
using System.Data;

namespace Alloy.Api.Controllers;

public class EventTemplateMembershipsController : BaseController
{
    private readonly IAlloyAuthorizationService _authorizationService;
    private readonly IEventTemplateMembershipService _eventTemplateMembershipService;

    public EventTemplateMembershipsController(IAlloyAuthorizationService authorizationService, IEventTemplateMembershipService eventTemplateMembershipService)
    {
        _authorizationService = authorizationService;
        _eventTemplateMembershipService = eventTemplateMembershipService;
    }

    /// <summary>
    /// Get a single EventTemplateMembership.
    /// </summary>
    /// <param name="id">ID of a EventTemplateMembership.</param>
    /// <returns></returns>
    [HttpGet("eventTemplates/memberships/{id}")]
    [ProducesResponseType(typeof(EventTemplateMembership), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetEventTemplateMembership")]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
    {
        if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewEventTemplates], ct))
            throw new ForbiddenException();

        var result = await _eventTemplateMembershipService.GetAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get all EventTemplateMemberships.
    /// </summary>
    /// <returns></returns>
    [HttpGet("eventTemplates/{id}/memberships")]
    [ProducesResponseType(typeof(IEnumerable<EventTemplateMembership>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetAllEventTemplateMemberships")]
    public async Task<IActionResult> GetAll(Guid id, CancellationToken ct)
    {
        var result = await _eventTemplateMembershipService.GetByEventTemplateAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Create a new EventTemplate Membership.
    /// </summary>
    /// <param name="eventTemplateId"></param>
    /// <param name="eventTemplateMembership"></param>
    /// <returns></returns>
    [HttpPost("eventTemplates/{eventTemplateId}/memberships")]
    [ProducesResponseType(typeof(EventTemplateMembership), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreateEventTemplateMembership")]
    public async Task<IActionResult> CreateMembership([FromRoute] Guid eventTemplateId, EventTemplateMembership eventTemplateMembership, CancellationToken ct)
    {
        if (!await _authorizationService.AuthorizeAsync<EventTemplate>(eventTemplateId, [SystemPermission.ManageEventTemplates], [EventTemplatePermission.ManageEventTemplate], ct))
            throw new ForbiddenException();

        if (eventTemplateMembership.EventTemplateId != eventTemplateId)
            throw new DataException("The EventTemplateId of the membership must match the EventTemplateId of the URL.");

        var result = await _eventTemplateMembershipService.CreateAsync(eventTemplateMembership, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// Updates a EventTemplateMembership
    /// </summary>
    /// <remarks>
    /// Updates a EventTemplateMembership with the attributes specified
    /// </remarks>
    /// <param name="id">The Id of the Exericse to update</param>
    /// <param name="eventTemplateMembership">The updated EventTemplateMembership values</param>
    /// <param name="ct"></param>
    [HttpPut("EventTemplates/Memberships/{id}")]
    [ProducesResponseType(typeof(EventTemplateMembership), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "updateEventTemplateMembership")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] EventTemplateMembership eventTemplateMembership, CancellationToken ct)
    {
        if (!await _authorizationService.AuthorizeAsync<EventTemplate>(id, [SystemPermission.ManageEventTemplates], [EventTemplatePermission.ManageEventTemplate], ct))
            throw new ForbiddenException();

        var updatedEventTemplateMembership = await _eventTemplateMembershipService.UpdateAsync(id, eventTemplateMembership, ct);
        return Ok(updatedEventTemplateMembership);
    }

    /// <summary>
    /// Delete a EventTemplate Membership.
    /// </summary>
    /// <returns></returns>
    [HttpDelete("eventTemplates/memberships/{id}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteEventTemplateMembership")]
    public async Task<IActionResult> DeleteMembership([FromRoute] Guid id, CancellationToken ct)
    {
        await _eventTemplateMembershipService.DeleteAsync(id, ct);
        return NoContent();
    }


}