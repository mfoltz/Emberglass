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
    internal static void Initialize()
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
            VWorld.Log.LogError($"Failed to initialize client event modules: {ex}");
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
            static PrefabGUID TombCoffinSpawn { get; } = new(722466953); // AB_Interact_TombCoffinSpawn_Travel; one-off is okay but if we need more PrefabGUIDs elsewhere should embed as file w/ fields instead
            public override void Initialize()
            {
                _harmony = Harmony.CreateAndPatchAll(typeof(Patch), MyPluginInfo.PLUGIN_GUID);
            }
            public override void Uninitialize()
            {
                _harmony?.UnpatchSelf();
                _ready = false;
                _instance = null;
            }
            public ClientHandshakeModule()
            {
                _instance = this;
                ModuleRegistry.Register(_instance);
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
                                && !_ready && _instance != null)
                            {
                                _instance.Raise(new());
                                _ready = true;
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
