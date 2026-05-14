using System;
using System.Collections.Generic;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Validates scheduling behavior for transfer work queue execution.
/// </summary>
[Collection("Assembly setup")]
public sealed class TransferWorkQueueTests
{
    /// <summary>
    /// Ensures a bounded time budget stops processing before all work is complete.
    /// </summary>
    [Fact]
    public void Process_WithTimeBudget_StopsBeforeAllTransfersComplete()
    {
        ManualTransferWorkQueueClock Clock = new(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        TransferWorkQueue Queue = new(Clock);
        List<Guid> ExecutionLog = new();

        TestTransferWorkItem FirstTransfer = new(
            Guid.NewGuid(),
            totalSteps: 3,
            Clock,
            TimeSpan.FromMilliseconds(60),
            ExecutionLog);
        TestTransferWorkItem SecondTransfer = new(
            Guid.NewGuid(),
            totalSteps: 3,
            Clock,
            TimeSpan.FromMilliseconds(60),
            ExecutionLog);

        Queue.Enqueue(FirstTransfer);
        Queue.Enqueue(SecondTransfer);

        TransferWorkQueueProgress Progress = Queue.Process(TimeSpan.FromMilliseconds(100));

        Assert.True(Progress.BudgetExceeded);
        Assert.True(Progress.StepsProcessed < 6);
        Assert.True(Queue.Count > 0);
        Assert.True(FirstTransfer.RemainingSteps + SecondTransfer.RemainingSteps > 0);
    }

    /// <summary>
    /// Ensures transfers are interleaved when multiple items are queued.
    /// </summary>
    [Fact]
    public void Process_InterleavesTransfersAcrossSteps()
    {
        ManualTransferWorkQueueClock Clock = new(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        TransferWorkQueue Queue = new(Clock);
        List<Guid> ExecutionLog = new();

        Guid FirstId = Guid.NewGuid();
        Guid SecondId = Guid.NewGuid();

        Queue.Enqueue(new TestTransferWorkItem(FirstId, 3, Clock, TimeSpan.FromMilliseconds(1), ExecutionLog));
        Queue.Enqueue(new TestTransferWorkItem(SecondId, 3, Clock, TimeSpan.FromMilliseconds(1), ExecutionLog));

        TransferWorkQueueProgress Progress = Queue.Process(TimeSpan.FromMilliseconds(500));

        Assert.False(Progress.BudgetExceeded);
        Assert.Equal(new[] { FirstId, SecondId, FirstId, SecondId, FirstId, SecondId }, ExecutionLog);
    }

    /// <summary>
    /// Ensures later transfers receive time slices instead of being starved.
    /// </summary>
    [Fact]
    public void Process_DoesNotStarveLaterTransfers()
    {
        ManualTransferWorkQueueClock Clock = new(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        TransferWorkQueue Queue = new(Clock);
        List<Guid> ExecutionLog = new();

        Guid FirstId = Guid.NewGuid();
        Guid SecondId = Guid.NewGuid();
        Guid ThirdId = Guid.NewGuid();

        Queue.Enqueue(new TestTransferWorkItem(FirstId, 4, Clock, TimeSpan.FromMilliseconds(1), ExecutionLog));
        Queue.Enqueue(new TestTransferWorkItem(SecondId, 4, Clock, TimeSpan.FromMilliseconds(1), ExecutionLog));
        Queue.Enqueue(new TestTransferWorkItem(ThirdId, 4, Clock, TimeSpan.FromMilliseconds(1), ExecutionLog));

        TransferWorkQueueProgress Progress = Queue.Process(TimeSpan.FromMilliseconds(3));

        Assert.True(Progress.BudgetExceeded);
        Assert.Equal(3, ExecutionLog.Count);
        Assert.Equal(new[] { FirstId, SecondId, ThirdId }, ExecutionLog);
    }

    /// <summary>
    /// Provides a controllable clock for deterministic scheduling tests.
    /// </summary>
    sealed class ManualTransferWorkQueueClock : ITransferWorkQueueClock
    {
        DateTime utcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualTransferWorkQueueClock"/> class.
        /// </summary>
        /// <param name="initialTime">The initial UTC timestamp.</param>
        public ManualTransferWorkQueueClock(DateTime initialTime)
        {
            utcNow = initialTime;
        }

        /// <summary>
        /// Gets the current UTC timestamp.
        /// </summary>
        public DateTime UtcNow => utcNow;

        /// <summary>
        /// Advances the clock by the specified duration.
        /// </summary>
        /// <param name="duration">The amount of time to add.</param>
        public void Advance(TimeSpan duration)
        {
            utcNow = utcNow.Add(duration);
        }
    }

    /// <summary>
    /// Simulates transfer steps while recording execution order.
    /// </summary>
    sealed class TestTransferWorkItem : ITransferWorkItem
    {
        readonly ManualTransferWorkQueueClock clock;
        readonly TimeSpan stepDuration;
        readonly List<Guid> executionLog;
        int remainingSteps;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestTransferWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="totalSteps">The total number of steps to execute.</param>
        /// <param name="clock">Clock used to advance time per step.</param>
        /// <param name="stepDuration">Time elapsed per step.</param>
        /// <param name="executionLog">Execution log to append to.</param>
        public TestTransferWorkItem(
            Guid transferId,
            int totalSteps,
            ManualTransferWorkQueueClock clock,
            TimeSpan stepDuration,
            List<Guid> executionLog)
        {
            if (totalSteps < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalSteps), "Total steps must be non-negative.");
            }

            TransferId = transferId;
            remainingSteps = totalSteps;
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.stepDuration = stepDuration;
            this.executionLog = executionLog ?? throw new ArgumentNullException(nameof(executionLog));
        }

        /// <summary>
        /// Gets the transfer identifier.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Gets the number of remaining steps.
        /// </summary>
        public int RemainingSteps => remainingSteps;

        /// <summary>
        /// Executes a single step and advances the clock.
        /// </summary>
        /// <returns><c>true</c> when all steps are completed; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (remainingSteps <= 0)
            {
                return true;
            }

            executionLog.Add(TransferId);
            remainingSteps--;
            clock.Advance(stepDuration);
            return remainingSteps == 0;
        }
    }
}
