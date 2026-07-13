// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IGroupService _groupService;
        private readonly IAlloyAuthorizationService _authorizationService;

        public GroupController(IGroupService groupService, IAlloyAuthorizationService authorizationService)
        {
            _groupService = groupService;
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
            if (!await _authorizationService.AuthorizeAsync<Group>(id, [SystemPermission.ViewGroups], [GroupPermission.ManageMembership], ct))
                throw new ForbiddenException();

            var result = await _groupService.GetAsync(id, ct);
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
            IEnumerable<Group> result;

            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewGroups], ct))
            {
                result = await _groupService.GetAsync(ct);
            }
            else
            {
                var managedGroupIds = _authorizationService.GetGroupPermissions()
                    .Where(x => x.Permissions.Contains(GroupPermission.ManageMembership))
                    .Select(x => x.GroupId)
                    .ToList();

                if (managedGroupIds.Count == 0)
                    throw new ForbiddenException();

                result = await _groupService.GetAsync(managedGroupIds, ct);
            }

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

            var result = await _groupService.CreateAsync(group, ct);
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

            var updatedGroup = await _groupService.UpdateAsync(id, group, ct);
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

            await _groupService.DeleteAsync(id, ct);
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
            if (!await _authorizationService.AuthorizeAsync<GroupMembership>(id, [SystemPermission.ViewGroups], [GroupPermission.ManageMembership], ct))
                throw new ForbiddenException();

            var result = await _groupService.GetMembershipAsync(id, ct);
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
            if (!await _authorizationService.AuthorizeAsync<Group>(groupId, [SystemPermission.ViewGroups], [GroupPermission.ManageMembership], ct))
                throw new ForbiddenException();

            var result = await _groupService.GetMembershipsForGroupAsync(groupId, ct);
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
            if (!await _authorizationService.AuthorizeAsync<Group>(groupId, [SystemPermission.ManageGroups], [GroupPermission.ManageMembership], ct))
                throw new ForbiddenException();

            groupMembership.GroupId = groupId;
            var result = await _groupService.CreateMembershipAsync(groupMembership, ct);
            return CreatedAtAction(nameof(GetGroupMembership), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update a Group Membership.
        /// </summary>
        /// <returns></returns>
        [HttpPut("groups/memberships/{id}")]
        [ProducesResponseType(typeof(GroupMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditGroupMembership")]
        public async STT.Task<IActionResult> UpdateMembership([FromRoute] Guid id, GroupMembership groupMembership, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync<GroupMembership>(id, [SystemPermission.ManageGroups], [GroupPermission.ManageMembership], ct))
                throw new ForbiddenException();

            var result = await _groupService.UpdateMembershipAsync(id, groupMembership, ct);
            return Ok(result);
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
            if (!await _authorizationService.AuthorizeAsync<GroupMembership>(id, [SystemPermission.ManageGroups], [GroupPermission.ManageMembership], ct))
                throw new ForbiddenException();

            await _groupService.DeleteMembershipAsync(id, ct);
            return NoContent();
        }
    }
}
