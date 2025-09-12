# Emberglass (WIP)

Emberglass is a modding framework for [V Rising](https://playvrising.com/) built as a BepInEx plugin.

## Project Overview

The project is distributed through [Thunderstore][thunderstore] and provides utilities for:

* Allows server & client mods to share information and coordinate logic; just register blittable types with associated actions and Emberglass handles the rest.
* Runtime plugin sharing of preloaded mods (server operator places them in local folder) from servers to consenting clients.
* Supports custom keybinds & menu options with associated actions.

[thunderstore]: https://thunderstore.io/c/v-rising/p/zfolmt/Emberglass/

## Installation

1. Install [BepInEx](https://bepinex.github.io/) for V Rising.
2. Download Emberglass from Thunderstore and unzip contents.
3. Place `Emberglass.dll` in your `BepInEx/plugins` directory.
