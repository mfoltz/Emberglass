using Emberglass.API.Client;
using HarmonyLib;
using ProjectM;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberglass.Patches.Client;
internal static class InputActionSystemPatch
{
    static Harmony _harmony;
    public static void Initialize()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(InputActionSystemPatch), MyPluginInfo.PLUGIN_GUID);
    }

    public static void Uninitialize()
    {
        _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(InputActionSystem), nameof(InputActionSystem.OnCreate))]
    [HarmonyPostfix]
    static void OnCreatePostfix(InputActionSystem __instance)
    {
        __instance._LoadedInputActions.Disable();

        InputActionMap inputActionMap = new(MyPluginInfo.PLUGIN_NAME);
        __instance._LoadedInputActions.m_ActionMaps.AddItem(inputActionMap);

        __instance._LoadedInputActions.Enable();
    }

    [HarmonyPatch(typeof(InputActionSystem), nameof(InputActionSystem.OnUpdate))]
    [HarmonyPrefix]
    static void OnUpdatePrefix()
    {
        foreach (var kvp in KeybindManager.Categories.Values)
        {
            foreach (Keybinding keybind in kvp.Values)
            {
                if (IsKeybindDown(keybind))
                {
                    keybind.KeyDown();
                }

                if (IsKeybindUp(keybind))
                {
                    keybind.KeyUp();
                }

                if (IsKeybindPressed(keybind))
                {
                    keybind.KeyPressed();
                }
            }
        }
    }
    static bool IsKeybindDown(Keybinding keybind)
    {
        return Input.GetKeyDown(keybind.Primary);
    }
    static bool IsKeybindUp(Keybinding keybind)
    {
        return Input.GetKeyUp(keybind.Primary);
    }
    static bool IsKeybindPressed(Keybinding keybind)
    {
        return Input.GetKey(keybind.Primary);
    }
}
