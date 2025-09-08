# Emberglass (WIP)

Emberglass is a modding framework for [V Rising](https://playvrising.com/) built as a BepInEx plugin.

## Project Overview

The project is distributed through [Thunderstore][thunderstore] and provides utilities for:

* Typed packet networking via `VNetwork`; see PingPong under Network/Examples for guidance.
* Runtime plugin sharing of preloaded mods (server operator places them in local folder) from servers to consenting connected clients.
* Supports custom keybinds & menu options with associated actions.

[thunderstore]: https://thunderstore.io/c/v-rising/p/zfolmt/Emberglass/

## Installation

1. Install [BepInEx](https://bepinex.github.io/) for V Rising.
2. Download Emberglass from Thunderstore and unzip contents.
3. Place `Emberglass.dll` in your `BepInEx/plugins` directory.
