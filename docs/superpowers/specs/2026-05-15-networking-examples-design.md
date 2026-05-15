# Emberglass Networking Examples Design

## Purpose

Emberglass should teach modders how to compose the stable `VNetwork` beta surface into common networking flows. The examples should be primitive-first so Emberglass reads as a reusable networking library, not as a Bloodcraft/Eclipse-specific bridge helper. After the primitives are clear, the docs can reconstruct the Bloodcraft/Eclipse bridge as a consumer story using those same pieces.

The current PingPong example remains useful as a transport sanity check, but it is too small to explain the scenarios Emberglass is meant to replace: brittle `ChatMessage` bridges, readiness races, server authority, optimistic client state, and optional migration paths.

## Goals

- Present a short ladder of networking primitives that consumers can learn independently.
- Use examples that map to real Bloodcraft/Eclipse proof evidence without copying consumer-owned implementation details.
- Keep `VNetwork` as the headline stable beta promise: registration, send helpers, ready events, and request/response.
- Explain compatibility and fallback as adoption patterns, not as a recommendation to keep using legacy transports.
- Make each example small enough to test or reason about without requiring a V Rising gameplay feature.

## Non-Goals

- Do not introduce new public API requirements for the examples.
- Do not migrate Bloodcraft, Eclipse, UI, HUD, tooltip, VCF, RCON, or control-plane behavior in this pass.
- Do not promote experimental surfaces such as `VEvents`, `VSystemBase`, VShare, config/menu helpers, or transfer helpers as part of the stable networking promise.
- Do not require manual trust-key copy as the default example path.
- Do not make Bloodcraft or Eclipse the authority for Emberglass API semantics.

## Primitive Ladder

### 1. Ready Gate

Shows when it is legal to send. Client code should wait for `VNetwork.OnClientReady` or check `VNetwork.IsReady` before sending, while server code can use `VNetwork.OnReady` to react to a client completing the session.

This should replace examples that send packets directly from plugin load or from a constructor. The important lesson is that authenticated networking has a lifecycle.

### 2. Packet

Shows fire-and-forget typed messages in both directions:

- client to server for a small signal
- server to one client for a small update
- server to all clients for a broadcast

The packet example should be the new spiritual replacement for PingPong. It can still be playful, but it should name the practical shape: "typed signal" rather than "chat bridge."

### 3. Registration

Shows a client announcing capability after the ready gate. The payload should include a simple client feature name and version. The server stores only the latest registration for the user and logs a concise receipt.

This teaches the Bloodcraft/Eclipse bridge's first real shape: client-side features need to announce that they are present before the server sends feature-specific data.

### 4. Request and Receipt

Shows client intent plus server authority. The client sends a typed request, the server validates it, and the response includes:

- the original request key or identifier
- whether it was accepted
- an authoritative value
- an optional rejection reason

This should use the callback request API so the example is safe for Unity, IL2CPP, and ECS-facing consumers. The example should also show rollback or no-op behavior when the receipt rejects the change.

### 5. Server Push

Shows server-owned state flowing to clients after registration. The payload should look like a config/progress/state update, but keep the names generic enough that it does not become a Bloodcraft-specific schema.

This is the clearest replacement for manually signed legacy strings over `ChatMessage`: the server can send typed state directly once the client has registered and the secure session is ready.

### 6. Fallback Bridge

Shows an optional Emberglass path with a legacy fallback. The fallback example should demonstrate the decision points:

- Emberglass is missing
- Emberglass is present but not ready
- Emberglass setup fails
- Emberglass send succeeds

The fallback should be framed as a migration aid. New integrations should prefer `VNetwork`, but existing paired mods can adopt softly without stranding older versions.

### 7. Large Snapshot or Transfer

Keep this advanced and separate. Only include it when the relevant transfer or fragmentation surface is documented and tested enough for public guidance. The introductory ladder should not require large payloads.

## Consumer Story Reconstruction

The Bloodcraft/Eclipse bridge can be explained as a composition of primitives:

1. Eclipse waits for the ready gate.
2. Eclipse sends a registration packet with its feature identity.
3. Bloodcraft receives registration and records that the user supports the client-side feature.
4. Bloodcraft sends config/progress updates as server push packets.
5. Eclipse receives typed updates and applies them through its existing data path.
6. Optional client actions use request/receipt so Bloodcraft remains authoritative.
7. Existing `ChatMessage` transport remains a fallback only when Emberglass is disabled, missing, or not ready.

This story should avoid legacy signed message bodies in the public example. It can mention that the first Bloodcraft/Eclipse spike preserved old payload strings for compatibility, but the public examples should show typed DTOs as the desired shape.

## Proposed Example Set

### `ReadyGateExample`

Smallest lifecycle example. Demonstrates subscribing to ready events and checking `IsReady` before send helpers.

### `TypedSignalExample`

Replacement for PingPong. Demonstrates serverbound and clientbound packet registration with simple DTOs.

### `ClientRegistrationExample`

Demonstrates capability registration after readiness and server-side tracking of registered clients.

### `RequestReceiptExample`

Demonstrates client intent, server validation, accepted/rejected response, and client rollback on rejection or callback failure.

### `ServerPushExample`

Demonstrates sending typed state to a registered client and broadcasting a small update to all ready clients.

### `SoftBridgeMigrationExample`

Demonstrates how a paired-mod bridge can prefer Emberglass and fall back to a legacy path without hard failure. This should be docs-heavy and code-light because the reflection-backed adapter details are consumer-owned.

## Testing Strategy

- Unit-test request/receipt behavior where possible, especially callback routing and rollback.
- Keep packet registration, registry direction, MAC direction, session key, and request/response tests in `.codex/tests/Network`.
- Treat live Bloodcraft/Eclipse proof as end-to-end evidence for the composed story, not as a required test for every example edit.
- Do not require a manual client connect for docs-only example changes unless an example changes runtime packet behavior.

## Documentation Placement

- Add a short examples overview under `docs/`.
- Keep runnable or compile-oriented example code under `Network/Examples` only if it does not add runtime risk.
- Cross-link from `README.md` after examples exist.
- Keep `docs/API.md` focused on surface stability labels rather than becoming a tutorial.

## Open Decisions

- Whether the first implementation pass should replace PingPong outright or keep it as `TypedSignalExample`.
- Whether examples should be compile-only source snippets, docs-first snippets, or small test-backed sample classes.
- Whether the fallback bridge example should live in Emberglass docs only or be paired with consumer repo notes after Bloodcraft/Eclipse adoption settles.
