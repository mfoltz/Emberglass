# PacketRelay Handshake Validation

## Goal
Validate that the authenticated ECDH handshake rejects MITM tampering, derives distinct session keys per session/user, and preserves the client trust pin across reconnects.

## Preconditions
- Build and run the server and client with the updated handshake code.
- Use a clean client trust config for the first TOFU pin test, or record the existing pin before starting.
- Keep `ServerPublicKeyBase64` empty for TOFU validation. Set it only when validating the strict explicit-key override path.

## Validation Steps
1. **Baseline handshake succeeds**
   - Connect a single client to the server.
   - Confirm the server logs its trust ID and public-key fingerprint.
   - Confirm the client either pins the first seen server key or accepts the existing pin.
   - Confirm the handshake completes and packets are accepted in both directions.

2. **Pinned trust mismatch fails**
   - Change the server signing key while keeping the same `ServerTrustId`.
   - Connect the client with the existing pin.
   - Expected result: client rejects the trust offer, no session key is established, and the log reports a concise trust mismatch marker without printing keys or payloads.

3. **Strict explicit-key override succeeds**
   - Configure the client `ServerPublicKeyBase64` with the server public key.
   - Connect to the server.
   - Expected result: client accepts the explicit key and completes the authenticated handshake without writing a new TOFU pin.

4. **MITM tampering fails (public key swap)**
   - Intercept the `KeyExchange` payload from server to client.
   - Replace the server public key bytes with a random key while keeping the nonce unchanged.
   - Expected result: client should reject the server hello signature and the handshake should not complete.

5. **MITM tampering fails (response signature)**
   - Intercept the client `KeyExchange` response and alter any byte in the signature field.
   - Expected result: server should reject the handshake and no session key is established.

6. **Per-session keys differ**
   - Connect the same client twice, capturing the derived session key hash (add temporary logging if needed).
   - Expected result: keys differ between sessions because the handshake nonce changes.

7. **Per-user keys differ**
   - Connect two different clients concurrently.
   - Compare the derived session key hashes for each client.
   - Expected result: keys differ across users because the key derivation mixes the per-session nonce and each client’s public key.

## Notes
- If legacy handshake compatibility is enabled, ensure both legacy and authenticated paths are exercised and confirm authenticated sessions still enforce MAC verification.
