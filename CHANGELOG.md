# Changelog

## Unreleased

## v0.3.1

- Added an internal custom-prefab lifecycle proof with deterministic prefab IDs, manifest-backed cleanup identity, early orphan cleanup/registration systems, and client mirror recipe acknowledgements.
- Added the Blood Rage proof clone path with server-side edit-plan support and mirror-gated proof buff application for runtime validation.
- Hardened custom-prefab startup behavior by retrying pending registrations, tolerating malformed manifests, and retrying transient client mirror source-availability failures.

## v0.3.0

- Added a VShare provenance ladder with metadata-first GitHub release identity, required release digest checks for clientbound sharing, strict local preflight receipts, and GitHub Actions artifact attestations for Emberglass DLL release jobs.
- Made VShare transfer logs quieter and easier to follow, with info-level offer/download/load receipts and coarse digest-verified progress milestones.

## v0.2.1

- Added generated GitHub prerelease notes and staged Thunderstore changelog validation for release handoff.

## v0.2.0

- Made the shared-mod request button act as the consent boundary for initial VShare demos, auto-accepting eligible clientbound offers received in the request window.
- Added configurable VShare transfer throttling for per-frame work budget, per-frame work steps, and active outgoing transfer limits.
- Queued accepted outgoing transfers fairly when the active transfer limit is reached, reducing server hitch risk during shared-mod delivery.
- Added local staged-DLL digest support for runtime proof flows without requiring a temporary GitHub prerelease.

## v0.1.6

- Hardened VShare runtime hotload loading with explicit success/failure receipts, already-loaded assembly guards, duplicate plugin GUID detection, and repo-name metadata key fallback.

## v0.1.5

- Documented the experimental SystemBase foundation posture and the parked resource naming boundary.
- Expanded self-contained VSystemBase tests for lookup/handle access intent, refresh ordering, and missing builder declarations.

## v0.1.4

- Changed the release nudge into a default-blocking release hygiene gate, with `-WarnOnly` retained for local draft inspection.

## v0.1.3

- Added low-risk shared extension parity for safe component mutation, buffer reads, entity enable/disable, and player identity helpers.
- Added coroutine delay, byte truthiness, configurable string matching, and Il2Cpp dictionary reversal utility helpers.
- Added a soft release nudge script that warns when meaningful source/API changes should prompt changelog and version-bump consideration.

## v0.1.2

- Added an explicit experimental `VSystemBuilder.ChunkJob<TJob>` entry point for synchronous per-chunk `IChunkJob` scans.
- Added self-contained tests for chunk-job handle metadata binding and related Unity handle stubs.

## v0.1.1

- Hardened main-thread invoker shutdown so request completions and callbacks do not queue onto detached invokers during teardown.
- Made queued main-thread callback draining resilient to individual callback failures.
- Added network tests covering shutdown drain cleanup, stale-invoker request completion, and callback failure continuation.

## v0.1.0

- Public networking beta centered on the stable `VNetwork` API.
- Added typed client/server packet registration, ready events, send helpers, and request/response support.
- Added a main-thread callback request API for Unity, IL2CPP, and ECS-facing code while retaining `SendRequestAsync` for compatibility.
- Routed `SendRequestAsync` task completion through the main-thread invoker when available for safer compatibility with existing async consumers.
- Added trust-on-first-use client pinning for server signing keys, with `ServerPublicKeyBase64` retained as a strict advanced override.
- Repaired and expanded repo-local network tests for handshake signatures, trust offers, MAC direction, registry direction, session keys, transfers, event subscriptions, and bootstrap seams.
- Marked `VEvents` and `VSystemBase` as experimental while their runtime proof and test coverage mature.
- Prepared Thunderstore package metadata and root package artifacts for pre-publish inspection.
