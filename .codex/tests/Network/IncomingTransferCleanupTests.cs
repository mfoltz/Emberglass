using System;
using System.Security.Cryptography;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Validates incoming transfer timeout cleanup behavior.
/// </summary>
[Collection("Assembly setup")]
public sealed class IncomingTransferCleanupTests
{
    /// <summary>
    /// Ensures expired incoming transfers are removed after cleanup.
    /// </summary>
    [Fact]
    public void CleanupStaleIncomingTransfers_RemovesExpiredTransfers()
    {
        DateTime CurrentTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Func<DateTime> OriginalProvider = Transference.UtcNowProvider;

        try
        {
            Transference.ResetIncomingTransferStateForTesting();
            Transference.UtcNowProvider = () => CurrentTime;

            byte[] Payload = CreateSequentialPayload(Registry.Const.PACKET_BYTES);
            Transference.TransferSession Session = CreateTransferSession(Payload, "ExpiredTransfer.dll");

            Transference.RegisterIncomingTransferForTesting(Session, sourcePlatformId: 11);

            CurrentTime = CurrentTime.Add(Transference.IncomingTransferTimeout).Add(TimeSpan.FromSeconds(1));
            Transference.CleanupStaleIncomingTransfersForTesting();

            Assert.False(Transference.HasIncomingTransferForTesting(Session.Id));
        }
        finally
        {
            Transference.UtcNowProvider = OriginalProvider;
            Transference.ResetIncomingTransferStateForTesting();
        }
    }

    /// <summary>
    /// Ensures active transfers are preserved when chunks arrive recently.
    /// </summary>
    [Fact]
    public void CleanupStaleIncomingTransfers_DoesNotRemoveActiveTransfers()
    {
        DateTime CurrentTime = new(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        Func<DateTime> OriginalProvider = Transference.UtcNowProvider;

        try
        {
            Transference.ResetIncomingTransferStateForTesting();
            Transference.UtcNowProvider = () => CurrentTime;

            byte[] Payload = CreateSequentialPayload(Registry.Const.PACKET_BYTES);
            Transference.TransferSession Session = CreateTransferSession(Payload, "ActiveTransfer.dll");
            Transference.IncomingTransfer Transfer = Transference.RegisterIncomingTransferForTesting(Session, sourcePlatformId: 22);

            CurrentTime = CurrentTime.Add(Transference.IncomingTransferTimeout).Subtract(TimeSpan.FromSeconds(5));
            Transfer.AddChunk(0, Payload);

            CurrentTime = CurrentTime.Add(TimeSpan.FromSeconds(10));
            Transference.CleanupStaleIncomingTransfersForTesting();

            Assert.True(Transference.HasIncomingTransferForTesting(Session.Id));
        }
        finally
        {
            Transference.UtcNowProvider = OriginalProvider;
            Transference.ResetIncomingTransferStateForTesting();
        }
    }

    /// <summary>
    /// Ensures timeout notifications fire once per expired transfer.
    /// </summary>
    [Fact]
    public void CleanupStaleIncomingTransfers_RaisesTimeoutOncePerTransfer()
    {
        DateTime CurrentTime = new(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc);
        Func<DateTime> OriginalProvider = Transference.UtcNowProvider;
        int TimeoutCount = 0;
        Guid TimedOutTransferId = Guid.Empty;

        void HandleTimeout(Transference.IncomingTransfer transfer, TimeSpan idleDuration)
        {
            _ = idleDuration;
            TimeoutCount++;
            TimedOutTransferId = transfer.Id;
        }

        try
        {
            Transference.ResetIncomingTransferStateForTesting();
            Transference.UtcNowProvider = () => CurrentTime;

            byte[] Payload = CreateSequentialPayload(Registry.Const.PACKET_BYTES);
            Transference.TransferSession Session = CreateTransferSession(Payload, "TimeoutSignal.dll");

            Transference.RegisterIncomingTransferForTesting(Session, sourcePlatformId: 33);
            Transference.IncomingTransferTimedOut += HandleTimeout;

            CurrentTime = CurrentTime.Add(Transference.IncomingTransferTimeout).Add(TimeSpan.FromSeconds(1));
            Transference.CleanupStaleIncomingTransfersForTesting();
            Transference.CleanupStaleIncomingTransfersForTesting();

            Assert.Equal(1, TimeoutCount);
            Assert.Equal(Session.Id, TimedOutTransferId);
        }
        finally
        {
            Transference.IncomingTransferTimedOut -= HandleTimeout;
            Transference.UtcNowProvider = OriginalProvider;
            Transference.ResetIncomingTransferStateForTesting();
        }
    }

    /// <summary>
    /// Creates a deterministic payload of the requested length.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <returns>The generated payload bytes.</returns>
    static byte[] CreateSequentialPayload(int length)
    {
        byte[] Payload = new byte[length];

        for (int Index = 0; Index < Payload.Length; Index++)
        {
            Payload[Index] = (byte)(Index % byte.MaxValue);
        }

        return Payload;
    }

    /// <summary>
    /// Builds a transfer session with a calculated SHA-256 hash.
    /// </summary>
    /// <param name="payload">The payload bytes used for hashing.</param>
    /// <param name="fileName">The file name to store in the session.</param>
    /// <returns>The initialized transfer session.</returns>
    static Transference.TransferSession CreateTransferSession(byte[] payload, string fileName)
    {
        using SHA256 Sha = SHA256.Create();
        byte[] Hash = Sha.ComputeHash(payload);
        byte[] EmptyThunderstoreHash = new byte[Registry.Const.STANDARD_LENGTH];

        return new Transference.TransferSession(
            Guid.NewGuid(),
            payload.Length,
            fileName.AsSpan(),
            Hash,
            EmptyThunderstoreHash);
    }
}
