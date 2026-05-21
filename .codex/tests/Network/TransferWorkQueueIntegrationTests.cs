using System;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Ensures transfer setup enqueues work items into the transfer work queue.
/// </summary>
[Collection("Assembly setup")]
public sealed class TransferWorkQueueIntegrationTests
{
    /// <summary>
    /// Verifies transfer setup hashing enqueues a work item.
    /// </summary>
    [Fact]
    public void QueueTransferSetupHashForTesting_EnqueuesWorkItem()
    {
        Transference.ResetTransferWorkQueueForTesting();

        byte[] Payload = new byte[Registry.Const.PACKET_BYTES];
        Transference.QueueTransferSetupHashForTesting(Payload);

        Assert.True(Transference.GetTransferWorkQueueCountForTesting() > 0);

        Transference.ResetTransferWorkQueueForTesting();
    }

    /// <summary>
    /// Ensures transfer completion waits behind already queued chunk receive work.
    /// </summary>
    [Fact]
    public void QueueTransferCompleteForTesting_WaitsBehindPendingChunkWork()
    {
        Transference.ResetIncomingTransferStateForTesting();
        Transference.ResetTransferWorkQueueForTesting();

        try
        {
            Guid transferId = Guid.NewGuid();
            byte[] payload = new byte[Registry.Const.PACKET_BYTES];
            byte[] mismatchedHash = new byte[Registry.Const.STANDARD_LENGTH];
            var session = new Transference.TransferSession(
                transferId,
                payload.Length,
                "Eclipse.dll",
                mismatchedHash,
                Array.Empty<byte>());

            Transference.RegisterIncomingTransferForTesting(session, 1234);
            Transference.QueueTransferChunkForTesting(transferId, 0, payload);
            Transference.QueueTransferCompleteForTesting(new Transference.TransferComplete(transferId, false));

            Assert.Equal(2, Transference.GetTransferWorkQueueCountForTesting());
        }
        finally
        {
            Transference.ResetIncomingTransferStateForTesting();
            Transference.ResetTransferWorkQueueForTesting();
        }
    }
}
