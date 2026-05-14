# Bloodcraft/Eclipse Emberglass Bridge Proof

This runbook documents the receipt pattern for the manual enabled-path proof that Bloodcraft and Eclipse can use the Emberglass `VNetwork` bridge for their legacy signed `ChatMessage` payloads.

The proof is a runtime acceptance pass, not a migration plan. It should classify the bridge floor and stop on log evidence if startup, trust bootstrap, client connection, registration, or config/progress delivery fails.

## Scope

Use this proof when a change touches any of these surfaces:

- `VNetwork` registration, dispatch, ready events, request/response, or packet MAC behavior.
- Authenticated handshake, TOFU trust bootstrap, explicit server-key override, or trust mismatch handling.
- The Bloodcraft/Eclipse opt-in Emberglass bridge adapter.
- Harness staging for Emberglass plus Bloodcraft/Eclipse bridge validation.

Do not use this proof to validate HUD, tooltip, VCF/community notes, control-plane behavior, gameplay-state migration, or broader consumer migration work.

## Preconditions

- Build Emberglass through the canonical gate before staging a DLL:

```powershell
bash .codex/install.sh
```

- Stage the rebuilt `Emberglass.dll` to the V Rising client plugin folder and verify the source and staged hashes match.
- Keep Bloodcraft and Eclipse bridge flags enabled only for the enabled-path proof:
  - Bloodcraft: `General.UseEmberglassEclipseBridge = true`
  - Eclipse: `UIOptions.UseEmberglassBridge = true`
- For the default public bootstrap path, keep the client `ServerPublicKeyBase64` empty before starting the client. Use an explicit key only when validating the strict override path.
- The handshake trust file is `BepInEx/config/Emberglass/HandshakeSignature.json`. For a first-pin TOFU proof, clear `ServerPublicKeyBase64` and remove the relevant `TrustedServers` pin, or move the file aside and let Emberglass recreate it.
- Restart the V Rising client after staging a new `Emberglass.dll` or changing trust/key config.

## Server Harness Flow

Run the Bloodcraft wrapper from the Bloodcraft repo root. The proof profile lives in its own config file, so always pass `-ConfigPath`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .codex\run-harness.ps1 -Action start -Profile bloodcraft-emberglass-bridge-proof -ConfigPath .codex\emberglass-bridge-proof.harness.settings.json
powershell -NoProfile -ExecutionPolicy Bypass -File .codex\run-harness.ps1 -Action wait-ready -Profile bloodcraft-emberglass-bridge-proof -ConfigPath .codex\emberglass-bridge-proof.harness.settings.json
```

After `wait-ready` succeeds, connect the V Rising client to `127.0.0.1:28015` with LAN mode enabled. Create or load a character, remain connected long enough for Eclipse to receive config/progress packets, then disconnect.

Collect and stop after the manual pass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .codex\run-harness.ps1 -Action collect -Profile bloodcraft-emberglass-bridge-proof -ConfigPath .codex\emberglass-bridge-proof.harness.settings.json
powershell -NoProfile -ExecutionPolicy Bypass -File .codex\run-harness.ps1 -Action stop -Profile bloodcraft-emberglass-bridge-proof -ConfigPath .codex\emberglass-bridge-proof.harness.settings.json
```

Summarize the run from Bloodcraft with its repo-local harness summary helper when available. The public Emberglass repo should record the acceptance markers and classification, while raw receipt paths stay with the consumer harness run artifacts.

## Accepted Receipt Pattern

The harness receipt should show:

- `preflight:ok`
- `build:ok`
- `deploy:ok`
- `start:ok`
- `wait-ready:ok`
- `collect:ok`
- `stop:ok`

The captured server BepInEx log should show:

- `[VNetwork.Trust] server trust ready`
- `[VNetwork.Handshake] server received handshake start`
- `[VNetwork.Handshake] server sent key exchange offer`
- `[VNetwork.Handshake] server accepted key exchange`
- `[EclipseBridge:Emberglass] registered`
- `[EclipseBridge:Emberglass] registration received`
- `[EclipseBridge:Emberglass] configs sent`
- `[EclipseBridge:Emberglass] progress sent`
- no `MacMismatch` or MAC failure marker

The client BepInEx log should show one of the trust accept paths:

- TOFU path: `[VNetwork.Trust] client pinned server key` or `[VNetwork.Trust] client accepted pinned server key`
- Explicit-key path: `[VNetwork.Trust] client accepted explicit server key`

The client log should also show:

- `[VNetwork.Handshake] client session key established`
- `[EclipseBridge:Emberglass] registered`
- `[EclipseBridge:Emberglass] registration queued`
- `[EclipseBridge:Emberglass] client ready`
- `[EclipseBridge:Emberglass] registration sent`
- `[EclipseBridge:Emberglass] configs received`
- `[EclipseBridge:Emberglass] progress received`
- no `MacMismatch` or MAC failure marker

## Classification Stops

Stop and classify instead of pushing farther if any of these occur:

- The harness does not reach `wait-ready:ok`.
- The client cannot connect or spawn.
- The client rejects the trust offer.
- No session key is established.
- Bloodcraft does not receive the Eclipse registration.
- Eclipse does not receive configs or progress after registration.
- Any `MacMismatch`, `failed to verify ... MAC`, trust mismatch, startup failure, or packet truncation marker appears.

When stopping, collect the harness artifacts if the server started, stop the server, and summarize the receipt path plus the first missing or failing marker. Do not continue into HUD, tooltip, client UI, VCF/community, control-plane, or shared-harness redesign work.

## Current Accepted Evidence

Three enabled-path proofs completed on 2026-05-13 before the networking beta branch was split into the public Emberglass repo:

- Explicit-key override path: green for authenticated bridge registration plus config/progress delivery.
- Fresh TOFU path: green for first-pin trust bootstrap, authenticated bridge registration, and config/progress delivery.
- Pinned-key reconnect path: green for pinned-key trust acceptance, authenticated session key establishment, Bloodcraft/Eclipse bridge registration, configs delivery, progress delivery, and clean harness collect/stop with no MAC/trust/startup failure marker.

The detailed receipt and raw log artifacts remain in the consumer-owned harness runs. Public release readiness should be revalidated from the exact Emberglass release commit and release DLL before publication.
