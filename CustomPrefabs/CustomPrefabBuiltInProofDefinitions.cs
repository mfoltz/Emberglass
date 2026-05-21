using ProjectM;

namespace Emberglass.CustomPrefabs;

internal static class CustomPrefabBuiltInProofDefinitions
{
    internal const string BloodRageProviderId = "emberglass.proof.blood-rage";
    const int BloodRagePrefabGuid = -491593410;
    const string BloodRageGeneratedAssetName = "AB_Blood_BloodRage_Buff_EmberglassProof";

    public static void Register()
    {
        CustomPrefabRegistry.Register(new(
            ProviderId: BloodRageProviderId,
            SourcePrefabGuid: BloodRagePrefabGuid,
            GeneratedAssetName: BloodRageGeneratedAssetName,
            ClientSyncRequired: true,
            CleanupPolicy: CustomPrefabCleanupPolicy.Destroy,
            EditPlan: CustomPrefabEditPlan.Create(
                "blood-rage-proof-edits",
                Edit => Edit.RemoveBuffer<ModifyUnitStatBuff_DOTS>("remove inherited stat modifiers"))));
    }
}
