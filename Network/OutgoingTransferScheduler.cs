namespace Emberglass.Network;

/// <summary>
/// Describes work that can be scheduled as an outgoing transfer.
/// </summary>
internal interface IOutgoingTransferWork
{
    /// <summary>
    /// Gets the transfer identifier.
    /// </summary>
    Guid TransferId { get; }
}

/// <summary>
/// Keeps outgoing transfers under an active limit while preserving FIFO fairness.
/// </summary>
/// <typeparam name="TWork">The outgoing transfer work item type.</typeparam>
internal sealed class OutgoingTransferScheduler<TWork>
    where TWork : IOutgoingTransferWork
{
    readonly Queue<TWork> queuedTransfers = new();
    readonly HashSet<Guid> activeTransfers = new();
    int maxActiveTransfers;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutgoingTransferScheduler{TWork}"/> class.
    /// </summary>
    /// <param name="maxActiveTransfers">The maximum number of transfers that may run at once.</param>
    public OutgoingTransferScheduler(int maxActiveTransfers)
    {
        SetMaxActiveTransfers(maxActiveTransfers);
    }

    /// <summary>
    /// Gets the number of active outgoing transfers.
    /// </summary>
    public int ActiveCount => activeTransfers.Count;

    /// <summary>
    /// Gets the number of queued outgoing transfers.
    /// </summary>
    public int QueuedCount => queuedTransfers.Count;

    /// <summary>
    /// Updates the active transfer limit.
    /// </summary>
    /// <param name="value">The maximum number of active transfers.</param>
    public void SetMaxActiveTransfers(int value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "At least one active transfer must be allowed.");
        }

        maxActiveTransfers = value;
    }

    /// <summary>
    /// Clears active and queued transfer state.
    /// </summary>
    public void Clear()
    {
        queuedTransfers.Clear();
        activeTransfers.Clear();
    }

    /// <summary>
    /// Starts the transfer immediately when capacity is available; otherwise queues it.
    /// </summary>
    /// <param name="work">The transfer work to schedule.</param>
    /// <param name="startTransfer">Callback used to start work immediately.</param>
    /// <returns><c>true</c> when the transfer was started; <c>false</c> when it was queued.</returns>
    public bool EnqueueOrStart(TWork work, Action<TWork> startTransfer)
    {
        if (startTransfer is null)
        {
            throw new ArgumentNullException(nameof(startTransfer));
        }

        if (activeTransfers.Count >= maxActiveTransfers)
        {
            queuedTransfers.Enqueue(work);
            return false;
        }

        activeTransfers.Add(work.TransferId);
        startTransfer(work);
        return true;
    }

    /// <summary>
    /// Marks a transfer complete and starts queued work while capacity is available.
    /// </summary>
    /// <param name="transferId">The completed transfer identifier.</param>
    /// <param name="startTransfer">Callback used to start queued work.</param>
    public void Complete(Guid transferId, Action<TWork> startTransfer)
    {
        if (startTransfer is null)
        {
            throw new ArgumentNullException(nameof(startTransfer));
        }

        activeTransfers.Remove(transferId);

        while (activeTransfers.Count < maxActiveTransfers && queuedTransfers.Count > 0)
        {
            TWork next = queuedTransfers.Dequeue();
            activeTransfers.Add(next.TransferId);
            startTransfer(next);
        }
    }

    /// <summary>
    /// Removes queued transfers that match the provided predicate without touching active transfers.
    /// </summary>
    /// <param name="shouldRemove">Predicate that identifies queued work to remove.</param>
    /// <returns>The removed queued work items in their original FIFO order.</returns>
    public IReadOnlyList<TWork> RemoveQueued(Predicate<TWork> shouldRemove)
    {
        if (shouldRemove is null)
        {
            throw new ArgumentNullException(nameof(shouldRemove));
        }

        if (queuedTransfers.Count == 0)
        {
            return Array.Empty<TWork>();
        }

        List<TWork> removed = [];
        int queuedCount = queuedTransfers.Count;
        for (int i = 0; i < queuedCount; i++)
        {
            TWork work = queuedTransfers.Dequeue();
            if (shouldRemove(work))
            {
                removed.Add(work);
                continue;
            }

            queuedTransfers.Enqueue(work);
        }

        return removed;
    }
}
