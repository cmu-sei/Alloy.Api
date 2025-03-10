// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using Swashbuckle.AspNetCore.Annotations;

namespace Alloy.Api.Controllers
{
    public class SystemRolesController : BaseController
    {
        private readonly IAlloyAuthorizationService _authorizationService;
        private readonly ISystemRoleService _systemRoleService;

        public SystemRolesController(IAlloyAuthorizationService authorizationService, ISystemRoleService systemRoleService)
        {
            _authorizationService = authorizationService;
            _systemRoleService = systemRoleService;
        }

        /// <summary>
        /// Get a single SystemRole.
        /// </summary>
        /// <param name="id">ID of a SystemRole.</param>
        /// <returns></returns>
        [HttpGet("system-roles/{id}")]
        [ProducesResponseType(typeof(SystemRole), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetSystemRole")]
        public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewRoles], ct))
                throw new ForbiddenException();

            var result = await _systemRoleService.GetAsync(id, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get all SystemRoles.
        /// </summary>
        /// <returns></returns>
        [HttpGet("system-roles")]
        [ProducesResponseType(typeof(IEnumerable<SystemRole>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllSystemRoles")]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewRoles], ct))
                throw new ForbiddenException();

            var result = await _systemRoleService.GetAsync(ct);
            return Ok(result);
        }

        /// <summary>
        /// Create a new SystemRole.
        /// </summary>
        /// <param name="systemRole"></param>
        /// <returns></returns>
        [HttpPost("system-roles")]
        [ProducesResponseType(typeof(SystemRole), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateSystemRole")]
        public async Task<IActionResult> Create([FromBody] SystemRole systemRole, CancellationToken ct)
        {
            var result = await _systemRoleService.CreateAsync(systemRole, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update a SystemRole.
        /// </summary>
        /// <param name="id">ID of an SystemRole.</param>
        /// <param name="systemRole"></param>
        /// <returns></returns>
        [HttpPut("system-roles/{id}")]
        [ProducesResponseType(typeof(SystemRole), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "UpdateSystemRole")]
        public async Task<IActionResult> Update([FromRoute] Guid id, SystemRole systemRole, CancellationToken ct)
        {
            var result = await _systemRoleService.UpdateAsync(id, systemRole, ct);
            return Ok(result);
        }

        /// <summary>
        /// Delete a SystemRole.
        /// </summary>
        /// <param name="id">ID of an SystemRole.</param>
        /// <returns></returns>
        [HttpDelete("system-roles/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteSystemRole")]
        public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
        {
            await _systemRoleService.DeleteAsync(id, ct);
            return NoContent();
        }
    }
}
