// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Crucible.Common.EntityEvents.Events;
using Alloy.Api.Hubs;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Events.EventHandlers
{
    public class GroupMembershipCreatedSignalRHandler(AlloyContext _db, IMapper _mapper, IHubContext<EngineHub> _projectHub) :
        GroupMembershipBaseSignalRHandler(_db, _mapper, _projectHub),
        INotificationHandler<EntityCreated<GroupMembershipEntity>>
    {
        public async Task Handle(EntityCreated<GroupMembershipEntity> notification, CancellationToken cancellationToken)
        {
            await base.Handle(notification.Entity, EngineHubMethods.GroupMembershipCreated, null, cancellationToken);
        }
    }

    public class GroupMembershipUpdatedSignalRHandler(AlloyContext _db, IMapper _mapper, IHubContext<EngineHub> _projectHub) :
        GroupMembershipBaseSignalRHandler(_db, _mapper, _projectHub),
        INotificationHandler<EntityUpdated<GroupMembershipEntity>>
    {
        public async Task Handle(EntityUpdated<GroupMembershipEntity> notification, CancellationToken cancellationToken)
        {
            await base.Handle(notification.Entity, EngineHubMethods.GroupMembershipUpdated, notification.ModifiedProperties, cancellationToken);
        }
    }

    public class GroupMembershipDeletedSignalRHandler(IHubContext<EngineHub> projectHub) :
        INotificationHandler<EntityDeleted<GroupMembershipEntity>>
    {
        public async Task Handle(EntityDeleted<GroupMembershipEntity> notification, CancellationToken cancellationToken)
        {
            await projectHub.Clients.Group(EngineHub.ADMIN_EVENT_GROUP).SendAsync(EngineHubMethods.GroupMembershipDeleted, notification.Entity.Id);
        }
    }

    public class GroupMembershipBaseSignalRHandler(AlloyContext db, IMapper mapper, IHubContext<EngineHub> projectHub)
    {
        protected async Task Handle(GroupMembershipEntity entity, string method, string[] modifiedProperties, CancellationToken cancellationToken)
        {
            var groupMembership = await db.GroupMemberships
                .Where(x => x.Id == entity.Id)
                .ProjectTo<GroupMembership>(mapper.ConfigurationProvider)
                .FirstOrDefaultAsync();

            await projectHub.Clients.Group(EngineHub.ADMIN_EVENT_GROUP).SendAsync(method, groupMembership, modifiedProperties, cancellationToken);
        }
    }
}
