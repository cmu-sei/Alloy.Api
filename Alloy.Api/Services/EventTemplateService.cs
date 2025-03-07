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
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Services
{
    public interface IEventTemplateService
    {
        Task<IEnumerable<ViewModels.EventTemplate>> GetAsync(CancellationToken ct);
        Task<IEnumerable<ViewModels.EventTemplate>> GetByUserAsync(CancellationToken ct);
        Task<IEnumerable<ViewModels.EventTemplate>> GetPublishedAsync(CancellationToken ct);
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
            var items = await _context.EventTemplates.ToListAsync(ct);

            return _mapper.Map<IEnumerable<EventTemplate>>(items);
        }

        /// <summary>
        /// Get user's eventTemplates
        /// </summary>
        /// <param name="ct"></param>
        /// <returns>EventTemplates</returns>
        public async Task<IEnumerable<ViewModels.EventTemplate>> GetByUserAsync(CancellationToken ct)
        {
            var userId = _user.GetId();
            var items = await _context.EventTemplateMemberships
                .Where(m => m.EventTemplate.CreatedBy == userId || m.UserId == userId)
                .Select(m => m.EventTemplate)
                .Distinct()
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<EventTemplate>>(items);
        }

        /// <summary>
        /// Get all published eventTemplates
        /// </summary>
        /// <param name="ct"></param>
        /// <returns>EventTemplates</returns>
        public async Task<IEnumerable<ViewModels.EventTemplate>> GetPublishedAsync(CancellationToken ct)
        {
            var items = await _context.EventTemplates.Where(d => d.IsPublished).ToListAsync(ct);

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
            var item = await _context.EventTemplates
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (item == null)
                throw new ForbiddenException();

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
            var eventTemplateEntity = await GetTheEventTemplateAsync(id, ct);
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
            var eventTemplateEntity = await GetTheEventTemplateAsync(id, ct);

            if (eventTemplateEntity == null)
                throw new EntityNotFoundException<EventTemplate>();

            _context.EventTemplates.Remove(eventTemplateEntity);
            await _context.SaveChangesAsync(ct);

            return true;
        }

        private async Task<EventTemplateEntity> GetTheEventTemplateAsync(Guid eventTemplateId, CancellationToken ct)
        {
            var eventTemplateEntity = await _context.EventTemplates.SingleOrDefaultAsync(v => v.Id == eventTemplateId, ct);

            if (eventTemplateEntity == null)
            {
                _logger.LogError($"EventTemplate {eventTemplateId} was not found.");
                throw new EntityNotFoundException<EventTemplate>();
            }

            return eventTemplateEntity;
        }

    }
}
