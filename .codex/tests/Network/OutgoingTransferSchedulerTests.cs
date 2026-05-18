using System;
using System.Collections.Generic;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers outgoing transfer start and queue scheduling.
/// </summary>
[Collection("Assembly setup")]
public sealed class OutgoingTransferSchedulerTests
{
    /// <summary>
    /// Ensures transfers beyond the active limit wait in FIFO order.
    /// </summary>
    [Fact]
    public void EnqueueOrStart_QueuesWhenActiveLimitReached()
    {
        OutgoingTransferScheduler<TestOutgoingTransfer> scheduler = new(maxActiveTransfers: 2);
        List<Guid> started = new();

        TestOutgoingTransfer first = new(Guid.NewGuid());
        TestOutgoingTransfer second = new(Guid.NewGuid());
        TestOutgoingTransfer third = new(Guid.NewGuid());

        Assert.True(scheduler.EnqueueOrStart(first, transfer => started.Add(transfer.TransferId)));
        Assert.True(scheduler.EnqueueOrStart(second, transfer => started.Add(transfer.TransferId)));
        Assert.False(scheduler.EnqueueOrStart(third, transfer => started.Add(transfer.TransferId)));

        Assert.Equal(new[] { first.TransferId, second.TransferId }, started);
        Assert.Equal(2, scheduler.ActiveCount);
        Assert.Equal(1, scheduler.QueuedCount);
    }

    /// <summary>
    /// Ensures completing an active transfer starts the oldest queued transfer.
    /// </summary>
    [Fact]
    public void Complete_StartsQueuedTransferInFifoOrder()
    {
        OutgoingTransferScheduler<TestOutgoingTransfer> scheduler = new(maxActiveTransfers: 2);
        List<Guid> started = new();

        TestOutgoingTransfer first = new(Guid.NewGuid());
        TestOutgoingTransfer second = new(Guid.NewGuid());
        TestOutgoingTransfer third = new(Guid.NewGuid());
        TestOutgoingTransfer fourth = new(Guid.NewGuid());

        scheduler.EnqueueOrStart(first, transfer => started.Add(transfer.TransferId));
        scheduler.EnqueueOrStart(second, transfer => started.Add(transfer.TransferId));
        scheduler.EnqueueOrStart(third, transfer => started.Add(transfer.TransferId));
        scheduler.EnqueueOrStart(fourth, transfer => started.Add(transfer.TransferId));

        scheduler.Complete(first.TransferId, transfer => started.Add(transfer.TransferId));
        scheduler.Complete(second.TransferId, transfer => started.Add(transfer.TransferId));

        Assert.Equal(
            new[] { first.TransferId, second.TransferId, third.TransferId, fourth.TransferId },
            started);
        Assert.Equal(2, scheduler.ActiveCount);
        Assert.Equal(0, scheduler.QueuedCount);
    }

    readonly record struct TestOutgoingTransfer(Guid TransferId) : IOutgoingTransferWork;
}
