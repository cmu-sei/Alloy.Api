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
    public interface IEventTemplateRoleService
    {
        STT.Task<IEnumerable<EventTemplateRole>> GetAsync(CancellationToken ct);
        STT.Task<EventTemplateRole> GetAsync(Guid id, CancellationToken ct);
    }

    public class EventTemplateRoleService : IEventTemplateRoleService
    {
        private readonly AlloyContext _context;
        private readonly ClaimsPrincipal _user;

        public EventTemplateRoleService(AlloyContext context, IPrincipal user)
        {
            _context = context;
            _user = user as ClaimsPrincipal;
        }

        public async STT.Task<IEnumerable<EventTemplateRole>> GetAsync(CancellationToken ct)
        {
            var items = await _context.EventTemplateRoles
                .ToListAsync(ct);

            return items.Select(x => x.ToEventTemplateRole()).ToList();
        }

        public async STT.Task<EventTemplateRole> GetAsync(Guid id, CancellationToken ct)
        {
            var item = await _context.EventTemplateRoles
                .SingleOrDefaultAsync(o => o.Id == id, ct);

            if (item == null)
                throw new EntityNotFoundException<EventTemplateRole>();

            return item.ToEventTemplateRole();
        }

    }
}
