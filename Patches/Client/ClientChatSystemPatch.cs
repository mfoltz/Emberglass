using Emberglass.API.Shared;
using HarmonyLib;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Entities;
using static Emberglass.API.Shared.VExtensions;
using static Emberglass.Network.PacketRelay;

namespace Emberglass.Patches.Client;
internal class ClientChatSystemPatch
{
    static Harmony _harmony;
    public static void Initialize()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(ClientChatSystemPatch), MyPluginInfo.PLUGIN_GUID);
    }
    public static void Uninitialize()
    {
        _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
    [HarmonyPrefix]
    public static void OnUpdatePrefix(ClientChatSystem __instance)
    {
        if (!VWorld.LocalCharacter.Exists())
        {
            return;
        }

        using NativeAccessor<Entity> entities = __instance._ReceiveChatMessagesQuery.ToEntityArrayAccessor();
        using NativeAccessor<ChatMessageServerEvent> chatMessageServerEvents = __instance._ReceiveChatMessagesQuery.ToComponentDataArrayAccessor<ChatMessageServerEvent>();

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            ChatMessageServerEvent chatMessage = chatMessageServerEvents[i];
            string messageText = chatMessage.MessageText.Value;

            if (HasPacketPrefix(messageText))
            {
                /*
                FromCharacter fromCharacter = new()
                {
                    Character = VWorld.LocalCharacter,
                    User = VWorld.LocalUser
                };
                */

                OnServerPacketReceived(entity, VWorld.LocalUser.GetUser(), messageText);
                entity.Destroy(true);
            }
        }
    }
}
