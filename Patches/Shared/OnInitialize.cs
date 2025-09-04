using Emberglass.API.Client;
using Emberglass.API.Shared;
using Emberglass.Patches.Client;
using Emberglass.Patches.Server;
using Emberglass.Services;
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

            if (VWorld.IsServer)
            {
                ChatMessageSystemPatch.Initialize();
                VShare.Initialize();
            }

            if (VWorld.IsClient)
            {
                ClientChatSystemPatch.Initialize();
                InputActionSystemPatch.Initialize();
                OptionsMenuPatches.Initialize();
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
        VNetwork.Initialize();

        if (VWorld.IsServer)
        {
            PlayerService.Initialize();
        }

        if (VWorld.IsClient)
        {
            KeybindManager.TryLoadKeybinds();
            OptionsManager.TryLoadOptions();
        }

        _initialized = true;
    }
}