// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.ViewModels;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Steamfitter.Api.Client;

namespace Alloy.Api.Services
{
    public interface IEventService
    {
        Task<IEnumerable<Event>> GetAsync(CancellationToken ct);
        Task<IEnumerable<Event>> GetEventTemplateEventsAsync(Guid eventTemplateId, CancellationToken ct);
        Task<IEnumerable<Event>> GetMyEventTemplateEventsAsync(Guid eventTemplateId, bool includeInvites, CancellationToken ct);
        Task<IEnumerable<Event>> GetMyViewEventsAsync(Guid viewId, CancellationToken ct);
        Task<IEnumerable<Event>> GetMyEventsAsync(CancellationToken ct);
        Task<Event> GetAsync(Guid id, CancellationToken ct);
        Task<Event> CreateAsync(Event eventx, CancellationToken ct);
        Task<Event> LaunchEventFromEventTemplateAsync(Guid eventTemplateId, Guid? userId, string username, List<Guid> additionalUserIds, CancellationToken ct);
        Task<Event> LaunchEventFromEventTemplateAsync(CreateEventCommand command, CancellationToken ct);
        Task<Event> UpdateAsync(Guid id, Event eventx, CancellationToken ct);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct);
        Task<Event> EndAsync(Guid eventId, CancellationToken ct);
        Task<Event> RedeployAsync(Guid eventId, CancellationToken ct);
        Task<Event> CreateInviteAsync(Guid eventId, CancellationToken ct);
        Task<Event> EnlistAsync(string code, CancellationToken ct);
        Task<IEnumerable<VirtualMachine>> GetEventVirtualMachinesAsync(Guid eventId, CancellationToken ct);
        Task<IEnumerable<QuestionView>> GetEventQuestionsAsync(Guid eventId, CancellationToken ct);
        Task<IEnumerable<QuestionView>> GradeEventAsync(Guid eventId, IEnumerable<string> answers, CancellationToken ct);
    }

    public class EventService : IEventService
    {
        private readonly AlloyContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;
        private readonly ICasterService _casterService;
        private readonly IPlayerService _playerService;
        private readonly ISteamfitterService _steamfitterService;
        private readonly IAlloyEventQueue _alloyEventQueue;
        private readonly ILogger<EventService> _logger;
        private readonly IOptionsMonitor<ResourceOptions> _resourceOptions;
        private readonly IUserClaimsService _claimsService;
        private readonly ResourceOwnerAuthorizationOptions _resourceOwnerAuthorizationOptions;
        private readonly ClientOptions _clientOptions;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceProvider _serviceProvider;


        public EventService(
            AlloyContext context,
            IAuthorizationService authorizationService,
            IPrincipal user,
            IMapper mapper,
            IPlayerService playerService,
            ISteamfitterService steamfitterService,
            ICasterService casterService,
            IAlloyEventQueue alloyBackgroundService,
            ILogger<EventService> logger,
            IOptionsMonitor<ResourceOptions> resourceOptions,
            IUserClaimsService claimsService,
            ResourceOwnerAuthorizationOptions resourceOwnerAuthorizationOptions,
            ClientOptions clientOptions,
            IHttpClientFactory httpClientFactory,
            IServiceProvider serviceProvider)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _casterService = casterService;
            _playerService = playerService;
            _steamfitterService = steamfitterService;
            _alloyEventQueue = alloyBackgroundService;
            _logger = logger;
            _resourceOptions = resourceOptions;
            _claimsService = claimsService;
            _resourceOwnerAuthorizationOptions = resourceOwnerAuthorizationOptions;
            _clientOptions = clientOptions;
            _httpClientFactory = httpClientFactory;
            _serviceProvider = serviceProvider;
        }

        public async Task<IEnumerable<Event>> GetAsync(CancellationToken ct)
        {
            var items = await _context.Events.ToListAsync(ct);

            return _mapper.Map<IEnumerable<Event>>(items);
        }

        public async Task<IEnumerable<Event>> GetEventTemplateEventsAsync(Guid eventTemplateId, CancellationToken ct)
        {
            var items = await _context.Events
                .Where(x => x.EventTemplateId == eventTemplateId)
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<Event>>(items);
        }

        public async Task<IEnumerable<Event>> GetMyEventTemplateEventsAsync(Guid eventTemplateId, bool includeInvites, CancellationToken ct)
        {
            IQueryable<EventEntity> query;
            var userId = _user.GetId();

            if (includeInvites)
            {
                query = _context.EventMemberships
                    .Include(m => m.Event)
                    .Where(m => m.Event.EventTemplateId == eventTemplateId && (m.Event.UserId == userId || m.UserId == userId))
                    .Select(m => m.Event);
            }
            else
            {
                query = _context.Events
                    .Where(x => x.EventTemplateId == eventTemplateId && x.UserId == userId);
            }

            var items = await query.ToListAsync(ct);

            return _mapper.Map<IEnumerable<Event>>(items);
        }

        public async Task<IEnumerable<Event>> GetMyViewEventsAsync(Guid viewId, CancellationToken ct)
        {
            var userId = _user.GetId();
            var items = await _context.EventMemberships
                .Where(m => m.Event.ViewId == viewId &&
                            m.Event.UserId == userId ||
                            m.UserId == userId)
                .Select(m => m.Event)
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<Event>>(items);
        }

        public async Task<IEnumerable<Event>> GetMyEventsAsync(CancellationToken ct)
        {
            var userId = _user.GetId();
            var items = await _context.EventMemberships
                .Where(x => x.UserId == userId && (x.Event.Status == EventStatus.Active || x.Event.Status == EventStatus.Paused))
                .Select(m => m.Event)
                .ToListAsync();

            return _mapper.Map<IEnumerable<Event>>(items);
        }

        public async Task<Event> GetAsync(Guid id, CancellationToken ct)
        {
            var item = await GetTheEventAsync(id, ct);

            return _mapper.Map<Event>(item);
        }

        public async Task<Event> CreateAsync(Event eventx, CancellationToken ct)
        {
            eventx.CreatedBy = _user.GetId();
            var eventEntity = _mapper.Map<EventEntity>(eventx);

            _context.Events.Add(eventEntity);
            await _context.SaveChangesAsync(ct);

            return _mapper.Map<Event>(eventEntity);
        }

        public async Task<Event> LaunchEventFromEventTemplateAsync(CreateEventCommand command, CancellationToken ct)
        {
            return await LaunchEventFromEventTemplateAsync(command.EventTemplateId, command.UserId, command.Username, command.AdditionalUserIds, ct);
        }

        public async Task<Event> LaunchEventFromEventTemplateAsync(Guid eventTemplateId, Guid? userId, string username, List<Guid> additionalUserIds, CancellationToken ct)
        {
            if (!userId.HasValue)
            {
                userId = _user.GetId();
            }

            // check for resource limitations
            if (!await ResourcesAreAvailableAsync(eventTemplateId, userId.Value, ct))
                throw new Exception($"The appropriate resources are not available to create an event from the EventTemplate {eventTemplateId}.");

            // create the event from the eventTemplate
            var eventEntity = await CreateEventEntityAsync(eventTemplateId, userId.Value, username, additionalUserIds, ct);
            await _claimsService.RefreshClaims((Guid)userId);
            // add the event to the event queue for AlloyBackgroundService to process.
            _alloyEventQueue.Add(eventEntity);
            return _mapper.Map<Event>(eventEntity);
        }

        public async Task<Event> UpdateAsync(Guid id, Event eventx, CancellationToken ct)
        {
            var eventEntity = await GetTheEventAsync(id, ct);
            eventx.ModifiedBy = _user.GetId();
            _mapper.Map(eventx, eventEntity);

            _context.Events.Update(eventEntity);
            await _context.SaveChangesAsync(ct);

            return _mapper.Map(eventEntity, eventx);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
        {
            var eventEntity = await GetTheEventAsync(id, ct);
            _context.Events.Remove(eventEntity);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        public async Task<Event> EndAsync(Guid eventId, CancellationToken ct)
        {
            try
            {
                var eventEntity = await GetTheEventAsync(eventId, ct);
                if (eventEntity.Status != EventStatus.Failed && eventEntity.EndDate != null)
                {
                    var msg = $"Event {eventEntity.Id} has already been ended";
                    _logger.LogError(msg);
                    throw new Exception(msg);
                }
                eventEntity.EndDate = DateTime.UtcNow;
                eventEntity.Status = EventStatus.Ending;
                eventEntity.InternalStatus = InternalEventStatus.EndQueued;
                await _context.SaveChangesAsync(ct);
                // add the event to the event queue for AlloyBackgrounsService to process the caster destroy.
                _alloyEventQueue.Add(eventEntity);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error ending Event {eventId}.", ex);
                throw;
            }

            return await GetAsync(eventId, ct);
        }

        public async Task<Event> RedeployAsync(Guid eventId, CancellationToken ct)
        {
            try
            {
                var eventEntity = await GetTheEventAsync(eventId, ct);
                if (eventEntity.Status != EventStatus.Active)
                {
                    var msg = $"Only an Active Event can be redeployed";
                    _logger.LogError(msg);
                    throw new Exception(msg);
                }

                var tokenResponse = await ApiClientsExtensions.RequestTokenAsync(_resourceOwnerAuthorizationOptions, _httpClientFactory.CreateClient());
                var casterApiClient = CasterApiExtensions.GetCasterApiClient(_httpClientFactory, _clientOptions.urls.casterApi, tokenResponse);

                var result = await casterApiClient.TaintResourcesAsync(
                    eventEntity.WorkspaceId.Value,
                    new Caster.Api.Client.TaintResourcesCommand { SelectAll = true },
                    ct);

                if (result.Resources.Any(r => r.Tainted == false))
                {
                    var msg = $"Taint failed";
                    _logger.LogError(msg);
                    throw new Exception(msg);
                }

                eventEntity.Status = EventStatus.Planning;
                eventEntity.InternalStatus = InternalEventStatus.PlanningRedeploy;
                await _context.SaveChangesAsync(ct);
                // add the event to the event queue for AlloyBackgrounsService to process the caster destroy.
                _alloyEventQueue.Add(eventEntity);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error ending Event {eventId}.", ex);
                throw;
            }

            return await GetAsync(eventId, ct);
        }

        private async Task<EventEntity> CreateEventEntityAsync(Guid eventTemplateId, Guid userId, string username, List<Guid> additionalUserIds, CancellationToken ct)
        {
            _logger.LogInformation($"For EventTemplate {eventTemplateId}, Create Event.");

            if (string.IsNullOrEmpty(username))
            {
                username = _user.Claims.First(c => c.Type.ToLower() == "name").Value;
            }

            var eventEntity = new EventEntity()
            {
                Id = Guid.NewGuid(),
                CreatedBy = userId,
                UserId = userId,
                Username = username,
                EventTemplateId = eventTemplateId,
                Status = EventStatus.Creating,
                InternalStatus = InternalEventStatus.LaunchQueued
            };
            _context.Events.Add(eventEntity);
            // add the event creator role to this event creator
            var eventMembership = new EventMembershipEntity()
            {
                UserId = userId,
                EventId = eventEntity.Id,
                RoleId = EventRoleDefaults.EventCreatorRoleId
            };
            _context.EventMemberships.Add(eventMembership);
            // add any additional users
            if (additionalUserIds != null)
            {
                foreach (var additionalUserId in additionalUserIds.Where(m => m != userId))
                {
                    eventMembership = new EventMembershipEntity()
                    {
                        UserId = additionalUserId,
                        EventId = eventEntity.Id,
                        RoleId = EventRoleDefaults.EventMemberRoleId
                    };
                    _context.EventMemberships.Add(eventMembership);
                }
            }
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation($"Event {eventEntity.Id} created for EventTemplate {eventTemplateId}.");

            return eventEntity;
        }

        private async Task<bool> ResourcesAreAvailableAsync(Guid eventTemplateId, Guid userId, CancellationToken ct)
        {
            var resourcesAvailable = true;
            // check to see if this user already has this EventTemplate Implemented
            var notActiveStatuses = new List<EventStatus>() {
                EventStatus.Failed, EventStatus.Ended,
                EventStatus.Expired};
            var items = await _context.Events
                .Where(x => x.UserId == userId && x.EventTemplateId == eventTemplateId && !notActiveStatuses.Contains(x.Status))
                .ToListAsync(ct);
            resourcesAvailable = !items.Any();
            if (!resourcesAvailable)
            {
                _logger.LogError($"User {userId} already has an active Event for EventTemplate {eventTemplateId}.");
                throw new Exception($"User {userId} already has an active Event for EventTemplate {eventTemplateId}.");
            }
            {
                // check to see if this user has too many Events started
                items = await _context.Events
                    .Where(x => x.UserId == userId && !notActiveStatuses.Contains(x.Status))
                    .ToListAsync(ct);
                var upperLimit = _resourceOptions.CurrentValue.MaxEventsForBasicUser;
                resourcesAvailable = items.Count() < upperLimit;
                if (!resourcesAvailable)
                {
                    _logger.LogError($"User {userId} already has {upperLimit} Events active.");
                    throw new Exception($"User {userId} already has {upperLimit} Events active.");
                }
            }

            return resourcesAvailable;
        }

        private async Task<EventEntity> GetTheEventAsync(Guid eventId, CancellationToken ct)
        {
            var eventEntity = await _context.Events.SingleOrDefaultAsync(v => v.Id == eventId, ct);

            if (eventEntity == null)
            {
                _logger.LogError($"Event {eventId} was not found.");
                throw new EntityNotFoundException<EventTemplate>();
            }

            return eventEntity;
        }

        public async Task<Event> CreateInviteAsync(Guid eventId, CancellationToken ct)
        {
            var eventEntity = await GetTheEventAsync(eventId, ct);

            if (eventEntity.CreatedBy != _user.GetId())
            {
                _logger.LogError($"User {_user.GetId()} is not the owner, Only owners of an event can create an invite link");
                throw new ForbiddenException($"User {_user.GetId()} is not the owner, Only owners of an event can create an invite link");
            }

            if (eventEntity.ShareCode != null)
            {
                return _mapper.Map<Event>(eventEntity);
            }
            eventEntity.ShareCode = Guid.NewGuid().ToString("N");

            _context.Events.Update(eventEntity);
            await _context.SaveChangesAsync();

            return _mapper.Map<Event>(eventEntity);
        }

        private async Task<Event> GetEventByShareCodeAsync(string code, CancellationToken ct)
        {
            var eventEntity = await _context.Events.Where(e => e.ShareCode == code).SingleOrDefaultAsync();
            if (eventEntity == null)
            {
                throw new EntityNotFoundException<EventEntity>($"Event not found or has been ended");
            }

            return _mapper.Map<Event>(eventEntity);

        }

        public async Task<Event> EnlistAsync(string code, CancellationToken ct)
        {
            var userId = _user.GetId();
            // user may not have access to the player api, so we get the resource owner token
            var token = await ApiClientsExtensions.GetToken(_serviceProvider);
            var playerApiClient = PlayerApiExtensions.GetPlayerApiClient(_httpClientFactory, _clientOptions.urls.playerApi, token);
            var steamfitterApiClient = SteamfitterApiExtensions.GetSteamfitterApiClient(_httpClientFactory, _clientOptions.urls.steamfitterApi, token);

            var alloyEvent = await GetEventByShareCodeAsync(code, ct);
            if (alloyEvent.Status == EventStatus.Active || alloyEvent.Status == EventStatus.Paused)
            {
                if (alloyEvent != null)
                {
                    if (alloyEvent.ViewId.HasValue)
                    {
                        await PlayerApiExtensions.AddUserToViewTeamAsync(playerApiClient, alloyEvent.ViewId.Value, userId, ct);
                    }

                    if (alloyEvent.ScenarioId.HasValue)
                    {
                        try
                        {
                            var user = await steamfitterApiClient.GetUserAsync(userId, ct);
                        }
                        catch (System.Exception)
                        {
                            var newUser = new Steamfitter.Api.Client.User()
                            {
                                Id = userId,
                                Name = _user.FindFirst("Name").Value
                            };
                            await steamfitterApiClient.CreateUserAsync(newUser);
                        }

                        var scenarioMembership = new ScenarioMembership() { UserId = userId, ScenarioId = alloyEvent.ScenarioId.Value };
                        await steamfitterApiClient.CreateScenarioMembershipAsync(alloyEvent.ScenarioId.Value, scenarioMembership, ct);
                    }

                    try
                    {
                        var entity = await _context.EventMemberships.Where(m => m.UserId == userId && m.EventId == alloyEvent.Id).FirstOrDefaultAsync();
                        if (entity == null)
                        {
                            var eventMembership = new EventMembershipEntity
                            {
                                EventId = alloyEvent.Id,
                                UserId = userId,
                                RoleId = EventRoleDefaults.EventMemberRoleId
                            };
                            _context.EventMemberships.Add(eventMembership);
                            var eventTemplateMembership = new EventTemplateMembershipEntity
                            {
                                EventTemplateId = (Guid)alloyEvent.EventTemplateId,
                                UserId = userId,
                                RoleId = EventTemplateRoleEntityDefaults.EventTemplateReadOnlyRoleId
                            };
                            _context.EventTemplateMemberships.Add(eventTemplateMembership);
                            await _context.SaveChangesAsync();
                        }

                        return alloyEvent;
                    }
                    catch (Exception)
                    {
                        throw new InviteException("Invite Failed, Accepted Already");
                    }
                }
            }
            throw new InviteException($"Invite Failed, Event Status: {Enum.GetName(typeof(EventStatus), alloyEvent.Status)}");
        }

        public async Task<IEnumerable<VirtualMachine>> GetEventVirtualMachinesAsync(Guid eventId, CancellationToken ct)
        {
            var list = new List<VirtualMachine>();

            var evt = await GetTheEventAsync(eventId, ct);

            if (evt != null && evt.WorkspaceId.HasValue)
            {
                var resources = await _casterService.GetWorkspaceResourcesAsync(evt.WorkspaceId.Value, ct);

                foreach (var resource in resources.Where(x => x.Type == "crucible_player_virtual_machine"))
                {
                    var r = await _casterService.RefreshResourceAsync(evt.WorkspaceId.Value, resource, ct);
                    list.Add(new VirtualMachine
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Url = ((JsonElement)r.Attributes).GetProperty("url").ToString()
                    });
                }
            }

            return list;
        }

        public async Task<IEnumerable<QuestionView>> GetEventQuestionsAsync(Guid eventId, CancellationToken ct)
        {
            var list = new List<QuestionView>();

            var evt = await GetTheEventAsync(eventId, ct);

            if (evt != null && evt.WorkspaceId.HasValue)
            {
                try
                {
                    var outputsObj = await _casterService.GetWorkspaceOutputsAsync(evt.WorkspaceId.Value, ct);
                    var json = JsonSerializer.Serialize(outputsObj);
                    var root = JsonSerializer.Deserialize<Root>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    foreach (var question in root.Questions.Value)
                    {
                        list.Add(new QuestionView(question));
                    }
                }
                catch (Exception)
                {

                }
            }

            return list;
        }

        public async Task<IEnumerable<QuestionView>> GradeEventAsync(Guid eventId, IEnumerable<string> answers, CancellationToken ct)
        {
            var questionViews = new List<QuestionView>();

            var evt = await GetTheEventAsync(eventId, ct);

            if (evt != null && evt.WorkspaceId.HasValue)
            {
                try
                {
                    var outputsObj = await _casterService.GetWorkspaceOutputsAsync(evt.WorkspaceId.Value, ct);
                    var json = JsonSerializer.Serialize(outputsObj);
                    var root = JsonSerializer.Deserialize<Root>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    for (var i = 0; i < root.Questions.Value.Count(); i++)
                    {
                        var question = root.Questions.Value[i];
                        var questionView = new QuestionView(question);
                        var answer = answers.ElementAt(i);

                        if (question.Answer == answer)
                        {
                            questionView.IsCorrect = true;
                        }

                        if (!string.IsNullOrEmpty(answer))
                        {
                            questionView.Answer = answer;
                            questionView.IsGraded = true;
                        }

                        questionViews.Add(questionView);
                    }
                }
                catch (Exception)
                {

                }
            }

            return questionViews;
        }
    }
}
