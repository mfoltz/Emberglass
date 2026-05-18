using Emberglass.Patches.Shared;
using HarmonyLib;
using System.Reflection;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for the internally testable world-bootstrap registration seam.
/// </summary>
[Collection("Assembly setup")]
public sealed class WorldBootstrapPatchesTests : IDisposable
{
    /// <summary>
    /// Ensures client and server registrations are deduplicated and isolated.
    /// </summary>
    [Fact]
    public void RegisterSystems_DeduplicatesAndKeepsWorldListsSeparate()
    {
        WorldBootstrapPatches.RegisterClientSystems(new[] { typeof(PlainClientSystem), typeof(PlainClientSystem) });
        WorldBootstrapPatches.RegisterServerSystems(new[] { typeof(PlainServerSystem), typeof(PlainServerSystem) });

        Assert.Equal(new[] { typeof(PlainClientSystem) }, WorldBootstrapPatches.TestHooks.RegisteredClientSystems);
        Assert.Equal(new[] { typeof(PlainServerSystem) }, WorldBootstrapPatches.TestHooks.RegisteredServerSystems);
    }

    /// <summary>
    /// Ensures IL2CPP validation rejects generic injected system shapes before runtime injection.
    /// </summary>
    [Fact]
    public void ValidateSystemType_RejectsOpenAndConstructedGenericSystemShapes()
    {
        WorldBootstrapPatches.TestHooks.ValidateSystemType(typeof(PlainServerSystem));

        InvalidOperationException OpenGenericException = Assert.Throws<InvalidOperationException>(
            () => WorldBootstrapPatches.TestHooks.ValidateSystemType(typeof(OpenGenericSystem<>)));
        InvalidOperationException ConstructedGenericException = Assert.Throws<InvalidOperationException>(
            () => WorldBootstrapPatches.TestHooks.ValidateSystemType(typeof(ConstructedGenericSystem)));

        Assert.Contains("generic", OpenGenericException.Message);
        Assert.Contains("constructed generic base", ConstructedGenericException.Message);
    }

    /// <summary>
    /// Ensures the registration executor invokes registrations before sorting.
    /// </summary>
    [Fact]
    public void ExecuteRegistration_RegistersSystemsBeforeSorting()
    {
        List<string> Calls = new();

        WorldBootstrapPatches.TestHooks.ExecuteRegistration(
            new[] { typeof(PlainClientSystem), typeof(PlainServerSystem) },
            type => Calls.Add($"add:{type.Name}"),
            () => Calls.Add("sort"));

        Assert.Equal(new[] { "add:PlainClientSystem", "add:PlainServerSystem", "sort" }, Calls);
    }

    /// <summary>
    /// Ensures teardown clears the bootstrap patch sentinel so later initialization can patch again.
    /// </summary>
    [Fact]
    public void Uninitialize_ClearsHarmonySentinel()
    {
        FieldInfo HarmonyField = GetHarmonyField();
        HarmonyField.SetValue(null, new Harmony("emberglass.test.worldbootstrap"));

        WorldBootstrapPatches.Uninitialize();

        Assert.Null(HarmonyField.GetValue(null));
    }

    /// <summary>
    /// Ensures initialization keeps the bootstrap patch ready for systems registered later.
    /// </summary>
    [Fact]
    public void ShouldInitializePatch_DoesNotRequireRegisteredSystems()
    {
        Assert.True(WorldBootstrapPatches.TestHooks.ShouldInitializePatch());
    }

    /// <summary>
    /// Clears registered systems after each test.
    /// </summary>
    public void Dispose()
    {
        WorldBootstrapPatches.TestHooks.ClearRegisteredSystems();
        GetHarmonyField().SetValue(null, null);
    }

    static FieldInfo GetHarmonyField()
        => typeof(WorldBootstrapPatches)
            .GetField("_harmony", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WorldBootstrapPatches._harmony field not found.");

    sealed class PlainClientSystem
    {
    }

    sealed class PlainServerSystem
    {
    }

    sealed class OpenGenericSystem<T>
    {
    }

    abstract class GenericBaseSystem<T>
    {
    }

    sealed class ConstructedGenericSystem : GenericBaseSystem<int>
    {
    }
}
