// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        // Events that are queued or being processed, each mapped to a request that arrived
        // while it was already in flight, or null if there is no such request.
        private readonly Dictionary<Guid, EventEntity> _inFlightEvents = new Dictionary<Guid, EventEntity>();
        private readonly object _inFlightLock = new object();

        public void Add(EventEntity eventEntity)
        {
            if (eventEntity == null)
            {
                throw new ArgumentNullException(nameof(eventEntity));
            }

            lock (_inFlightLock)
            {
                if (_inFlightEvents.ContainsKey(eventEntity.Id))
                {
                    // Only one thread works an Event at a time. Hold this request so that
                    // Complete re-queues it, since the thread that is already running may
                    // be too far along to observe it.
                    _inFlightEvents[eventEntity.Id] = eventEntity;
                    return;
                }

                _inFlightEvents.Add(eventEntity.Id, null);
            }

            _eventQueue.Add(eventEntity);
        }

        public EventEntity Take(CancellationToken cancellationToken)
        {
            return _eventQueue.Take(cancellationToken);
        }

        public void Complete(EventEntity eventEntity)
        {
            if (eventEntity == null)
            {
                return;
            }

            EventEntity pendingEventEntity;

            lock (_inFlightLock)
            {
                _inFlightEvents.TryGetValue(eventEntity.Id, out pendingEventEntity);

                if (pendingEventEntity == null)
                {
                    _inFlightEvents.Remove(eventEntity.Id);
                }
                else
                {
                    // keep the Event in flight for the thread that picks up the re-queue
                    _inFlightEvents[eventEntity.Id] = null;
                }
            }

            if (pendingEventEntity != null)
            {
                _eventQueue.Add(pendingEventEntity);
            }
        }
    }

}
