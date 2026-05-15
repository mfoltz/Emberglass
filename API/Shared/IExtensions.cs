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
        Dictionary<TValue, TKey> reversed = new();

        foreach (var kvp in source)
        {
            reversed[kvp.Value] = kvp.Key;
        }

        return reversed;
    }
    /// <summary>
    /// Builds a managed reverse lookup from an Il2Cpp dictionary, preserving the first key for duplicate values.
    /// </summary>
    /// <typeparam name="TKey">Source dictionary key type.</typeparam>
    /// <typeparam name="TValue">Source dictionary value type.</typeparam>
    /// <param name="source">Il2Cpp dictionary to reverse.</param>
    /// <returns>A managed dictionary keyed by source values.</returns>
    public static Dictionary<TValue, TKey> ReverseIl2CppDictionary<TKey, TValue>(
        this Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> source)
        where TKey : notnull
        where TValue : notnull
    {
        Dictionary<TValue, TKey> reversed = new();

        if (source is null)
        {
            return reversed;
        }

        foreach (var kvp in source)
        {
            if (reversed.ContainsKey(kvp.Value))
            {
                continue;
            }

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

    /// <summary>
    /// Checks whether the source string contains any candidate using the requested comparison behavior.
    /// </summary>
    /// <param name="stringChars">String to inspect.</param>
    /// <param name="strings">Candidate substrings.</param>
    /// <param name="stringComparison">Comparison behavior for each substring lookup.</param>
    /// <returns>True when at least one candidate is present.</returns>
    public static bool ContainsAny(
        this string stringChars,
        List<string> strings,
        StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
    {
        foreach (string str in strings)
        {
            if (stringChars.Contains(str, stringComparison))
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
    /// <summary>
    /// Starts an enumerator as a coroutine after a delay.
    /// </summary>
    /// <param name="routine">Coroutine routine to start.</param>
    /// <param name="delay">Delay in seconds before starting the routine.</param>
    public static void Run(this IEnumerator routine, float delay)
    {
        if (delay <= 0f)
        {
            routine.Run();
            return;
        }

        Instance.StartCoroutine(Delay(routine, delay).WrapToIl2Cpp());
    }
    /// <summary>
    /// Waits for the requested delay before starting another coroutine routine.
    /// </summary>
    /// <param name="routine">Coroutine routine to start after the delay.</param>
    /// <param name="delay">Delay in seconds.</param>
    /// <returns>An enumerator suitable for Unity coroutine execution.</returns>
    public static IEnumerator Delay(IEnumerator routine, float delay)
    {
        yield return new WaitForSeconds(delay);
        routine.Run();
    }
    /// <summary>
    /// Converts a byte flag to a boolean value.
    /// </summary>
    /// <param name="value">Byte flag value.</param>
    /// <returns>False only when the value is zero.</returns>
    public static bool AsBool(this byte value)
        => value != 0;
}
