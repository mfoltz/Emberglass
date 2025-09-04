using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace Emberglass.API.Shared;
public class VSystem : SystemBase
{
    internal static VSystem Instance { get; set; }
    static bool IsServer => VWorld.IsServer;
    static bool IsClient => VWorld.IsClient;

    NetworkIdSystem.Singleton _networkIdSystem;

    EntityQuery _clientEventQuery;
    EntityQuery _serverEventQuery;

    public override void OnCreate()
    {
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<NetworkId>()));

        _clientEventQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<ChatMessageServerEvent>(),
                ComponentType.ReadOnly<FromCharacter>()
            }
        });

        _serverEventQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<ChatMessageEvent>(),
                ComponentType.ReadOnly<FromCharacter>(),
                ComponentType.ReadOnly<ReceiveNetworkEventTag>()
            }
        });

        _networkIdSystem = GetSingleton<NetworkIdSystem.Singleton>();

        Instance = this;
    }
    public override void OnDestroy()
    {
        Instance = null;
    }
    public override void OnUpdate()
    {
        if (IsServer)
        {
            OnClientEvent();
        }

        if (IsClient)
        {
            OnServerEvent();
        }
    }
    void OnClientEvent()
    {
        using var entities = _clientEventQuery.ToEntityArrayAccessor();

        foreach (var entity in entities)
        {
            try
            {
                var chat = EntityManager.GetComponentData<ChatMessageServerEvent>(entity);
                var sender = EntityManager.GetComponentData<FromCharacter>(entity);

                entity.Receive();
            }
            catch (Exception ex)
            {
                VWorld.Log.LogError($"OnClientEvent: {ex}");
            }
        }
    }
    void OnServerEvent()
    {
        using var entities = _serverEventQuery.ToEntityArrayAccessor();

        foreach (var entity in entities)
        {
            var chat = EntityManager.GetComponentData<ChatMessageEvent>(entity);
            var sender = EntityManager.GetComponentData<FromCharacter>(entity);

            entity.Receive();
        }
    }

    static readonly NetworkEventType _clientEventType = new()
    {
        EventId = NetworkEvents.EventId_ChatMessageServerEvent,
        IsAdminEvent = false,
        IsDebugEvent = false
    };

    static readonly ComponentType[] _clientEvent =
    [
        ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
        ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
        ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
        ComponentType.ReadOnly(Il2CppType.Of<ChatMessageServerEvent>())
    ];

    static readonly NetworkEventType _serverEventType = new()
    {
        EventId = NetworkEvents.EventId_ChatMessageEvent,
        IsAdminEvent = false,
        IsDebugEvent = false
    };

    static readonly ComponentType[] _serverEvent =
    [
        ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
        ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
        ComponentType.ReadOnly(Il2CppType.Of<ReceiveNetworkEventTag>()),
        ComponentType.ReadOnly(Il2CppType.Of<ChatMessageEvent>())
    ];

    static void SendClientEvent(
        Entity user,
        Entity character,
        FixedString512Bytes message)
    {
        Entity entity = _clientEvent.Create();

        FromCharacter fromCharacter = new()
        {
            User = user,
            Character = character
        };

        entity.Write(fromCharacter);
        entity.Write(_clientEventType);
        entity.Write(new ChatMessageServerEvent
        {
            MessageText = message,
            MessageType = ServerChatMessageType.System,
            FromCharacter = fromCharacter.Character.GetNetworkId(),
            FromUser = fromCharacter.User.GetNetworkId(),
            TimeUTC = DateTime.UtcNow.Ticks
        });
    }
    static void SendServerEvent(
        Entity user,
        Entity character,
        FixedString512Bytes message)
    {
        Entity entity = _serverEvent.Create();

        FromCharacter fromCharacter = new()
        {
            User = user,
            Character = character
        };

        entity.Write(fromCharacter);
        entity.Write(_serverEventType);
        entity.Write(new ChatMessageEvent
        {
            MessageText = message,
            MessageType = ChatMessageType.System,
            ReceiverEntity = user.GetNetworkId()
        });
    }
}
