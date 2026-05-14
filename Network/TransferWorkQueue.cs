using System;
using System.Collections.Generic;

namespace Emberglass.Network;

/// <summary>
/// Represents a unit of transfer work that can be executed step-by-step.
/// </summary>
public interface ITransferWorkItem
{
    /// <summary>
    /// Gets the identifier for the transfer being processed.
    /// </summary>
    Guid TransferId { get; }

    /// <summary>
    /// Executes a single unit of transfer work.
    /// </summary>
    /// <returns><c>true</c> when the transfer is complete; otherwise, <c>false</c>.</returns>
    bool TryExecuteStep();
}

/// <summary>
/// Provides the current time used by the transfer work queue.
/// </summary>
public interface ITransferWorkQueueClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    DateTime UtcNow { get; }
}

/// <summary>
/// Uses the system clock for transfer work scheduling.
/// </summary>
public sealed class SystemTransferWorkQueueClock : ITransferWorkQueueClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Describes the results of a transfer work queue processing pass.
/// </summary>
/// <param name="StepsProcessed">Number of steps executed during the pass.</param>
/// <param name="TransfersCompleted">Number of transfers completed during the pass.</param>
/// <param name="BudgetExceeded">Whether processing stopped due to the time budget.</param>
public readonly record struct TransferWorkQueueProgress(
    int StepsProcessed,
    int TransfersCompleted,
    bool BudgetExceeded);

/// <summary>
/// Schedules transfer steps across multiple transfers with a bounded time budget.
/// </summary>
public sealed class TransferWorkQueue
{
    readonly ITransferWorkQueueClock clock;
    readonly Queue<ITransferWorkItem> workItems = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TransferWorkQueue"/> class.
    /// </summary>
    public TransferWorkQueue()
        : this(new SystemTransferWorkQueueClock())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransferWorkQueue"/> class.
    /// </summary>
    /// <param name="clock">Clock implementation used to track the time budget.</param>
    public TransferWorkQueue(ITransferWorkQueueClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Gets the number of queued transfers.
    /// </summary>
    public int Count => workItems.Count;

    /// <summary>
    /// Clears all queued transfer work items.
    /// </summary>
    public void Clear()
    {
        workItems.Clear();
    }

    /// <summary>
    /// Enqueues a transfer work item for processing.
    /// </summary>
    /// <param name="workItem">The transfer work item to enqueue.</param>
    public void Enqueue(ITransferWorkItem workItem)
    {
        if (workItem is null)
        {
            throw new ArgumentNullException(nameof(workItem));
        }

        workItems.Enqueue(workItem);
    }

    /// <summary>
    /// Processes queued transfer work until the time budget is exhausted.
    /// </summary>
    /// <param name="timeBudget">Maximum amount of time to spend processing.</param>
    /// <returns>The processing summary for the pass.</returns>
    public TransferWorkQueueProgress Process(TimeSpan timeBudget)
    {
        if (timeBudget < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeBudget), "Time budget must be non-negative.");
        }

        if (workItems.Count == 0)
        {
            return new TransferWorkQueueProgress(0, 0, false);
        }

        if (timeBudget == TimeSpan.Zero)
        {
            return new TransferWorkQueueProgress(0, 0, true);
        }

        DateTime startTime = clock.UtcNow;
        int stepsProcessed = 0;
        int transfersCompleted = 0;
        bool budgetExceeded = false;

        while (workItems.Count > 0 && !budgetExceeded)
        {
            int itemsThisCycle = workItems.Count;
            for (int i = 0; i < itemsThisCycle; i++)
            {
                if (IsBudgetExceeded(startTime, timeBudget))
                {
                    budgetExceeded = true;
                    break;
                }

                ITransferWorkItem workItem = workItems.Dequeue();
                bool isComplete = workItem.TryExecuteStep();
                stepsProcessed++;

                if (isComplete)
                {
                    transfersCompleted++;
                }
                else
                {
                    workItems.Enqueue(workItem);
                }

                if (workItems.Count == 0)
                {
                    break;
                }
            }
        }

        return new TransferWorkQueueProgress(stepsProcessed, transfersCompleted, budgetExceeded);
    }

    /// <summary>
    /// Determines whether the time budget has been exhausted.
    /// </summary>
    /// <param name="startTime">The time when processing began.</param>
    /// <param name="timeBudget">The allocated time budget.</param>
    /// <returns><c>true</c> if the budget has been exhausted; otherwise, <c>false</c>.</returns>
    bool IsBudgetExceeded(DateTime startTime, TimeSpan timeBudget)
    {
        return clock.UtcNow - startTime >= timeBudget;
    }
}
