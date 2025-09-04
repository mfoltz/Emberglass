using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Emberglass.Network;
internal static class Serialization
{
    public delegate byte[] PackDelHandler(object obj);
    public delegate object UnpackDelHandler(ReadOnlySpan<byte> data);

    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    public static PackDelHandler GetPacker(Type type) => _packers.GetOrAdd(type, CreatePacker);
    public static UnpackDelHandler GetUnpacker(Type type) => _unpackers.GetOrAdd(type, CreateUnpacker);

    static readonly ConcurrentDictionary<Type, PackDelHandler> _packers = new();
    static readonly ConcurrentDictionary<Type, UnpackDelHandler> _unpackers = new();
    static PackDelHandler CreatePacker(Type type)
    {
        if (IsBlittable(type))
        {
            int size = Marshal.SizeOf(type);

            return obj =>
            {
                byte[] bytes = new byte[size];
                IntPtr ptr = Marshal.AllocHGlobal(size);

                try
                {
                    Marshal.StructureToPtr(obj, ptr, false);
                    Marshal.Copy(ptr, bytes, 0, size);
                }
                finally { Marshal.FreeHGlobal(ptr); }

                return bytes;
            };
        }

        return obj => JsonSerializer.SerializeToUtf8Bytes(obj, type, _jsonOptions);
    }
    static UnpackDelHandler CreateUnpacker(Type type)
    {
        if (IsBlittable(type))
        {
            int size = Marshal.SizeOf(type);

            return dataSpan =>
            {
                byte[] buffer = dataSpan.ToArray();
                IntPtr ptr = Marshal.AllocHGlobal(size);

                try
                {
                    Marshal.Copy(buffer, 0, ptr, size);
                    return Marshal.PtrToStructure(ptr, type)!;
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            };
        }

        return obj => JsonSerializer.Deserialize(obj, type, _jsonOptions)!;
    }
    static bool IsBlittable(Type t)
    {
        if (!t.IsValueType || t.IsEnum)
        {
            return false;
        }

        var mi = typeof(Serialization)
            .GetMethod(nameof(IsBlittableGeneric), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(t);

        return (bool)mi.Invoke(null, null)!;
    }
    static bool IsBlittableGeneric<T>() where T : struct
        => !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
}
