using Emberglass.API.Shared;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using static Emberglass.API.Shared.VExtensions;
using static Emberglass.Network.PacketRelay;

namespace Emberglass.Patches.Server;
internal class ChatMessageSystemPatch
{
    public delegate void ChatEventHandler(Entity entity, ChatMessageEvent chatMessage, FromCharacter fromCharacter);
    public static event ChatEventHandler OnChatMessageHandler;

    static Harmony _harmony;
    public static void Initialize()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(ChatMessageSystemPatch), MyPluginInfo.PLUGIN_GUID);
    }
    public static void Uninitialize()
    {
        _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(ChatMessageSystem), nameof(ChatMessageSystem.OnUpdate))]
    [HarmonyPrefix]
    public static void OnUpdatePrefix(ChatMessageSystem __instance)
    {
        using NativeAccessor<Entity> entities = __instance.__query_661171423_0.ToEntityArrayAccessor();
        using NativeAccessor<ChatMessageEvent> chatMessageEvents = __instance.__query_661171423_0.ToComponentDataArrayAccessor<ChatMessageEvent>();
        using NativeAccessor<FromCharacter> fromCharacters = __instance.__query_661171423_0.ToComponentDataArrayAccessor<FromCharacter>();

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            ChatMessageEvent chatMessage = chatMessageEvents[i];
            FromCharacter fromCharacter = fromCharacters[i];
            string messageText = chatMessage.MessageText.Value;

            if (HasPacketPrefix(messageText))
            {
                OnClientPacketReceived(entity, fromCharacter.User.GetUser(), messageText);
                entity.Destroy(true);
                continue;
            }

            try
            {
                OnChatMessageHandler?.Invoke(entity, chatMessage, fromCharacter);
            }
            catch (Exception ex)
            {
                VWorld.Log.LogError($"Error dispatching chat event: {ex}");
            }
        }
    }
}
