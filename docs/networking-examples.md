# Networking Examples

Emberglass networking examples are organized around primitives. Learn each primitive on its own, then compose them into paired-mod bridges such as the Bloodcraft/Eclipse client-feature flow.

## Primitive Ladder

### Ready Gate

Clients send only after `VNetwork.OnClientReady` fires or `VNetwork.IsReady` is true. Servers can use `VNetwork.OnReady` to react when a client completes the authenticated session.

### Typed Signal

Use `RegisterServerbound<T>`, `RegisterClientbound<T>`, `SendToServer`, and `SendToClient` for small fire-and-forget messages. This is the practical replacement for chat-message signaling.

### Client Registration

After the ready gate, a client can send a registration packet with a feature name and version. The server records the feature support and can send feature-specific packets later.

### Request and Receipt

Use `SendRequest<TRequest, TResponse>` when the client proposes an action and the server owns the decision. The response should include an accepted flag, an authoritative value, and an optional rejection reason.

### Server Push

After registration, the server can send typed state to one client, selected clients, or all clients. This is the clean route for config, progress, capability, or status updates.

### Soft Bridge Migration

Existing paired mods can prefer Emberglass and keep a legacy fallback while adoption settles. Treat fallback as compatibility, not the recommended new path.

## Bloodcraft/Eclipse Reconstruction

The Bloodcraft/Eclipse bridge maps cleanly onto the primitive ladder:

1. Eclipse waits for the ready gate.
2. Eclipse sends client registration.
3. Bloodcraft records the user as supporting the client-side feature.
4. Bloodcraft sends config/progress state through server push packets.
5. Eclipse receives typed state and applies it through its existing data path.
6. Client-originated changes use request/receipt so Bloodcraft remains authoritative.
7. The older `ChatMessage` bridge remains only as a fallback when Emberglass is disabled, missing, or not ready.
