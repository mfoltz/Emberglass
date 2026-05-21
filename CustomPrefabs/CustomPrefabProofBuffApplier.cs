using Emberglass.API.Server;
using Emberglass.API.Shared;
using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Entities;
using static Emberglass.API.Server.ServerModules.PlayerPresenceModules;
using static Emberglass.API.Shared.VEvents;

namespace Emberglass.CustomPrefabs;

internal static class CustomPrefabProofBuffApplier
{
    internal const string EnvironmentVariable = "EMBERGLASS_CUSTOM_PREFAB_PROOF_BUFF";

    internal static bool IsEnabled { get; set; } = IsEnvironmentEnabled();

    static readonly Dictionary<ulong, Entity> AttachedCharactersByPlatformId = [];
    static readonly HashSet<ulong> AppliedPlatformIds = [];
    static bool _initialized;

    internal static void Initialize()
    {
        if (!VWorld.IsServer || !IsEnabled || _initialized)
        {
            return;
        }

        ModuleRegistry.Subscribe<PlayerCharacterAttached>(OnPlayerCharacterAttached);
        ModuleRegistry.Subscribe<PlayerCharacterDetached>(OnPlayerCharacterDetached);
        CustomPrefabMirrorCoordinator.MirrorAckRecorded += OnMirrorAckRecorded;
        _initialized = true;

        VWorld.Log.LogInfo("[CustomPrefabs.Proof] Blood Rage proof buff application enabled.");
    }

    internal static void Uninitialize()
    {
        if (!_initialized)
        {
            return;
        }

        ModuleRegistry.Unsubscribe<PlayerCharacterAttached>(OnPlayerCharacterAttached);
        ModuleRegistry.Unsubscribe<PlayerCharacterDetached>(OnPlayerCharacterDetached);
        CustomPrefabMirrorCoordinator.MirrorAckRecorded -= OnMirrorAckRecorded;

        AttachedCharactersByPlatformId.Clear();
        AppliedPlatformIds.Clear();
        _initialized = false;
    }

    internal static bool CanAttemptApply(
        bool enabled,
        bool hasCharacter,
        bool hasRegistration,
        bool serverPrefabRegistered,
        bool clientSyncRequired,
        bool clientMirrorSucceeded,
        bool alreadyApplied)
        => enabled
            && hasCharacter
            && hasRegistration
            && serverPrefabRegistered
            && !alreadyApplied
            && (!clientSyncRequired || clientMirrorSucceeded);

    static void OnPlayerCharacterAttached(PlayerCharacterAttached args)
    {
        ulong platformId = args.PlayerInfo.SteamId;
        if (platformId == 0)
        {
            return;
        }

        AttachedCharactersByPlatformId[platformId] = args.CharacterEntity;
        TryApplyToAttachedCharacter(platformId, logPending: true);
    }

    static void OnPlayerCharacterDetached(PlayerCharacterDetached args)
    {
        ulong platformId = args.PlayerInfo.SteamId;
        if (platformId == 0)
        {
            return;
        }

        AttachedCharactersByPlatformId.Remove(platformId);
        AppliedPlatformIds.Remove(platformId);
    }

    static void OnMirrorAckRecorded(CustomPrefabMirrorAckReceipt receipt)
    {
        if (receipt.Succeeded)
        {
            TryApplyToAttachedCharacter(receipt.PlatformId, logPending: false);
        }
    }

    static void TryApplyToAttachedCharacter(ulong platformId, bool logPending)
    {
        bool hasCharacter = AttachedCharactersByPlatformId.TryGetValue(platformId, out Entity characterEntity)
            && characterEntity.Exists();
        bool hasRegistration = TryGetBloodRageRegistration(out CustomPrefabRegistration registration);
        bool serverPrefabRegistered = hasRegistration && IsServerPrefabRegistered(registration.GeneratedPrefabGuid);
        bool clientMirrorSucceeded = !hasRegistration
            || !registration.ClientSyncRequired
            || CustomPrefabMirrorCoordinator.TryGetSucceededAck(platformId, registration.GeneratedPrefabGuid, out _);
        bool alreadyApplied = AppliedPlatformIds.Contains(platformId);

        if (!CanAttemptApply(
            IsEnabled,
            hasCharacter,
            hasRegistration,
            serverPrefabRegistered,
            hasRegistration && registration.ClientSyncRequired,
            clientMirrorSucceeded,
            alreadyApplied))
        {
            if (logPending && hasRegistration && registration.ClientSyncRequired && !clientMirrorSucceeded)
            {
                VWorld.Log.LogInfo($"[CustomPrefabs.Proof] Waiting for client mirror ack before applying proof buff; platformId={platformId}, generatedPrefabGuid={registration.GeneratedPrefabGuid}.");
            }

            return;
        }

        if (TryApplyBuff(characterEntity, registration, out string reason))
        {
            AppliedPlatformIds.Add(platformId);
            VWorld.Log.LogInfo($"[CustomPrefabs.Proof] Applied generated buff clone; provider={registration.ProviderId}, platformId={platformId}, generatedPrefabGuid={registration.GeneratedPrefabGuid}, character={FormatEntity(characterEntity)}.");
            return;
        }

        VWorld.Log.LogWarning($"[CustomPrefabs.Proof] Failed to apply generated buff clone; provider={registration.ProviderId}, platformId={platformId}, generatedPrefabGuid={registration.GeneratedPrefabGuid}, reason={reason}.");
    }

    static bool TryGetBloodRageRegistration(out CustomPrefabRegistration registration)
    {
        registration = CustomPrefabRegistry.ActiveRegistrations.FirstOrDefault(
            active => string.Equals(
                active.ProviderId,
                CustomPrefabBuiltInProofDefinitions.BloodRageProviderId,
                StringComparison.Ordinal));

        return !string.IsNullOrWhiteSpace(registration.ProviderId);
    }

    static bool IsServerPrefabRegistered(int generatedPrefabGuid)
    {
        PrefabGUID prefabGuid = new(generatedPrefabGuid);
        PrefabCollectionSystem prefabCollectionSystem = VWorld.GetSystem<PrefabCollectionSystem>();
        return prefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(prefabGuid, out Entity prefabEntity)
            && prefabEntity.Exists();
    }

    static bool TryApplyBuff(Entity characterEntity, CustomPrefabRegistration registration, out string reason)
    {
        reason = string.Empty;

        if (!characterEntity.Exists())
        {
            reason = "character entity missing";
            return false;
        }

        PrefabGUID generatedPrefabGuid = new(registration.GeneratedPrefabGuid);
        if (!IsServerPrefabRegistered(registration.GeneratedPrefabGuid))
        {
            reason = "generated prefab missing from server prefab map";
            return false;
        }

        ServerGameManager serverGameManager = VWorld.GetSystem<ServerScriptMapper>().GetServerGameManager();
        if (serverGameManager.HasBuff(characterEntity, generatedPrefabGuid.ToIdentifier()))
        {
            reason = "already applied";
            return true;
        }

        serverGameManager.InstantiateBuffEntityImmediate(characterEntity, characterEntity, generatedPrefabGuid);
        reason = "applied";
        return true;
    }

    static string FormatEntity(Entity entity)
        => entity == Entity.Null
            ? "null"
            : $"{entity.Index}:{entity.Version}";

    static bool IsEnvironmentEnabled()
    {
        string value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
