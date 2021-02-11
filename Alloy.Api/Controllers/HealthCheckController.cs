/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Alloy.Api.ViewModels;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using Microsoft.AspNetCore.Authorization;

namespace Alloy.Api.Controllers
{
    public class HealthController : BaseController
    {
        private readonly HostedServiceHealthCheck _hostedHealthCheckService;
        private readonly StartupHealthCheck _startupHealthCheckService;
        public HealthController(HostedServiceHealthCheck hostedHealthCheckService,
            StartupHealthCheck startupHealthCheckService)
        {
            _hostedHealthCheckService = hostedHealthCheckService;
            _startupHealthCheckService = startupHealthCheckService;
        }
        
        /// <summary>
        /// Checks the liveliness health endpoint
        /// </summary>
        /// <remarks>
        /// Returns a HealthReport of the liveliness health check
        /// </remarks>
        /// <returns></returns>
        [HttpGet("liveliness")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(HealthReport), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "Health_GetLiveliness")]
        public async Task<IActionResult> GetLiveliness(CancellationToken ct)
        {
            var report = await _hostedHealthCheckService.CheckHealthAsync(null,ct);

            return report.Status == HealthStatus.Healthy ? Ok(report) : StatusCode((int)HttpStatusCode.ServiceUnavailable, report);
        }

        /// <summary>
        /// Checks the readiness health endpoint
        /// </summary>
        /// <remarks>
        /// Returns a HealthReport of the readiness health check
        /// </remarks>
        /// <returns></returns>
        [HttpGet("readiness")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(HealthReport), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "Health_GetReadiness")]
        public async Task<IActionResult> GetReadiness(CancellationToken ct)
        {
            var report = await _startupHealthCheckService.CheckHealthAsync(null,ct);

            return report.Status == HealthStatus.Healthy ? Ok(report) : StatusCode((int)HttpStatusCode.ServiceUnavailable, report);
        }
    }
}