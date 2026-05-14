using Emberglass.API.Client;
using Emberglass.API.Server;
using Emberglass.API.Shared;
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
    public static void Initialize()
    {
        try
        {
            WorldBootstrapPatches.Initialize();
            VBehaviour.Initialize();
            VBehaviour.MainThreadInvoker = new MainThreadInvoker();

            if (VWorld.IsServer)
            {
                ObserverSystemRegistry.RegisterAll();
                ChatMessageSystemPatch.Initialize();
                VShare.Initialize();
            }

            if (VWorld.IsClient)
            {
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

        VEvents.Initialize();
        API.Shared.VNetwork.Initialize();

        if (VWorld.IsServer)
        {
            Players.Initialize();
            PlayerPresenceValidationProbe.Initialize();
        }

        if (VWorld.IsClient)
        {
            KeybindManager.TryLoadKeybinds();
            OptionsManager.TryLoadOptions();
        }

        _initialized = true;
    }
}
