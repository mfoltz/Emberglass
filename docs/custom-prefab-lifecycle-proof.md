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

`CustomPrefabProofBuffApplier` is an opt-in runtime proof hook enabled by `EMBERGLASS_CUSTOM_PREFAB_PROOF_BUFF=1`. It
subscribes to the player-character attach observer and custom-prefab mirror acknowledgements, then applies the generated
Blood Rage buff to the attached character only after the server clone exists and the client mirror ack has succeeded.
This is a validation aid for the first generated-prefab use proof, not a reusable buff API.

The manifest is the save-safety identity. Emberglass intentionally does not rely on provider-owned ECS component types,
because those types disappear when the provider mod is removed. A custom ECS marker is also deferred until component
persistence and IL2CPP registration are proven separately.

## Stunlock-Shaped Design Notes

`C:\Users\mitch\Downloads\CustomPrefab.cs` is evidence for the intended native flow, not an API Emberglass can call or
copy. Its useful design lesson is that custom prefabs are descriptors first and prefab entities second: a builder carries
asset identity, an optional base prefab, target worlds, component edits, optional blob data, and an optional network
snapshot override, then a lifecycle system materializes that descriptor during a narrow game-data readiness window.

Emberglass should rhyme with that shape while staying inside proven managed seams. The near-term internal descriptor can
remain much smaller than Stunlock's builder, but its vocabulary should leave room for the same lifecycle:

- `providerId`
- deterministic asset identity and generated prefab identity
- `basePrefabGuid` as the clone/inheritance source
- `worldTargets` for server, client, or both
- `clientMirrorRequired`
- `cleanupPolicy`
- `snapshotMode=none` until snapshot attachment is proven
- empty `componentEdits` until unmanaged component and buffer mutation is separately proven

The current Blood Rage proof maps to that descriptor as a clone-base recipe: base prefab `PrefabGUID(-491593410)`,
server and client targets, no component edits, no snapshot override, and `cleanupPolicy=Destroy`.

Do not implement raw stable-type-hash component mutation, blob serialization/remap, empty-prefab authoring, or snapshot
override from this evidence alone. Those are future rungs with separate receipts. The next durable code shape should be a
small Emberglass-owned recipe/coordinator around the lifecycle states we can already prove: declared, server
materialized, client mirrored, use-proven, and cleanup-proven.

## Buff Clone Editing Proof

The first KindredAuras-shaped follow-up is reliable buff cloning with ordinary, typed Emberglass component edits. This is
still internal and proof-only, but it is the practical lane for visual buff reuse: clone a known buff so its visual/icon
data survives, then strip or adjust server-side gameplay/stat behavior before the generated prefab is registered.

`CustomPrefabDefinition` now carries the near-term Stunlock-shaped vocabulary without implementing the full native
builder surface:

- `BasePrefabGuid` currently aliases the proven clone source.
- `WorldTargets` defaults to server and client.
- `SnapshotMode` is fixed to `None`.
- `EditPlan` is an optional root-entity, server-side typed edit plan.

`CustomPrefabEditPlan` supports root-entity `Remove<T>`, `RemoveBuffer<T>`, `Edit<T>`, and `EditBuffer<T>` actions. These
edits run after the prefab entity is cloned and assigned its generated `PrefabGUID`, but before the prefab lookup maps
and `RegisterPrefabEvent` are emitted. Missing components are logged as skipped edits instead of failing registration,
so proof recipes can be tried against nearby buffs without making startup brittle.

The Blood Rage proof currently uses `blood-rage-proof-edits` to remove the inherited `ModifyUnitStatBuff_DOTS` buffer on
the server clone. The client mirror still receives the deterministic clone recipe only; client-side edit serialization is
parked until a visual proof shows the client needs edited components to render correctly.

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
3. With `EMBERGLASS_CUSTOM_PREFAB_PROOF_BUFF=1`, confirm the server logs application of the generated buff clone after
   player-character attach and mirror ack.
4. Confirm the server logs the `blood-rage-proof-edits` receipt with applied/skipped counts, and verify the edited clone
   still presents the expected visual/UI evidence.
5. Persist at least one entity using that generated prefab id, or document that the Blood Rage buff use proof is
   non-persistent and add a separate persistence fixture before cleanup testing.
6. Restart with the provider absent but Emberglass present.
7. Confirm cleanup logs the orphan receipt and destroys the entity before normal game systems process it.
8. Stop if the client cannot mirror before visual verification, if detection requires provider-owned component types, if
   the entity survives into dangerous processing, or if
   proving timing requires broad harness/control-plane changes.
