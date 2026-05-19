# Custom Prefab Lifecycle Proof

This note captures the first Emberglass-owned custom-prefab proof. It is internal and experimental: it does not promote
custom prefabs as a public API, and it does not claim full client-synced prefab or network snapshot support.

## Evidence Boundary

`C:\Users\mitch\Downloads\PrefabFactory.cs` demonstrates the viable runtime shape: find source prefab entities by
`PrefabGUID`, instantiate a source prefab, assign a generated `PrefabGUID`, add `Prefab`, update
`PrefabCollectionSystem` lookup maps, emit `RegisterPrefab` / `RegisterPrefabEvent`, and call `RegisterPrefabs`.

Emberglass keeps only that registration lesson. It does not carry forward `DarkMateria`, `CoreShared`,
`DarkMateria.Services`, item/localization expansion, spawnable-name rewrites, inheritance registration, metadata
expansion, or dynamic query-registry behavior.

The first known-good proof source is:

- source prefab: `AB_Blood_BloodRage_Buff`
- source guid: `PrefabGUID(-491593410)`
- first client mirror proof: `clientSyncRequired=true`

The Dracula spell-phase and werewolf shapeshift attempts stay parked. Their source-margin behavior suggests a later
client replacement or network snapshot proof, not a safe server-only lifecycle claim.

## Internal Shape

Custom prefab definitions are reduced to the fields needed for lifecycle and cleanup:

- `providerId`
- `sourcePrefabGuid`
- deterministic `generatedPrefabGuid`
- deterministic `generatedAssetGuid`
- `generatedAssetName`
- `clientSyncRequired`
- `cleanupPolicy`

`CustomPrefabRegistry` is internal. It records active registrations. In this proof, `clientSyncRequired=true` means the
server still registers the clone, then sends the same deterministic recipe to ready clients and waits for a mirror ack.
It does not mean Emberglass can generate new network snapshots.

`CustomPrefabRegistrar` performs the minimal clone registration. Server registration records each generated prefab in
`BepInEx/config/Emberglass/CustomPrefabs.json`; client mirror registration does not write the save-safety manifest.
`CustomPrefabRegistrationSystem` runs once on the server after cleanup and before later Emberglass observers, so active
internal proof definitions are manufactured without making the registry a public API.

`CustomPrefabBuiltInProofDefinitions` currently seeds the Blood Rage proof definition during server bootstrap. This is a
temporary runtime-test preflight hook, not the future provider API.

The manifest is the save-safety identity. Emberglass intentionally does not rely on provider-owned ECS component types,
because those types disappear when the provider mod is removed. A custom ECS marker is also deferred until component
persistence and IL2CPP registration are proven separately.

## Client Mirror Proof Gate

`CustomPrefabMirrorCoordinator` registers two internal VNetwork packets:

- `CustomPrefabMirrorRecipePacket`: server-to-client deterministic clone recipe.
- `CustomPrefabMirrorAckPacket`: client-to-server mirror result receipt.

When a client completes the VNetwork handshake, the server sends one mirror recipe per active registration where
`clientSyncRequired=true`. The client validates that the generated prefab id and generated asset guid match Emberglass's
deterministic id calculation, finds the local source prefab, runs the same minimal clone registrar against the client
world, and replies with an ack. The server logs and stores the ack by platform id and generated prefab id.

This mirror path is deliberately only the "same prefab exists locally, clone it locally" path. It does not handle item
data, localization, spawnable names, inheritance, metadata, conversion-state expansion, or generated snapshot data. Stop
the proof if Blood Rage cannot be mirrored from `PrefabCollectionSystem` at the ready point or if a client-visible case
requires snapshot machinery before this simple mirror receipt is stable.

## Cleanup Proof Gate

`CustomPrefabCleanupSystem` is registered before the existing server observer systems. On its first update it:

- loads the Emberglass manifest;
- queries entities with `PrefabGUID` using `EntityQueryOptions.IncludeDisabled`;
- compares observed prefab ids against the active registry;
- disables each orphan entity before applying its cleanup policy;
- destroys entities with `cleanupPolicy=Destroy`;
- logs one receipt per orphan generated prefab id.

This is not enough, by itself, to claim save safety. Manual runtime acceptance comes after the Blood Rage mirror proof
and must prove that the first cleanup update runs after save load but before dangerous game systems process orphaned
custom-prefab entities. If normal `UpdateGroup` injection is too late, stop and design an earlier bootstrap hook instead
of force-moving systems through WIP update-list manipulation.

## Network Snapshot Research

Client-visible or weird prefab attempts may require new network snapshots or client replacement data before they are
safe. Treat that as a later rung. The first VRA discovery pass should start from
`VampireReferenceAssemblies 1.1.12-r99041-b2`, especially:

- `ProjectM.GeneratedNetCode.dll`
- `ProjectM.dll`
- `ProjectM.Shared.dll`
- `Unity.Entities.dll`
- `Stunlock.Network.dll`

Do not promote broad client-synced custom prefabs until the snapshot/replacement path has its own receipt. The Blood Rage
mirror only proves deterministic client-side manufacture for a source prefab already present in the client world.

## Manual Acceptance

1. Register the Blood Rage proof definition with Emberglass active, `clientSyncRequired=true`, and log the generated
   prefab id.
2. Connect a client with Emberglass present and confirm the server logs a successful mirror ack for that platform id and
   generated prefab id.
3. Spawn and persist at least one entity using that generated prefab id.
4. Restart with the provider absent but Emberglass present.
5. Confirm cleanup logs the orphan receipt and destroys the entity before normal game systems process it.
6. Stop if the client cannot mirror before visual verification, if detection requires provider-owned component types, if
   the entity survives into dangerous processing, or if
   proving timing requires broad harness/control-plane changes.
