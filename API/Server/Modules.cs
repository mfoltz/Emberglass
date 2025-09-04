using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Stunlock.Network;
using Unity.Entities;
using static Emberglass.API.Shared.VEvents;
using static Emberglass.API.Shared.VExtensions;
using static Emberglass.Services.PlayerService;
using Emberglass.API.Shared;
using Emberglass.Services;

namespace Emberglass.API.Server;
public static class ServerModules
{
    public static IReadOnlyCollection<Type> Modules => _modules;
    static readonly Type[] _modules =
    [
        typeof(ConnectionModules.UserConnectedModule),
        typeof(ConnectionModules.UserDisconnectedModule),
        typeof(ConnectionModules.UserCreatedModule),
        typeof(ConnectionModules.UserKickedModule)
    ];
    internal static bool Initialize()
    {
        try
        {
            foreach (Type module in Modules)
            {
                Activator.CreateInstance(module);
            }

            return true;
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"Failed to initialize server event modules: {ex}");
            return false;
        }
    }
    public static class ConnectionModules
    {
        public class UserConnected : IGameEvent
        {
            public PlayerInfo PlayerInfo { get; set; }
        }
        public class UserDisconnected : IGameEvent
        {
            public PlayerInfo PlayerInfo { get; set; }
        }
        public class UserCreated : IGameEvent
        {
            public PlayerInfo PlayerInfo { get; set; }
        }
        public class UserKicked : IGameEvent
        {
            public PlayerInfo PlayerInfo { get; set; }
        }
        public class UserConnectedModule : GameEvent<UserConnected>
        {
            static UserConnectedModule _instance;
            static Harmony _harmony;
            public override void Initialize()
            {
                _harmony = Harmony.CreateAndPatchAll(typeof(Patch), MyPluginInfo.PLUGIN_GUID);
            }
            public override void Uninitialize()
            {
                _harmony?.UnpatchSelf();
                _instance = null;
            }
            public UserConnectedModule()
            {
                _instance = this;
                ModuleRegistry.Register(_instance);
            }
            static class Patch
            {
                [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserConnected))]
                [HarmonyPostfix]
                static void OnUserConnected(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
                {
                    if (!__instance._NetEndPointToApprovedUserIndex.TryGetValue(netConnectionId, out var userIndex))
                    {
                        return;
                    }

                    var client = __instance._ApprovedUsersLookup[userIndex];

                    if (!client.HasPlayerInfo(out var playerInfo))
                    {
                        return;
                    }

                    _instance?.Raise(new UserConnected { PlayerInfo = playerInfo });
                }
            }
        }
        public class UserDisconnectedModule : GameEvent<UserDisconnected>
        {
            static UserDisconnectedModule _instance;
            static Harmony _harmony;
            public override void Initialize()
            {
                _harmony = Harmony.CreateAndPatchAll(typeof(Patch), MyPluginInfo.PLUGIN_GUID);
            }
            public override void Uninitialize()
            {
                _harmony?.UnpatchSelf();
                _instance = null;
            }
            public UserDisconnectedModule()
            {
                _instance = this;
                ModuleRegistry.Register(_instance);
            }
            static class Patch
            {
                [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserDisconnected))]
                [HarmonyPrefix]
                static void OnUserDisconnected(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
                {
                    if (!__instance._NetEndPointToApprovedUserIndex.TryGetValue(netConnectionId, out var userIndex))
                    {
                        return;
                    }

                    var client = __instance._ApprovedUsersLookup[userIndex];

                    if (!client.HasPlayerInfo(out var playerInfo))
                    {
                        return;
                    }

                    _instance?.Raise(new UserDisconnected { PlayerInfo = playerInfo });
                }
            }
        }
        public class UserCreatedModule : GameEvent<UserCreated>
        {
            static UserCreatedModule _instance;
            static Harmony _harmony;
            public override void Initialize()
            {
                _harmony = Harmony.CreateAndPatchAll(typeof(Patch), MyPluginInfo.PLUGIN_GUID);
            }
            public override void Uninitialize()
            {
                _harmony?.UnpatchSelf();
                _instance = null;
            }
            public UserCreatedModule()
            {
                _instance = this;
                ModuleRegistry.Register(_instance);
            }
            static class Patch
            {
                [HarmonyPatch(typeof(HandleCreateCharacterEventSystem), nameof(HandleCreateCharacterEventSystem.CreateFadeToBlackEntity))]
                [HarmonyPostfix]
                static void OnCharacterCreated(EntityManager entityManager, FromCharacter fromCharacter)
                {
                    Entity userEntity = fromCharacter.User;
                    User user = userEntity.GetUser();

                    PlayerInfo playerInfo = CreatePlayerInfo(userEntity, user);
                    _instance?.Raise(new UserCreated { PlayerInfo = playerInfo });
                }
            }
        }
        public class UserKickedModule : GameEvent<UserKicked>
        {
            static UserKickedModule _instance;
            static Harmony _harmony;
            public override void Initialize()
            {
                _harmony = Harmony.CreateAndPatchAll(typeof(Patch), MyPluginInfo.PLUGIN_GUID);
            }
            public override void Uninitialize()
            {
                _harmony?.UnpatchSelf();
                _instance = null;
            }
            public UserKickedModule()
            {
                _instance = this;
                ModuleRegistry.Register(_instance);
            }
            static class Patch
            {
                [HarmonyPatch(typeof(KickBanSystem_Server), nameof(KickBanSystem_Server.OnUpdate))]
                [HarmonyPrefix]
                static void OnUpdatePrefix(KickBanSystem_Server __instance)
                {
                    using NativeAccessor<KickEvent> kickEvents = __instance._KickQuery.ToComponentDataArrayAccessor<KickEvent>();

                    try
                    {
                        for (int i = 0; i < kickEvents.Length; i++)
                        {
                            KickEvent kickEvent = kickEvents[i];
                            ulong steamId = kickEvent.PlatformId;

                            if (!steamId.TryGetPlayerInfo(out PlayerInfo playerInfo))
                            {
                                continue;
                            }

                            _instance?.Raise(new UserKicked { PlayerInfo = playerInfo });
                        }
                    }
                    catch (Exception ex)
                    {
                        VWorld.Log.LogError($"[KickBanSystem_Server] Exception in OnUpdatePrefix: {ex}");
                    }
                }
            }
        }
    }
}
