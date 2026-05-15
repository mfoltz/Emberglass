# SystemBase Foundation Posture

`VSystemBase` is experimental in the networking beta. Its current job is to make the basic V Rising ECS lifecycle
boring and repeatable, not to become a general ECS framework.

## Current Contract

The current evidence source is Bloodcraft's `ServantUpgradeSystem`: create the query during `OnCreate`, call
`RequireForUpdate`, refresh handles and lookups immediately before processing, drain a typed intent or event, scan
chunks or entities with deterministic temporary allocation disposal, and emit receipt/log evidence for what happened.

Emberglass keeps that shape in two layers:

- direct `SystemBase` systems may still be appropriate for one-off observer proofs such as
  `PlayerCharacterPresenceObserverSystem`;
- `VSystemBase<TWork>` is the candidate reusable shape for systems that benefit from declared queries, refreshed
  handles/lookups, scoped chunk iteration, and testable work objects.

`VSystemBuilder.ChunkJob<TJob>` and `IChunkJob` are the first explicit chunk-scan contract. They are synchronous and
intended for simple Bloodcraft-style scans. Command buffers, deferred mutation queues, broad scheduling helpers, and
custom ECS abstractions remain outside the current promise.

## Refresh Contract

Handles and lookups declared through `VSystemBuilder` are refreshed by the base before custom refresh callbacks run.
The same refreshed values are then available to `Work.OnUpdate` and to planned jobs. If a planned job declares a handle
or lookup field that was not registered in `Configure`, Emberglass fails with an explicit declaration message instead of
silently running the job with a default handle.

The supported declaration set is:

- `EntityTypeHandle()`;
- `EntityStorageInfoLookup()`;
- `ComponentTypeHandle<T>(readOnly)`;
- `BufferTypeHandle<T>(readOnly)`;
- `ComponentLookup<T>(readOnly)`;
- `BufferLookup<T>(readOnly)`.

Chunk jobs should remain limited to synchronous query scans until one runtime proof exercises the final shape. Observer
systems should stay semantically unchanged unless converting them to `VSystemBase<TWork>` removes real duplication.

`PlayerCharacterPresenceObserverSystem` is the best local observer candidate for a future conversion, but only if the
change preserves the current attach/detach semantics and the existing manual proof pattern.

## Parked Resource Naming Note

Prefab, sequence, and localization naming should stay a separate provenance-backed design note. Emberglass already
contains generated `PrefabGUIDs` and `SequenceGUIDs` plus client localization helpers, but broader generic modder
resource naming should be promoted only from source-backed evidence and consumer usage. Do not make resource naming the
center of a SystemBase pass.
