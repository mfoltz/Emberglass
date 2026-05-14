#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
PROJECT_PATH="$REPO_ROOT/Emberglass.csproj"
INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
CHANNEL="${DOTNET_INSTALL_CHANNEL:-6.0}"
SDK_CHANNEL="${DOTNET_SDK_CHANNEL:-8.0}"
BEPINEX_PLUGIN_DIR="${BEPINEX_PLUGIN_DIR:-}"
DOTNET_INSTALLED=0
REQUIRED_RUNTIME="Microsoft.NETCore.App 6.0"

install_dotnet() {
    mkdir -p "$INSTALL_DIR"
    local install_script
    install_script="$(mktemp)"

    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$install_script"

    # Ensure .NET 8 SDK is available (needed for preview features / newer compiler behavior)
    bash "$install_script" --install-dir "$INSTALL_DIR" --channel "$SDK_CHANNEL"
    # Project targets net6.0; ensure the .NET 6 runtime exists too
    bash "$install_script" --install-dir "$INSTALL_DIR" --runtime dotnet --channel "$CHANNEL"

    rm -f "$install_script"

    export DOTNET_ROOT="$INSTALL_DIR"
    export PATH="$INSTALL_DIR:$INSTALL_DIR/tools:$PATH"
    hash -r

    if command -v dotnet >/dev/null 2>&1; then
        echo "Installed .NET SDK $(dotnet --version) to $INSTALL_DIR"
        echo "Add the following to your shell profile to use it outside this script:"
        echo "export DOTNET_ROOT=\"$INSTALL_DIR\""
        echo "export PATH=\"$INSTALL_DIR:$INSTALL_DIR/tools:\$PATH\""
    else
        echo "Installation completed but dotnet is not on PATH. Add $INSTALL_DIR to PATH manually." >&2
        exit 1
    fi
}

if command -v dotnet >/dev/null 2>&1; then
    echo ".NET SDK already installed: $(dotnet --version)"

    SDK_OK=0
    RT_OK=0

    # Ensure an 8.x SDK is present
    if dotnet --list-sdks 2>/dev/null | grep -q "^8\\."; then
        SDK_OK=1
    fi
    # Ensure the net6 runtime exists
    if dotnet --list-runtimes 2>/dev/null | grep -q "^Microsoft.NETCore.App 6\\.0"; then
        RT_OK=1
    fi

    if [ "$SDK_OK" -eq 1 ] && [ "$RT_OK" -eq 1 ]; then
        DOTNET_INSTALLED=1
    else
        echo "Required .NET components missing; installing into $INSTALL_DIR"
        [ "$SDK_OK" -eq 0 ] && echo " - Missing .NET SDK 8.x"
        [ "$RT_OK" -eq 0 ] && echo " - Missing Microsoft.NETCore.App 6.0 runtime"
        install_dotnet
    fi
else
    install_dotnet
fi

# Sanity: make sure the *active* dotnet is SDK 8+ (otherwise preview features won't compile)
DOTNET_MAJOR="$(dotnet --version | cut -d. -f1)"
if [ "${DOTNET_MAJOR:-0}" -lt 8 ]; then
    echo "dotnet on PATH is $(dotnet --version) but .NET 8 SDK is required for preview features." >&2
    echo "If you have a global.json pinning an older SDK, update/remove it, then re-run." >&2
    exit 1
fi

if [ ! -f "$PROJECT_PATH" ]; then
    echo "Project file not found at $PROJECT_PATH" >&2
    exit 1
fi

echo "Restoring..."
dotnet restore "$PROJECT_PATH"

echo "Building..."
dotnet build "$PROJECT_PATH" --configuration Release --no-restore

DLL_PATH="$REPO_ROOT/bin/Release/net6.0/Emberglass.dll"
if [ ! -f "$DLL_PATH" ]; then
    echo "Build failed: $DLL_PATH not found." >&2
    exit 1
fi

echo "Build succeeded: $DLL_PATH"

if [ -n "$BEPINEX_PLUGIN_DIR" ]; then
    if [ ! -d "$BEPINEX_PLUGIN_DIR" ]; then
        echo "BEPINEX_PLUGIN_DIR does not exist: $BEPINEX_PLUGIN_DIR" >&2
        exit 1
    fi

    cp "$DLL_PATH" "$BEPINEX_PLUGIN_DIR"
    echo "Copied $(basename "$DLL_PATH") to $BEPINEX_PLUGIN_DIR"
else
    echo "Set BEPINEX_PLUGIN_DIR to copy the built DLL into your BepInEx plugins directory."
fi

if [ "$DOTNET_INSTALLED" -eq 1 ]; then
    exit 0
fi
