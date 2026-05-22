using Emberglass.API.Shared;
using Emberglass.CustomPrefabs;
using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Emberglass.Systems;

public sealed class CustomPrefabCleanupSystem : SystemBase
{
    EntityQuery _prefabGuidQuery;
    bool _hasRun;

    public override void OnCreate()
    {
        _prefabGuidQuery = EntityManager.CreateEntityQuery(CreateCleanupQueryDesc());
    }

    public override void OnUpdate()
    {
        if (_hasRun)
        {
            return;
        }

        _hasRun = true;
        RunCleanup();
    }

    internal static EntityQueryDesc CreateCleanupQueryDesc()
    {
        return new()
        {
            All = CreateCleanupAllComponents(),
            Options = CleanupQueryOptions
        };
    }

    internal static EntityQueryOptions CleanupQueryOptions => EntityQueryOptions.IncludeDisabled;

    internal static ComponentType[] CreateCleanupAllComponents()
    {
        return
        [
            ComponentType.ReadOnly(Il2CppType.Of<PrefabGUID>())
        ];
    }

    void RunCleanup()
    {
        CustomPrefabManifest manifest = CustomPrefabManifestStore.Default.Load();
        if (manifest.Entries.Count == 0)
        {
            VWorld.Log.LogInfo("[CustomPrefabs.Cleanup] Manifest empty; no custom prefab cleanup needed.");
            return;
        }

        IReadOnlyList<CustomPrefabRegistration> activeRegistrations = CustomPrefabRegistry.ActiveRegistrations;
        HashSet<int> activeGeneratedPrefabGuids = activeRegistrations
            .Select(registration => registration.GeneratedPrefabGuid)
            .ToHashSet();

        NativeArray<Entity> entities = _prefabGuidQuery.ToEntityArray(Allocator.Temp);
        List<int> observedPrefabGuids = new(entities.Length);
        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entity.TryGetComponent(out PrefabGUID prefabGuid))
                {
                    observedPrefabGuids.Add(prefabGuid._Value);
                }
            }

            IReadOnlyList<CustomPrefabCleanupCandidate> candidates = CustomPrefabCleanupPlanner.SelectOrphans(
                manifest.Entries,
                activeGeneratedPrefabGuids,
                observedPrefabGuids);

            if (candidates.Count == 0)
            {
                VWorld.Log.LogInfo($"[CustomPrefabs.Cleanup] No orphaned custom prefab entities found; manifestEntries={manifest.Entries.Count}, activeRegistrations={activeRegistrations.Count}.");
                return;
            }

            DestroyOrQuarantineCandidates(entities, candidates);
        }
        finally
        {
            if (entities.IsCreated)
            {
                entities.Dispose();
            }
        }
    }

    static void DestroyOrQuarantineCandidates(NativeArray<Entity> entities, IReadOnlyList<CustomPrefabCleanupCandidate> candidates)
    {
        Dictionary<int, CustomPrefabCleanupCandidate> candidatesByGuid = candidates.ToDictionary(candidate => candidate.GeneratedPrefabGuid);
        Dictionary<int, int> cleanupCountsByGuid = [];

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            if (!entity.TryGetComponent(out PrefabGUID prefabGuid)
                || !candidatesByGuid.TryGetValue(prefabGuid._Value, out CustomPrefabCleanupCandidate candidate))
            {
                continue;
            }

            entity.Disable();
            if (candidate.CleanupPolicy == CustomPrefabCleanupPolicy.Destroy)
            {
                entity.Destroy();
            }

            cleanupCountsByGuid[prefabGuid._Value] = cleanupCountsByGuid.GetValueOrDefault(prefabGuid._Value) + 1;
        }

        foreach (CustomPrefabCleanupCandidate candidate in candidates)
        {
            cleanupCountsByGuid.TryGetValue(candidate.GeneratedPrefabGuid, out int entityCount);
            VWorld.Log.LogWarning($"[CustomPrefabs.Cleanup] Orphan cleanup receipt; provider={candidate.ProviderId}, generatedPrefabGuid={candidate.GeneratedPrefabGuid}, cleanupPolicy={candidate.CleanupPolicy}, entityCount={entityCount}.");
        }
    }
}
