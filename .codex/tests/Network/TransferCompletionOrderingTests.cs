using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx.Logging;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers ordering between transfer completion packets and queued chunk receive work.
/// </summary>
[Collection("Assembly setup")]
public sealed class TransferCompletionOrderingTests
{
    /// <summary>
    /// Ensures completion does not discard a transfer while received chunks are still queued for processing.
    /// </summary>
    [Fact]
    public void OnTransferComplete_WithQueuedUnprocessedChunks_KeepsIncomingTransfer()
    {
        EnsurePluginLogger();

        byte[] payload = CreateSequentialPayload(Registry.Const.PACKET_BYTES * 3 + 17);
        Transference.TransferSession session = CreateTransferSession(payload, "QueuedCompletion.dll");
        List<byte[]> chunks = SplitIntoChunks(payload);

        Transference.ResetIncomingTransferStateForTesting();
        Transference.ResetTransferWorkQueueForTesting();
        try
        {
            Transference.RegisterIncomingTransferForTesting(session, sourcePlatformId: 1);

            for (int index = 0; index < chunks.Count; index++)
            {
                InvokeOnTransferChunk(new Transference.TransferChunk(session.Id, index, chunks[index]));
            }

            InvokeOnTransferComplete(new Transference.TransferComplete(session.Id, hotload: false));

            Assert.True(Transference.HasIncomingTransferForTesting(session.Id));
            Assert.Equal(chunks.Count + 1, Transference.GetTransferWorkQueueCountForTesting());
        }
        finally
        {
            Transference.ResetIncomingTransferStateForTesting();
            Transference.ResetTransferWorkQueueForTesting();
        }
    }

    static void InvokeOnTransferChunk(Transference.TransferChunk chunk)
        => InvokePrivateStatic("OnTransferChunk", chunk);

    static void InvokeOnTransferComplete(Transference.TransferComplete complete)
        => InvokePrivateStatic("OnTransferComplete", complete);

    static void InvokePrivateStatic(string methodName, object argument)
    {
        MethodInfo method = typeof(Transference).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        method.Invoke(null, new[] { argument });
    }

    static void EnsurePluginLogger()
    {
        Type pluginType = typeof(Transference).Assembly.GetType("Emberglass.Plugin")
            ?? throw new InvalidOperationException("Emberglass.Plugin was not found.");
        PropertyInfo loggerProperty = pluginType.GetProperty("Logger", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Plugin.Logger was not found.");

        if (loggerProperty.GetValue(null) == null)
        {
            loggerProperty.SetValue(null, new ManualLogSource("Emberglass.Tests"));
        }
    }

    static Transference.TransferSession CreateTransferSession(byte[] payload, string fileName)
    {
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(payload);
        byte[] emptyReleaseHash = new byte[Registry.Const.STANDARD_LENGTH];

        return new Transference.TransferSession(
            Guid.NewGuid(),
            payload.Length,
            fileName.AsSpan(),
            hash,
            emptyReleaseHash);
    }

    static byte[] CreateSequentialPayload(int length)
    {
        byte[] payload = new byte[length];

        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % byte.MaxValue);
        }

        return payload;
    }

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
