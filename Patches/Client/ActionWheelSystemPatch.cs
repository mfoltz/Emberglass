using HarmonyLib;
using ProjectM.UI;

namespace Emberglass.Patches.Client;

/*
[HarmonyPatch]
internal static class ActionWheelSystemPatch
{
    public static bool _wheelVisible;

    static DateTime _wheelOpened = DateTime.MinValue;
    static DateTime _lastQuipSendTime = DateTime.MinValue;
    const float QUIP_COOLDOWN_SECONDS = 0.5f;

    [HarmonyPatch(typeof(ActionWheelSystem), nameof(ActionWheelSystem.SendQuipChatMessage))]
    [HarmonyPrefix]
    static bool SendQuipChatMessagePrefix(byte index)
    {
        DateTime now = DateTime.UtcNow;

        if (_wheelOpened.Equals(DateTime.MinValue))
        {
            _wheelOpened = now;
        }

        if ((now - _wheelOpened).TotalSeconds < 0.1f)
            return false;

        if ((now - _lastQuipSendTime).TotalSeconds < QUIP_COOLDOWN_SECONDS)
            return false;

        _lastQuipSendTime = now;

        if (CommandQuips.TryGetValue(index, out CommandQuip commandQuip))
        {
            SendCommandQuip(commandQuip);
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(ActionWheelSystem), nameof(ActionWheelSystem.HideCurrentWheel))]
    [HarmonyPrefix]
    static bool HideCurrentWheelPrefix(ActionWheelSystem __instance)
    {
        if (SocialWheelActive)
        {
            return false;
        }
        else if (!_wheelOpened.Equals(DateTime.MinValue))
        {
            _wheelOpened = DateTime.MinValue;
        }

        return true;
    }
}
*/
