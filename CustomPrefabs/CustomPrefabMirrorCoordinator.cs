using Emberglass.API.Shared;
using ProjectM.Network;
using System.Collections;
using UnityEngine;

namespace Emberglass.CustomPrefabs;

internal static class CustomPrefabMirrorCoordinator
{
    internal const int ClientMirrorRegistrationMaxAttempts = 20;
    internal const float ClientMirrorRegistrationRetryDelaySeconds = 0.5f;

    static readonly object Gate = new();
    static readonly Dictionary<(ulong PlatformId, int GeneratedPrefabGuid), CustomPrefabMirrorAckReceipt> AckReceipts = [];
    static bool _initialized;

    internal static event Action<CustomPrefabMirrorAckReceipt> MirrorAckRecorded;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (VWorld.IsServer)
        {
            VNetwork.RegisterServerbound<CustomPrefabMirrorAckPacket>(OnMirrorAck);
            VNetwork.OnReady += SendMirrorRecipes;
        }

        if (VWorld.IsClient)
        {
            VNetwork.RegisterClientbound<CustomPrefabMirrorRecipePacket>(OnMirrorRecipe);
        }

        _initialized = true;
    }

    public static void Uninitialize()
    {
        if (!_initialized)
        {
            return;
        }

        VNetwork.OnReady -= SendMirrorRecipes;
        VNetwork.Unregister<CustomPrefabMirrorRecipePacket>();
        VNetwork.Unregister<CustomPrefabMirrorAckPacket>();
        _initialized = false;

        ClearForTesting();
    }

    internal static CustomPrefabMirrorRecipePacket CreateRecipePacket(CustomPrefabRegistration registration)
        => new(
            registration.ProviderId,
            registration.SourcePrefabGuid,
            registration.GeneratedPrefabGuid,
            registration.GeneratedAssetGuid,
            registration.GeneratedAssetName,
            registration.ClientSyncRequired,
            (int)registration.CleanupPolicy);

    internal static bool TryCreateRegistration(
        CustomPrefabMirrorRecipePacket packet,
        out CustomPrefabRegistration registration,
        out string reason)
    {
        registration = default;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(packet.ProviderId))
        {
            reason = "provider id is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(packet.GeneratedAssetName))
        {
            reason = "generated asset name is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(packet.GeneratedAssetGuid)
            || !Guid.TryParseExact(packet.GeneratedAssetGuid, "N", out _))
        {
            reason = "generated asset guid is invalid";
            return false;
        }

        if (!Enum.IsDefined(typeof(CustomPrefabCleanupPolicy), packet.CleanupPolicy))
        {
            reason = "cleanup policy is invalid";
            return false;
        }

        CustomPrefabRegistration expected = CustomPrefabRegistration.Create(new(
            packet.ProviderId,
            packet.SourcePrefabGuid,
            packet.GeneratedAssetName,
            packet.ClientSyncRequired,
            (CustomPrefabCleanupPolicy)packet.CleanupPolicy));

        if (expected.GeneratedPrefabGuid != packet.GeneratedPrefabGuid
            || !string.Equals(expected.GeneratedAssetGuid, packet.GeneratedAssetGuid, StringComparison.OrdinalIgnoreCase))
        {
            reason = "generated ids do not match deterministic Emberglass ids";
            return false;
        }

        registration = expected;
        reason = "accepted";
        return true;
    }

    internal static void RecordAckForTesting(ulong platformId, CustomPrefabMirrorAckPacket packet)
        => RecordAck(platformId, packet);

    internal static bool TryGetAckForTesting(
        ulong platformId,
        int generatedPrefabGuid,
        out CustomPrefabMirrorAckReceipt receipt)
        => TryGetAck(platformId, generatedPrefabGuid, out receipt);

    internal static bool TryGetSucceededAck(
        ulong platformId,
        int generatedPrefabGuid,
        out CustomPrefabMirrorAckReceipt receipt)
        => TryGetAck(platformId, generatedPrefabGuid, out receipt)
            && receipt.Succeeded;

    static bool TryGetAck(
        ulong platformId,
        int generatedPrefabGuid,
        out CustomPrefabMirrorAckReceipt receipt)
    {
        lock (Gate)
        {
            return AckReceipts.TryGetValue((platformId, generatedPrefabGuid), out receipt);
        }
    }

    internal static void ClearForTesting()
    {
        lock (Gate)
        {
            AckReceipts.Clear();
        }
    }

    static void SendMirrorRecipes(User user)
    {
        IReadOnlyList<CustomPrefabRegistration> registrations = CustomPrefabRegistry.ActiveRegistrations;
        int sentCount = 0;

        foreach (CustomPrefabRegistration registration in registrations)
        {
            if (!registration.ClientSyncRequired)
            {
                continue;
            }

            VNetwork.SendToClient(user, CreateRecipePacket(registration));
            sentCount++;
        }

        if (sentCount > 0)
        {
            VWorld.Log.LogInfo($"[CustomPrefabs] Sent {sentCount} custom prefab mirror recipe(s); platformId={user.PlatformId}.");
        }
    }

    static void OnMirrorRecipe(User sender, CustomPrefabMirrorRecipePacket packet)
    {
        if (!TryCreateRegistration(packet, out CustomPrefabRegistration registration, out string reason))
        {
            SendAck(packet.ProviderId, packet.GeneratedPrefabGuid, false, reason);
            return;
        }

        RegisterMirrorRecipeWithRetry(registration).Run();
    }

    static IEnumerator RegisterMirrorRecipeWithRetry(CustomPrefabRegistration registration)
    {
        for (int attempt = 1; attempt <= ClientMirrorRegistrationMaxAttempts; attempt++)
        {
            try
            {
                CustomPrefabRegistrar registrar = new(CustomPrefabManifestStore.Default);
                if (registrar.TryRegister(registration, recordManifest: false, out string reason))
                {
                    SendAck(registration.ProviderId, registration.GeneratedPrefabGuid, true, reason);
                    yield break;
                }

                if (!ShouldRetryClientMirrorRegistration(reason, attempt, ClientMirrorRegistrationMaxAttempts))
                {
                    SendAck(registration.ProviderId, registration.GeneratedPrefabGuid, false, reason);
                    yield break;
                }

                if (attempt == 1 || attempt % 5 == 0)
                {
                    VWorld.Log.LogInfo($"[CustomPrefabs] Waiting for client mirror source availability; provider={registration.ProviderId}, generatedPrefabGuid={registration.GeneratedPrefabGuid}, attempts={attempt}.");
                }
            }
            catch (Exception ex)
            {
                SendAck(registration.ProviderId, registration.GeneratedPrefabGuid, false, ex.GetType().Name);
                yield break;
            }

            yield return new WaitForSeconds(ClientMirrorRegistrationRetryDelaySeconds);
        }
    }

    internal static bool ShouldRetryClientMirrorRegistration(string reason, int attempt, int maxAttempts)
        => attempt < maxAttempts
            && (string.Equals(reason, "source prefab missing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "converted asset data missing", StringComparison.OrdinalIgnoreCase));

    static void OnMirrorAck(User sender, CustomPrefabMirrorAckPacket packet)
    {
        CustomPrefabMirrorAckReceipt receipt = RecordAck(sender.PlatformId, packet);
        string status = packet.Succeeded ? "succeeded" : "failed";
        VWorld.Log.LogInfo($"[CustomPrefabs] Client prefab mirror {status}; platformId={sender.PlatformId}, provider={packet.ProviderId}, generatedPrefabGuid={packet.GeneratedPrefabGuid}, reason={packet.Reason}.");
        MirrorAckRecorded?.Invoke(receipt);
    }

    static void SendAck(string providerId, int generatedPrefabGuid, bool succeeded, string reason)
    {
        string safeReason = string.IsNullOrWhiteSpace(reason)
            ? (succeeded ? "registered" : "failed")
            : reason;
        VNetwork.SendToServer(new CustomPrefabMirrorAckPacket(providerId, generatedPrefabGuid, succeeded, safeReason));
    }

    static CustomPrefabMirrorAckReceipt RecordAck(ulong platformId, CustomPrefabMirrorAckPacket packet)
    {
        CustomPrefabMirrorAckReceipt receipt = new(
            platformId,
            packet.ProviderId,
            packet.GeneratedPrefabGuid,
            packet.Succeeded,
            packet.Reason);

        lock (Gate)
        {
            AckReceipts[(platformId, packet.GeneratedPrefabGuid)] = receipt;
        }

        return receipt;
    }
}
