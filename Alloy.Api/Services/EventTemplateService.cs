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
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Extensions;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Services
{
    public interface IEventTemplateService
    {
        Task<IEnumerable<ViewModels.EventTemplate>> GetAsync(CancellationToken ct);
        Task<ViewModels.EventTemplate> GetAsync(Guid id, CancellationToken ct);
        // Task<IEnumerable<ViewModels.EventTemplate>> GetByUserIdAsync(Guid userId, CancellationToken ct);
        Task<ViewModels.EventTemplate> CreateAsync(ViewModels.EventTemplate eventTemplate, CancellationToken ct);
        Task<ViewModels.EventTemplate> UpdateAsync(Guid id, ViewModels.EventTemplate eventTemplate, CancellationToken ct);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    }

    public class EventTemplateService : IEventTemplateService
    {
        private readonly AlloyContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;
        private readonly ILogger<EventTemplateService> _logger;

        public EventTemplateService(
            AlloyContext context,
            IAuthorizationService authorizationService,
            IPrincipal user,
            IMapper mapper,
            ILogger<EventTemplateService> logger)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// Get all eventTemplates
        /// </summary>
        /// <param name="ct"></param>
        /// <returns>EventTemplates</returns>
        public async Task<IEnumerable<ViewModels.EventTemplate>> GetAsync(CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new BasicRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            List<EventTemplateEntity> items;
            if ((await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRightsRequirement())).Succeeded ||
                (await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded)
            {
                items = await _context.EventTemplates.ToListAsync(ct);
            }
            else
            {
                items = await _context.EventTemplates.Where(d => d.IsPublished).ToListAsync(ct);
            }

            return _mapper.Map<IEnumerable<EventTemplate>>(items);
        }

        /// <summary>
        /// Get a single EventTemplate
        /// </summary>
        /// <param name="id">Guid</param>
        /// <param name="ct"></param>
        /// <returns>The EventTemplate</returns>
        public async Task<ViewModels.EventTemplate> GetAsync(Guid id, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new BasicRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var item = await _context.EventTemplates
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (!item.IsPublished &&
                !(
                    (await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRightsRequirement())).Succeeded ||
                    (await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded))
            {
                if (!await _context.EventUsers.AnyAsync(x =>
                    x.UserId == _user.GetId() &&
                    x.Event.EventTemplateId == id &&
                    x.Event.Status != EventStatus.Ended &&
                    x.Event.Status != EventStatus.Failed &&
                    x.Event.Status != EventStatus.Expired, ct))
                {
                    throw new ForbiddenException();
                }
            }


            return _mapper.Map<EventTemplate>(item);
        }

        /// <summary>
        /// Create a EventTemplate
        /// </summary>
        /// <param name="eventTemplate"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<ViewModels.EventTemplate> CreateAsync(ViewModels.EventTemplate eventTemplate, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRightsRequirement())).Succeeded &&
                !(await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded)
            {
                throw new ForbiddenException();
            }

            eventTemplate.CreatedBy = _user.GetId();
            var eventTemplateEntity = _mapper.Map<EventTemplateEntity>(eventTemplate);

            _context.EventTemplates.Add(eventTemplateEntity);
            await _context.SaveChangesAsync(ct);

            return await GetAsync(eventTemplateEntity.Id, ct);
        }

        /// <summary>
        /// update the EventTemplate
        /// </summary>
        /// <param name="id">Guid</param>
        /// <param name="eventTemplate">the new information</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<ViewModels.EventTemplate> UpdateAsync(Guid id, ViewModels.EventTemplate eventTemplate, CancellationToken ct)
        {
            var eventTemplateEntity = await GetTheEventTemplateAsync(id, true, true, ct);
            eventTemplate.ModifiedBy = _user.GetId();
            _mapper.Map(eventTemplate, eventTemplateEntity);

            _context.EventTemplates.Update(eventTemplateEntity);
            await _context.SaveChangesAsync(ct);

            return _mapper.Map(eventTemplateEntity, eventTemplate);
        }

        /// <summary>
        /// delete the eventTemplate
        /// </summary>
        /// <param name="id">Guid</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
        {
            var eventTemplateEntity = await GetTheEventTemplateAsync(id, true, true, ct);

            if (eventTemplateEntity == null)
                throw new EntityNotFoundException<EventTemplate>();

            _context.EventTemplates.Remove(eventTemplateEntity);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        private async Task<EventTemplateEntity> GetTheEventTemplateAsync(Guid eventTemplateId, bool mustBeOwner, bool mustBeContentDeveloper, CancellationToken ct)
        {
            var isContentDeveloper = (await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRightsRequirement())).Succeeded;
            var isSystemAdmin = (await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded;
            if (mustBeContentDeveloper && !isContentDeveloper && !isSystemAdmin)
            {
                _logger.LogInformation($"User {_user.GetId()} is not a content developer.");
                throw new ForbiddenException();
            }

            var eventTemplateEntity = await _context.EventTemplates.SingleOrDefaultAsync(v => v.Id == eventTemplateId, ct);

            if (eventTemplateEntity == null)
            {
                _logger.LogError($"EventTemplate {eventTemplateId} was not found.");
                throw new EntityNotFoundException<EventTemplate>();
            }
            else if (mustBeOwner && eventTemplateEntity.CreatedBy != _user.GetId() && !isSystemAdmin)
            {
                _logger.LogError($"User {_user.GetId()} is not permitted to access EventTemplate {eventTemplateId}.");
                throw new ForbiddenException($"User {_user.GetId()} is not permitted to access EventTemplate {eventTemplateId}.");
            }

            return eventTemplateEntity;
        }

    }
}
