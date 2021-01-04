// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alloy.Api.Extensions;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using Alloy.Api.Infrastructure.Authorization;
using Swashbuckle.AspNetCore.Annotations;
using Caster.Api.Models;

namespace Alloy.Api.Controllers
{
    public class CasterController : BaseController
    {
        private readonly ICasterService _casterService;
        private readonly IAuthorizationService _authorizationService;

        public CasterController(ICasterService casterService, IAuthorizationService authorizationService)
        {
            _casterService = casterService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all Directories
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Directories.
        /// </remarks>       
        /// <returns></returns>
        [HttpGet("directories")]
        [ProducesResponseType(typeof(IEnumerable<Directory>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getDirectories")]
        public async Task<IActionResult> GetDirectories(CancellationToken ct)
        {
            var list = await _casterService.GetDirectoriesAsync(ct);
            return Ok(list);
        }

        // /// <summary>
        // /// Gets all workspaces
        // /// </summary>
        // /// <remarks>
        // /// Returns a list of all of Workspaces.
        // /// </remarks>       
        // /// <returns></returns>
        // [HttpGet("workspaces")]
        // [ProducesResponseType(typeof(IEnumerable<Workspace>), (int)HttpStatusCode.OK)]
        // [SwaggerOperation(OperationId = "getWorkspaces")]
        // public async Task<IActionResult> GetWorkspaces(CancellationToken ct)
        // {
        //     var list = await _casterService.GetWorkspacesAsync(ct);
        //     return Ok(list);
        // }

    }

}

