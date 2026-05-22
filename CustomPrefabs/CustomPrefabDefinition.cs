namespace Emberglass.CustomPrefabs;

internal enum CustomPrefabCleanupPolicy
{
    Destroy = 0,
    Quarantine = 1
}

internal enum CustomPrefabSnapshotMode
{
    None = 0
}

[Flags]
internal enum CustomPrefabWorldTargets
{
    None = 0,
    Server = 1 << 0,
    Client = 1 << 1
}

internal readonly record struct CustomPrefabDefinition
{
    public CustomPrefabDefinition(
        string ProviderId,
        int SourcePrefabGuid,
        string GeneratedAssetName,
        bool ClientSyncRequired,
        CustomPrefabCleanupPolicy CleanupPolicy,
        CustomPrefabWorldTargets WorldTargets = CustomPrefabWorldTargets.Server | CustomPrefabWorldTargets.Client,
        CustomPrefabSnapshotMode SnapshotMode = CustomPrefabSnapshotMode.None,
        CustomPrefabEditPlan EditPlan = null)
    {
        this.ProviderId = ProviderId;
        this.SourcePrefabGuid = SourcePrefabGuid;
        this.GeneratedAssetName = GeneratedAssetName;
        this.ClientSyncRequired = ClientSyncRequired;
        this.CleanupPolicy = CleanupPolicy;
        this.WorldTargets = WorldTargets;
        this.SnapshotMode = SnapshotMode;
        this.EditPlan = EditPlan ?? CustomPrefabEditPlan.Empty;
    }

    public string ProviderId { get; init; }
    public int SourcePrefabGuid { get; init; }
    public string GeneratedAssetName { get; init; }
    public bool ClientSyncRequired { get; init; }
    public CustomPrefabCleanupPolicy CleanupPolicy { get; init; }
    public CustomPrefabWorldTargets WorldTargets { get; init; }
    public CustomPrefabSnapshotMode SnapshotMode { get; init; }
    public CustomPrefabEditPlan EditPlan { get; init; }
    public int BasePrefabGuid => SourcePrefabGuid;
}

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
