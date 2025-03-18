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
        STT.Task<IEnumerable<EventTemplateMembership>> GetByEventTemplateAsync(Guid eventTemplateId, CancellationToken ct);
        STT.Task<EventTemplateMembership> CreateAsync(EventTemplateMembership eventTemplateMembership, CancellationToken ct);
        STT.Task<EventTemplateMembership> UpdateAsync(Guid id, EventTemplateMembership eventTemplateMembership, CancellationToken ct);
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

        public async STT.Task<IEnumerable<EventTemplateMembership>> GetByEventTemplateAsync(Guid eventTemplateId, CancellationToken ct)
        {
            var items = await _context.EventTemplateMemberships
                .Where(m => m.EventTemplateId == eventTemplateId)
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<EventTemplateMembership>>(items);
        }

        public async STT.Task<EventTemplateMembership> CreateAsync(EventTemplateMembership eventTemplateMembership, CancellationToken ct)
        {
            var eventTemplateMembershipEntity = _mapper.Map<EventTemplateMembershipEntity>(eventTemplateMembership);

            _context.EventTemplateMemberships.Add(eventTemplateMembershipEntity);
            await _context.SaveChangesAsync(ct);
            var createdEvent = await GetAsync(eventTemplateMembershipEntity.Id, ct);

            return createdEvent;
        }
        public async STT.Task<EventTemplateMembership> UpdateAsync(Guid id, EventTemplateMembership eventTemplateMembership, CancellationToken ct)
        {
            var eventTemplateMembershipToUpdate = await _context.EventTemplateMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (eventTemplateMembershipToUpdate == null)
                throw new EntityNotFoundException<Event>();

            _mapper.Map(eventTemplateMembership, eventTemplateMembershipToUpdate);

            await _context.SaveChangesAsync(ct);

            return _mapper.Map<EventTemplateMembership>(eventTemplateMembershipToUpdate);
        }
        public async STT.Task DeleteAsync(Guid id, CancellationToken ct)
        {
            var eventTemplateMembershipToDelete = await _context.EventTemplateMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (eventTemplateMembershipToDelete == null)
                throw new EntityNotFoundException<EventTemplateMembership>();

            _context.EventTemplateMemberships.Remove(eventTemplateMembershipToDelete);
            await _context.SaveChangesAsync(ct);

            return;
        }

    }
}
