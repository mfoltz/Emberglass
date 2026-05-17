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

VShare is opt-in. The server responds to a request with transfer offers for each eligible mod. The client sees the
incoming offer(s) and can accept or decline each one. Transfers only proceed after acceptance, and declines are recorded
so clients stay in control of what gets downloaded.

## Share Metadata

Shared clientbound mods require `BepInEx/config/Emberglass/ShareMetadata.json` metadata. A mod can be offered to clients
when it is marked with `ClientSafe: true` or includes the `client` tag/category. Runtime loading is a separate opt-in:
only entries that are also marked `HotloadAllowed: true` are offered with hotload enabled.

Example:

```json
{
  "Plugins": {
    "Eclipse": {
      "GitHubRepo": "mfoltz/Eclipse",
      "GitHubTag": "v1.3.14-pre",
      "ClientSafe": true,
      "HotloadAllowed": true,
      "Tags": ["client"],
      "Categories": []
    }
  }
}
```

## Supported File Types and GitHub Release Naming

* Supported file types: `.dll` and `.zip`.
* GitHub release auto-resolution uses the `Owner_Repo_Tag.dll` naming convention (or `.zip`).
* `Owner` and `Repo` are always the first two underscore-separated segments; the remaining segments are the `Tag`.
* If an `Owner`, `Repo`, or `Tag` segment needs underscores, escape them by doubling: `Owner__Repo__Tag`.

Examples:

* `Author_Mod_v1.2.3.dll` → `Author/Mod` at tag `v1.2.3`.
* `Author__Name_Mod__With__Underscores_v1_2_3.zip` → `Author_Name/Mod_With_Underscores` at tag `v1_2_3`.

## Automatic Staging Folder Creation

When VShare initializes on the server, it creates the `LocalMods` and `Server` staging folders automatically. This
means administrators can drop files into those locations without pre-creating the directories.
