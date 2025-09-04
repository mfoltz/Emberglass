using Emberglass.API.Shared;
using Emberglass.Network;
using Emberglass.Patches.Shared;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using static Emberglass.API.Server.ServerModules.ConnectionModules;
using static Emberglass.API.Shared.VEvents;

namespace Emberglass.Services;
public static class PlayerService
{
    static EntityManager EntityManager => VWorld.EntityManager;
    public static IReadOnlyDictionary<ulong, PlayerInfo> SteamIdPlayerInfoCache => _steamIdPlayerInfoCache;
    static readonly Dictionary<ulong, PlayerInfo> _steamIdPlayerInfoCache = [];
    public static IReadOnlyDictionary<ulong, PlayerInfo> SteamIdOnlinePlayerInfoCache => _steamIdOnlinePlayerInfoCache;
    static readonly Dictionary<ulong, PlayerInfo> _steamIdOnlinePlayerInfoCache = [];
    public static IReadOnlyDictionary<string, PlayerInfo> CharacterNamePlayerInfoCache => _characterNamePlayerInfoCache;
    static readonly Dictionary<string, PlayerInfo> _characterNamePlayerInfoCache = [];
    public static IReadOnlyDictionary<string, PlayerInfo> CharacterNameOnlinePlayerInfoCache => _characterNameOnlinePlayerInfoCache;
    static readonly Dictionary<string, PlayerInfo> _characterNameOnlinePlayerInfoCache = [];

    static bool _initialized = false;
    public struct PlayerInfo(Entity userEntity = default, Entity characterEntity = default)
    {
        public readonly ulong SteamId => User.PlatformId;
        public readonly string Name => User.CharacterName.Value;
        public readonly bool IsAdmin => User.IsAdmin;
        public readonly bool IsConnected => User.IsConnected;
        public readonly User User => UserEntity.GetUser();
        public Entity UserEntity { get; set; } = userEntity;
        public Entity CharacterEntity { get; set; } = characterEntity;
    }
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        ComponentType[] userAllComponents =
        [
            ComponentType.ReadOnly(Il2CppType.Of<User>())
        ];

        EntityQuery userQuery = EntityManager.BuildQuery(
            allTypes: userAllComponents,
            options: EntityQueryOptions.IncludeDisabled
        );

        BuildPlayerInfoCache(userQuery);
        Register();

        _initialized = true;
    }
    public static void Register()
    {
        ModuleRegistry.Subscribe<UserConnected>(OnConnect);
        ModuleRegistry.Subscribe<UserDisconnected>(OnDisconnect);
        ModuleRegistry.Subscribe<UserCreated>(OnCreate);
        ModuleRegistry.Subscribe<UserKicked>(OnKick);
    }
    static void BuildPlayerInfoCache(EntityQuery userQuery)
    {
        NativeArray<Entity> userEntities = userQuery.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (Entity userEntity in userEntities)
            {
                if (!userEntity.Exists())
                {
                    continue;
                }

                User user = userEntity.GetUser();

                PlayerInfo playerInfo = CreatePlayerInfo(userEntity, user);
                AddPlayerInfo(playerInfo);

                if (user.IsConnected)
                {
                    AddOnlinePlayerInfo(playerInfo);
                }
            }
        }
        catch (Exception ex)
        {
            VWorld.Log.LogWarning($"[PlayerService] BuildPlayerInfoCache() - {ex}");
        }
        finally
        {
            userEntities.Dispose();
        }
    }
    internal static PlayerInfo CreatePlayerInfo(Entity userEntity, User user)
    {
        Entity characterEntity = user.LocalCharacter.GetEntityOnServer();
        return new(userEntity, characterEntity);
    }
    internal static bool HasPlayerInfo(this ServerBootstrapSystem.ServerClient serverClient, out PlayerInfo playerInfo)
    {
        Entity userEntity = serverClient.UserEntity;
        User user = userEntity.GetUser();

        return SteamIdPlayerInfoCache.TryGetValue(user.PlatformId, out playerInfo);
    }
    static void AddPlayerInfo(PlayerInfo playerInfo)
    {
        _steamIdPlayerInfoCache[playerInfo.SteamId] = playerInfo;
        _characterNamePlayerInfoCache[playerInfo.Name] = playerInfo;
    }
    static void AddOnlinePlayerInfo(PlayerInfo playerInfo)
    {
        _steamIdOnlinePlayerInfoCache[playerInfo.SteamId] = playerInfo;
        _characterNameOnlinePlayerInfoCache[playerInfo.Name] = playerInfo;
    }
    static void RemoveOnlinePlayerInfo(PlayerInfo playerInfo)
    {
        _steamIdOnlinePlayerInfoCache.Remove(playerInfo.SteamId);
        _characterNameOnlinePlayerInfoCache.Remove(playerInfo.Name);
    }
    static void OnConnect(UserConnected userConnected)
    {
        VWorld.Log.LogInfo($"[PlayerService] OnConnect: {userConnected.PlayerInfo.Name} ({userConnected.PlayerInfo.SteamId})");
        AddPlayerInfo(userConnected.PlayerInfo);
        AddOnlinePlayerInfo(userConnected.PlayerInfo);
    }
    static void OnCreate(UserCreated characterCreated)
    {
        VWorld.Log.LogInfo($"[PlayerService] OnCreate: {characterCreated.PlayerInfo.Name} ({characterCreated.PlayerInfo.SteamId})");
        AddPlayerInfo(characterCreated.PlayerInfo);
        AddOnlinePlayerInfo(characterCreated.PlayerInfo);
    }
    static void OnDisconnect(UserDisconnected userDisconnected)
    {
        VWorld.Log.LogInfo($"[PlayerService] OnDisconnect: {userDisconnected.PlayerInfo.Name} ({userDisconnected.PlayerInfo.SteamId})");
        PlayerInfo playerInfo = userDisconnected.PlayerInfo;
        RemoveOnlinePlayerInfo(playerInfo);
        PacketRelay.TryRemoveKey(playerInfo.SteamId);
    }
    static void OnKick(UserKicked userKicked)
    {
        VWorld.Log.LogInfo($"[PlayerService] OnKick: {userKicked.PlayerInfo.Name} ({userKicked.PlayerInfo.SteamId})");
        PlayerInfo playerInfo = userKicked.PlayerInfo;
        RemoveOnlinePlayerInfo(userKicked.PlayerInfo);
        PacketRelay.TryRemoveKey(playerInfo.SteamId);
    }
    public static bool TryGetPlayerInfo(this ulong steamId, out PlayerInfo playerInfo)
    {
        return SteamIdPlayerInfoCache.TryGetValue(steamId, out playerInfo);
    }
    public static bool TryGetPlayerInfo(this string characterName, out PlayerInfo playerInfo)
    {
        return CharacterNamePlayerInfoCache.TryGetValue(characterName, out playerInfo);
    }
}
