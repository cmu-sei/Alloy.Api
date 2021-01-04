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
using Player.Api.Models;

namespace Alloy.Api.Controllers
{
    public class PlayerController : BaseController
    {
        private readonly IPlayerService _playerService;
        private readonly IAuthorizationService _authorizationService;

        public PlayerController(IPlayerService playerService, IAuthorizationService authorizationService)
        {
            _playerService = playerService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all Views
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Views.
        /// </remarks>
        /// <returns></returns>
        [HttpGet("views")]
        [ProducesResponseType(typeof(IEnumerable<View>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViews")]
        public async Task<IActionResult> GetViews(CancellationToken ct)
        {
            var list = await _playerService.GetViewsAsync(ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets the user as defined in Player
        /// </summary>
        /// <remarks>
        /// Returns a player User
        /// </remarks>
        /// <returns></returns>
        [HttpGet("users/me")]
        [ProducesResponseType(typeof(User), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getUserMe")]
        public async Task<IActionResult> GetUserMe(CancellationToken ct)
        {
            var user = await _playerService.GetUserAsync(ct);
            return Ok(user);
        }

    }

}
