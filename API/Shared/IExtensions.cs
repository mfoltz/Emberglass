using BepInEx.Unity.IL2CPP.Utils.Collections;
using Stunlock.Core;
using System.Collections;
using UnityEngine;

namespace Emberglass.API.Shared;
public static class IExtensions
{
    static MonoBehaviour Instance => VBehaviour.Instance;
    public static Dictionary<TValue, TKey> Reverse<TKey, TValue>(this IDictionary<TKey, TValue> source)
        where TKey : notnull
        where TValue : notnull
    {
        var reversed = new Dictionary<TValue, TKey>();

        foreach (var kvp in source)
        {
            reversed[kvp.Value] = kvp.Key;
        }

        return reversed;
    }

    public static void ForEach<T>(this IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection)
        {
            action(item);
        }
    }

    public static bool ContainsAll(this string stringChars, List<string> strings)
    {
        foreach (string str in strings)
        {
            if (!stringChars.Contains(str, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool ContainsAny(this string stringChars, List<string> strings)
    {
        foreach (string str in strings)
        {
            if (stringChars.Contains(str, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsIndexWithinRange<T>(this IList<T> list, int index)
    {
        return index >= 0 && index < list.Count;
    }

    public static bool Equals(this PrefabGUID value, params PrefabGUID[] prefabGuids)
    {
        foreach (PrefabGUID prefabGuid in prefabGuids)
        {
            if (value.Equals(prefabGuid))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Equals<T>(this T value, params T[] options)
    {
        foreach (var option in options)
        {
            if (value.Equals(option))
            {
                return true;
            }
        }

        return false;
    }
    public static Coroutine Start(this IEnumerator routine)
    {
        return Instance.StartCoroutine(routine.WrapToIl2Cpp());
    }
    public static void Stop(this Coroutine routine)
    {
        Instance.StopCoroutine(routine);
    }
    public static void Run(this IEnumerator routine)
    {
        Instance.StartCoroutine(routine.WrapToIl2Cpp());
    }
}
