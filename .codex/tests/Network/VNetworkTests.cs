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

        Assert.Contains("SendToServerStruct cannot be used before the network session is ready", Exception.Message);
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

        Assert.Contains("SendToClientStruct cannot be used before the network session is ready", Exception.Message);
    }

    /// <summary>
    /// Ensures request sends cannot enqueue work before an authenticated session is available.
    /// </summary>
    [Fact]
    public async Task SendRequestAsync_ThrowsWhenNetworkIsNotReady()
    {
        using IDisposable runtimeContextScope = VWorld.BeginRuntimeContextOverride(isClient: true);

        InvalidOperationException Exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await VNetwork.SendRequestAsync<TestPacket, TestPacket>(
                default(User),
                new TestPacket(1),
                TimeSpan.FromSeconds(1)));

        Assert.Contains("SendRequestAsync cannot be used before the network session is ready", Exception.Message);
    }

    /// <summary>
    /// Ensures client readiness follows the authenticated session lifecycle.
    /// </summary>
    [Fact]
    public void ClientSessionReadiness_TogglesAroundHandshake()
    {
        using IDisposable runtimeContextScope = VWorld.BeginRuntimeContextOverride(isClient: true);

        SetIsReady(true);
        VNetwork.MarkClientSessionNotReady();

        Assert.False(VNetwork.IsReady);

        VNetwork.RaiseClientReady();

        Assert.True(VNetwork.IsReady);
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
