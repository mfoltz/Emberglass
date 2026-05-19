namespace Emberglass.CustomPrefabs;

internal enum CustomPrefabCleanupPolicy
{
    Destroy = 0,
    Quarantine = 1
}

internal readonly record struct CustomPrefabDefinition(
    string ProviderId,
    int SourcePrefabGuid,
    string GeneratedAssetName,
    bool ClientSyncRequired,
    CustomPrefabCleanupPolicy CleanupPolicy);

internal readonly record struct CustomPrefabRegistration(
    string ProviderId,
    int SourcePrefabGuid,
    int GeneratedPrefabGuid,
    string GeneratedAssetGuid,
    string GeneratedAssetName,
    bool ClientSyncRequired,
    CustomPrefabCleanupPolicy CleanupPolicy)
{
    public static CustomPrefabRegistration Create(CustomPrefabDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ProviderId))
        {
            throw new ArgumentException("Provider id is required.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.GeneratedAssetName))
        {
            throw new ArgumentException("Generated asset name is required.", nameof(definition));
        }

        CustomPrefabIds ids = CustomPrefabIds.Create(
            definition.ProviderId,
            definition.SourcePrefabGuid,
            definition.GeneratedAssetName);

        return new(
            definition.ProviderId,
            definition.SourcePrefabGuid,
            ids.GeneratedPrefabGuid,
            ids.GeneratedAssetGuid,
            definition.GeneratedAssetName,
            definition.ClientSyncRequired,
            definition.CleanupPolicy);
    }
}
