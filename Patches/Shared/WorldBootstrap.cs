using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Emberglass.Patches.Shared;
public static class WorldBootstrapPatches
{
    static Harmony _harmony;
    public static void Initialize()
    {
        if (_harmony != null || !HasRegisteredSystems())
        {
            return;
        }

        _harmony = Harmony.CreateAndPatchAll(typeof(WorldBootstrapPatches), MyPluginInfo.PLUGIN_GUID);
    }
    public static void Uninitialize()
    {
        Harmony harmony = _harmony;
        _harmony = null;
        harmony?.UnpatchSelf();
    }

    static readonly List<Type> _clientSystems =
    [
    ];

    static readonly List<Type> _serverSystems =
    [
    ];

    static MethodInfo _getOrCreate;

    [HarmonyPatch(typeof(WorldBootstrapUtilities), nameof(WorldBootstrapUtilities.AddSystemsToWorld))]
    [HarmonyPrefix]
    static void Prefix(World world, WorldBootstrap worldConfig, WorldSystemConfig worldSystemConfig)
    {
        try
        {
            if (world.IsClientWorld())
            {
                var updateGroup = world.GetOrCreateSystemManaged<UpdateGroup>();
                ExecuteRegistration(_clientSystems, type => AddSystem(world, updateGroup, type), updateGroup.SortSystems);
            }

            if (world.IsServerWorld())
            {
                var updateGroup = world.GetOrCreateSystemManaged<UpdateGroup>();
                ExecuteRegistration(_serverSystems, type => AddSystem(world, updateGroup, type), updateGroup.SortSystems);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"[WorldBootstrapUtilities] Failed to inject systems: {e}");
        }
    }
    public static void RegisterClientSystem<T>() where T : ComponentSystemBase
    {
        RegisterClientSystems([typeof(T)]);
    }
    public static void RegisterServerSystem<T>() where T : ComponentSystemBase
    {
        RegisterServerSystems([typeof(T)]);
    }
    public static void RegisterClientSystems(IEnumerable<Type> systemTypes)
    {
        RegisterSystems(_clientSystems, systemTypes);
    }
    public static void RegisterServerSystems(IEnumerable<Type> systemTypes)
    {
        RegisterSystems(_serverSystems, systemTypes);
    }
    static void AddSystem(World world, ComponentSystemGroup systemGroup, Type systemType)
    {
        ValidateIl2CppSystemType(systemType);
        ClassInjector.RegisterTypeInIl2Cpp(systemType);

        var getOrCreate = GetOrCreateSystemManagedMethod().MakeGenericMethod(systemType);
        ComponentSystemBase systemInstance = (ComponentSystemBase)getOrCreate.Invoke(world, null);

        systemGroup.AddSystemToUpdateList(systemInstance);
    }
    static MethodInfo GetOrCreateSystemManagedMethod()
    {
        return _getOrCreate ??= typeof(World)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m =>
                m.Name == nameof(World.GetOrCreateSystemManaged) &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 0
            );
    }
    static void RegisterSystems(List<Type> systems, IEnumerable<Type> systemTypes)
    {
        foreach (Type systemType in systemTypes)
        {
            ValidateIl2CppSystemType(systemType);
            if (!systems.Contains(systemType))
            {
                systems.Add(systemType);
            }
        }
    }

    static void ValidateIl2CppSystemType(Type systemType)
    {
        if (systemType.ContainsGenericParameters)
        {
            throw new InvalidOperationException($"Injected system '{systemType.FullName}' is generic and cannot be registered with IL2CPP.");
        }

        for (Type current = systemType.BaseType; current != null && current != typeof(object); current = current.BaseType)
        {
            if (current.IsGenericType && !current.ContainsGenericParameters)
            {
                throw new InvalidOperationException($"Injected system '{systemType.FullName}' inherits from constructed generic base '{current.FullName}'. Convert the registered system to inherit a non-generic base before IL2CPP registration.");
            }
        }
    }
    static void ExecuteRegistration(IEnumerable<Type> systems, Action<Type> registerSystem, Action sortSystems)
    {
        foreach (Type type in systems)
        {
            registerSystem(type);
        }

        sortSystems();
    }

    internal static class TestHooks
    {
        public static IReadOnlyList<Type> RegisteredClientSystems
            => _clientSystems.ToArray();
        public static IReadOnlyList<Type> RegisteredServerSystems
            => _serverSystems.ToArray();
        public static void ClearRegisteredSystems()
        {
            _clientSystems.Clear();
            _serverSystems.Clear();
        }
        public static void ValidateSystemType(Type systemType)
            => ValidateIl2CppSystemType(systemType);
        public static void ExecuteRegistration(IEnumerable<Type> systems, Action<Type> registerSystem, Action sortSystems)
            => WorldBootstrapPatches.ExecuteRegistration(systems, registerSystem, sortSystems);
    }

    static bool HasRegisteredSystems()
        => _clientSystems.Count > 0 || _serverSystems.Count > 0;

    // Unmanaged Detour
    /*
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    unsafe delegate void OnUpdateHandler(void* systemPtr, void* statePtr);

    static OnUpdateHandler _onUpdateOriginal;     // trampoline returned by detour
    static OnUpdateHandler _onUpdatePatch;        // GC root for reverse P/Invoke
    static INativeDetour _onUpdateDetour;
    static bool _installed;
    static long TypeHash { get; } = 545027831363157002; // CastleRebuildRegistryServerEventSystem (logged manually prior to ease testing proof of concept)
    unsafe static void OnUpdatePatch(void* systemPtr, void* statePtr)
    {
        VWorld.Log.LogWarning("[CastleRebuildRegistryServerEventSystem] OnUpdate");

        try { Before(systemPtr, statePtr); }
        catch (Exception e) { Plugin.Logger.LogError($"Before error: {e}"); }

        try { _onUpdateOriginal!(systemPtr, statePtr); }
        catch (Exception e) { Plugin.Logger.LogError($"OnUpdate error: {e}"); }

        try { After(systemPtr, statePtr); }
        catch (Exception e) { Plugin.Logger.LogError($"After error: {e}"); }
    }
    unsafe static void Before(void* systemPtr, void* statePtr)
    {
        // prefix
    }
    unsafe static void After(void* systemPtr, void* statePtr)
    {
        // postfix
    }

    [HarmonyPatch(typeof(UnmanagedSystemTypeRegistryData), nameof(UnmanagedSystemTypeRegistryData.AddSystemType))]
    [HarmonyPostfix]
    static unsafe void AddSystemTypePostfix(long typeHash, UnmanagedComponentSystemDelegates delegates)
    {
        // Plugin.Logger.LogWarning($"[UnmanagedSystemTypeRegistryData] {typeHash} (Postfix)");

        if (!typeHash.Equals(TypeHash))
            return;

        try
        {
            // const int ON_CREATE = 0, ON_UPDATE = 1, ON_DESTROY = 2, ON_START = 3, ON_STOP = 4, ON_CREATE_FOR_COMPILER = 5;
            const int ON_UPDATE = (int)UnmanagedSystemFunctionType.OnUpdate;
            nint onUpdatePtr = 0;

            // Read function pointers from fixed buffer fields
            ulong* burstBase = &delegates.BurstFunctions.FixedElementField;
            ulong* managedBase = &delegates.ManagedFunctions.FixedElementField;

            nint* burstFns = (nint*)burstBase;
            nint* managedFns = (nint*)managedBase;

            // Checking Burst compiled bit for OnUpdate is set
            bool burstHasOnUpdate = (delegates.BurstFunctionBits & (1 << ON_UPDATE)) != 0;
            onUpdatePtr = burstHasOnUpdate
                ? burstFns[ON_UPDATE]
                : managedFns[ON_UPDATE];

            _onUpdatePatch = OnUpdatePatch; // keep delegate alive (reverse P/Invoke thunk)
            _onUpdateDetour = INativeDetour.CreateAndApply(
                onUpdatePtr,
                _onUpdatePatch,
                out _onUpdateOriginal);

            _installed = true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"[UnmanagedSystemTypeRegistryData] {e}");
        }
    }
    public static void Dispose()
    {
        _onUpdateDetour?.Dispose();
        _onUpdateDetour = null;

        _onUpdateOriginal = null;
        _onUpdatePatch = null;

        _installed = false;
    }
    */
}
