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
using Alloy.Api.Infrastructure.Mappings;
using System.Linq;

namespace Alloy.Api.Services
{
    public interface IEventRoleService
    {
        STT.Task<IEnumerable<EventRole>> GetAsync(CancellationToken ct);
        STT.Task<EventRole> GetAsync(Guid id, CancellationToken ct);
    }

    public class EventRoleService : IEventRoleService
    {
        private readonly AlloyContext _context;
        private readonly ClaimsPrincipal _user;

        public EventRoleService(AlloyContext context, IPrincipal user)
        {
            _context = context;
            _user = user as ClaimsPrincipal;
        }

        public async STT.Task<IEnumerable<EventRole>> GetAsync(CancellationToken ct)
        {
            var items = await _context.EventRoles
                .ToListAsync(ct);

            return items.Select(x => x.ToEventRole()).ToList();
        }

        public async STT.Task<EventRole> GetAsync(Guid id, CancellationToken ct)
        {
            var item = await _context.EventRoles
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (item == null)
                throw new EntityNotFoundException<EventRole>();

            return item.ToEventRole();
        }

    }
}
