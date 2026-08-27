// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Alloy.Api.Events.EventHandlers;

public class GroupMembershipCreatedAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<EntityCreated<GroupMembershipEntity>>
{
    public Task Handle(EntityCreated<GroupMembershipEntity> notification, CancellationToken cancellationToken)
    {
        cache.Remove(notification.Entity.UserId);
        return Task.CompletedTask;
    }
}

public class GroupMembershipUpdatedAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<EntityUpdated<GroupMembershipEntity>>
{
    public Task Handle(EntityUpdated<GroupMembershipEntity> notification, CancellationToken cancellationToken)
    {
        cache.Remove(notification.Entity.UserId);
        return Task.CompletedTask;
    }
}

public class GroupMembershipDeletedAuthCacheHandler(IMemoryCache cache) :
    INotificationHandler<EntityDeleted<GroupMembershipEntity>>
{
    public Task Handle(EntityDeleted<GroupMembershipEntity> notification, CancellationToken cancellationToken)
    {
        cache.Remove(notification.Entity.UserId);
        return Task.CompletedTask;
    }
}
