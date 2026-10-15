// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Controllers
{
    public class EventTemplateController : BaseController
    {
        private readonly IEventTemplateService _eventTemplateService;
        private readonly IEventTemplateArchiveService _eventTemplateArchiveService;
        private readonly IAlloyAuthorizationService _authorizationService;

        public EventTemplateController(
            IEventTemplateService eventTemplateService,
            IEventTemplateArchiveService eventTemplateArchiveService,
            IAlloyAuthorizationService authorizationService)
        {
            _eventTemplateService = eventTemplateService;
            _eventTemplateArchiveService = eventTemplateArchiveService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all EventTemplate in the system
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the EventTemplates in the system.
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        /// <returns></returns>
        [HttpGet("eventTemplates")]
        [ProducesResponseType(typeof(IEnumerable<EventTemplate>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getEventTemplates")]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            IEnumerable<EventTemplate> list = new List<EventTemplate>();
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewEventTemplates], ct))
            {
                list = await _eventTemplateService.GetAsync(ct);
            }
            else
            {
                list = await _eventTemplateService.GetByUserAsync(ct);
            }

            // add this user's permissions for each event template
            AddPermissions(list);

            return Ok(list);
        }

        /// <summary>
        /// Gets a specific EventTemplate by id
        /// </summary>
        /// <remarks>
        /// Returns the EventTemplate with the id specified
        /// <para />
        /// Accessible to a SuperUser or a User that is a member of a Team within the specified EventTemplate
        /// </remarks>
        /// <param name="id">The id of the EventTemplate</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("eventTemplates/{id}")]
        [ProducesResponseType(typeof(EventTemplate), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.NotFound)]
        [SwaggerOperation(OperationId = "getEventTemplate")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync<EventTemplate>(id, [SystemPermission.ViewEventTemplates], [EventTemplatePermission.ViewEventTemplate], ct))
                throw new ForbiddenException();

            var eventTemplate = await _eventTemplateService.GetAsync(id, ct);

            if (eventTemplate == null)
                throw new EntityNotFoundException<EventTemplate>();

            // add this user's permissions for the event template
            AddPermissions(eventTemplate);

            return Ok(eventTemplate);
        }

        /// <summary>
        /// Creates a new EventTemplate
        /// </summary>
        /// <remarks>
        /// Creates a new EventTemplate with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or an Administrator
        /// </remarks>
        /// <param name="eventTemplate">The data to create the EventTemplate with</param>
        /// <param name="ct"></param>
        [HttpPost("eventTemplates")]
        [ProducesResponseType(typeof(EventTemplate), (int)HttpStatusCode.Created)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        [SwaggerOperation(OperationId = "createEventTemplate")]
        public async Task<IActionResult> Create([FromBody] EventTemplate eventTemplate, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.CreateEventTemplates], ct))
                throw new ForbiddenException();

            var createdEventTemplate = await _eventTemplateService.CreateAsync(eventTemplate, ct);
            // add this user's permissions for the event template
            AddPermissions(createdEventTemplate);
            return CreatedAtAction(nameof(this.Get), new { id = createdEventTemplate.Id }, createdEventTemplate);
        }

        /// <summary>
        /// Updates an EventTemplate
        /// </summary>
        /// <remarks>
        /// Updates an EventTemplate with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified EventTemplate
        /// </remarks>
        /// <param name="id">The Id of the Exericse to update</param>
        /// <param name="eventTemplate">The updated EventTemplate values</param>
        /// <param name="ct"></param>
        [HttpPut("eventTemplates/{id}")]
        [ProducesResponseType(typeof(EventTemplate), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        [SwaggerOperation(OperationId = "updateEventTemplate")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] EventTemplate eventTemplate, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync<EventTemplate>(id, [SystemPermission.EditEventTemplates], [EventTemplatePermission.EditEventTemplate], ct))
                throw new ForbiddenException();

            var updatedEventTemplate = await _eventTemplateService.UpdateAsync(id, eventTemplate, ct);
            // add this user's permissions for the event template
            AddPermissions(updatedEventTemplate);
            return Ok(updatedEventTemplate);
        }

        /// <summary>
        /// Deletes an EventTemplate
        /// </summary>
        /// <remarks>
        /// Deletes an EventTemplate with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified EventTemplate
        /// </remarks>
        /// <param name="id">The id of the EventTemplate to delete</param>
        /// <param name="ct"></param>
        [HttpDelete("eventTemplates/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.NotFound)]
        [SwaggerOperation(OperationId = "deleteEventTemplate")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync<EventTemplate>(id, [SystemPermission.ManageEventTemplates], [EventTemplatePermission.ManageEventTemplate], ct))
                throw new ForbiddenException();

            await _eventTemplateService.DeleteAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Exports EventTemplates
        /// </summary>
        /// <remarks>
        /// Exports the specified EventTemplates, or all EventTemplates if none are specified, as a zip or tar.gz
        /// archive containing event_templates.json. The Player View, Caster Directory and Steamfitter Scenario
        /// Template are exported as ids, each with its name at export time. Memberships are not exported.
        /// <para />
        /// Exporting specific EventTemplates requires permission to view each of them.
        /// Exporting all EventTemplates requires the ViewEventTemplates system permission.
        /// </remarks>
        /// <param name="ids">The ids of the EventTemplates to export. If empty, all EventTemplates are exported.</param>
        /// <param name="archiveType">The archive format</param>
        /// <param name="ct"></param>
        [HttpGet("eventTemplates/actions/export")]
        [ProducesResponseType(typeof(FileResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "exportEventTemplates")]
        public async Task<IActionResult> Export([FromQuery] Guid[] ids, [FromQuery] ArchiveType archiveType, CancellationToken ct)
        {
            ids ??= [];

            if (ids.Length == 0)
            {
                if (!await _authorizationService.AuthorizeAsync([SystemPermission.ViewEventTemplates], ct))
                    throw new ForbiddenException();
            }
            else
            {
                foreach (var id in ids.Distinct())
                {
                    if (!await _authorizationService.AuthorizeAsync<EventTemplate>(id, [SystemPermission.ViewEventTemplates], [EventTemplatePermission.ViewEventTemplate], ct))
                        throw new ForbiddenException();
                }
            }

            var result = await _eventTemplateArchiveService.ExportAsync(ids, archiveType, ct);
            return File(result.Data, result.Type, result.Name);
        }

        /// <summary>
        /// Imports EventTemplates
        /// </summary>
        /// <remarks>
        /// Imports the EventTemplates in an archive created by exportEventTemplates. Ids are preserved, so an
        /// EventTemplate whose id already exists is not imported; use clone to copy a template within one system.
        /// <para />
        /// Each Player View, Caster Directory and Steamfitter Scenario Template is kept if it exists and is visible
        /// to the caller, otherwise it is matched by name if requested, otherwise it is cleared and a warning is
        /// returned. A published template that loses a reference is imported unpublished.
        /// <para />
        /// Requires the CreateEventTemplates system permission
        /// </remarks>
        /// <param name="form">The archive, and whether to match references by name</param>
        /// <param name="ct"></param>
        [HttpPost("eventTemplates/actions/import")]
        [ProducesResponseType(typeof(ImportEventTemplatesResult), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        [SwaggerOperation(OperationId = "importEventTemplates")]
        public async Task<IActionResult> Import([FromForm] ImportEventTemplatesForm form, CancellationToken ct)
        {
            if (!await _authorizationService.AuthorizeAsync([SystemPermission.CreateEventTemplates], ct))
                throw new ForbiddenException();

            if (form?.Archive == null)
                throw new ValidationException("An archive file is required");

            if (!ArchiveTypeHelpers.IsValidFileName(form.Archive.FileName))
                throw new ValidationException($"File extension must be one of {string.Join(", ", ArchiveTypeHelpers.GetValidExtensions())}");

            await using var stream = form.Archive.OpenReadStream();
            var result = await _eventTemplateArchiveService.ImportAsync(stream, form.Archive.FileName, form.MatchReferencesByName, ct);

            foreach (var eventTemplate in result.Imported)
            {
                AddPermissions(eventTemplate);
            }

            return Ok(result);
        }

        private void AddPermissions(IEnumerable<EventTemplate> list)
        {
            foreach (var item in list)
            {
                AddPermissions(item);
            }
        }

        private void AddPermissions(EventTemplate item)
        {
            item.EventTemplatePermissions =
            _authorizationService.GetEventTemplatePermissions(item.Id).Select((m) => String.Join(",", m.Permissions))
            .Concat(_authorizationService.GetSystemPermissions().Select((m) => m.ToString()));
        }

    }
}
