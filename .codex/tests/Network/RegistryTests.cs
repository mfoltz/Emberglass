using Emberglass.Network;
using Xunit;
using static Emberglass.Network.Registry;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for packet handler registration.
/// </summary>
[Collection("Assembly setup")]
public sealed class RegistryTests : IDisposable
{
    /// <summary>
    /// Ensures registering the same packet type in both directions does not overwrite either handler.
    /// </summary>
    [Fact]
    public void Register_SameTypeInBothDirections_KeepsHandlersSeparate()
    {
        Registry.Register<BidirectionalPacket>(Direction.Serverbound, (_, packet) =>
            Assert.Equal("server", ((BidirectionalPacket)packet).Value));
        Registry.Register<BidirectionalPacket>(Direction.Clientbound, (_, packet) =>
            Assert.Equal("client", ((BidirectionalPacket)packet).Value));

        uint TypeId = Registry.Hash32(typeof(BidirectionalPacket).FullName!);

        Assert.True(Registry.TryGet(Direction.Serverbound, TypeId, out Handler ServerboundHandler));
        Assert.True(Registry.TryGet(Direction.Clientbound, TypeId, out Handler ClientboundHandler));
        Assert.Equal(Direction.Serverbound, ServerboundHandler.Dir);
        Assert.Equal(Direction.Clientbound, ClientboundHandler.Dir);

        ServerboundHandler.Invoke(default, new BidirectionalPacket("server"));
        ClientboundHandler.Invoke(default, new BidirectionalPacket("client"));
    }

    /// <summary>
    /// Ensures unregistering a packet type removes both directional handlers.
    /// </summary>
    [Fact]
    public void Unregister_RemovesBothDirectionsForPacketType()
    {
        Registry.Register<BidirectionalPacket>(Direction.Serverbound, (_, _) => { });
        Registry.Register<BidirectionalPacket>(Direction.Clientbound, (_, _) => { });

        Registry.Unregister<BidirectionalPacket>();

        uint TypeId = Registry.Hash32(typeof(BidirectionalPacket).FullName!);
        Assert.False(Registry.TryGet(Direction.Serverbound, TypeId, out _));
        Assert.False(Registry.TryGet(Direction.Clientbound, TypeId, out _));
    }

    /// <summary>
    /// Clears test registrations after each test.
    /// </summary>
    public void Dispose()
        => Registry.Unregister<BidirectionalPacket>();

    sealed record BidirectionalPacket(string Value);
}
