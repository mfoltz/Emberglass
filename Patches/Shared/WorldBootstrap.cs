using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ProjectM;
using Stunlock.Core;
using System.Reflection;
using Unity.Entities;

namespace Emberglass.Patches.Shared;
public static class WorldBootstrapPatches
{
    static Harmony _harmony;
    public static void Initialize()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(WorldBootstrapPatches), MyPluginInfo.PLUGIN_GUID);
    }
    public static void Uninitialize()
    {
        _harmony?.UnpatchSelf();
    }

    static readonly HashSet<Type> _systems =
    [
        // typeof(VSystem),
    ];

    static readonly MethodInfo _getOrCreate = typeof(World)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .First(m =>
            m.Name == nameof(World.GetOrCreateSystemManaged) &&
            m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 0
        );

    [HarmonyPatch(typeof(WorldBootstrapUtilities), nameof(WorldBootstrapUtilities.AddSystemsToWorld))]
    [HarmonyPrefix]
    static void Prefix(World world, WorldBootstrap worldConfig, WorldSystemConfig worldSystemConfig)
    {
        try
        {
            if (world.IsClientWorld() || world.IsServerWorld())
            {
                var updateGroup = world.GetOrCreateSystemManaged<UpdateGroup>();

                foreach (Type type in _systems)
                {
                    AddSystem(world, updateGroup, type);
                }

                updateGroup.SortSystems();
            }

            /*
            if (world.IsServerWorld())
            {
                try
                {
                    var updateGroup = world.GetOrCreateSystemManaged<UpdateGroup>();
                    var prefabInitGroup = world.GetOrCreateSystemManaged<PrefabInitializationGroup>();

                    var generateCastleSystem = world.GetOrCreateSystemManaged<GenerateCastleSystem>();
                    var generateCastlePrefabsCollectionSystem = world.GetOrCreateSystemManaged<GenerateCastlePrefabsCollectionSystem>();
                    var prefabCollectionSystem = world.GetOrCreateSystemManaged<PrefabCollectionSystem>();

                    updateGroup.AddSystemToUpdateList(generateCastleSystem);
                    prefabInitGroup.AddSystemToUpdateList(generateCastlePrefabsCollectionSystem);

                    generateCastleSystem._GenerateCastleCollection = world.GetOrCreateSystemManaged<GenerateCastlePrefabsCollectionSystem>();
                    prefabInitGroup.AddSystemToUpdateList(generateCastleSystem._GenerateCastleCollection);

                    generateCastleSystem.Enabled = true;
                    generateCastlePrefabsCollectionSystem.Enabled = true;

                    updateGroup.SortSystems();
                    prefabInitGroup.SortSystems();
                    Plugin.Logger.LogWarning("GenerateCastle systems created and enabled successfully!");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"Error creating GenerateCastle systems: {ex}");
                }
            }
            */
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"[WorldBootstrapUtilities] Failed to inject systems: {e}");
        }
    }
    static void RegisterSystem<T>(Type systemType) where T : ComponentSystemBase
    {
        if (!_systems.Contains(systemType))
        {
            _systems.Add(systemType);
        }
    }
    static void AddSystem(World world, ComponentSystemGroup systemGroup, Type systemType)
    {
        ClassInjector.RegisterTypeInIl2Cpp(systemType);

        var getOrCreate = _getOrCreate.MakeGenericMethod(systemType);
        var systemInstance = (ComponentSystemBase)getOrCreate.Invoke(world, null);

        systemGroup.AddSystemToUpdateList(systemInstance);
    }
}