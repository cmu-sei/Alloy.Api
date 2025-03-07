// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using STT = System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.ViewModels;
using System.Linq;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Services
{
    public interface IEventTemplateMembershipService
    {
        STT.Task<EventTemplateMembership> GetAsync(Guid id, CancellationToken ct);
        STT.Task<IEnumerable<EventTemplateMembership>> GetByEventTemplateAsync(Guid scenarioTemplateId, CancellationToken ct);
        STT.Task<EventTemplateMembership> CreateAsync(EventTemplateMembership scenarioTemplateMembership, CancellationToken ct);
        STT.Task<EventTemplateMembership> UpdateAsync(Guid id, EventTemplateMembership scenarioTemplateMembership, CancellationToken ct);
        STT.Task DeleteAsync(Guid id, CancellationToken ct);
    }

    public class EventTemplateMembershipService : IEventTemplateMembershipService
    {
        private readonly AlloyContext _context;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;

        public EventTemplateMembershipService(AlloyContext context, IPrincipal user, IMapper mapper)
        {
            _context = context;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
        }

        public async STT.Task<EventTemplateMembership> GetAsync(Guid id, CancellationToken ct)
        {
            var item = await _context.EventTemplateMemberships
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (item == null)
                throw new EntityNotFoundException<EventTemplateMembership>();

            return _mapper.Map<EventTemplateMembership>(item);
        }

        public async STT.Task<IEnumerable<EventTemplateMembership>> GetByEventTemplateAsync(Guid scenarioTemplateId, CancellationToken ct)
        {
            var items = await _context.EventTemplateMemberships
                .Where(m => m.EventTemplateId == scenarioTemplateId)
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<EventTemplateMembership>>(items);
        }

        public async STT.Task<EventTemplateMembership> CreateAsync(EventTemplateMembership scenarioTemplateMembership, CancellationToken ct)
        {
            var scenarioTemplateMembershipEntity = _mapper.Map<EventTemplateMembershipEntity>(scenarioTemplateMembership);

            _context.EventTemplateMemberships.Add(scenarioTemplateMembershipEntity);
            await _context.SaveChangesAsync(ct);
            var scenario = await GetAsync(scenarioTemplateMembershipEntity.Id, ct);

            return scenario;
        }
        public async STT.Task<EventTemplateMembership> UpdateAsync(Guid id, EventTemplateMembership scenarioTemplateMembership, CancellationToken ct)
        {
            var scenarioTemplateMembershipToUpdate = await _context.EventTemplateMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (scenarioTemplateMembershipToUpdate == null)
                throw new EntityNotFoundException<Event>();

            _mapper.Map(scenarioTemplateMembership, scenarioTemplateMembershipToUpdate);

            await _context.SaveChangesAsync(ct);

            return _mapper.Map<EventTemplateMembership>(scenarioTemplateMembershipToUpdate);
        }
        public async STT.Task DeleteAsync(Guid id, CancellationToken ct)
        {
            var scenarioTemplateMembershipToDelete = await _context.EventTemplateMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (scenarioTemplateMembershipToDelete == null)
                throw new EntityNotFoundException<EventTemplateMembership>();

            _context.EventTemplateMemberships.Remove(scenarioTemplateMembershipToDelete);
            await _context.SaveChangesAsync(ct);

            return;
        }

    }
}
