// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Alloy.Api.Controllers
{
    public class EventController : BaseController
    {
        private readonly IEventService _eventService;
        private readonly IAuthorizationService _authorizationService;

        public EventController(IEventService eventService, IAuthorizationService authorizationService)
        {
            _eventService = eventService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all Event in the system
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Events in the system.
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        /// <returns></returns>
        [HttpGet("events")]
        [ProducesResponseType(typeof(IEnumerable<Event>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getEvents")]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var list = await _eventService.GetAsync(ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all Events for the indicated EventTemplate
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Events for the EventTemplate.
        /// </remarks>
        /// <returns></returns>
        [HttpGet("eventTemplates/{eventTemplateId}/events")]
        [ProducesResponseType(typeof(IEnumerable<Event>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getEventTemplateEvents")]
        public async Task<IActionResult> GetEventTemplateEvents(string eventTemplateId, CancellationToken ct)
        {
            var list = await _eventService.GetEventTemplateEventsAsync(Guid.Parse(eventTemplateId), ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets the user's Events for the indicated EventTemplate
        /// </summary>
        /// <remarks>
        /// Returns a list of the user's Events for the EventTemplate.
        /// </remarks>
        /// <returns></returns>
        [HttpGet("eventTemplates/{eventTemplateId}/events/mine")]
        [ProducesResponseType(typeof(IEnumerable<Event>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getMyEventTemplateEvents")]
        public async Task<IActionResult> GetMyEventTemplateEvents(string eventTemplateId, bool includeInvites, CancellationToken ct)
        {
            var list = await _eventService.GetMyEventTemplateEventsAsync(Guid.Parse(eventTemplateId), includeInvites, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets the user's Events for the indicated Player View Id
        /// </summary>
        /// <remarks>
        /// Returns a list of the user's Events for the View.
        /// </remarks>
        /// <returns></returns>
        [HttpGet("views/{viewId}/events/mine")]
        [ProducesResponseType(typeof(IEnumerable<Event>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getMyViewEvents")]
        public async Task<IActionResult> GetMyViewEvents(string viewId, CancellationToken ct)
        {
            var list = await _eventService.GetMyViewEventsAsync(Guid.Parse(viewId), ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all events for a user
        /// </summary>
        /// <returns>
        /// Returns a list of events for a user
        /// </returns>
        [HttpGet("events/mine")]
        [ProducesResponseType(typeof(IEnumerable<Event>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetMyEvents")]
        public async Task<IActionResult> GetMyEventsAsync(CancellationToken ct)
        {
            var list = await _eventService.GetMyEventsAsync(ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific Event by id
        /// </summary>
        /// <remarks>
        /// Returns the Event with the id specified
        /// <para />
        /// Accessible to a SuperUser or a User that is a member of a Team within the specified Event
        /// </remarks>
        /// <param name="id">The id of the Event</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("events/{id}")]
        [ProducesResponseType(typeof(Event), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getEvent")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var eventx = await _eventService.GetAsync(id, ct);

            if (eventx == null)
                throw new EntityNotFoundException<Event>();

            return Ok(eventx);
        }

        /// <summary>
        /// Creates a new Event
        /// </summary>
        /// <remarks>
        /// Creates a new Event with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or an Administrator
        /// </remarks>
        /// <param name="eventx">The data to create the Event with</param>
        /// <param name="ct"></param>
        [HttpPost("events")]
        [ProducesResponseType(typeof(Event), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createEvent")]
        public async Task<IActionResult> Create([FromBody] Event eventx, CancellationToken ct)
        {
            var createdEvent = await _eventService.CreateAsync(eventx, ct);
            return CreatedAtAction(nameof(this.Get), new { id = createdEvent.Id }, createdEvent);
        }

        /// <summary>
        /// Creates a new Event from an Event Template
        /// </summary>
        /// <remarks>
        /// Creates a new Event from the specified Event Template
        /// </remarks>
        /// <param name="command"></param>
        /// <param name="ct"></param>
        [HttpPost("eventTemplates/{eventTemplateId}/events2")]
        [ProducesResponseType(typeof(Event), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createEventFromEventTemplate2")]
        public async Task<IActionResult> CreateEventFromEventTemplate2(CreateEventCommand command, CancellationToken ct)
        {
            var createdEvent = await _eventService.LaunchEventFromEventTemplateAsync(command, ct);
            return CreatedAtAction(nameof(this.Get), new { id = createdEvent.Id }, createdEvent);
        }

        /// <summary>
        /// Creates a new Event from an Event Template. Legacy endpoint for backwards compatibility. Use createEventFromEventTemplate2 instead.
        /// </summary>
        /// <remarks>
        /// Creates a new Event from the specified Event Template
        /// </remarks>
        /// <param name="eventTemplateId">The ID of the EventTemplate to use to create the Event</param>
        /// <param name="userId">Id of the User that will be the owner of this Event</param>
        /// <param name="username"></param>
        /// <param name="ct"></param>
        [HttpPost("eventTemplates/{eventTemplateId}/events")]
        [ProducesResponseType(typeof(Event), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createEventFromEventTemplate")]
        public async Task<IActionResult> CreateEventFromEventTemplate(string eventTemplateId, Guid? userId, string username, CancellationToken ct)
        {
            var createdEvent = await _eventService.LaunchEventFromEventTemplateAsync(Guid.Parse(eventTemplateId), userId, username, new List<Guid>(), ct);
            return CreatedAtAction(nameof(this.Get), new { id = createdEvent.Id }, createdEvent);
        }

        /// <summary>
        /// Updates an Event
        /// </summary>
        /// <remarks>
        /// Updates an Event with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified Event
        /// </remarks>
        /// <param name="id">The Id of the Exericse to update</param>
        /// <param name="eventx">The updated Event values</param>
        /// <param name="ct"></param>
        [HttpPut("events/{id}")]
        [ProducesResponseType(typeof(Event), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateEvent")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] Event eventx, CancellationToken ct)
        {
            var updatedEvent = await _eventService.UpdateAsync(id, eventx, ct);
            return Ok(updatedEvent);
        }

        /// <summary>
        /// Deletes an Event
        /// </summary>
        /// <remarks>
        /// Deletes an Event with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified Event
        /// </remarks>
        /// <param name="id">The id of the Event to delete</param>
        /// <param name="ct"></param>
        [HttpDelete("events/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteEvent")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            await _eventService.DeleteAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Ends an Event
        /// </summary>
        /// <remarks>
        /// Ends an Event with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified Event
        /// </remarks>
        /// <param name="id">The id of the Event to end</param>
        /// <param name="ct"></param>
        [HttpDelete("events/{id}/end")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "endEvent")]
        public async Task<IActionResult> End(Guid id, CancellationToken ct)
        {
            await _eventService.EndAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Redeploys the Caster Workspace of an Event
        /// </summary>
        /// <remarks>
        /// Redeploys the Caster Workspace for the Event with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified Event
        /// </remarks>
        /// <param name="id">The id of the Event to redeploy</param>
        /// <param name="ct"></param>
        [HttpPost("events/{id}/redeploy")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "redeployEvent")]
        public async Task<IActionResult> Redeploy(Guid id, CancellationToken ct)
        {
            await _eventService.RedeployAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Generate an invitation code for an event
        /// </summary>
        /// <param name="id"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("events/{id}/invite")]
        [ProducesResponseType(typeof(Event), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "invite")]
        public async Task<ActionResult<EventUser>> Invite(Guid id, CancellationToken ct)
        {
            var state = await _eventService.CreateInviteAsync(id, ct);
            return CreatedAtAction(nameof(this.Get), state);
        }

        [HttpPost("events/enlist/{code}")]
        [ProducesResponseType(typeof(EventUser), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "enlist")]
        public async Task<ActionResult> Enlist(string code, CancellationToken ct)
        {
            var enlistedUser = await _eventService.EnlistAsync(code, ct);

            return CreatedAtAction(nameof(this.Get), enlistedUser);
        }

        /// <summary>
        /// Gets all virtual machines for an event
        /// </summary>
        /// <returns>
        /// Returns a list of virtual machines for an event
        /// </returns>
        [HttpGet("events/{id}/virtual-machines")]
        [ProducesResponseType(typeof(IEnumerable<VirtualMachine>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetEventVirtualMachines")]
        public async Task<IActionResult> GetEventVirtualMachinesAsync(Guid id, CancellationToken ct)
        {
            var list = await _eventService.GetEventVirtualMachinesAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all questions for an event
        /// </summary>
        /// <returns>
        /// Returns a list of questions for an event
        /// </returns>
        [HttpGet("events/{id}/questions")]
        [ProducesResponseType(typeof(IEnumerable<QuestionView>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetEventQuestions")]
        public async Task<IActionResult> GetEventQuestionsAsync(Guid id, CancellationToken ct)
        {
            var list = await _eventService.GetEventQuestionsAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Grade an event
        /// </summary>
        /// <returns>
        /// Returns a score for an event
        /// </returns>
        [HttpPost("events/{id}/qrade")]
        [ProducesResponseType(typeof(IEnumerable<QuestionView>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GradeEvent")]
        public async Task<IActionResult> GradeEventAsync(Guid id, [FromBody] IEnumerable<string> answers, CancellationToken ct)
        {
            var list = await _eventService.GradeEventAsync(id, answers, ct);
            return Ok(list);
        }
    }
}
