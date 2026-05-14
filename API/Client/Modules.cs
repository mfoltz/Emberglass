using Emberglass.API.Shared;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using static Emberglass.API.Shared.VEvents;

namespace Emberglass.API.Client;
public static class ClientModules
{
    public static IReadOnlyCollection<Type> Modules => _modules;
    static readonly Type[] _modules =
    [
        typeof(ConnectionModules.ClientHandshakeModule)
    ];
    internal static void Bootstrap()
    {
        try
        {
            foreach (Type module in Modules)
            {
                Activator.CreateInstance(module);
            }
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"Failed to bootstrap client event modules: {ex}");
        }
    }
    public static class ConnectionModules
    {
        public readonly struct ClientHandshake : IGameEvent;
        public class ClientHandshakeModule : GameEvent<ClientHandshake>
        {
            static ClientHandshakeModule _instance;
            static Harmony _harmony;
            static bool _ready;
            static Entity _readyLocalUser;
            static PrefabGUID TombCoffinSpawn { get; } = new(722466953);
            public override void Initialize()
            {
                _harmony = Harmony.CreateAndPatchAll(typeof(Patch), MyPluginInfo.PLUGIN_GUID);
            }
            public override void Uninitialize()
            {
                _harmony?.UnpatchSelf();
                ResetSessionReady();
                _instance = null;
            }
            public ClientHandshakeModule()
            {
                _instance = this;
                ModuleRegistry.Register(_instance);
            }
            /// <summary>
            /// Marks the current client session as ready to send its handshake.
            /// </summary>
            /// <param name="localUser">The local user entity for the current client session.</param>
            /// <returns>True when this session should raise a new handshake event.</returns>
            internal static bool TryMarkSessionReady(Entity localUser)
            {
                if (localUser == Entity.Null)
                {
                    return false;
                }

                if (_ready && _readyLocalUser != localUser)
                {
                    _ready = false;
                }

                if (_ready)
                {
                    return false;
                }

                _ready = true;
                _readyLocalUser = localUser;
                return true;
            }
            /// <summary>
            /// Resets the cached client session marker.
            /// </summary>
            internal static void ResetSessionReady()
            {
                _ready = false;
                _readyLocalUser = Entity.Null;
            }
            static class Patch
            {
                [HarmonyPatch(typeof(Destroy_TravelBuffSystem), nameof(Destroy_TravelBuffSystem.OnUpdate))]
                [HarmonyPostfix]
                static void HandleInputPostfix(Destroy_TravelBuffSystem __instance)
                {
                    using NativeAccessor<Entity> entities = __instance.__query_615927226_0.ToEntityArrayAccessor();

                    try
                    {
                        for (int i = 0; i < entities.Length; i++)
                        {
                            Entity entity = entities[i];
                            PrefabGUID prefabGuid = entity.GetPrefabGuid();

                            if (prefabGuid.Equals(TombCoffinSpawn)
                                && _instance != null
                                && TryMarkSessionReady(VWorld.LocalUser))
                            {
                                _instance.Raise(new());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        VWorld.Log.LogError($"Error in ClientHandshakeModule: {ex}");
                    }
                }
            }
        }
    }
}
