#!/usr/bin/env bash
# Linux/cloud development setup (Ubuntu 24.04). Windows development keeps using tools/bootstrap.ps1.
#
# Installs: .NET 8 SDK (Ubuntu archive), Godot 4.5.1 .NET for Linux, Xvfb + Mesa software Vulkan
# (lavapipe) so Forward+ scenes can render screenshots without a GPU. Idempotent.
#
#   bash tools/setup-cloud.sh          # install everything
#   godot --headless --import --path . # first import after setup
#
# Why these choices: dot.net / builds.dotnet.microsoft.com are blocked by the cloud egress proxy, but the
# Ubuntu archive and github.com release downloads are allowed. The Windows NuGet.Config points at
# .tools/godot/<win64>/GodotSharp/Tools/nupkgs; we link that path to the Linux bundle's identical packages.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT_VERSION=4.5.1
GODOT_DIR=/opt/godot/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64
SUDO=""; [ "$(id -u)" -ne 0 ] && SUDO=sudo

if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | grep -q '^8\.'; then
  $SUDO apt-get update -qq
  $SUDO DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-8.0
fi

if [ ! -x "$GODOT_DIR/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64" ]; then
  $SUDO mkdir -p /opt/godot
  tmp=$(mktemp -d)
  curl -sSL -o "$tmp/godot.zip" "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"
  $SUDO unzip -q -o "$tmp/godot.zip" -d /opt/godot
  rm -rf "$tmp"
fi
$SUDO ln -sf "$GODOT_DIR/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64" /usr/local/bin/godot

# Match the Windows NuGet.Config local package source.
NUPKG_LINK="$ROOT/.tools/godot/Godot_v${GODOT_VERSION}-stable_mono_win64/GodotSharp/Tools/nupkgs"
mkdir -p "$(dirname "$NUPKG_LINK")"
[ -e "$NUPKG_LINK" ] || ln -s "$GODOT_DIR/GodotSharp/Tools/nupkgs" "$NUPKG_LINK"

if ! command -v xvfb-run >/dev/null; then
  $SUDO DEBIAN_FRONTEND=noninteractive apt-get install -y -qq xvfb mesa-vulkan-drivers libgl1-mesa-dri time
fi

echo "Setup complete:"
dotnet --list-sdks
godot --version
echo "Render a scene without a GPU:  xvfb-run -a -s '-screen 0 1440x900x24' godot --path . <scene> -- <args>"
