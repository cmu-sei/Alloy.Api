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
using SAVM = Alloy.Api.ViewModels;
using Alloy.Api.ViewModels;
using System.Linq;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Services
{
    public interface IEventMembershipService
    {
        STT.Task<EventMembership> GetAsync(Guid id, CancellationToken ct);
        STT.Task<IEnumerable<EventMembership>> GetByEventAsync(Guid eventId, CancellationToken ct);
        STT.Task<EventMembership> CreateAsync(EventMembership eventMembership, CancellationToken ct);
        STT.Task<EventMembership> UpdateAsync(Guid id, EventMembership eventMembership, CancellationToken ct);
        STT.Task DeleteAsync(Guid id, CancellationToken ct);
    }

    public class EventMembershipService : IEventMembershipService
    {
        private readonly AlloyContext _context;
        private readonly ClaimsPrincipal _user;
        private readonly IMapper _mapper;

        public EventMembershipService(AlloyContext context, IPrincipal user, IMapper mapper)
        {
            _context = context;
            _user = user as ClaimsPrincipal;
            _mapper = mapper;
        }

        public async STT.Task<EventMembership> GetAsync(Guid id, CancellationToken ct)
        {
            var item = await _context.EventMemberships
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (item == null)
                throw new EntityNotFoundException<EventMembership>();

            return _mapper.Map<SAVM.EventMembership>(item);
        }

        public async STT.Task<IEnumerable<EventMembership>> GetByEventAsync(Guid eventId, CancellationToken ct)
        {
            var items = await _context.EventMemberships
                .Where(m => m.EventId == eventId)
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<SAVM.EventMembership>>(items);
        }

        public async STT.Task<EventMembership> CreateAsync(EventMembership eventMembership, CancellationToken ct)
        {
            var eventMembershipEntity = _mapper.Map<EventMembershipEntity>(eventMembership);

            _context.EventMemberships.Add(eventMembershipEntity);
            await _context.SaveChangesAsync(ct);
            var createdEvent = await GetAsync(eventMembershipEntity.Id, ct);

            return createdEvent;
        }
        public async STT.Task<EventMembership> UpdateAsync(Guid id, EventMembership eventMembership, CancellationToken ct)
        {
            var eventMembershipToUpdate = await _context.EventMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (eventMembershipToUpdate == null)
                throw new EntityNotFoundException<SAVM.Event>();

            _mapper.Map(eventMembership, eventMembershipToUpdate);

            await _context.SaveChangesAsync(ct);

            return _mapper.Map<SAVM.EventMembership>(eventMembershipToUpdate);
        }
        public async STT.Task DeleteAsync(Guid id, CancellationToken ct)
        {
            var eventMembershipToDelete = await _context.EventMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (eventMembershipToDelete == null)
                throw new EntityNotFoundException<SAVM.EventMembership>();

            _context.EventMemberships.Remove(eventMembershipToDelete);
            await _context.SaveChangesAsync(ct);

            return;
        }

    }
}
