// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using STT = System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using SAVM = Alloy.Api.ViewModels;
using Swashbuckle.AspNetCore.Annotations;
using Alloy.Api.ViewModels;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;

namespace Alloy.Api.Controllers
{
    public class GroupController : BaseController
    {
        private readonly IGroupService _GroupService;
        private readonly IAlloyAuthorizationService _authorizationService;

        public GroupController(IGroupService GroupService, IAlloyAuthorizationService authorizationService)
        {
            _GroupService = GroupService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Get a single group.
        /// </summary>
        /// <param name="id">ID of an group.</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("groups/{id}")]
        [ProducesResponseType(typeof(Group), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getGroup")]
        public async STT.Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewGroups], ct))
                throw new ForbiddenException();

            var result = await _GroupService.GetAsync(id, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get all groups.
        /// </summary>
        /// <returns></returns>
        [HttpGet("groups")]
        [ProducesResponseType(typeof(IEnumerable<Group>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getAllGroups")]
        public async STT.Task<IActionResult> GetAll(CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewGroups], ct))
                throw new ForbiddenException();

            var result = await _GroupService.GetAsync(ct);
            return Ok(result);
        }

        /// <summary>
        /// Create a new group.
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        [HttpPost("groups")]
        [ProducesResponseType(typeof(Group), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createGroup")]
        public async STT.Task<IActionResult> Create([FromBody] SAVM.Group group, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ManageGroups], ct))
                throw new ForbiddenException();

            var result = await _GroupService.CreateAsync(group, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update a group.
        /// </summary>
        /// <returns></returns>
        [HttpPut("groups/{id}")]
        [ProducesResponseType(typeof(Group), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateGroup")]
        public async STT.Task<IActionResult> Update([FromRoute] Guid id, [FromBody] SAVM.Group group, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ManageGroups], ct))
                throw new ForbiddenException();

            var updatedGroup = await _GroupService.UpdateAsync(id, group, ct);
            return Ok(updatedGroup);
        }

        /// <summary>
        /// Delete a group.
        /// </summary>
        /// <param name="id">ID of an group.</param>
        /// <returns></returns>
        [HttpDelete("groups/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteGroup")]
        public async STT.Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ManageGroups], ct))
                throw new ForbiddenException();

            await _GroupService.DeleteAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Get a single Group Membership.
        /// </summary>
        /// <returns></returns>
        [HttpGet("groups/memberships/{id}")]
        [ProducesResponseType(typeof(GroupMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetGroupMembership")]
        public async STT.Task<IActionResult> GetGroupMembership([FromRoute] Guid id, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewGroups], ct))
                throw new ForbiddenException();

            var result = await _GroupService.GetMembershipAsync(id, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get all Group Memberships of a Group.
        /// </summary>
        /// <returns></returns>
        [HttpGet("groups/{groupId}/memberships")]
        [ProducesResponseType(typeof(IEnumerable<GroupMembership>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetGroupMemberships")]
        public async STT.Task<IActionResult> GetMemberships([FromRoute] Guid groupId, CancellationToken ct)
        {
            var result = await _GroupService.GetMembershipsForGroupAsync(groupId, ct);
            return Ok(result);
        }

        /// <summary>
        /// Create a new Group Membership.
        /// </summary>
        /// <param name="groupId"></param>
        /// <param name="groupMembership"></param>
        /// <returns></returns>
        [HttpPost("groups/{groupId}/memberships")]
        [ProducesResponseType(typeof(GroupMembership), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateGroupMembership")]
        public async STT.Task<IActionResult> CreateMembership([FromRoute] Guid groupId, GroupMembership groupMembership, CancellationToken ct)
        {
            var result = await _GroupService.CreateMembershipAsync(groupMembership, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Delete a Group Membership.
        /// </summary>
        /// <returns></returns>
        [HttpDelete("groups/memberships/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteGroupMembership")]
        public async STT.Task<IActionResult> DeleteMembership([FromRoute] Guid id, CancellationToken ct)
        {
            await _GroupService.DeleteMembershipAsync(id, ct);
            return NoContent();
        }
    }
}