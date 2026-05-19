namespace Emberglass.CustomPrefabs;

internal sealed class CustomPrefabMirrorRecipePacket
{
    public CustomPrefabMirrorRecipePacket()
    {
    }

    public CustomPrefabMirrorRecipePacket(
        string providerId,
        int sourcePrefabGuid,
        int generatedPrefabGuid,
        string generatedAssetGuid,
        string generatedAssetName,
        bool clientSyncRequired,
        int cleanupPolicy)
    {
        ProviderId = providerId;
        SourcePrefabGuid = sourcePrefabGuid;
        GeneratedPrefabGuid = generatedPrefabGuid;
        GeneratedAssetGuid = generatedAssetGuid;
        GeneratedAssetName = generatedAssetName;
        ClientSyncRequired = clientSyncRequired;
        CleanupPolicy = cleanupPolicy;
    }

    public string ProviderId { get; set; } = string.Empty;

    public int SourcePrefabGuid { get; set; }

    public int GeneratedPrefabGuid { get; set; }

    public string GeneratedAssetGuid { get; set; } = string.Empty;

    public string GeneratedAssetName { get; set; } = string.Empty;

    public bool ClientSyncRequired { get; set; }

    public int CleanupPolicy { get; set; }
}

internal sealed class CustomPrefabMirrorAckPacket
{
    public CustomPrefabMirrorAckPacket()
    {
    }

    public CustomPrefabMirrorAckPacket(
        string providerId,
        int generatedPrefabGuid,
        bool succeeded,
        string reason)
    {
        ProviderId = providerId;
        GeneratedPrefabGuid = generatedPrefabGuid;
        Succeeded = succeeded;
        Reason = reason;
    }

    public string ProviderId { get; set; } = string.Empty;

    public int GeneratedPrefabGuid { get; set; }

    public bool Succeeded { get; set; }

    public string Reason { get; set; } = string.Empty;
}

internal readonly record struct CustomPrefabMirrorAckReceipt(
    ulong PlatformId,
    string ProviderId,
    int GeneratedPrefabGuid,
    bool Succeeded,
    string Reason);
