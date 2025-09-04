using Emberglass.Network;
using ProjectM.Network;
using static Emberglass.Network.Registry;

namespace Emberglass.API.Shared;
public static class VNetwork
{
    // register blittable types
    public static void RegisterServerboundStruct<T>(Action<User, T> handler) where T : unmanaged
        => RegisterServerbound(handler);
    public static void RegisterClientboundStruct<T>(Action<User, T> handler) where T : unmanaged
        => RegisterClientbound(handler);
    public static void RegisterBiDirectionalStruct<T>(
        Action<User, T> serverHandler,
        Action<User, T> clientHandler) where T : unmanaged
    {
        RegisterServerboundStruct(serverHandler);
        RegisterClientboundStruct(clientHandler);
    }
    public static void Unregister<T>() => Registry.Unregister<T>();

    // send typed packets to server or client
    public static void SendToServer<T>(T packet) where T : unmanaged
        => PacketRelay.SendPacketFromClient(VWorld.LocalUser.GetUser(), packet);
    public static void SendToClient<T>(User target, T packet) where T : unmanaged
        => PacketRelay.SendPacketFromServer(target, packet);

    // internal methods
    internal static void RegisterServerbound<T>(Action<User, T> handler) => Register<T>(Direction.Serverbound,
        (sender, obj) => handler(sender, (T)obj));
    internal static void RegisterClientbound<T>(Action<User, T> handler) => Register<T>(Direction.Clientbound,
        (sender, obj) => handler(sender, (T)obj));
    internal static void Register<T>(Direction dir, Action<User, object> boxedHandler)
        => Registry.Register<T>(dir, boxedHandler);
    internal static void Initialize()
        => Bootstrapper.Awake();
}