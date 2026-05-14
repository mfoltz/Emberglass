using Emberglass.API.Shared;
using ProjectM.Network;
using System.Collections.Concurrent;
using static Emberglass.Network.Serialization;

namespace Emberglass.Network;
internal static class Registry
{
    public enum Direction : byte
    {
        Serverbound = 0,
        Clientbound = 1
    }
    public static class Const
    {
        [Obsolete("Legacy handshake only; authenticated handshakes use P-256 signatures.")]
        public const string SHARED_KEY = MyPluginInfo.PLUGIN_VERSION;
        public const string HKDF_INFO = "VNET-HMAC";
        public const int PROTOCOL_VERSION = 2;
        public const int LEGACY_PROTOCOL_VERSION = 1;
        public const bool ALLOW_LEGACY_HANDSHAKE = true;
        public const int MAX_BYTES = 512;
        public const int PACKET_BYTES = 320;
        public const int PREFIX_BYTES = 8;
        public const int HEADER_BYTES = 32;
        public const int RESERVED_BYTES = PREFIX_BYTES + HEADER_BYTES;
        public const int REMAINING_BYTES = MAX_BYTES - RESERVED_BYTES;
        public const string PREFIX = "#VNET:";
        public const int STANDARD_LENGTH = 32;
        public const int EXCHANGE_LENGTH = 160;
        public const int HANDSHAKE_NONCE_BYTES = 16;
        public const int HANDSHAKE_SIGNATURE_BYTES = 64;
        public const int MAC_TAG_BYTES = 8;
        public const int CHUNK_BYTES = KiB * KB;
        public const float CHUNK_DELAY = 0.05f;
        const int KB = 32;
        const int KiB = 1024;
    }
    public record Handler(Direction Dir,
        Action<User, object> Invoke,
        UnpackDelHandler Unpack);

    readonly record struct HandlerKey(Direction Direction, uint TypeId);

    static readonly ConcurrentDictionary<HandlerKey, Handler> _handlers = new();
    public static void Register<T>(Direction direction, Action<User, object> action)
    {
        Type type = typeof(T);
        uint id = Hash32(type.FullName!);

        VWorld.Log?.LogInfo($"[VNetwork.Registry] registered {type.Name} as {direction}");

        UnpackDelHandler unpacker = GetUnpacker(type);
        _handlers[new HandlerKey(direction, id)] = new Handler(direction, action, unpacker);
    }
    public static void Unregister<T>()
    {
        uint id = Hash32(typeof(T).FullName!);
        _handlers.TryRemove(new HandlerKey(Direction.Serverbound, id), out _);
        _handlers.TryRemove(new HandlerKey(Direction.Clientbound, id), out _);
    }
    public static bool TryGet(Direction direction, uint id, out Handler handler)
        => _handlers.TryGetValue(new HandlerKey(direction, id), out handler);
    public static IEnumerable<KeyValuePair<(Direction Direction, uint TypeId), Handler>> All
    {
        get
        {
            foreach (var entry in _handlers)
            {
                yield return new KeyValuePair<(Direction Direction, uint TypeId), Handler>(
                    (entry.Key.Direction, entry.Key.TypeId),
                    entry.Value);
            }
        }
    }
    public static uint Hash32(string s)
    {
        unchecked
        {
            uint hash = 0x811C9DC5;
            foreach (char c in s)
            {
                hash = (hash ^ c) * 0x01000193;
            }

            return hash == 0 ? 1u : hash;
        }
    }
    public static string PrettyBytes(this int size)
    {
        long bytes = size;
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        const double TB = GB * 1024;

        if (bytes < KB)
        {
            return $"{bytes} B";
        }

        if (bytes < MB)
        {
            return $"{bytes / KB:0.##} KB";
        }

        if (bytes < GB)
        {
            return $"{bytes / MB:0.##} MB";
        }

        if (bytes < TB)
        {
            return $"{bytes / GB:0.##} GB";
        }

        return $"{bytes / TB:0.##} TB";
    }
}
