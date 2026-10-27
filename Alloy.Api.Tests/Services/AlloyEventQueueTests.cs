// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;

namespace Alloy.Api.Tests.Services;

/// <summary>The worker's queue: one entry per event, and a notification during processing is not lost.</summary>
public class AlloyEventQueueTests
{
    [Fact]
    public void A_notification_received_while_processing_is_taken_again_after_completion()
    {
        var queue = new AlloyEventQueue();
        var entity = new EventEntity { Id = Guid.NewGuid() };
        queue.Add(entity);
        queue.Take(TestContext.Current.CancellationToken);
        queue.Add(entity);

        queue.Complete(entity);

        Assert.Equal(entity.Id, queue.Take(TestContext.Current.CancellationToken).Id);
    }

    /// <summary>The take is cancelled because nothing is queued: recovery did not add a second attempt.</summary>
    [Fact]
    public void A_recovery_notification_does_not_queue_an_extra_attempt_behind_the_worker()
    {
        var queue = new AlloyEventQueue();
        var entity = new EventEntity { Id = Guid.NewGuid() };
        queue.Add(entity);
        queue.Take(TestContext.Current.CancellationToken);
        queue.Add(entity, requeueIfProcessing: false);
        queue.Complete(entity);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => queue.Take(cancelled.Token));
    }
}
