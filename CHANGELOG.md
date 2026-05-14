# Changelog

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
