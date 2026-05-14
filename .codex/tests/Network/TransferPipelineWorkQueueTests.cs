using System;
using System.Collections.Generic;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Exercises the transfer pipeline work queue using actual queue entry points.
/// </summary>
[Collection("Assembly setup")]
public sealed class TransferPipelineWorkQueueTests
{
    /// <summary>
    /// Ensures enqueued transfer work items interleave progress through the pipeline queue.
    /// </summary>
    [Fact]
    public void ProcessTransferWorkQueueForTesting_InterleavesPipelineWorkItems()
    {
        Transference.ResetTransferWorkQueueForTesting();
        try
        {
            List<Guid> executionLog = new();
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();

            Transference.EnqueueTransferWorkItemForTesting(new LoggingTransferWorkItem(firstId, 3, executionLog));
            Transference.EnqueueTransferWorkItemForTesting(new LoggingTransferWorkItem(secondId, 3, executionLog));

            TransferWorkQueueProgress progress = Transference.ProcessTransferWorkQueueForTesting(TimeSpan.FromMilliseconds(250));

            Assert.False(progress.BudgetExceeded);
            Assert.Equal(new[] { firstId, secondId, firstId, secondId, firstId, secondId }, executionLog);
        }
        finally
        {
            Transference.ResetTransferWorkQueueForTesting();
        }
    }

    /// <summary>
    /// Logs execution order for deterministic queue interleaving assertions.
    /// </summary>
    sealed class LoggingTransferWorkItem : ITransferWorkItem
    {
        readonly List<Guid> executionLog;
        int remainingSteps;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggingTransferWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="totalSteps">The total number of steps to execute.</param>
        /// <param name="executionLog">The log to append to.</param>
        public LoggingTransferWorkItem(Guid transferId, int totalSteps, List<Guid> executionLog)
        {
            if (totalSteps < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalSteps), "Total steps must be non-negative.");
            }

            TransferId = transferId;
            remainingSteps = totalSteps;
            this.executionLog = executionLog ?? throw new ArgumentNullException(nameof(executionLog));
        }

        /// <summary>
        /// Gets the transfer identifier.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Executes a single step and records the transfer identifier.
        /// </summary>
        /// <returns><c>true</c> when all steps complete; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (remainingSteps <= 0)
            {
                return true;
            }

            executionLog.Add(TransferId);
            remainingSteps--;
            return remainingSteps == 0;
        }
    }
}
