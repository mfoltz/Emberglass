using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers incoming transfer chunk ordering, duplication, and hash verification.
/// </summary>
[Collection("Assembly setup")]
public sealed class IncomingTransferTests
{
    /// <summary>
    /// Ensures out-of-order chunks complete the transfer only after all chunks arrive.
    /// </summary>
    [Fact]
    public void AddChunk_OutOfOrder_CompletesWhenAllChunksArrive()
    {
        byte[] Payload = CreateSequentialPayload(Registry.Const.PACKET_BYTES * 2 + 10);
        Transference.TransferSession Session = CreateTransferSession(Payload, "OutOfOrder.dll");
        Transference.IncomingTransfer Transfer = new(Session, sourcePlatformId: 1);
        List<byte[]> Chunks = SplitIntoChunks(Payload);

        Transfer.AddChunk(1, Chunks[1]);
        Assert.False(Transfer.IsComplete);
        Transfer.AddChunk(0, Chunks[0]);
        Assert.False(Transfer.IsComplete);
        Transfer.AddChunk(2, Chunks[2]);

        Assert.True(Transfer.IsComplete);
        Assert.Equal(Payload.Length, Transfer.ReceivedBytes);
        Assert.Equal(Chunks.Count, Transfer.ReceivedChunkCount);
        Assert.Equal(Chunks.Count, Transfer.ExpectedChunkCount);
    }

    /// <summary>
    /// Ensures duplicate chunks are ignored and do not change completion status.
    /// </summary>
    [Fact]
    public void AddChunk_DuplicateChunk_IgnoresDuplicate()
    {
        byte[] Payload = CreateSequentialPayload(Registry.Const.PACKET_BYTES + 5);
        Transference.TransferSession Session = CreateTransferSession(Payload, "DuplicateChunk.dll");
        Transference.IncomingTransfer Transfer = new(Session, sourcePlatformId: 2);
        List<byte[]> Chunks = SplitIntoChunks(Payload);

        Transfer.AddChunk(0, Chunks[0]);
        int ReceivedBytesAfterFirstChunk = Transfer.ReceivedBytes;
        int ReceivedChunksAfterFirstChunk = Transfer.ReceivedChunkCount;

        Transfer.AddChunk(0, Chunks[0]);

        Assert.False(Transfer.IsComplete);
        Assert.Equal(ReceivedBytesAfterFirstChunk, Transfer.ReceivedBytes);
        Assert.Equal(ReceivedChunksAfterFirstChunk, Transfer.ReceivedChunkCount);

        Transfer.AddChunk(1, Chunks[1]);

        Assert.True(Transfer.IsComplete);
        Assert.Equal(Payload.Length, Transfer.ReceivedBytes);
    }

    /// <summary>
    /// Ensures altered payload data fails hash verification even when all chunks are present.
    /// </summary>
    [Fact]
    public void Verify_WithAlteredPayload_ReturnsFalse()
    {
        byte[] OriginalPayload = CreateSequentialPayload(Registry.Const.PACKET_BYTES + 17);
        Transference.TransferSession Session = CreateTransferSession(OriginalPayload, "AlteredPayload.dll");
        Transference.IncomingTransfer Transfer = new(Session, sourcePlatformId: 3);
        byte[] AlteredPayload = (byte[])OriginalPayload.Clone();
        AlteredPayload[5] ^= 0xFF;
        List<byte[]> Chunks = SplitIntoChunks(AlteredPayload);

        Transfer.AddChunk(0, Chunks[0]);
        Transfer.AddChunk(1, Chunks[1]);

        Assert.True(Transfer.IsComplete);
        Assert.False(Transfer.Verify());
    }

    /// <summary>
    /// Ensures boundary payload sizes resolve to the expected chunk counts.
    /// </summary>
    /// <param name="payloadSize">The payload size to test.</param>
    /// <param name="expectedChunkCount">The expected number of chunks.</param>
    [Theory]
    [InlineData(Registry.Const.PACKET_BYTES - 1, 1)]
    [InlineData(Registry.Const.PACKET_BYTES, 1)]
    [InlineData(Registry.Const.PACKET_BYTES + 1, 2)]
    public void AddChunk_BoundarySizes_TracksExpectedChunkCount(int payloadSize, int expectedChunkCount)
    {
        byte[] Payload = CreateSequentialPayload(payloadSize);
        Transference.TransferSession Session = CreateTransferSession(Payload, $"Boundary-{payloadSize}.dll");
        Transference.IncomingTransfer Transfer = new(Session, sourcePlatformId: 4);
        List<byte[]> Chunks = SplitIntoChunks(Payload);

        Assert.Equal(expectedChunkCount, Transfer.ExpectedChunkCount);

        for (int index = 0; index < Chunks.Count; index++)
        {
            Transfer.AddChunk(index, Chunks[index]);
        }

        Assert.True(Transfer.IsComplete);
        Assert.Equal(payloadSize, Transfer.ReceivedBytes);
    }

    /// <summary>
    /// Creates a deterministic payload of the requested length.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <returns>The generated payload bytes.</returns>
    static byte[] CreateSequentialPayload(int length)
    {
        byte[] payload = new byte[length];

        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % byte.MaxValue);
        }

        return payload;
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

    /// <summary>
    /// Splits a payload into fixed-size chunks.
    /// </summary>
    /// <param name="payload">The payload to chunk.</param>
    /// <returns>The ordered chunk list.</returns>
    static List<byte[]> SplitIntoChunks(byte[] payload)
    {
        List<byte[]> chunks = new();

        for (int offset = 0; offset < payload.Length; offset += Registry.Const.PACKET_BYTES)
        {
            int chunkLength = Math.Min(Registry.Const.PACKET_BYTES, payload.Length - offset);
            byte[] chunk = new byte[chunkLength];
            Buffer.BlockCopy(payload, offset, chunk, 0, chunkLength);
            chunks.Add(chunk);
        }

        return chunks;
    }
}
