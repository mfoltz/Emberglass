using Emberglass.CustomPrefabs;
using Emberglass.Network;
using Emberglass.Systems;
using ProjectM;
using Unity.Entities;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the internal custom-prefab lifecycle proof without requiring a live V Rising world.
/// </summary>
[Collection("Assembly setup")]
public sealed class CustomPrefabLifecycleTests
{
    [Fact]
    public void CreateRegistration_IsDeterministicForSameProviderSourceAndName()
    {
        CustomPrefabDefinition Definition = new(
            ProviderId: "proof.provider",
            SourcePrefabGuid: -491593410,
            GeneratedAssetName: "AB_Blood_BloodRage_Buff_EmberglassProof",
            ClientSyncRequired: false,
            CleanupPolicy: CustomPrefabCleanupPolicy.Destroy);

        CustomPrefabRegistration First = CustomPrefabRegistration.Create(Definition);
        CustomPrefabRegistration Second = CustomPrefabRegistration.Create(Definition);

        Assert.Equal(First.GeneratedPrefabGuid, Second.GeneratedPrefabGuid);
        Assert.Equal(First.GeneratedAssetGuid, Second.GeneratedAssetGuid);
        Assert.Equal("proof.provider", First.ProviderId);
        Assert.Equal(-491593410, First.SourcePrefabGuid);
        Assert.False(First.ClientSyncRequired);
        Assert.Equal(CustomPrefabCleanupPolicy.Destroy, First.CleanupPolicy);
    }

    [Fact]
    public void Definition_ExposesStunlockShapedLifecycleDefaults()
    {
        CustomPrefabDefinition Definition = new(
            ProviderId: "proof.provider",
            SourcePrefabGuid: -491593410,
            GeneratedAssetName: "AB_Blood_BloodRage_Buff_EmberglassProof",
            ClientSyncRequired: false,
            CleanupPolicy: CustomPrefabCleanupPolicy.Destroy);

        Assert.Equal(Definition.SourcePrefabGuid, Definition.BasePrefabGuid);
        Assert.Equal(CustomPrefabWorldTargets.Server | CustomPrefabWorldTargets.Client, Definition.WorldTargets);
        Assert.Equal(CustomPrefabSnapshotMode.None, Definition.SnapshotMode);
        Assert.True(Definition.EditPlan.IsEmpty);
    }

    [Fact]
    public void Registry_AllowsClientMirrorRequiredRegistrations()
    {
        try
        {
            CustomPrefabRegistration Registration = CustomPrefabRegistry.Register(new(
                ProviderId: "proof.provider",
                SourcePrefabGuid: -491593410,
                GeneratedAssetName: "AB_Blood_BloodRage_Buff_EmberglassProof",
                ClientSyncRequired: true,
                CleanupPolicy: CustomPrefabCleanupPolicy.Destroy));

            Assert.True(Registration.ClientSyncRequired);
            Assert.Contains(
                CustomPrefabRegistry.ActiveRegistrations,
                Active => Active.GeneratedPrefabGuid == Registration.GeneratedPrefabGuid);
        }
        finally
        {
            CustomPrefabRegistry.ClearForTesting();
        }
    }

    [Fact]
    public void Registry_StoresServerSideEditPlanByGeneratedPrefabGuid()
    {
        try
        {
            CustomPrefabEditPlan EditPlan = CustomPrefabEditPlan.Create(
                "strip-stat-modifiers",
                Edit => Edit.RemoveBuffer<ModifyUnitStatBuff_DOTS>("remove inherited stat modifiers"));

            CustomPrefabRegistration Registration = CustomPrefabRegistry.Register(new(
                ProviderId: "proof.provider",
                SourcePrefabGuid: -491593410,
                GeneratedAssetName: "AB_Blood_BloodRage_Buff_EmberglassProof",
                ClientSyncRequired: true,
                CleanupPolicy: CustomPrefabCleanupPolicy.Destroy,
                EditPlan: EditPlan));

            Assert.True(CustomPrefabRegistry.TryGetEditPlan(
                Registration.GeneratedPrefabGuid,
                out CustomPrefabEditPlan StoredPlan));
            Assert.Equal(1, StoredPlan.Count);
            Assert.Equal("strip-stat-modifiers", StoredPlan.Name);
        }
        finally
        {
            CustomPrefabRegistry.ClearForTesting();
        }
    }

    [Fact]
    public void BuiltInProofDefinitions_RegisterBloodRageMirrorSeed()
    {
        try
        {
            CustomPrefabBuiltInProofDefinitions.Register();

            CustomPrefabRegistration Registration = Assert.Single(CustomPrefabRegistry.ActiveRegistrations);
            Assert.Equal(CustomPrefabBuiltInProofDefinitions.BloodRageProviderId, Registration.ProviderId);
            Assert.Equal(-491593410, Registration.SourcePrefabGuid);
            Assert.Equal("AB_Blood_BloodRage_Buff_EmberglassProof", Registration.GeneratedAssetName);
            Assert.True(Registration.ClientSyncRequired);
            Assert.Equal(CustomPrefabCleanupPolicy.Destroy, Registration.CleanupPolicy);
            Assert.True(CustomPrefabRegistry.TryGetEditPlan(
                Registration.GeneratedPrefabGuid,
                out CustomPrefabEditPlan EditPlan));
            Assert.Equal("blood-rage-proof-edits", EditPlan.Name);
            Assert.Equal(1, EditPlan.Count);
        }
        finally
        {
            CustomPrefabRegistry.ClearForTesting();
        }
    }

    [Fact]
    public void MirrorRecipePacket_RoundTripsAndStaysSmall()
    {
        CustomPrefabRegistration Registration = CustomPrefabRegistration.Create(new(
            ProviderId: "proof.provider",
            SourcePrefabGuid: -491593410,
            GeneratedAssetName: "AB_Blood_BloodRage_Buff_EmberglassProof",
            ClientSyncRequired: true,
            CleanupPolicy: CustomPrefabCleanupPolicy.Destroy));

        CustomPrefabMirrorRecipePacket Packet = CustomPrefabMirrorCoordinator.CreateRecipePacket(Registration);
        byte[] Packed = Serialization.GetPacker(typeof(CustomPrefabMirrorRecipePacket))(Packet);
        CustomPrefabMirrorRecipePacket RoundTrip = (CustomPrefabMirrorRecipePacket)Serialization
            .GetUnpacker(typeof(CustomPrefabMirrorRecipePacket))(Packed);

        Assert.True(Packed.Length <= Registry.Const.REMAINING_BYTES, Packed.Length.ToString());
        Assert.Equal(Registration.ProviderId, RoundTrip.ProviderId);
        Assert.Equal(Registration.SourcePrefabGuid, RoundTrip.SourcePrefabGuid);
        Assert.Equal(Registration.GeneratedPrefabGuid, RoundTrip.GeneratedPrefabGuid);
        Assert.Equal(Registration.GeneratedAssetGuid, RoundTrip.GeneratedAssetGuid);
        Assert.Equal(Registration.GeneratedAssetName, RoundTrip.GeneratedAssetName);
        Assert.True(RoundTrip.ClientSyncRequired);
        Assert.Equal((int)CustomPrefabCleanupPolicy.Destroy, RoundTrip.CleanupPolicy);
    }

    [Fact]
    public void MirrorRecipePacket_RehydratesRegistration()
    {
        CustomPrefabRegistration Registration = CustomPrefabRegistration.Create(new(
            ProviderId: "proof.provider",
            SourcePrefabGuid: -491593410,
            GeneratedAssetName: "AB_Blood_BloodRage_Buff_EmberglassProof",
            ClientSyncRequired: true,
            CleanupPolicy: CustomPrefabCleanupPolicy.Destroy));
        CustomPrefabMirrorRecipePacket Packet = CustomPrefabMirrorCoordinator.CreateRecipePacket(Registration);

        bool Success = CustomPrefabMirrorCoordinator.TryCreateRegistration(
            Packet,
            out CustomPrefabRegistration Rehydrated,
            out string Reason);

        Assert.True(Success, Reason);
        Assert.Equal(Registration, Rehydrated);
    }

    [Fact]
    public void MirrorAckReceipt_RecordsClientMirrorResultByPlatformAndPrefab()
    {
        try
        {
            CustomPrefabMirrorAckPacket Ack = new(
                providerId: "proof.provider",
                generatedPrefabGuid: 12345,
                succeeded: true,
                reason: "registered");

            CustomPrefabMirrorCoordinator.RecordAckForTesting(76561198000000000UL, Ack);

            Assert.True(CustomPrefabMirrorCoordinator.TryGetAckForTesting(
                76561198000000000UL,
                12345,
                out CustomPrefabMirrorAckReceipt Receipt));
            Assert.Equal("proof.provider", Receipt.ProviderId);
            Assert.True(Receipt.Succeeded);
            Assert.Equal("registered", Receipt.Reason);
        }
        finally
        {
            CustomPrefabMirrorCoordinator.ClearForTesting();
        }
    }

    [Fact]
    public void ManifestStore_RecordsGeneratedPrefabIdsAndSurvivesProviderRemoval()
    {
        string ManifestPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            CustomPrefabManifestStore Store = new(ManifestPath);
            CustomPrefabRegistration Registration = CustomPrefabRegistration.Create(new(
                ProviderId: "proof.provider",
                SourcePrefabGuid: -491593410,
                GeneratedAssetName: "AB_Blood_BloodRage_Buff_EmberglassProof",
                ClientSyncRequired: false,
                CleanupPolicy: CustomPrefabCleanupPolicy.Destroy));

            Store.RecordRegistration(Registration);
            CustomPrefabManifest Reloaded = Store.Load();

            CustomPrefabManifestEntry Entry = Assert.Single(Reloaded.Entries);
            Assert.Equal(Registration.ProviderId, Entry.ProviderId);
            Assert.Equal(Registration.GeneratedPrefabGuid, Entry.GeneratedPrefabGuid);
            Assert.Equal(Registration.GeneratedAssetGuid, Entry.GeneratedAssetGuid);
            Assert.Equal(Registration.CleanupPolicy, Entry.CleanupPolicy);
        }
        finally
        {
            if (File.Exists(ManifestPath))
            {
                File.Delete(ManifestPath);
            }
        }
    }

    [Fact]
    public void CleanupPlanner_SelectsManifestKnownPrefabIdsAbsentFromActiveRegistry()
    {
        CustomPrefabManifestEntry Orphan = new(
            ProviderId: "missing.provider",
            SourcePrefabGuid: -491593410,
            GeneratedPrefabGuid: 1001,
            GeneratedAssetGuid: "11111111111111111111111111111111",
            GeneratedAssetName: "MissingClone",
            ClientSyncRequired: false,
            CleanupPolicy: CustomPrefabCleanupPolicy.Destroy);
        CustomPrefabManifestEntry Active = Orphan with
        {
            ProviderId = "active.provider",
            GeneratedPrefabGuid = 1002,
            GeneratedAssetGuid = "22222222222222222222222222222222"
        };

        IReadOnlyList<CustomPrefabCleanupCandidate> Candidates = CustomPrefabCleanupPlanner.SelectOrphans(
            new[] { Orphan, Active },
            activeGeneratedPrefabGuids: new[] { 1002 },
            observedPrefabGuids: new[] { 1001, 1002, 777 });

        CustomPrefabCleanupCandidate Candidate = Assert.Single(Candidates);
        Assert.Equal("missing.provider", Candidate.ProviderId);
        Assert.Equal(1001, Candidate.GeneratedPrefabGuid);
        Assert.Equal(CustomPrefabCleanupPolicy.Destroy, Candidate.CleanupPolicy);
    }

    [Fact]
    public void CleanupPlanner_IgnoresActiveRegisteredIdsAndUnrelatedVanillaIds()
    {
        CustomPrefabManifestEntry Active = new(
            ProviderId: "active.provider",
            SourcePrefabGuid: -491593410,
            GeneratedPrefabGuid: 1002,
            GeneratedAssetGuid: "22222222222222222222222222222222",
            GeneratedAssetName: "ActiveClone",
            ClientSyncRequired: false,
            CleanupPolicy: CustomPrefabCleanupPolicy.Destroy);

        IReadOnlyList<CustomPrefabCleanupCandidate> Candidates = CustomPrefabCleanupPlanner.SelectOrphans(
            new[] { Active },
            activeGeneratedPrefabGuids: new[] { 1002 },
            observedPrefabGuids: new[] { 1002, 777 });

        Assert.Empty(Candidates);
    }

    [Fact]
    public void CleanupSystem_QueryIncludesDisabledEntities()
    {
        EntityQueryOptions Options = CustomPrefabCleanupSystem.CleanupQueryOptions;

        Assert.True(Options.HasFlag(EntityQueryOptions.IncludeDisabled));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    public void RegistrationSystem_DisablesOnlyWhenThereIsNoWorkOrRegistrationSucceeded(
        int activeRegistrationCount,
        int registeredCount,
        bool expectedShouldDisable)
    {
        Assert.Equal(
            expectedShouldDisable,
            CustomPrefabRegistrationSystem.ShouldDisableAfterAttempt(activeRegistrationCount, registeredCount));
    }

    [Theory]
    [InlineData(true, true, true, true, true, true, false, true)]
    [InlineData(false, true, true, true, true, true, false, false)]
    [InlineData(true, false, true, true, true, true, false, false)]
    [InlineData(true, true, false, true, true, true, false, false)]
    [InlineData(true, true, true, false, true, true, false, false)]
    [InlineData(true, true, true, true, true, false, false, false)]
    [InlineData(true, true, true, true, false, false, false, true)]
    [InlineData(true, true, true, true, true, true, true, false)]
    public void ProofBuffApplier_WaitsForCharacterRegistrationServerPrefabAndMirrorAck(
        bool enabled,
        bool hasCharacter,
        bool hasRegistration,
        bool serverPrefabRegistered,
        bool clientSyncRequired,
        bool clientMirrorSucceeded,
        bool alreadyApplied,
        bool expected)
    {
        Assert.Equal(
            expected,
            CustomPrefabProofBuffApplier.CanAttemptApply(
                enabled,
                hasCharacter,
                hasRegistration,
                serverPrefabRegistered,
                clientSyncRequired,
                clientMirrorSucceeded,
                alreadyApplied));
    }
}
