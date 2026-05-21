using Emberglass.API.Client;
using Emberglass.API.Shared;
using Emberglass.Utilities;
using HarmonyLib;
using ProjectM;
using ProjectM.UI;
using Stunlock.Localization;
using static Emberglass.API.Client.LocalizationKeyManager;
using static Emberglass.API.Client.OptionsManager;

namespace Emberglass.Patches.Client;
internal static class OptionsMenuPatches
{
    static Harmony _harmony;
    public static void Initialize()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(OptionsMenuPatches), MyPluginInfo.PLUGIN_GUID);
    }
    public static void Uninitialize()
    {
        try
        {
            Persistence.SaveKeybinds();
            Persistence.SaveOptions();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"{ex}");
        }

        _harmony?.UnpatchSelf();
    }


    static OptionsMenu _optionsMenu;
    internal static SettingsEntry_Button ButtonPrefab
        => _optionsMenu?.ControlsPanel?.ButtonPrefab ?? _optionsMenu?.GraphicsPanel?.ButtonPrefab;

    [HarmonyPatch(typeof(OptionsMenu_Base), nameof(OptionsMenu_Base.OnDestroy))]
    [HarmonyPostfix]
    static void OnDestroyPostfix()
    {
        Persistence.SaveOptions();
    }

    [HarmonyPatch(typeof(OptionsMenu), nameof(OptionsMenu.Update))]
    [HarmonyPostfix]
    static void UpdatePostfix(OptionsMenu __instance)
    {
        _optionsMenu = __instance;
    }

    [HarmonyPatch(typeof(OptionsMenu), nameof(OptionsMenu.Start))]
    [HarmonyPostfix]
    static void StartPostfix(OptionsMenu __instance)
    {
        _optionsMenu = __instance;
    }

    internal static SettingsEntry_Button ResolveButtonPrefab(OptionsPanel_Interface panel)
    {
        _optionsMenu ??= panel.GetComponentInParent<OptionsMenu>();
        return ButtonPrefab;
    }

    [HarmonyPatch(typeof(OptionsPanel_Interface), nameof(OptionsPanel_Interface.Start))]
    [HarmonyPostfix]
    static void StartPostfix(OptionsPanel_Interface __instance)
    {
        try
        {
            LocalizeText();
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"[OptionsPanel_Interface.Start] Failed to localize keys - {ex.Message}");
        }

        foreach (var menuOptions in CategoryEntries)
        {
            __instance.AddHeader(menuOptions.Key);

            foreach (var entry in menuOptions.Value.OrderBy(entry => entry.Order))
            {
                try
                {
                    entry.BuildUI(__instance);
                }
                catch (Exception ex)
                {
                    string entryIdentifier = $"{menuOptions.Key}:{entry.Order}:{entry.GetType().Name}";
                    VWorld.Log.LogError($"[OptionsPanel_Interface.Start] Failed to create option entry '{entryIdentifier}' - {ex.Message}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(RebindingMenu), nameof(RebindingMenu.Start))]
    [HarmonyPostfix]
    static void StartPostfix(RebindingMenu __instance)
    {
        if (__instance._BindingTypeToDisplay != ControllerType.KeyboardAndMouse)
        {
            return;
        }

        try
        {
            LocalizeText();
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"[OptionsPanel_Interface.Start] Failed to localize keys - {ex.Message}");
        }

        foreach (var keybindCategory in KeybindManager.Categories)
        {
            LocalizationKey headerKey = keybindCategory.Key;
            __instance.AddHeader(headerKey);

            foreach (var entry in keybindCategory.Value.OrderBy(entry => entry.Order))
            {
                try
                {
                    entry.BuildUI(__instance);
                }
                catch (Exception ex)
                {
                    string entryIdentifier = $"{headerKey}:{entry.Order}:{entry.GetType().Name}";
                    VWorld.Log.LogError($"[RebindingMenu.Start] Failed to create keybind entry '{entryIdentifier}' - {ex.Message}");
                }
            }
        }
    }
}
