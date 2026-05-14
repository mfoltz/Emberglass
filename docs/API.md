# API Inventory

This document tracks the public API surface of Emberglass. Each entry is labeled as **Stable**, **Supporting**, **Experimental**, or **Deprecated**. For the networking beta, `VNetwork` is the primary public promise; other APIs are listed so consumers can make explicit stability decisions instead of treating the whole assembly as equally mature.

Status meanings:

- **Stable**: part of the networking beta promise.
- **Supporting**: usable public API that existing Emberglass features rely on, but not the headline beta promise.
- **Experimental**: available for early validation, but still subject to shape changes.
- **Deprecated**: retained for compatibility only.

## Emberglass.API.Shared

| API | Status | Notes |
| --- | --- | --- |
| `VNetwork` | Stable | Typed packet registration, send helpers, ready events, and request/response helpers for the networking beta. Prefer callback request helpers for Unity/IL2CPP/ECS-facing code; `SendRequestAsync` remains available for compatibility and completes through the main-thread invoker when one is available. |
| `VWorld` | Supporting | World/context utilities and shared state. |
| `VEvents` | Experimental | Semantic runtime event helpers (`VEvents.IGameEvent`, `VEvents.GameEvent<T>`, `VEvents.ModuleRegistry`). Keep scoped to runtime facts while the subscription surface and observer proofs harden. |
| `VBehaviour` | Supporting | Shared MonoBehaviour base. |
| `VExtensions` | Supporting | Entity/component convenience extensions. |
| `IExtensions` | Supporting | Entity/component interface extensions. |
| `IMainThreadInvoker` | Supporting | Main-thread scheduling contract. |
| `MainThreadInvoker` | Supporting | Main-thread scheduling implementation. |
| `UnmanagedDetourManager` | Experimental | Unmanaged system detour helpers (`DetourHandle`, `UnmanagedPrefix`, `UnmanagedPostfix`). Keep experimental until a public runtime proof and docs pass cover the intended support boundary. |

## Emberglass.API.Shared.Config

| API | Status | Notes |
| --- | --- | --- |
| `ConfigSpec<TSettings>` | Supporting | Config binding registration. |
| `LiveSettings<TSettings>` | Supporting | Immutable live settings snapshots. |
| `ReloadContext<TSettings>` | Supporting | Reload context data passed to observers. |
| `IBinding<TSettings>` | Supporting | Config binding abstraction. |
| `Binding<TSettings, TValue>` | Supporting | Config binding implementation. |
| `ConfigScope` | Supporting | Declares whether a binding is Client, Server, or Shared. |
| `ReloadPolicy` | Supporting | Declares when menu-driven changes request reloads (None, OnChange, Manual). |
| `ServerConfigChangeRequest<TValue>` | Supporting | Client request payload for server-scoped config updates. |
| `ServerConfigChangeResponse<TValue>` | Supporting | Server response payload for config updates (authoritative value + rejection reason). |
| `ServerConfigChangeValidation<TValue>` | Supporting | Validation result for server-scoped config updates. |
| `ServerConfigChangeHandlers` | Supporting | Server-side registration of config change handlers. |
| `MySettings` | Experimental | Demo-only sample settings model. |
| `MySettingsSpec` | Experimental | Demo-only binding spec for `MySettings`. |
| `ConfigDemo` | Experimental | Demo-only wiring example for live settings. |

`Binding<TSettings, TValue>` now includes `ConfigScope` and `ReloadPolicy` metadata. `MenuOptionBindings` honors
`ReloadPolicy.OnChange` by requesting reloads only when a menu option actually changes its value; `ReloadPolicy.None`
and `ReloadPolicy.Manual` suppress automatic reloads so callers can opt out or trigger reloads explicitly.

Server-scoped bindings (`ConfigScope.Server`) now use the main-thread callback request/response flow when driven by menu options:

1. Client menu changes send `ServerConfigChangeRequest<TValue>` to the server.
2. The server must register the binding with `ServerConfigChangeHandlers.RegisterBinding` (handled automatically
   when `ConfigSpec.Bind` runs on the server) to validate and apply the change.
3. The server responds with `ServerConfigChangeResponse<TValue>`, including the authoritative value and an optional
   rejection reason.
4. The client applies the authoritative value when the response arrives and triggers reloads when the binding's
   reload policy requires it.

Server-side handlers must be in place for each server-scoped binding, or change requests will be rejected.

## Emberglass.API.Client

| API | Status | Notes |
| --- | --- | --- |
| `KeybindManager` | Supporting | Keybind registration and persistence plus UI-driven keybind menu entries (`IKeybindMenuEntry`). Menu entries are visual only; persisted data remains the actual keybind values. |
| `Keybinding` | Supporting | Keybind model. Persistence only covers keybind values; menu entries are UI-only (`IKeybindMenuEntry`). |
| `IKeybindMenuEntry` | Supporting | UI-facing keybind menu entry descriptor. Menu entries are visual only and do not change persisted keybind values. |
| `MenuOptionBindings` | Supporting | Settings-to-menu binding helpers; reloads fire only when values change and the reload policy is `OnChange`. |
| `MenuOption` | Supporting | Base menu option types (`MenuOption<T>`, `Toggle`, `Slider`, `Dropdown`) with control scope and reload metadata for UI labeling. |
| `MenuOptionControlScope` | Supporting | Declares whether a menu option is client-only (live) or server-controlled. |
| `OptionsManager` | Supporting | Options registration (`IMenuEntry`). Menu entries can include UI-only entries (for example, dividers), while persistence only covers registered `MenuOption` values. |
| `LocalizationKeyManager` | Supporting | Localization key registration. |
| `ClientModules` | Supporting | Client event modules (`ConnectionModules.ClientHandshakeModule`). |

Menu options can now surface status labels in their descriptions. Set `MenuOption.ControlScope` to
`ClientOnlyLive` or `ServerControlled` and `MenuOption.RequiresReload` to indicate reload requirements.
`MenuOptionBindings` populates these values automatically based on binding scope and reload policy, and
the UI appends localized labels such as "Client-only (live)", "Server-controlled (requires server approval)",
and "Reload required" when applicable.

## Emberglass.API.Server

| API | Status | Notes |
| --- | --- | --- |
| `Players` | Supporting | Online player helpers. |
| `ServerModules.PlayerPresenceModules` | Experimental | Server-side player character presence events (`PlayerCharacterAttached`, `PlayerCharacterDetached`) emitted by the first injected observer proof. |
| `ServerModules` | Supporting | Server event modules (`ConnectionModules.UserConnected`, `UserDisconnected`, `UserCreated`, `UserKicked`). |

## Emberglass.Systems

| API | Status | Notes |
| --- | --- | --- |
| `VSystemBase` | Experimental | Non-generic ECS system helper plus `InstallWork<TWork>` bridge for query/handle setup. Keep ChunkJob and native-resource planning experimental until a runtime proof promotes the surface. |
