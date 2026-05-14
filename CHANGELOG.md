# Changelog

## v0.1.0

- Public networking beta centered on the stable `VNetwork` API.
- Added typed client/server packet registration, ready events, send helpers, and request/response support.
- Added trust-on-first-use client pinning for server signing keys, with `ServerPublicKeyBase64` retained as a strict advanced override.
- Repaired and expanded repo-local network tests for handshake signatures, trust offers, MAC direction, registry direction, session keys, transfers, event subscriptions, and bootstrap seams.
- Marked `VEvents` and `VSystemBase` as experimental while their runtime proof and test coverage mature.
- Prepared Thunderstore package metadata and root package artifacts for pre-publish inspection.
