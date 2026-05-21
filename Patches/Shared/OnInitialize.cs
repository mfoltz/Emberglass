using Emberglass.API.Client;
using Emberglass.API.Server;
using Emberglass.API.Shared;
using Emberglass.CustomPrefabs;
using Emberglass.Network;
using Emberglass.Patches.Client;
using Emberglass.Patches.Server;
using Emberglass.Systems;
using HarmonyLib;
using ProjectM;

namespace Emberglass.Patches.Shared;
internal static class GameBootstrapPatch
{
    static Harmony _harmony;
    static bool _initialized;
    static bool _unityBehaviourInitialized;
    public static void Initialize()
    {
        try
        {
            if (VWorld.IsServer)
            {
                CustomPrefabBuiltInProofDefinitions.Register();
                VWorld.Log.LogInfo("[GameBootstrapPatch] Registering server observer systems.");
                ObserverSystemRegistry.RegisterAll();
                VWorld.Log.LogInfo("[GameBootstrapPatch] Server observer registration returned.");
            }

            VWorld.Log.LogInfo("[GameBootstrapPatch] Initializing world bootstrap patches.");
            WorldBootstrapPatches.Initialize();
            VWorld.Log.LogInfo("[GameBootstrapPatch] World bootstrap patch initialization returned.");

            if (VWorld.IsServer)
            {
                VWorld.Log.LogInfo("[GameBootstrapPatch] Server-side runtime module initialization deferred.");
            }

            if (VWorld.IsClient)
            {
                VWorld.Log.LogInfo("[GameBootstrapPatch] Initializing client-side modules.");
                ClientChatSystemPatch.Initialize();
                InputActionSystemPatch.Initialize();
                OptionsMenuPatches.Initialize();
                OptionsManager.AddButton(
                    "emberglass.request_shared_mods",
                    "Request Shared Mods",
                    "Request server-shared mods for this client (requires your consent).",
                    MyPluginInfo.PLUGIN_NAME,
                    () =>
                    {
                        if (!VWorld.IsClient)
                        {
                            VWorld.Log.LogWarning("Shared mod requests can only be initiated by clients.");
                            return;
                        }

                        Transference.RequestSharedModsFromMenu();
                    });
                VWorld.Log.LogInfo("[GameBootstrapPatch] Client-side module initialization returned.");
            }
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"[GameBootstrapPatch] {ex}");
        }

        _harmony = Harmony.CreateAndPatchAll(typeof(GameBootstrapPatch), MyPluginInfo.PLUGIN_GUID);
    }
    public static void Uninitialize()
    {
        _harmony?.UnpatchSelf();
        _initialized = false;
        _unityBehaviourInitialized = false;

        WorldBootstrapPatches.Uninitialize();
        VBehaviour.Uninitialize();

        if (VWorld.IsServer)
        {
            ChatMessageSystemPatch.Uninitialize();
            VShare.Uninitialize();
        }

        if (VWorld.IsClient)
        {
            ClientChatSystemPatch.Uninitialize();
            InputActionSystemPatch.Uninitialize();
            OptionsMenuPatches.Uninitialize();
        }

        RequestResponse.Uninitialize();
        CustomPrefabProofBuffApplier.Uninitialize();
        CustomPrefabMirrorCoordinator.Uninitialize();

        if (VWorld.IsServer)
        {
            PlayerPresenceValidationProbe.Uninitialize();
        }

        VEvents.ModuleRegistry.Uninitialize();
    }

    [HarmonyPatch(typeof(GameBootstrap), nameof(GameBootstrap.Update))]
    [HarmonyPostfix]
    static void UpdatePostfix()
    {
        if (_initialized)
        {
            return;
        }

        EnsureUnityBehaviourInitialized();
        VEvents.Initialize();
        API.Shared.VNetwork.Initialize();
        CustomPrefabMirrorCoordinator.Initialize();

        if (VWorld.IsServer)
        {
            VWorld.Log.LogInfo("[GameBootstrapPatch] Initializing server-side runtime modules.");
            VWorld.Log.LogInfo("[GameBootstrapPatch] Initializing server chat message patch.");
            ChatMessageSystemPatch.Initialize();
            VWorld.Log.LogInfo("[GameBootstrapPatch] Server chat message patch initialization returned.");
            VWorld.Log.LogInfo("[GameBootstrapPatch] Initializing VShare.");
            VShare.Initialize();
            VWorld.Log.LogInfo("[GameBootstrapPatch] VShare initialization returned.");
            VWorld.Log.LogInfo("[GameBootstrapPatch] Server-side runtime module initialization returned.");
            Players.Initialize();
            PlayerPresenceValidationProbe.Initialize();
            CustomPrefabProofBuffApplier.Initialize();
        }

        if (VWorld.IsClient)
        {
            KeybindManager.TryLoadKeybinds();
            OptionsManager.TryLoadOptions();
        }

        _initialized = true;
    }

    static void EnsureUnityBehaviourInitialized()
    {
        if (_unityBehaviourInitialized)
        {
            return;
        }

        VBehaviour.Initialize();
        VBehaviour.MainThreadInvoker = new MainThreadInvoker();
        _unityBehaviourInitialized = true;
    }
}
