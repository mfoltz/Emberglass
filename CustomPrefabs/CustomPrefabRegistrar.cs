using Emberglass.API.Shared;
using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Emberglass.CustomPrefabs;

internal sealed class CustomPrefabRegistrar
{
    static readonly ComponentType[] PrefabGuidComponent =
    [
        ComponentType.ReadOnly(Il2CppType.Of<PrefabGUID>())
    ];

    static readonly ComponentType[] RegisterPrefabComponents =
    [
        ComponentType.ReadOnly(Il2CppType.Of<RegisterPrefab>()),
        ComponentType.ReadOnly(Il2CppType.Of<RegisterPrefabEvent>()),
        ComponentType.ReadOnly(Il2CppType.Of<Prefab>())
    ];

    readonly CustomPrefabManifestStore _manifestStore;

    public CustomPrefabRegistrar(CustomPrefabManifestStore manifestStore)
    {
        _manifestStore = manifestStore;
    }

    public int RegisterActiveServerPrefabs(bool logMissingSources = true)
    {
        IReadOnlyList<CustomPrefabRegistration> registrations = CustomPrefabRegistry.ActiveRegistrations;
        if (registrations.Count == 0)
        {
            return 0;
        }

        Dictionary<int, Entity> prefabsByGuid = GatherSourcePrefabs(registrations.Select(r => r.SourcePrefabGuid));
        int registeredCount = 0;

        foreach (CustomPrefabRegistration registration in registrations)
        {
            registeredCount += TryRegister(registration, prefabsByGuid, recordManifest: true, logMissingSources, out _) ? 1 : 0;
        }

        return registeredCount;
    }

    public bool TryRegister(
        CustomPrefabRegistration registration,
        bool recordManifest,
        out string reason)
        => TryRegister(registration, recordManifest, logMissingSources: true, out reason);

    public bool TryRegister(
        CustomPrefabRegistration registration,
        bool recordManifest,
        bool logMissingSources,
        out string reason)
    {
        Dictionary<int, Entity> prefabsByGuid = GatherSourcePrefabs(new[] { registration.SourcePrefabGuid });
        return TryRegister(registration, prefabsByGuid, recordManifest, logMissingSources, out reason);
    }

    static Dictionary<int, Entity> GatherSourcePrefabs(IEnumerable<int> sourcePrefabGuids)
    {
        HashSet<int> wanted = sourcePrefabGuids.ToHashSet();
        Dictionary<int, Entity> prefabsByGuid = [];
        EntityQuery prefabQuery = VWorld.EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = PrefabGuidComponent,
            Options = EntityQueryOptions.IncludeAll
        });

        NativeArray<Entity> entities = prefabQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (Entity entity in entities)
            {
                if (entity.TryGetComponent(out PrefabGUID prefabGuid)
                    && wanted.Contains(prefabGuid._Value))
                {
                    prefabsByGuid[prefabGuid._Value] = entity;
                }
            }
        }
        finally
        {
            if (entities.IsCreated)
            {
                entities.Dispose();
            }

            prefabQuery.Dispose();
        }

        return prefabsByGuid;
    }

    bool TryRegister(
        CustomPrefabRegistration registration,
        IReadOnlyDictionary<int, Entity> prefabsByGuid,
        bool recordManifest,
        bool logMissingSources,
        out string reason)
    {
        if (!prefabsByGuid.TryGetValue(registration.SourcePrefabGuid, out Entity sourcePrefab)
            || !sourcePrefab.Exists())
        {
            reason = "source prefab missing";
            if (logMissingSources)
            {
                VWorld.Log.LogWarning($"[CustomPrefabs] Source prefab missing; provider={registration.ProviderId}, sourcePrefabGuid={registration.SourcePrefabGuid}.");
            }

            return false;
        }

        CustomPrefabRegistry.TryGetEditPlan(registration.GeneratedPrefabGuid, out CustomPrefabEditPlan editPlan);
        if (!RegisterClone(registration, sourcePrefab, editPlan ?? CustomPrefabEditPlan.Empty, out reason))
        {
            return false;
        }

        if (recordManifest)
        {
            _manifestStore.RecordRegistration(registration);
        }

        return true;
    }

    static bool RegisterClone(
        CustomPrefabRegistration registration,
        Entity sourcePrefab,
        CustomPrefabEditPlan editPlan,
        out string reason)
    {
        reason = string.Empty;
        PrefabGUID sourcePrefabGuid = new(registration.SourcePrefabGuid);
        if (!PrefabLookupUtility.TryGetConvertedAssetDataForPrefab(sourcePrefabGuid, out ConvertedAssetData sourceAsset))
        {
            reason = "converted asset data missing";
            VWorld.Log.LogWarning($"[CustomPrefabs] ConvertedAssetData not found; provider={registration.ProviderId}, sourcePrefabGuid={registration.SourcePrefabGuid}.");
            return false;
        }

        Entity prefabTarget = VWorld.EntityManager.Instantiate(sourcePrefab);
        PrefabGUID generatedPrefabGuid = new(registration.GeneratedPrefabGuid);
        prefabTarget.With((ref PrefabGUID prefabGuid) =>
        {
            prefabGuid = generatedPrefabGuid;
        });
        prefabTarget.Add<Prefab>();

        CustomPrefabEditReceipt editReceipt = editPlan.Apply(registration, prefabTarget);
        if (editReceipt.TotalCount > 0)
        {
            VWorld.Log.LogInfo($"[CustomPrefabs] Applied custom prefab edit plan; provider={registration.ProviderId}, generatedPrefabGuid={registration.GeneratedPrefabGuid}, plan={editReceipt.PlanName}, applied={editReceipt.AppliedCount}, skipped={editReceipt.SkippedCount}, edits={string.Join(",", editReceipt.Messages)}.");
        }

        AssetGuid generatedAssetGuid = new CustomPrefabIds(
            registration.GeneratedPrefabGuid,
            registration.GeneratedAssetGuid).ToAssetGuid();
        ConvertedAssetData convertedAssetData = new()
        {
            AssetGuid = generatedAssetGuid,
            AssetName = new FixedString128Bytes(registration.GeneratedAssetName),
            LabelFlags = sourceAsset.LabelFlags
        };

        PrefabCollectionSystem prefabCollectionSystem = VWorld.GetSystem<PrefabCollectionSystem>();
        prefabCollectionSystem._PrefabDataLookup.TryAdd(generatedPrefabGuid, convertedAssetData);
        prefabCollectionSystem._PrefabGuidToEntityMap.TryAdd(generatedPrefabGuid, prefabTarget);

        RegisterPrefab registerPrefab = new()
        {
            AssetGuid = generatedAssetGuid,
            Name = new FixedString128Bytes(registration.GeneratedAssetName),
            PrefabEntity = prefabTarget,
            Caller = new FixedString512Bytes($"Emberglass custom prefab proof: {registration.ProviderId}")
        };
        RegisterPrefabEvent registerPrefabEvent = new()
        {
            PrefabGUID = generatedPrefabGuid
        };

        Entity registerPrefabEntity = VWorld.EntityManager.CreateEntity(RegisterPrefabComponents);
        registerPrefabEntity.Write(registerPrefab);
        registerPrefabEntity.Write(registerPrefabEvent);

        prefabCollectionSystem.RegisterPrefabs();
        reason = "registered";
        string runtimeSide = VWorld.IsClient ? "client mirror" : "server";
        VWorld.Log.LogInfo($"[CustomPrefabs] Registered {runtimeSide} prefab clone; provider={registration.ProviderId}, sourcePrefabGuid={registration.SourcePrefabGuid}, generatedPrefabGuid={registration.GeneratedPrefabGuid}, generatedAssetName={registration.GeneratedAssetName}.");
        return true;
    }
}
