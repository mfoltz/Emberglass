using System.Reflection;
using Emberglass.API.Shared;
using ProjectM.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for the public VNetwork surface.
/// </summary>
[Collection("Assembly setup")]
public sealed class VNetworkTests : IDisposable
{
    readonly bool originalIsReady;

    /// <summary>
    /// Initializes the test and records mutable VNetwork state.
    /// </summary>
    public VNetworkTests()
    {
        originalIsReady = VNetwork.IsReady;
        SetIsReady(false);
    }

    /// <summary>
    /// Ensures unmanaged client sends use the same readiness guard as JSON-backed sends.
    /// </summary>
    [Fact]
    public void SendToServerStruct_ThrowsWhenNetworkIsNotReady()
    {
        using IDisposable runtimeContextScope = VWorld.BeginRuntimeContextOverride(isClient: true);

        InvalidOperationException Exception = Assert.Throws<InvalidOperationException>(
            () => VNetwork.SendToServerStruct(new TestPacket(1)));

        Assert.Contains("SendToServerStruct cannot be used before VNetwork.Initialize completes", Exception.Message);
    }

    /// <summary>
    /// Ensures unmanaged server sends use the same readiness guard as JSON-backed sends.
    /// </summary>
    [Fact]
    public void SendToClientStruct_ThrowsWhenNetworkIsNotReady()
    {
        using IDisposable runtimeContextScope = VWorld.BeginRuntimeContextOverride(isClient: false);

        InvalidOperationException Exception = Assert.Throws<InvalidOperationException>(
            () => VNetwork.SendToClientStruct(default(User), new TestPacket(1)));

        Assert.Contains("SendToClientStruct cannot be used before VNetwork.Initialize completes", Exception.Message);
    }

    /// <summary>
    /// Restores mutable VNetwork state.
    /// </summary>
    public void Dispose()
        => SetIsReady(originalIsReady);

    static void SetIsReady(bool isReady)
    {
        PropertyInfo Property = typeof(VNetwork).GetProperty(
            nameof(VNetwork.IsReady),
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("VNetwork.IsReady property not found.");
        Property.SetValue(null, isReady);
    }

    readonly record struct TestPacket(int Value);
}
