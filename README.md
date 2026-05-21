# Emberglass

Emberglass is a networking beta for [V Rising](https://playvrising.com/) mods built as a BepInEx plugin. Its first public promise is `VNetwork`: a typed client/server packet API intended to replace brittle one-off `ChatMessage` bridges between paired mods.

## Project Overview

The beta is being prepared for [Thunderstore][thunderstore] and GitHub Releases and currently focuses on:

* Typed packet networking via `VNetwork`; see [Networking Examples](docs/networking-examples.md) for primitive-first guidance.
* Authenticated handshake setup with a client-side trust-on-first-use pin, plus an advanced explicit server public-key override.
* Runtime plugin sharing of preloaded mods from servers to clients.
* Custom keybind and menu option helpers.

For this beta, `VNetwork` is the headline stable public surface. Other APIs remain useful, but consumers should treat them according to the stability labels in the API inventory instead of assuming the whole assembly has the same release promise.

## Networking Beta

`VNetwork` lets mods register typed packets by direction and send them between client and server without owning a custom chat-message transport. The supported beta surface includes packet registration, send helpers, ready events, and request/response helpers. Prefer the callback request API for Unity, IL2CPP, and ECS-facing code because callbacks are explicitly routed through the main-thread invoker; the `SendRequestAsync` task API remains available for compatibility and completes through the main-thread invoker when one is available.

Emberglass automatically generates and persists a server trust identity and P-256 signing keypair. Clients pin the first seen `(ServerTrustId, public key fingerprint)` when no explicit server key is configured. If a later connection presents a different key for the same trust ID, the handshake is rejected and the log includes a concise trust-remediation marker. Operators who want strict preconfiguration can still set `ServerPublicKeyBase64` manually.

Legacy transport paths exist for compatibility with older consumer builds, but new integrations should prefer `VNetwork`.

See the [API inventory](docs/API.md) for the current public surface and stability labels.
See the [SystemBase foundation posture](docs/systembase.md) for the experimental ECS helper boundary.
See the [Release Flow](docs/release.md) for the Thunderstore/GitHub release posture and metadata gates.
See the [Bloodcraft/Eclipse bridge proof runbook](docs/testing/bloodcraft-eclipse-bridge-proof.md) for the current manual end-to-end networking proof shape.

## Live Settings Snapshots

Use `ConfigSpec<TSettings>` to define config bindings and `LiveSettings<TSettings>` to provide immutable snapshots for gameplay.
Create the live settings instance during plugin initialization and read `Settings.Current` during gameplay; avoid touching
`ConfigEntry<T>.Value` outside the binding pipeline.

```csharp
public override void Load()
{
    var spec = new MySettingsSpec();
    var seedSettings = new MySettings(maxActivePets: 3, sprintMultiplier: 1.0f);

    Settings = new LiveSettings<MySettings>(spec, Config, MainThreadInvoker, seedSettings, "MySettings reload requested");
    Settings.Reloaded += (_, context) =>
        Logger.LogInfo($"MySettings reloaded: {string.Join(\", \", context.ChangedKeys)}");
}

public float GetSprintMultiplierForGameplay()
{
    return Settings.Current.SprintMultiplier;
}
```

## Local Server Mod Pipeline

Emberglass stages server-local mods from the following folders on disk:

* `BepInEx/config/LocalMods` for server-local mods you want to hotload/download.
* `BepInEx/config/Server` for clientbound packages the server shares with connecting clients.

See the [VShare guide](docs/vshare.md) for the full request/consent flow and staging details.

### Supported File Types & Naming

* Supported file types are `.dll` and `.zip`.
* The mod identity is derived from the file name **without** its extension (for example, `MyMod.dll` or `MyMod.zip` → `MyMod`).
* Clientbound share requests only offer the mods staged by the server operator, so only stage mods that are safe to
  load on clients.
* GitHub release identity should be declared in `BepInEx/config/Emberglass/ShareMetadata.json` with `GitHubRepo`,
  `GitHubTag`, and optional `GitHubAssetName`.
* GitHub release identity can still be auto-resolved from staged asset names in `BepInEx/config/Server` using the
  `Owner_Repo_Tag.dll` convention (or `Owner__Repo__Tag.dll` when segments contain underscores) when metadata is absent.

### GitHub Release Auto-Resolution

When a staged asset name follows the `Owner_Repo_Tag` pattern, Emberglass derives the GitHub release identity as:

* `Owner` → GitHub owner (organization or user).
* `Repo` → GitHub repository name.
* `Tag` → GitHub release tag.

The `Owner` and `Repo` segments are always the first two segments in the file name. The `Tag` is the remaining
segments joined with underscores. If you need underscores inside a segment, escape them by doubling the underscore.

Examples:

* `Author_Mod_v1.2.3.dll` → `Author/Mod` at tag `v1.2.3`.
* `Author__Name_Mod__With__Underscores_v1_2_3.dll` → `Author_Name/Mod_With_Underscores` at tag `v1_2_3`.

If the staged asset does not follow the pattern, Emberglass cannot auto-resolve the GitHub release identity.

### GitHub Release Digest Verification

* Before transfer, Emberglass fetches the GitHub Release digest for the staged asset and compares it to the local file hash.
  * If the hash **matches**, the transfer proceeds.
  * If the hash **mismatches**, the transfer is aborted, the cache entry is invalidated, and the user is prompted to re-download.
* Optional strict preflight receipts are available with `.codex/scripts/vshare-provenance-preflight.ps1`; add
  `-RequireAttestation` to verify GitHub artifact attestations before runtime.

### Troubleshooting

* **Unresolved GitHub release identity**: Emberglass cannot determine the GitHub owner, repo, and tag. Add
  `GitHubRepo` and `GitHubTag` to `ShareMetadata.json`, or rename the staged file to match the `Owner_Repo_Tag` pattern.
* **Hash mismatch**: Emberglass aborts the transfer and invalidates the cached entry. Re-download the package from
  the GitHub Release and restage it before trying again.
* **GitHub Release unavailable** (lookup failure or missing digest metadata): Emberglass aborts the transfer and asks you
  to retry later. Wait for GitHub Releases to respond, then retry the download.

[thunderstore]: https://thunderstore.io/c/v-rising/p/zfolmt/Emberglass/

## Installation

1. Install [BepInEx](https://bepinex.github.io/) for V Rising.
2. Download Emberglass from GitHub Releases or Thunderstore once a public package is available, or build from source (see below).
3. Place `Emberglass.dll` in your `BepInEx/plugins` directory.
4. Restart the game or dedicated server so Emberglass can generate or load handshake trust config.

## Developer Workflow

Before running any `dotnet` command, bootstrap the SDK and build dependencies by running the install script. On Windows, run this through Git Bash rather than WSL `bash`:

```bash
bash .codex/install.sh
```

The install script builds `Emberglass` in Release and exposes the .NET SDK for subsequent `dotnet` commands in this workspace.

Typical development commands (run from the repository root) are:

```bash
dotnet restore
dotnet build Emberglass.csproj
dotnet test .codex/tests/Network/Emberglass.Network.Tests.csproj
```

`Emberglass.csproj` declares the restore sources used by the canonical build gate.

The `.codex/tests` suites rely on `Emberglass.TestStubs` to stub Unity/runtime-only assemblies. The `Assembly setup` collection fixture runs `StubAssemblyResolver.Initialize()` once per test run, and you can set `EMBERGLASS_TEST_STUB_LOGGING=1` to enable resolver diagnostics.
