using Emberglass.Network;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers streamed decompression hashing and boundary-sized writes.
/// </summary>
[Collection("Assembly setup")]
public sealed class TransferStreamingHashTests
{
    /// <summary>
    /// Ensures streamed decompression writes and incremental hashing stay correct at boundary sizes.
    /// </summary>
    /// <param name="payloadSize">The payload size to validate.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(Registry.Const.PACKET_BYTES - 1)]
    [InlineData(Registry.Const.PACKET_BYTES)]
    [InlineData(Registry.Const.PACKET_BYTES + 1)]
    [InlineData(Registry.Const.PACKET_BYTES * 2 + 5)]
    public void DecompressChunkRoutine_StreamsSlices_WithIncrementalHashing(int payloadSize)
    {
        Transference.ResetTransferWorkQueueForTesting();
        try
        {
            byte[] payload = BuildPayload(payloadSize);
            byte[] compressed = null;
            var compressRoutine = Transference.CompressChunkRoutine(payload, result => compressed = result);
            while (compressRoutine.MoveNext())
            {
                Transference.ProcessTransferWorkQueueForTesting(TimeSpan.FromMilliseconds(50));
            }

            Assert.NotNull(compressed);

            using var outputStream = new MemoryStream();
            using var incrementalSha = SHA256.Create();
            bool decompressionComplete = false;

            var decompressRoutine = Transference.DecompressChunkRoutine(
                compressed,
                slice =>
                {
                    outputStream.Write(slice, 0, slice.Length);
                    incrementalSha.TransformBlock(slice, 0, slice.Length, null, 0);
                },
                () => decompressionComplete = true,
                Guid.Empty);

            while (decompressRoutine.MoveNext())
            {
                Transference.ProcessTransferWorkQueueForTesting(TimeSpan.FromMilliseconds(50));
            }

            Assert.True(decompressionComplete);
            incrementalSha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            byte[] decompressed = outputStream.ToArray();
            Assert.Equal(payload, decompressed);

            using var expectedSha = SHA256.Create();
            byte[] expectedHash = expectedSha.ComputeHash(payload);
            Assert.NotNull(incrementalSha.Hash);
            Assert.Equal(expectedHash, incrementalSha.Hash);
        }
        finally
        {
            Transference.ResetTransferWorkQueueForTesting();
        }
    }

    /// <summary>
    /// Creates a deterministic payload of the requested size.
    /// </summary>
    /// <param name="size">The payload size.</param>
    /// <returns>The payload bytes.</returns>
    static byte[] BuildPayload(int size)
    {
        byte[] payload = new byte[size];
        for (int index = 0; index < size; index++)
        {
            payload[index] = (byte)(index % byte.MaxValue);
        }

        return payload;
    }
}
