using Emberglass.API.Shared;
using Emberglass.Network;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Entities;
using static Emberglass.API.Shared.VExtensions;
using static Emberglass.Network.PacketRelay;

namespace Emberglass.Patches.Client;
internal class ClientChatSystemPatch
{
    static Harmony _harmony;
    static int _receiveDiagnosticsEmitted;
    static bool _localNoticesUnavailable;
    const int MAX_RECEIVE_DIAGNOSTICS = 20;
    const int MAX_LOCAL_NOTICES_PER_UPDATE = 4;
    public static void Initialize()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(ClientChatSystemPatch), MyPluginInfo.PLUGIN_GUID);
    }
    public static void Uninitialize()
    {
        _harmony?.UnpatchSelf();
        VShareClientNotices.Current.Reset();
        _localNoticesUnavailable = false;
    }

    [HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
    [HarmonyPrefix]
    static void OnUpdatePrefix(ClientChatSystem __instance)
    {
        FilterPackets(__instance);
        if (_localNoticesUnavailable || !VWorld.IsClient || !VNetwork.IsReady)
        {
            return;
        }

        for (int i = 0; i < MAX_LOCAL_NOTICES_PER_UPDATE
            && VShareClientNotices.Current.TryDequeue(VNetwork.ClientSessionGeneration, out string message); i++)
        {
            try
            {
                // The native local helper supplies the receive tag and the game's timestamp format.
                ClientSystemChatUtils.AddLocalMessage(__instance.EntityManager, message, ServerChatMessageType.System);
            }
            catch (Exception ex)
            {
                DisableLocalNotices(ex.Message);
                break;
            }
        }
    }

    static void DisableLocalNotices(string reason)
    {
        if (!_localNoticesUnavailable)
        {
            _localNoticesUnavailable = true;
            VShareClientNotices.Current.Reset();
            VWorld.Log?.LogWarning($"[VShare] Local chat notices disabled; see the console for progress. {reason}");
        }
    }

    static void FilterPackets(ClientChatSystem __instance)
    {
        using NativeAccessor<Entity> entities = __instance._ReceiveChatMessagesQuery.ToEntityArrayAccessor();
        using NativeAccessor<ChatMessageServerEvent> chatMessageServerEvents = __instance._ReceiveChatMessagesQuery.ToComponentDataArrayAccessor<ChatMessageServerEvent>();

        if (chatMessageServerEvents.Length > 0)
        {
            LogReceiveDiagnostic($"query count={chatMessageServerEvents.Length}");
        }

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            ChatMessageServerEvent chatMessage = chatMessageServerEvents[i];
            string messageText = chatMessage.MessageText.Value;

            if (HasPacketPrefix(messageText))
            {
                LogReceiveDiagnostic($"prefixed message type={chatMessage.MessageType} length={messageText.Length}");
                OnServerPacketReceived(entity, VWorld.LocalUser.GetUser(), messageText);
                entity.Destroy(true);
            }
            else if (chatMessage.MessageType == ServerChatMessageType.System)
            {
                LogReceiveDiagnostic($"system message without prefix length={messageText.Length}");
            }
        }
    }
    /// <summary>
    /// Writes bounded diagnostics for clientbound chat packet receive classification.
    /// </summary>
    /// <param name="message">Diagnostic message without payload contents.</param>
    static void LogReceiveDiagnostic(string message)
    {
        if (_receiveDiagnosticsEmitted >= MAX_RECEIVE_DIAGNOSTICS)
        {
            return;
        }

        _receiveDiagnosticsEmitted++;
        VWorld.Log.LogDebug($"[VNetwork.ClientReceive] {message}");
    }
}
