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
using Steamfitter.Api.Models;

namespace Alloy.Api.Controllers
{
    public class SteamfitterController : BaseController
    {
        private readonly ISteamfitterService _steamfitterService;
        private readonly IAuthorizationService _authorizationService;

        public SteamfitterController(ISteamfitterService steamfitterService, IAuthorizationService authorizationService)
        {
            _steamfitterService = steamfitterService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all ScenarioTemplates
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the ScenarioTemplates.
        /// </remarks>       
        /// <returns></returns>
        [HttpGet("scenarioTemplates")]
        [ProducesResponseType(typeof(IEnumerable<ScenarioTemplate>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getScenarioTemplates")]
        public async Task<IActionResult> GetScenarioTemplates(CancellationToken ct)
        {
            var list = await _steamfitterService.GetScenarioTemplatesAsync(ct);
            return Ok(list);
        }

    }

}

