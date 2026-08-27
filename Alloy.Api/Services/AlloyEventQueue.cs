// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Services
{

    public interface IAlloyEventQueue
    {
        void Add(EventEntity eventEntity);

        EventEntity Take(CancellationToken cancellationToken);

        void Complete(EventEntity eventEntity);
    }

    public class AlloyEventQueue : IAlloyEventQueue
    {
        private BlockingCollection<EventEntity> _eventQueue = new BlockingCollection<EventEntity>();
        private ConcurrentDictionary<Guid, byte> _queuedOrProcessingEventIds = new ConcurrentDictionary<Guid, byte>();

        public void Add(EventEntity eventEntity)
        {
            if (eventEntity == null)
            {
                throw new ArgumentNullException(nameof(eventEntity));
            }

            if (_queuedOrProcessingEventIds.TryAdd(eventEntity.Id, 0))
            {
                _eventQueue.Add(eventEntity);
            }
        }

        public EventEntity Take(CancellationToken cancellationToken)
        {
            return _eventQueue.Take(cancellationToken);
        }

        public void Complete(EventEntity eventEntity)
        {
            if (eventEntity != null)
            {
                _queuedOrProcessingEventIds.TryRemove(eventEntity.Id, out _);
            }
        }
    }

}
