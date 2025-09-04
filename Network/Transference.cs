using BepInEx;
using BepInEx.Unity.IL2CPP;
using Emberglass.API.Shared;
using Emberglass.Utilities;
using ProjectM.Network;
using ProjectM.UI;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using static Emberglass.Network.Registry;

namespace Emberglass.Network;
internal static class Transference
{
    public unsafe struct TransferRequest
    {
        public readonly bool Clientbound;
        public readonly bool Hotload;

        public fixed byte FileName[Const.STANDARD_LENGTH];
        public TransferRequest(ReadOnlySpan<char> fileName, bool clientbound, bool hotload)
        {
            fixed (byte* dest = FileName)
            {
                int bytesWritten = System.Text.Encoding.UTF8.GetBytes(fileName, new Span<byte>(dest, Const.STANDARD_LENGTH));
                for (int i = bytesWritten; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }

            Clientbound = clientbound;
            Hotload = hotload;
        }
        public string FileNameString
        {
            get
            {
                fixed (byte* p = FileName)
                {
                    int len = 0;
                    while (len < Const.STANDARD_LENGTH && p[len] != 0)
                    {
                        len++;
                    }

                    return System.Text.Encoding.UTF8.GetString(p, len);
                }
            }
        }
    }
    public unsafe struct TransferSession
    {
        public readonly Guid Id;
        public readonly int TotalBytes;

        public fixed byte FileName[Const.STANDARD_LENGTH];
        public fixed byte Sha256[Const.STANDARD_LENGTH];
        public TransferSession(Guid id, int totalBytes, ReadOnlySpan<char> fileName, ReadOnlySpan<byte> sha256)
        {
            Id = id;
            TotalBytes = totalBytes;

            fixed (byte* dest = FileName)
            {
                int bytesWritten = System.Text.Encoding.UTF8.GetBytes(fileName, new Span<byte>(dest, Const.STANDARD_LENGTH));
                for (int i = bytesWritten; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }

            fixed (byte* dest = Sha256)
            {
                int bytesToCopy = Math.Min(sha256.Length, Const.STANDARD_LENGTH);
                for (int i = 0; i < bytesToCopy; i++)
                {
                    dest[i] = sha256[i];
                }

                for (int i = bytesToCopy; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }
        }
        public string FileNameString
        {
            get
            {
                fixed (byte* p = FileName)
                {
                    int len = 0;
                    while (len < Const.STANDARD_LENGTH && p[len] != 0)
                    {
                        len++;
                    }

                    return System.Text.Encoding.UTF8.GetString(p, len);
                }
            }
        }
        public ReadOnlySpan<byte> Sha256Span
        {
            get
            {
                fixed (byte* ptr = Sha256)
                {
                    return new ReadOnlySpan<byte>(ptr, Const.STANDARD_LENGTH);
                }
            }
        }
        public byte[] Sha256Bytes => Sha256Span.ToArray();
    }
    public readonly struct TransferComplete
    {
        public readonly Guid Id;
        public readonly bool Hotload;
        public TransferComplete(Guid id, bool hotload)
        {
            Id = id;
            Hotload = hotload;
        }
    }
    public unsafe struct TransferChunk
    {
        public readonly Guid Id;
        public readonly int Index;
        public readonly ushort Length;

        public fixed byte Packet[Const.PACKET_BYTES];
        public TransferChunk(Guid id, int index, byte[] chunk)
        {
            if (chunk.Length > Const.PACKET_BYTES)
            {
                throw new ArgumentException($"Payload too large: {chunk.Length} > {Const.PACKET_BYTES}", nameof(chunk));
            }

            Id = id;
            Index = index;
            Length = (ushort)chunk.Length;

            for (int i = 0; i < Length; i++)
            {
                Packet[i] = chunk[i];
            }

            for (int i = Length; i < Const.PACKET_BYTES; i++)
            {
                Packet[i] = 0;
            }
        }
        public ReadOnlySpan<byte> ChunkSpan
        {
            get
            {
                fixed (byte* ptr = Packet)
                {
                    return new ReadOnlySpan<byte>(ptr, Length);
                }
            }
        }
        public byte[] ChunkBytes => ChunkSpan.ToArray();
    }

    static IncomingTransfer _incoming;
    static bool _initialized;

    static readonly WaitForSeconds _delay = new(Const.CHUNK_DELAY);
    public static void Bootstrap()
    {
        if (_initialized)
        {
            return;
        }

        // Register<TransferRequest>(Direction.Serverbound, (u, o) => OnTransferRequest(u, (TransferRequest)o));
        // Register<FileAck>(Direction.Serverbound, (u, o) => OnFileAck((FileAck)o));

        Register<TransferSession>(Direction.Clientbound, (u, o) => OnTransferSession((TransferSession)o));
        Register<TransferComplete>(Direction.Clientbound, (u, o) => OnTransferComplete((TransferComplete)o));
        Register<TransferChunk>(Direction.Clientbound, (u, o) => OnTransferChunk((TransferChunk)o));

        _initialized = true;
    }
    public static void InternalTransferRequest(User user, TransferRequest request)
    {
        TransferRoutine(user, request.FileNameString, request.Clientbound, request.Hotload).Run();
    }
    static IEnumerator TransferRoutine(User target, string fileName, bool clientbound, bool hotload = false)
    {
        bool isZip = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string pluginName = Path.GetFileNameWithoutExtension(fileName);
        byte[] fileBytes = null;

        if (isZip)
        {
            if (!VShare.TryGetCachedZip(pluginName, out fileBytes))
            {
                VWorld.Log.LogWarning($"Unable to locate {fileName} in cache directory...");
                yield break;
            }
            
            if (!clientbound)
            {
                bool decompressionDone = false;
                byte[] decompressed = null;
                yield return DecompressChunkRoutine(fileBytes, result => { decompressed = result; decompressionDone = true; });
                while (!decompressionDone)
                {
                    yield return null;
                }

                try
                {
                    using var mem = new MemoryStream(decompressed);
                    using var archive = new ZipArchive(mem, ZipArchiveMode.Read);
                    archive.ExtractToDirectory(Paths.GameRootPath, true);
                    VWorld.Log.LogWarning($"Extracted {fileName} to {Paths.GameRootPath}");
                }
                catch (Exception ex)
                {
                    VWorld.Log.LogError($"Package extraction failed: {ex}");
                }

                yield break;
            }
        }
        else
        {
            if (VShare.TryGetCachedDll(fileName, out byte[] cachedBytes))
            {
                string operation = clientbound ? "Sending" : "Writing";
                VWorld.Log.LogWarning($"{operation} from cache directory...");

                if (!clientbound)
                {
                    bool decompressionDone = false;

                    yield return DecompressChunkRoutine(
                        cachedBytes,
                        result =>
                        {
                            fileBytes = result;
                            decompressionDone = true;
                        }
                    );

                    while (!decompressionDone)
                    {
                        yield return null;
                    }

                    string filePath = Path.Combine(Paths.PluginPath, fileName);
                    File.WriteAllBytes(filePath, fileBytes);

                    if (!File.Exists(filePath))
                    {
                        VWorld.Log.LogWarning("Failed writing to disk...");
                    }
                    else
                    {
                        VWorld.Log.LogWarning($"Download for {fileName} complete! ({DateTime.Now:HH\\:mm\\:ss})");

                        if (hotload)
                        {
                            VWorld.Log.LogWarning("Loading plugin...");
                            LoadPlugin(filePath);
                        }
                    }

                    yield break;
                }
                else
                {
                    fileBytes = cachedBytes;
                }
            }
            else
            {
                VWorld.Log.LogWarning($"Unable to locate {fileName} in cache directory...");
                yield break;
            }
        }

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(fileBytes);

        Guid id = Guid.NewGuid();
        ushort chunkSize = Const.PACKET_BYTES;
        int idx = 0;

        VWorld.Log.LogWarning($"Starting plugin transfer ~ ID: {id} | Plugin: {fileName} | Size: {fileBytes.Length.PrettyBytes()} ({DateTime.Now.TimeOfDay})");

        var init = new TransferSession(id, fileBytes.Length, fileName.AsSpan(), hash.AsSpan());
        VNetwork.SendToClient(target, init);

        for (int i = 0; i < fileBytes.Length; i += chunkSize, idx++)
        {
            int len = Math.Min(chunkSize, fileBytes.Length - i);
            byte[] chunkBytes = new byte[len];
            Buffer.BlockCopy(fileBytes, i, chunkBytes, 0, len);

            var chunk = new TransferChunk(id, idx, chunkBytes);
            VNetwork.SendToClient(target, chunk);

            yield return _delay;
        }

        VNetwork.SendToClient(target, new TransferComplete(id, hotload));
    }
    static void OnTransferSession(TransferSession session)
    {
        VWorld.Log.LogWarning($"Starting plugin transfer ~ ID: {session.Id} | Plugin: {session.FileNameString} | Size: {session.TotalBytes.PrettyBytes()} ({DateTime.Now.TimeOfDay})");
        _incoming = new(session);
    }
    static void OnTransferChunk(TransferChunk chunk)
    {
        _incoming?.AddChunk(chunk.Index, chunk.ChunkBytes);
    }
    static void OnTransferComplete(TransferComplete complete)
    {
        bool ok = _incoming?.IsComplete == true && _incoming.Verify();

        if (!ok)
        {
            LogFailure(complete);
            return;
        }

        var incomingCopy = _incoming;
        _incoming = null;

        IncomingRoutine(complete, incomingCopy).Run();
    }
    static IEnumerator IncomingRoutine(TransferComplete complete, IncomingTransfer incoming)
    {
        bool decompressionDone = false;
        byte[] bytes = null;

        yield return DecompressChunkRoutine(
            incoming.Concat(),
            result =>
            {
                bytes = result;
                decompressionDone = true;
            }
        );

        while (!decompressionDone)
        {
            yield return null;
        }

        bool isZip = incoming.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (isZip)
        {
            try
            {
                using var mem = new MemoryStream(bytes);
                using var archive = new ZipArchive(mem, ZipArchiveMode.Read);
                archive.ExtractToDirectory(Paths.GameRootPath, true);
                VWorld.Log.LogWarning($"Extracted {incoming.FileName} to {Paths.GameRootPath}");
            }
            catch (Exception ex)
            {
                VWorld.Log.LogError($"Zip extraction failed: {ex}");
            }

            yield break;
        }

        string filePath = Path.Combine(Paths.PluginPath, incoming.FileName);

        try
        {
            File.WriteAllBytes(filePath, bytes);

            if (!File.Exists(filePath))
            {
                VWorld.Log.LogWarning("Failed writing to disk...");
            }
            else
            {
                VWorld.Log.LogWarning(
                    $"Download for {incoming.FileName} complete! " +
                    $"({DateTime.Now:HH\\:mm\\:ss})");

                if (complete.Hotload)
                {
                    VWorld.Log.LogWarning("Loading plugin...");
                    LoadPlugin(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"File write / load error: {ex}");
        }

        // VNetwork.SendToServer(new FileAck(done.Id, success));
    }
    static void LogFailure(TransferComplete _)
    {
        VWorld.Log.LogWarning($"Transfer failed! ({DateTime.Now:HH\\:mm\\:ss})");

        if (_incoming?.IsComplete == false)
        {
            VWorld.Log.LogWarning($"{_incoming.TotalBytes.PrettyBytes()} bytes expected, only {_incoming.Concat().Length.PrettyBytes()} bytes received...");
        }

        if (_incoming?.Verify() == false)
        {
            VWorld.Log.LogWarning("Didn't pass SHA-256 verification...");
        }

        // VNetwork.SendToServer(new FileAck(_.Id, false));
        _incoming = null;
    }
    static void LoadPlugin(string filePath)
    {
        Assembly assembly = Assembly.LoadFrom(filePath);
        Type type = assembly
            .GetTypes()
            .First(t => typeof(BasePlugin).IsAssignableFrom(t) && !t.IsAbstract) ?? throw new InvalidOperationException("Failed to get BasePlugin type...");

        var plugin = (BasePlugin)Activator.CreateInstance(type) ?? throw new InvalidOperationException("Failed to create BasePlugin instance...");
        BepInPlugin metadata = MetadataHelper.GetMetadata(plugin);

        plugin.Load();
        Hotloader.ReflectAndInitialize(assembly);
    }
    public static IEnumerator CompressChunkRoutine(
        byte[] bytes,
        Action<byte[]> onComplete)
    {
        using var combined = new MemoryStream();

        for (int i = 0; i < bytes.Length; i += Const.PACKET_BYTES)
        {
            int len = Math.Min(Const.PACKET_BYTES, bytes.Length - i);

            byte[] compressedSlice;
            using (var slice = new MemoryStream(bytes, i, len, writable: false))
            using (var sliceOut = new MemoryStream())
            {
                using (var brotli = new BrotliStream(
                    sliceOut,
                    System.IO.Compression.CompressionLevel.Fastest,
                    leaveOpen: true))
                {
                    slice.CopyTo(brotli);
                }

                compressedSlice = sliceOut.ToArray();
            }

            combined.Write(BitConverter.GetBytes(compressedSlice.Length));
            combined.Write(compressedSlice, 0, compressedSlice.Length);

            yield return _delay;
        }

        onComplete(combined.ToArray());
    }
    public static IEnumerator DecompressChunkRoutine(
        byte[] compressedBuffer,
        Action<byte[]> onComplete)
    {
        using var output = new MemoryStream();
        int offset = 0;

        while (offset < compressedBuffer.Length)
        {
            if (offset + 4 > compressedBuffer.Length)
            {
                throw new InvalidDataException("Truncated length prefix.");
            }

            int sliceLen = BitConverter.ToInt32(compressedBuffer, offset);
            offset += 4;

            if (offset + sliceLen > compressedBuffer.Length)
            {
                throw new InvalidDataException("Truncated compressed slice.");
            }

            using (var sliceIn = new MemoryStream(compressedBuffer, offset, sliceLen, writable: false))
            using (var brotli = new BrotliStream(sliceIn, CompressionMode.Decompress))
            {
                brotli.CopyTo(output);
            }

            offset += sliceLen;

            yield return _delay;
        }

        onComplete(output.ToArray());
    }
    public class IncomingTransfer
    {
        public Guid Id;
        public int TotalBytes;
        public string FileName;
        public byte[] Sha = [];
        public bool IsComplete => _chunks.Values.Sum(c => c.Length) >= TotalBytes;
        readonly Dictionary<int, byte[]> _chunks = [];
        public IncomingTransfer(TransferSession session)
        {
            Id = session.Id;
            FileName = session.FileNameString;
            TotalBytes = session.TotalBytes;
            Sha = session.Sha256Bytes;
        }
        public void AddChunk(int idx, byte[] bytes)
        {
            if (_chunks.ContainsKey(idx))
            {
                return;
            }

            _chunks[idx] = bytes;
        }
        public byte[] Concat()
        {
            return [.._chunks.OrderBy(k => k.Key).SelectMany(k => k.Value)];
        }
        public bool Verify()
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Concat()).SequenceEqual(Sha);
        }
    }

    /*  
    internal readonly struct FileAck
    {
        public readonly Guid Id;
        public readonly bool Ok;
        public FileAck(Guid id, bool ok)
        {
            Id = id;
            Ok = ok;
        }
    }

    static void OnFileAck(FileAck ack)
    {
        VWorld.Log.LogWarning($"File transfer {(ack.Ok ? "succeeded" : "failed")} ~ ID: {ack.Id} ({DateTime.Now.TimeOfDay})");
    }

    public static string GetDllPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".dll"))
            return string.Empty;

        string pluginsPath = Paths.PluginPath;
        var dllPaths = Directory.GetFiles(pluginsPath, "*.dll", SearchOption.AllDirectories);

        string targetName = Path.GetFileName(filename);
        var match = dllPaths.FirstOrDefault(p =>
            string.Equals(Path.GetFileName(p), targetName, StringComparison.OrdinalIgnoreCase));

        return match ?? string.Empty;
    }
    */
}