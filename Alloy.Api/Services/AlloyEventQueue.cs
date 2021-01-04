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
    }

    public class AlloyEventQueue : IAlloyEventQueue
    {
        private BlockingCollection<EventEntity> _eventQueue = new BlockingCollection<EventEntity>();

        public void Add(EventEntity eventEntity)
        {
            if (eventEntity == null)
            {
                throw new ArgumentNullException(nameof(eventEntity));
            }
            _eventQueue.Add(eventEntity);
        }

        public EventEntity Take(CancellationToken cancellationToken)
        {
            return _eventQueue.Take(cancellationToken);
        }
    }

}
