using Emberglass.API.Shared;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace Emberglass.Network;
internal static class Bootstrapper
{
    static EntityManager EntityManager => VWorld.EntityManager;
    static bool _initialized;

    static readonly ComponentType[] _componentTypes =
    {
        ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
        ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
        ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
        ComponentType.ReadOnly(Il2CppType.Of<ChatMessageEvent>())
    };

    static readonly NetworkEventType _eventType = new()
    {
        IsAdminEvent = false,
        EventId = NetworkEvents.EventId_ChatMessageEvent,
        IsDebugEvent = false,
    };
    public static void Awake()
    {
        if (_initialized)
        {
            return;
        }

        if (VWorld.IsServer)
        {
            ServerPacketRelay();
        }

        if (VWorld.IsClient)
        {
            ClientPacketRelay();
        }

        PacketRelay.Bootstrap();
        Transference.Bootstrap();

        _initialized = true;
    }
    static void ClientPacketRelay()
    {
        PacketRelay._sendClientPacket = (user, packet) =>
        {
            ChatMessageEvent chatMessageEvent = new()
            {
                MessageText = new FixedString512Bytes(packet),
                MessageType = ChatMessageType.Local,
                ReceiverEntity = VWorld.LocalUser.GetNetworkId()
            };

            Entity networkEntity = EntityManager.CreateEntity(_componentTypes);
            networkEntity.Write(new FromCharacter { Character = VWorld.LocalCharacter, User = VWorld.LocalUser });
            networkEntity.Write(_eventType);
            networkEntity.Write(chatMessageEvent);
        };
    }
    static void ServerPacketRelay()
    {
        PacketRelay._sendServerPacket = (user, packet) =>
        {
            FixedString512Bytes fixedPacket = new(packet);
            ServerChatUtils.SendSystemMessageToClient(EntityManager, user, ref fixedPacket);
        };
    }
}
