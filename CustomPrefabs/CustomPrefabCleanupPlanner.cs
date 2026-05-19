namespace Emberglass.CustomPrefabs;

internal readonly record struct CustomPrefabCleanupCandidate(
    string ProviderId,
    int GeneratedPrefabGuid,
    CustomPrefabCleanupPolicy CleanupPolicy);

internal static class CustomPrefabCleanupPlanner
{
    public static IReadOnlyList<CustomPrefabCleanupCandidate> SelectOrphans(
        IEnumerable<CustomPrefabManifestEntry> manifestEntries,
        IEnumerable<int> activeGeneratedPrefabGuids,
        IEnumerable<int> observedPrefabGuids)
    {
        HashSet<int> activeIds = activeGeneratedPrefabGuids.ToHashSet();
        HashSet<int> observedIds = observedPrefabGuids.ToHashSet();
        List<CustomPrefabCleanupCandidate> candidates = [];

        foreach (CustomPrefabManifestEntry entry in manifestEntries)
        {
            if (activeIds.Contains(entry.GeneratedPrefabGuid)
                || !observedIds.Contains(entry.GeneratedPrefabGuid))
            {
                continue;
            }

            candidates.Add(new(
                entry.ProviderId,
                entry.GeneratedPrefabGuid,
                entry.CleanupPolicy));
        }

        return candidates;
    }
}
