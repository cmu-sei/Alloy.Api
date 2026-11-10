// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using STT = System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.ViewModels;
using System.Linq;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Mappings;

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

        public EventTemplateMembershipService(AlloyContext context, IPrincipal user)
        {
            _context = context;
            _user = user as ClaimsPrincipal;
        }

        public async STT.Task<EventTemplateMembership> GetAsync(Guid id, CancellationToken ct)
        {
            var item = await _context.EventTemplateMemberships
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (item == null)
                throw new EntityNotFoundException<EventTemplateMembership>($"Event Template Membership {id} was not found.");

            return item.ToEventTemplateMembership();
        }

        public async STT.Task<IEnumerable<EventTemplateMembership>> GetByEventTemplateAsync(Guid eventTemplateId, CancellationToken ct)
        {
            var items = await _context.EventTemplateMemberships
                .Where(m => m.EventTemplateId == eventTemplateId)
                .ToListAsync(ct);

            return items.Select(x => x.ToEventTemplateMembership()).ToList();
        }

        public async STT.Task<EventTemplateMembership> CreateAsync(EventTemplateMembership eventTemplateMembership, CancellationToken ct)
        {
            var eventTemplateMembershipEntity = eventTemplateMembership.ToEntity();

            _context.EventTemplateMemberships.Add(eventTemplateMembershipEntity);
            await _context.SaveChangesAsync(ct);
            var createdEvent = await GetAsync(eventTemplateMembershipEntity.Id, ct);

            return createdEvent;
        }
        public async STT.Task<EventTemplateMembership> UpdateAsync(Guid id, EventTemplateMembership eventTemplateMembership, CancellationToken ct)
        {
            var eventTemplateMembershipToUpdate = await _context.EventTemplateMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);
            if (eventTemplateMembershipToUpdate == null)
                throw new EntityNotFoundException<EventTemplateMembership>($"Event Template Membership {id} was not found.");

            eventTemplateMembershipToUpdate.RoleId = eventTemplateMembership.RoleId;
            eventTemplateMembershipToUpdate.Role = null;
            await _context.SaveChangesAsync(ct);

            return eventTemplateMembershipToUpdate.ToEventTemplateMembership();
        }
        public async STT.Task DeleteAsync(Guid id, CancellationToken ct)
        {
            var eventTemplateMembershipToDelete = await _context.EventTemplateMemberships.SingleOrDefaultAsync(v => v.Id == id, ct);

            if (eventTemplateMembershipToDelete == null)
                throw new EntityNotFoundException<EventTemplateMembership>($"Event Template Membership {id} was not found.");

            _context.EventTemplateMemberships.Remove(eventTemplateMembershipToDelete);
            await _context.SaveChangesAsync(ct);

            return;
        }

    }
}
