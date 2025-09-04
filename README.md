# Emery

Emery is a modding toolkit for [V Rising](https://playvrising.com/) built as a BepInEx plugin. It supplies networking helpers and shared APIs used by other mods and serves as the foundation for a collection of community mods.

## Project Overview

The project is distributed through [Thunderstore][thunderstore] and provides utilities for:

* Typed packet networking via `VNetwork`; see PingPong under Network/Examples for guidance.
* Runtime plugin management for downloading and loading other mods, handling dependencies, and updates.
* Keybind registration with persistence through `KeybindManager`.
* Options menu integration for toggles, sliders and dropdowns with persistence through `OptionsManager`.

[thunderstore]: https://thunderstore.io/c/v-rising/p/zfolmt/Emery/

## Installation

1. Install [BepInEx](https://bepinex.github.io/) for V Rising.
2. Download Emery from Thunderstore or build from source (see below).
3. Place `Emery.dll` in your `BepInEx/plugins` directory.

## Development Setup

Install dependencies and build the project using the helper scripts:

```pwsh
# Windows (PowerShell)
.codex/install.ps1
./dev_init.ps1
```

```bash
# Linux/macOS or WSL
.codex/install.sh
./dev_init.sh
```

Each script detects the host OS and invokes the appropriate shell to install the .NET SDK and build assets. Building with preview features requires the .NET 8 SDK; verify `dotnet --list-sdks` reports an `8.*` entry.

To verify the build scripts without producing any plugin files, run `./dev_init.sh --dry-run`, which skips building and deployment.


## Contributing


Join the modding community [Discord](https://vrisingmods.com/discord) to help guide framework development!

Before committing, lint any changed YAML files:

```bash
pip install pre-commit
pre-commit run --files path/to/file.yml
```

This executes policy checks and actionlint on GitHub workflows.

The command schema is derived from `Documentation/COMMAND_REFERENCE.md` at runtime during verification, so no schema file needs to be committed.

## Workflow

See `AGENTS.md` and `Documentation/COMMAND_REFERENCE.md` for project workflow and command details.
