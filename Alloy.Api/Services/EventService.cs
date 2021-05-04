using System.Security.Cryptography.X509Certificates;
// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Extensions;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.ViewModels;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Infrastructure.Mappings;
using Caster.Api;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Services
{
  public interface IEventService
  {
    Task<IEnumerable<Event>> GetAsync(CancellationToken ct);
    Task<IEnumerable<Event>> GetEventTemplateEventsAsync(Guid eventTemplateId, CancellationToken ct);
    Task<IEnumerable<Event>> GetMyEventTemplateEventsAsync(Guid eventTemplateId, CancellationToken ct);
    Task<IEnumerable<Event>> GetMyViewEventsAsync(Guid viewId, CancellationToken ct);
    Task<IEnumerable<Event>> GetMyEventsAsync(CancellationToken ct);
    Task<Event> GetAsync(Guid id, CancellationToken ct);
    Task<Event> CreateAsync(Event eventx, CancellationToken ct);
    Task<Event> LaunchEventFromEventTemplateAsync(Guid eventTemplateId, CancellationToken ct);
    Task<Event> UpdateAsync(Guid id, Event eventx, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    Task<Event> EndAsync(Guid eventId, CancellationToken ct);
    Task<Event> RedeployAsync(Guid eventId, CancellationToken ct);
    Task<Event> CreateInviteAsync(Guid eventId, CancellationToken ct);
    Task<EventUser> EnlistAsync(string code, CancellationToken ct);
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
    private readonly IServiceScopeFactory _scopeFactory;

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
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory)
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
      _scopeFactory = scopeFactory;
      _httpClientFactory = httpClientFactory;
    }

    public async Task<IEnumerable<Event>> GetAsync(CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded &&
          !(await _authorizationService.AuthorizeAsync(user, null, new ContentDeveloperRightsRequirement())).Succeeded)
        throw new ForbiddenException();

      IEnumerable<EventEntity> items = null;
      if ((await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded)
      {
        items = await _context.Events.ToListAsync(ct);
      }
      else
      {
        items = await _context.Events.Where(x => x.CreatedBy == user.GetId()).ToListAsync(ct);
      }

      return _mapper.Map<IEnumerable<Event>>(items);
    }

    public async Task<IEnumerable<Event>> GetEventTemplateEventsAsync(Guid eventTemplateId, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded &&
          !(await _authorizationService.AuthorizeAsync(user, null, new ContentDeveloperRightsRequirement())).Succeeded)
      {
        _logger.LogInformation($"User {user.GetId()} is not a content developer.");
        throw new ForbiddenException();
      }

      var items = await _context.Events
          .Where(x => x.EventTemplateId == eventTemplateId)
          .ToListAsync(ct);

      return _mapper.Map<IEnumerable<Event>>(items);
    }

    public async Task<IEnumerable<Event>> GetMyEventTemplateEventsAsync(Guid eventTemplateId, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new BasicRightsRequirement())).Succeeded)
        throw new ForbiddenException();

      var items = await _context.Events
          .Where(x => x.UserId == user.GetId() && x.EventTemplateId == eventTemplateId)
          .ToListAsync(ct);

      return _mapper.Map<IEnumerable<Event>>(items);
    }

    public async Task<IEnumerable<Event>> GetMyViewEventsAsync(Guid viewId, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new BasicRightsRequirement())).Succeeded)
        throw new ForbiddenException();

      var items = await _context.Events
          .Where(x => x.UserId == user.GetId() && x.ViewId == viewId)
          .ToListAsync(ct);

      return _mapper.Map<IEnumerable<Event>>(items);
    }

    public async Task<IEnumerable<Event>> GetMyEventsAsync(CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new BasicRightsRequirement())).Succeeded)
        throw new ForbiddenException();
      var items = await _context.EventUsers
      .Where(u => u.UserId == _user.GetId())
      .Select(u => u.Event).Where(e => e.Status == EventStatus.Active || e.Status == EventStatus.Paused)
      .ToListAsync();
      return _mapper.Map<IEnumerable<Event>>(items);
    }

    public async Task<Event> GetAsync(Guid id, CancellationToken ct)
    {
      var item = await GetTheEventAsync(id, true, false, ct);

      return _mapper.Map<Event>(item);
    }

    public async Task<Event> CreateAsync(Event eventx, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded &&
          !(await _authorizationService.AuthorizeAsync(user, null, new ContentDeveloperRightsRequirement())).Succeeded)
        throw new ForbiddenException();

      eventx.CreatedBy = user.GetId();
      var eventEntity = _mapper.Map<EventEntity>(eventx);

      _context.Events.Add(eventEntity);
      await _context.SaveChangesAsync(ct);

      return _mapper.Map<Event>(eventEntity);
    }

    public async Task<Event> LaunchEventFromEventTemplateAsync(Guid eventTemplateId, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new BasicRightsRequirement())).Succeeded)
        throw new ForbiddenException();
      // check for resource limitations
      if (!(await ResourcesAreAvailableAsync(eventTemplateId, ct)))
      {
        throw new Exception($"The appropriate resources are not available to create an event from the EventTemplate {eventTemplateId}.");
      }
      // make sure the user can launch from the specified eventTemplate
      var eventTemplate = await _context.EventTemplates.SingleOrDefaultAsync(o => o.Id == eventTemplateId, ct);
      if (!eventTemplate.IsPublished &&
          !((await _authorizationService.AuthorizeAsync(user, null, new ContentDeveloperRightsRequirement())).Succeeded ||
              (await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded))
        throw new ForbiddenException();

      // create the event from the eventTemplate
      var eventEntity = await CreateEventEntityAsync(eventTemplateId, ct);
      // add the event to the event queue for AlloyBackgrounsService to process.
      _alloyEventQueue.Add(eventEntity);
      return _mapper.Map<Event>(eventEntity);
    }

    public async Task<Event> UpdateAsync(Guid id, Event eventx, CancellationToken ct)
    {
      var eventEntity = await GetTheEventAsync(id, true, true, ct);
      eventx.ModifiedBy = _user.GetId();
      _mapper.Map(eventx, eventEntity);

      _context.Events.Update(eventEntity);
      await _context.SaveChangesAsync(ct);

      return _mapper.Map(eventEntity, eventx);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
      var eventEntity = await GetTheEventAsync(id, true, true, ct);
      _context.Events.Remove(eventEntity);
      await _context.SaveChangesAsync(ct);

      return true;
    }

    public async Task<Event> EndAsync(Guid eventId, CancellationToken ct)
    {
      try
      {
        var eventEntity = await GetTheEventAsync(eventId, true, false, ct);
        if (eventEntity.EndDate != null)
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
        var eventEntity = await GetTheEventAsync(eventId, true, false, ct);
        if (eventEntity.Status != EventStatus.Active)
        {
          var msg = $"Only an Active Event can be redeployed";
          _logger.LogError(msg);
          throw new Exception(msg);
        }

        var tokenResponse = await ApiClientsExtensions.RequestTokenAsync(_resourceOwnerAuthorizationOptions);
        var casterApiClient = CasterApiExtensions.GetCasterApiClient(_httpClientFactory, _clientOptions.urls.casterApi, tokenResponse);

        var resources = await casterApiClient.TaintResourcesAsync(
            eventEntity.WorkspaceId.Value,
            new Caster.Api.Models.TaintResourcesCommand { SelectAll = true },
            ct);

        if (resources.Any(r => r.Tainted == false))
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

    private async Task<EventEntity> CreateEventEntityAsync(Guid eventTemplateId, CancellationToken ct)
    {
      _logger.LogInformation($"For EventTemplate {eventTemplateId}, Create Event.");
      var userId = _user.GetId();
      var username = _user.Claims.First(c => c.Type.ToLower() == "name").Value;
      var eventEntity = new EventEntity()
      {
        CreatedBy = userId,
        UserId = userId,
        Username = username,
        EventTemplateId = eventTemplateId,
        Status = EventStatus.Creating,
        InternalStatus = InternalEventStatus.LaunchQueued
      };

      _context.Events.Add(eventEntity);
      await _context.SaveChangesAsync(ct);
      _logger.LogInformation($"Event {eventEntity.Id} created for EventTemplate {eventTemplateId}.");

      return eventEntity;
    }

    private async Task<bool> ResourcesAreAvailableAsync(Guid eventTemplateId, CancellationToken ct)
    {
      var resourcesAvailable = true;
      // check to see if this user already has this EventTemplate Implemented
      var notActiveStatuses = new List<EventStatus>() {
                EventStatus.Failed, EventStatus.Ended,
                EventStatus.Expired};
      var items = await _context.Events
          .Where(x => x.UserId == _user.GetId() && x.EventTemplateId == eventTemplateId && !notActiveStatuses.Contains(x.Status))
          .ToListAsync(ct);
      resourcesAvailable = !items.Any();
      if (!resourcesAvailable)
      {
        _logger.LogError($"User {_user.GetId()} already has an active Event for EventTemplate {eventTemplateId}.");
        throw new Exception($"User {_user.GetId()} already has an active Event for EventTemplate {eventTemplateId}.");
      }
      {
        // check to see if this user has too many Events started
        items = await _context.Events
            .Where(x => x.UserId == _user.GetId() && !notActiveStatuses.Contains(x.Status))
            .ToListAsync(ct);
        if (!(await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded)
        {
          var upperLimit = _resourceOptions.CurrentValue.MaxEventsForBasicUser;
          resourcesAvailable = items.Count() < upperLimit;
          if (!resourcesAvailable)
          {
            _logger.LogError($"User {_user.GetId()} already has {upperLimit} Events active.");
            throw new Exception($"User {_user.GetId()} already has {upperLimit} Events active.");
          }
        }
      }

      return resourcesAvailable;
    }

    private async Task<EventEntity> GetTheEventAsync(Guid eventId, bool mustBeOwner, bool mustBeContentDeveloper, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      var isContentDeveloper = (await _authorizationService.AuthorizeAsync(user, null, new ContentDeveloperRightsRequirement())).Succeeded;
      var isSystemAdmin = (await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded;
      if (mustBeContentDeveloper && !isSystemAdmin && !isContentDeveloper)
      {
        _logger.LogInformation($"User {user.GetId()} is not a content developer.");
        throw new ForbiddenException($"User {user.GetId()} is not a content developer.");
      }

      var eventEntity = await _context.Events.SingleOrDefaultAsync(v => v.Id == eventId, ct);

      if (eventEntity == null)
      {
        _logger.LogError($"Event {eventId} was not found.");
        throw new EntityNotFoundException<EventTemplate>();
      }
      else if (mustBeOwner && eventEntity.CreatedBy != user.GetId() && !isSystemAdmin)
      {
        _logger.LogError($"User {user.GetId()} is not permitted to access Event {eventId}.");
        throw new ForbiddenException($"User {user.GetId()} is not permitted to access Event {eventId}.");
      }

      return eventEntity;
    }

    public async Task<Event> CreateInviteAsync(Guid eventId, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      var eventEntity = await GetTheEventAsync(eventId, true, false, ct);

      if (eventEntity.CreatedBy != user.GetId())
      {
        _logger.LogError($"User {user.GetId()} is not the owner, Only owners of an event can create an invite link");
        throw new ForbiddenException($"User {user.GetId()} is not the owner, Only owners of an event can create an invite link");
      }

      if (eventEntity.ShareCode != null)
      {
        return _mapper.Map<Event>(eventEntity);
      }
      eventEntity.ShareCode = Guid.NewGuid().ToString("N");
      await UpdateAsync(eventId, _mapper.Map<Event>(eventEntity), ct);

      return _mapper.Map<Event>(eventEntity);
    }

    private async Task<Event> GetEventByShareCodeAsync(string code, CancellationToken ct)
    {
      var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
      if (!(await _authorizationService.AuthorizeAsync(user, null, new BasicRightsRequirement())).Succeeded)
      {
        throw new ForbiddenException();
      }

      var eventEntity = await _context.Events.Where(e => e.ShareCode == code).SingleOrDefaultAsync();
      if (eventEntity == null)
      {
        throw new EntityNotFoundException<EventEntity>($"Event not found or has been ended");
      }

      return _mapper.Map<Event>(eventEntity);

    }

    public async Task<EventUser> EnlistAsync(string code, CancellationToken ct)
    {
      using (var scope = _scopeFactory.CreateScope())
      {

        // user may not have access to the player api, so we get the resource owner token
        var token = await ApiClientsExtensions.GetToken(scope);
        var playerApiClient = PlayerApiExtensions.GetPlayerApiClient(_httpClientFactory, _clientOptions.urls.playerApi, token);
        var item = await GetEventByShareCodeAsync(code, ct);
        if (item.Status == EventStatus.Active || item.Status == EventStatus.Paused)
        {
          // Add user to player view as first non-admin team. 
          if (item != null && item.ViewId.Value != null)
          {
            await PlayerApiExtensions.AddUserToViewTeamAsync(playerApiClient, item.ViewId.Value, _user.GetId(), ct);
            var eventUser = new EventUserEntity
            {
              EventId = item.Id,
              UserId = _user.GetId(),
              CreatedBy = _user.GetId()
            };
            try
            {
              _context.EventUsers.Add(eventUser);
              await _context.SaveChangesAsync();
              return _mapper.Map<EventUser>(eventUser);
            }
            catch (Exception e)
            {
              throw new InviteException("Invite Failed, Accepted Already");
            }

          }
        }
        throw new InviteException($"Invite Failed, Event Status: {Enum.GetName(typeof(EventStatus), item.Status)}");
      }
    }
  }
}
