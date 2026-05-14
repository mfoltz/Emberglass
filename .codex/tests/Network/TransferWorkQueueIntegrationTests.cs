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
}
