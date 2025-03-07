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
        STT.Task<IEnumerable<EventMembership>> GetByEventAsync(Guid scenarioId, CancellationToken ct);
        STT.Task<EventMembership> CreateAsync(EventMembership scenarioMembership, CancellationToken ct);
        STT.Task<EventMembership> UpdateAsync(Guid id, EventMembership scenarioMembership, CancellationToken ct);
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

        public async STT.Task<IEnumerable<EventMembership>> GetByEventAsync(Guid scenarioId, CancellationToken ct)
        {
            var items = await _context.EventMemberships
                .Where(m => m.EventId == scenarioId)
                .ToListAsync(ct);

            return _mapper.Map<IEnumerable<SAVM.EventMembership>>(items);
        }

        public async STT.Task<EventMembership> CreateAsync(EventMembership scenarioMembership, CancellationToken ct)
        {
            var scenarioMembershipEntity = _mapper.Map<EventMembershipEntity>(scenarioMembership);

            _context.EventMemberships.Add(scenarioMembershipEntity);
            await _context.SaveChangesAsync(ct);
            var scenario = await GetAsync(scenarioMembershipEntity.Id, ct);

            return scenario;
        }
        public async STT.Task<EventMembership> UpdateAsync(Guid id, EventMembership scenarioMembership, CancellationToken ct)
        {
            var scenarioMembershipToUpdate = await _context.EventMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (scenarioMembershipToUpdate == null)
                throw new EntityNotFoundException<SAVM.Event>();

            _mapper.Map(scenarioMembership, scenarioMembershipToUpdate);

            await _context.SaveChangesAsync(ct);

            return _mapper.Map<SAVM.EventMembership>(scenarioMembershipToUpdate);
        }
        public async STT.Task DeleteAsync(Guid id, CancellationToken ct)
        {
            var scenarioMembershipToDelete = await _context.EventMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (scenarioMembershipToDelete == null)
                throw new EntityNotFoundException<SAVM.EventMembership>();

            _context.EventMemberships.Remove(scenarioMembershipToDelete);
            await _context.SaveChangesAsync(ct);

            return;
        }

    }
}
