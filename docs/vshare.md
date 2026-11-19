# VShare Guide

## Purpose and Pipeline Role

VShare is Emberglass's shared-mod delivery system. It powers the "Local Server Mod Pipeline" by letting a server operator
stage approved mod packages and then offer them to clients at runtime, instead of requiring manual downloads.
The pipeline keeps server-local tools and clientbound packages separate while still allowing players to opt in to
downloads from the server they joined.

## Directory Layout

Emberglass uses these staging locations under `BepInEx/config`:

* `LocalMods` - Server-local mods that are hotloaded or used only on the server.
* `Server` - Clientbound packages that may be offered to connected clients.
* `ServerConfigs/<PluginKey>` - Optional companion config files for a matching shared mod.

The server creates the staging roots automatically. Operators create the per-plugin folders under `ServerConfigs`.

## Requesting Shared Mods (Client)

Clients initiate a request from the in-game options menu. On the **Shared Mods** row, selecting **Download**
sends a request to the server to offer any server-staged mods that the client does not yet have. The click also acts as
consent for catalog-listed shared-mod offers returned immediately after the request, so the request flow stays one-click
for mods advertised in the server details.

The same button's hover/details text is populated from the server after the authenticated VNetwork handshake. It lists
the current server-advertised shared mods with the mod name, GitHub source, release tag, staged file name, file type, and
runtime-load status. When a staged mod has companion configs, the details text shows the config count for that mod.
Before the server catalog arrives, the details text reports that it is waiting for server details. If the server
advertises no shared mods, it reports that no server-shared mods are advertised by this server.

## Consent and Offer Flow

VShare is opt-in. Selecting **Download** is the client consent action for shared clientbound mods. The
server responds with transfer offers for each eligible missing mod, and offers received shortly after that request are
accepted automatically only when they match the latest server-advertised catalog, so the initial flow stays one-button
from the player's perspective while still being bounded by the handshake details.

Accepted mod transfers show start, progress, and outcome notices in the downloading client's chatbox. Companion configs
are summarized in the mod's outcome. **Ready** means runtime loading succeeded; **Installed** means the files are on disk
without a runtime load. Detailed transfer and verification logs remain in the console.

## Companion Config Files

Shared mod metadata can declare config files that should travel with that mod:

```json
{
  "Plugins": {
    "Eclipse": {
      "GitHubRepo": "mfoltz/Eclipse",
      "GitHubTag": "v1.3.14-pre",
      "GitHubAssetName": "Eclipse.dll",
      "ClientSafe": true,
      "HotloadAllowed": true,
      "Configs": [
        {
          "Source": "io.zfolmt.Eclipse.cfg",
          "Target": "io.zfolmt.Eclipse.cfg"
        }
      ],
      "Tags": ["client"],
      "Categories": []
    }
  }
}
```

`Source` is relative to `BepInEx/config/ServerConfigs/<PluginKey>`. `Target` is relative to the client's
`BepInEx/config` folder. Both paths may name nested files, but must be relative and fit the packet's UTF-8 path fields.
Emberglass rejects traversal, invalid filenames and Windows aliases, missing staged sources, directories in place of files,
and symlinks, junctions, or other reparse points along the resolved path. `BepInEx/config/Emberglass` and names starting
with `.vshare-config-` are reserved from companion targets.

Companion configs are sent only after the client verifies and installs the matching shared mod on disk. They are not
offered by themselves, so a client that already has the mod does not receive config-only VShare traffic. For a
hotload-enabled mod, VShare waits for every companion to be applied or intentionally skipped before loading the
plugin. If the config phase fails, disconnects, or times out, the mod stays on disk but is not hotloaded. A later
hotload failure does not undo file installs. The companion protocol is beta; use matching Emberglass builds on both ends.

On the client, VShare writes companion configs into the normal BepInEx config tree. If the target file does not exist,
VShare writes it and records the file in `BepInEx/config/Emberglass/VShareManagedConfigs.json`. If the target already
exists, VShare overwrites it only when that manifest proves the file was previously written by VShare and the current
file hash still matches the recorded hash. User-edited or unmanaged local configs are skipped with a log receipt.
VShare stages the config and manifest separately and publishes the manifest last. A damaged or unreadable manifest
does not grant overwrite permission. If the manifest cannot be published, VShare attempts to restore the previous file
without touching any newer local change. A recovery backup is retained if that restore cannot finish. The two files
are not updated as one transaction: if interrupted between writes, any unrecorded config is treated as local and kept.
During successful hotload, VShare can refresh that hash for a just-applied `<plugin-guid>.cfg` if BepInEx generates
its config header and only comments or blank lines changed. This does not adopt existing files or relax the exact
hash check for later edits.

## Transfer Throttling

VShare transfer work is throttled globally so server and client frames stay gentle while files are sent, received,
verified, written, and hotloaded. The defaults favor runtime safety over raw throughput:

* `VShare.TransferWorkBudgetMs = 2` - per-frame transfer work time budget, clamped from `1` to `10`.
* `VShare.MaxTransferWorkStepsPerFrame = 8` - per-frame transfer work step cap across all transfers, clamped from `1`
  to `64`.
* `VShare.MaxActiveOutgoingTransfers = 2` - active outgoing transfer limit, clamped from `1` to `8`.

Accepted transfers beyond the active limit wait in FIFO order. The transfer work queue still interleaves work across
active transfers so busy servers can favor predictable frame cost over bursty downloads.

## Share Metadata

Shared clientbound mods require `BepInEx/config/Emberglass/ShareMetadata.json` metadata. A mod can be offered to clients
when it is marked with `ClientSafe: true` or includes the `client` tag/category. Runtime loading is a separate opt-in:
only DLL entries that are also marked `HotloadAllowed: true` are offered with hotload enabled. Metadata is also the
preferred place to declare GitHub release provenance. Entries may be keyed by the staged file base name or by the
GitHub repo/plugin name parsed from release-style staged assets. Plain
GitHub release asset names such as `Eclipse.dll` may also be staged directly when their metadata entry supplies
`GitHubRepo` and `GitHubTag`; digest checks still use the actual staged asset filename.

Example:

```json
{
  "Plugins": {
    "Eclipse": {
      "GitHubRepo": "mfoltz/Eclipse",
      "GitHubTag": "v1.3.14-pre",
      "GitHubAssetName": "Eclipse.dll",
      "ClientSafe": true,
      "HotloadAllowed": true,
      "LocalSha256": "",
      "Tags": ["client"],
      "Categories": []
    }
  }
}
```

`GitHubAssetName` is optional. When it is omitted, Emberglass uses the staged file name as the GitHub Release asset
name. This lets operators stage a friendly local file name while still checking the exact release asset.

## Supported File Types and GitHub Release Naming

* Supported file types: `.dll` and `.zip`.
* Explicit `ShareMetadata.json` `GitHubRepo` and `GitHubTag` values are preferred for GitHub release identity.
* GitHub release auto-resolution still supports the `Owner_Repo_Tag.dll` naming convention (or `.zip`) as a fallback.
* `LocalSha256` remains metadata-compatible for local proof notes, but clientbound VShare sharing requires GitHub release
  asset digest verification.
* `Owner` and `Repo` are always the first two underscore-separated segments; the remaining segments are the `Tag`.
* If an `Owner`, `Repo`, or `Tag` segment needs underscores, escape them by doubling: `Owner__Repo__Tag`.

Examples:

* `Author_Mod_v1.2.3.dll` → `Author/Mod` at tag `v1.2.3`.
* `Author__Name_Mod__With__Underscores_v1_2_3.zip` → `Author_Name/Mod_With_Underscores` at tag `v1_2_3`.

## Automatic Staging Folder Creation

When VShare initializes on the server, it creates the `LocalMods`, `Server`, and `ServerConfigs` staging roots.
Create a plugin's subfolder inside `ServerConfigs` when staging its companion files.

## Provenance Ladder

VShare treats release provenance as an integrity check for staged clientbound files. It does not prove that a mod is
safe, that its dependencies are safe, or that the release author should be trusted.

* Level 0, convention only: release docs and naming conventions are useful, but not enough for clientbound sharing.
* Level 1, required digest check: before offering or transferring a clientbound staged mod, Emberglass resolves the
  GitHub repo, tag, and asset name, reads the GitHub Release SHA-256 digest, and compares it to the local staged bytes.
  Missing identity, missing digest metadata, invalid digest metadata, or a hash mismatch blocks the offer or transfer.
* Level 2, strict local preflight: operators can run `.codex/scripts/vshare-provenance-preflight.ps1` to scan staged
  files and write a JSON receipt under `.codex/runs/vshare-provenance/`.
* Level 3, GitHub Actions attestation: strict preflight can add `-RequireAttestation` to verify GitHub artifact
  attestations with `gh attestation verify`. Emberglass runtime does not depend on `gh`.

Example strict preflight:

```powershell
pwsh .codex/scripts/vshare-provenance-preflight.ps1 `
  -ServerModsPath "BepInEx/config/Server" `
  -ShareMetadataPath "BepInEx/config/Emberglass/ShareMetadata.json" `
  -RequireAttestation
```
