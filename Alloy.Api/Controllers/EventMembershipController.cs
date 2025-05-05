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

namespace Alloy.Api.Controllers;

public class EventMembershipsController : BaseController
{
    private readonly IAlloyAuthorizationService _authorizationService;
    private readonly IEventMembershipService _eventMembershipService;

    public EventMembershipsController(IAlloyAuthorizationService authorizationService, IEventMembershipService eventMembershipService)
    {
        _authorizationService = authorizationService;
        _eventMembershipService = eventMembershipService;
    }

    /// <summary>
    /// Get a single EventMembership.
    /// </summary>
    /// <param name="id">ID of a EventMembership.</param>
    /// <returns></returns>
    [HttpGet("events/memberships/{id}")]
    [ProducesResponseType(typeof(EventMembership), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetEventMembership")]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _eventMembershipService.GetAsync(id, ct);
        if (!await _authorizationService.AuthorizeAsync<Event>(result.EventId, [SystemPermission.ViewEvents], [EventPermission.ViewEvent], ct))
            throw new ForbiddenException();

        return Ok(result);
    }

    /// <summary>
    /// Get all EventMemberships.
    /// </summary>
    /// <returns></returns>
    [HttpGet("events/{id}/memberships")]
    [ProducesResponseType(typeof(IEnumerable<EventMembership>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetAllEventMemberships")]
    public async Task<IActionResult> GetAll(Guid id, CancellationToken ct)
    {
        if (!await _authorizationService.AuthorizeAsync<Event>(id, [SystemPermission.ViewEvents], [EventPermission.ViewEvent], ct))
            throw new ForbiddenException();

        var result = await _eventMembershipService.GetByEventAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Create a new Event Membership.
    /// </summary>
    /// <param name="eventId"></param>
    /// <param name="eventMembership"></param>
    /// <returns></returns>
    [HttpPost("events/{eventId}/memberships")]
    [ProducesResponseType(typeof(EventMembership), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreateEventMembership")]
    public async Task<IActionResult> CreateMembership([FromRoute] Guid eventId, EventMembership eventMembership, CancellationToken ct)
    {
        if (!await _authorizationService.AuthorizeAsync<Event>(eventMembership.EventId, [SystemPermission.ManageEvents], [EventPermission.ManageEvent], ct))
            throw new ForbiddenException();

        var result = await _eventMembershipService.CreateAsync(eventMembership, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// Updates a EventMembership
    /// </summary>
    /// <remarks>
    /// Updates a EventMembership with the attributes specified
    /// </remarks>
    /// <param name="id">The Id of the Exericse to update</param>
    /// <param name="eventMembership">The updated EventMembership values</param>
    /// <param name="ct"></param>
    [HttpPut("Events/Memberships/{id}")]
    [ProducesResponseType(typeof(EventMembership), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "updateEventMembership")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] EventMembership eventMembership, CancellationToken ct)
    {
        if (!await _authorizationService.AuthorizeAsync<Event>(eventMembership.EventId, [SystemPermission.ManageEvents], [EventPermission.ManageEvent], ct))
            throw new ForbiddenException();

        var updatedEventMembership = await _eventMembershipService.UpdateAsync(id, eventMembership, ct);
        return Ok(updatedEventMembership);
    }

    /// <summary>
    /// Delete a Event Membership.
    /// </summary>
    /// <returns></returns>
    [HttpDelete("events/memberships/{id}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteEventMembership")]
    public async Task<IActionResult> DeleteMembership([FromRoute] Guid id, CancellationToken ct)
    {
        var eventMembership = await _eventMembershipService.GetAsync(id, ct);
        if (!await _authorizationService.AuthorizeAsync<Event>(eventMembership.EventId, [SystemPermission.ManageEvents], [EventPermission.ManageEvent], ct))
            throw new ForbiddenException();

        await _eventMembershipService.DeleteAsync(id, ct);
        return NoContent();
    }


}
