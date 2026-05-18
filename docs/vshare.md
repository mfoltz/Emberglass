# VShare Guide

## Purpose and Pipeline Role

VShare is Emberglass's shared-mod delivery system. It powers the "Local Server Mod Pipeline" by letting a server operator
stage approved mod packages and then offer them to clients at runtime, instead of requiring manual downloads.
The pipeline keeps server-local tools and clientbound packages separate while still allowing players to opt in to
downloads from the server they joined.

## Directory Layout

Emberglass expects two staging folders under `BepInEx/config`:

* `LocalMods` - Server-local mods that are hotloaded or used only on the server.
* `Server` - Clientbound packages that may be offered to connected clients.

These folders are created automatically when the server initializes the VShare system.

## Requesting Shared Mods (Client)

Clients initiate a request from the in-game options menu. Emberglass adds the menu button via
`OptionsManager.AddButton` in `Patches/Shared/OnInitialize.cs` with the label **"Request Shared Mods"**. Selecting it
sends a request to the server to list any server-staged mods that the client does not yet have.

## Consent and Offer Flow

VShare is opt-in. Selecting **"Request Shared Mods"** is the client consent action for shared clientbound mods. The
server responds with transfer offers for each eligible missing mod, and offers received shortly after that request are
accepted automatically so the initial flow stays one-button from the player's perspective.

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
only DLL entries that are also marked `HotloadAllowed: true` are offered with hotload enabled. Metadata may be keyed by
the staged release asset base name or by the GitHub repo/plugin name parsed from release-style staged assets.

Example:

```json
{
  "Plugins": {
    "Eclipse": {
      "GitHubRepo": "mfoltz/Eclipse",
      "GitHubTag": "v1.3.14-pre",
      "ClientSafe": true,
      "HotloadAllowed": true,
      "LocalSha256": "",
      "Tags": ["client"],
      "Categories": []
    }
  }
}
```

## Supported File Types and GitHub Release Naming

* Supported file types: `.dll` and `.zip`.
* GitHub release auto-resolution uses the `Owner_Repo_Tag.dll` naming convention (or `.zip`).
* Local development proofs can set `LocalSha256` to the exact SHA-256 of the staged local file. This keeps byte-level
  verification enabled without requiring a temporary GitHub prerelease for every rebuilt DLL.
* `Owner` and `Repo` are always the first two underscore-separated segments; the remaining segments are the `Tag`.
* If an `Owner`, `Repo`, or `Tag` segment needs underscores, escape them by doubling: `Owner__Repo__Tag`.

Examples:

* `Author_Mod_v1.2.3.dll` → `Author/Mod` at tag `v1.2.3`.
* `Author__Name_Mod__With__Underscores_v1_2_3.zip` → `Author_Name/Mod_With_Underscores` at tag `v1_2_3`.

## Automatic Staging Folder Creation

When VShare initializes on the server, it creates the `LocalMods` and `Server` staging folders automatically. This
means administrators can drop files into those locations without pre-creating the directories.
