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
        public const string SHARED_KEY = MyPluginInfo.PLUGIN_VERSION;
        public const int MAX_BYTES = 512;
        public const int PACKET_BYTES = 320;
        public const int PREFIX_BYTES = 8;
        public const int HEADER_BYTES = 32;
        public const int RESERVED_BYTES = PREFIX_BYTES + HEADER_BYTES;
        public const int REMAINING_BYTES = MAX_BYTES - RESERVED_BYTES;
        public const string PREFIX = "#VNET:";
        public const int STANDARD_LENGTH = 32;
        public const int EXCHANGE_LENGTH = 160;
        public const int CHUNK_BYTES = KiB * KB;
        public const float CHUNK_DELAY = 0.05f;
        const int KB = 32;
        const int KiB = 1024;
    }
    public record Handler(Direction Dir,
        Action<User, object> Invoke,
        UnpackDelHandler Unpack); static readonly ConcurrentDictionary<uint, Handler> _handlers = new();
    public static void Register<T>(Direction direction, Action<User, object> action)
    {
        Type type = typeof(T);
        uint id = Hash32(type.FullName!);

        VWorld.Log.LogWarning($"Registering -> {type.Name} w/ {direction}");

        UnpackDelHandler unpacker = GetUnpacker(type);
        _handlers[id] = new Handler(direction, action, unpacker);
    }
    public static void Unregister<T>()
    {
        uint id = Hash32(typeof(T).FullName!);
        _handlers.TryRemove(id, out _);
    }
    public static bool TryGet(uint id, out Handler handler)
        => _handlers.TryGetValue(id, out handler);
    public static IEnumerable<KeyValuePair<uint, Handler>> All => _handlers;
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
