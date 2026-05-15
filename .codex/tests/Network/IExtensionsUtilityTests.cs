using Emberglass.API.Shared;
using System.Reflection;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers pure shared utility extensions that do not require a live V Rising world.
/// </summary>
[Collection("Assembly setup")]
public sealed class IExtensionsUtilityTests
{
    /// <summary>
    /// Ensures byte truthiness matches the compact flag helpers used in V Rising structs.
    /// </summary>
    [Fact]
    public void AsBool_ReturnsFalseOnlyForZero()
    {
        Assert.False(((byte)0).AsBool());
        Assert.True(((byte)1).AsBool());
        Assert.True(((byte)255).AsBool());
    }

    /// <summary>
    /// Ensures callers can choose the string comparison behavior for contains-any checks.
    /// </summary>
    [Fact]
    public void ContainsAny_UsesRequestedStringComparison()
    {
        List<string> candidates = new() { "alpha" };

        Assert.True("ALPHA".ContainsAny(candidates));
        Assert.False("ALPHA".ContainsAny(candidates, StringComparison.Ordinal));
        Assert.True("ALPHA".ContainsAny(candidates, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures the pre-existing two-argument ContainsAny overload remains available to compiled consumers.
    /// </summary>
    [Fact]
    public void ContainsAny_PreservesTwoArgumentOverload()
    {
        Assert.Contains(
            typeof(IExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static),
            Method => Method.Name == nameof(IExtensions.ContainsAny)
                && Method.GetParameters().Length == 2);
    }

    /// <summary>
    /// Ensures coroutine delay and Il2Cpp dictionary parity helpers are exposed without requiring Unity execution.
    /// </summary>
    [Fact]
    public void IExtensions_ExposeCoroutineDelayAndIl2CppDictionaryHelpers()
    {
        Assert.Contains(
            typeof(IExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static),
            Method => Method.Name == nameof(IExtensions.Run)
                && Method.GetParameters().Length == 2
                && Method.GetParameters()[1].ParameterType == typeof(float));

        Assert.NotNull(typeof(IExtensions).GetMethod(
            nameof(IExtensions.Delay),
            BindingFlags.Public | BindingFlags.Static));

        Assert.Contains(
            typeof(IExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static),
            Method => Method.Name == nameof(IExtensions.ReverseIl2CppDictionary));
    }
}
