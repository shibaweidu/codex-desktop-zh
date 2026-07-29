#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-$(uname -m)}"
case "$ARCH" in
  x64) ARCH="x86_64"; ASSET="Codex-Zh-Launcher-macOS-x64.zip" ;;
  arm64) ASSET="Codex-Zh-Launcher-macOS-arm64.zip" ;;
  x86_64) ASSET="Codex-Zh-Launcher-macOS-x64.zip" ;;
  *) echo "Unsupported architecture: $ARCH" >&2; exit 2 ;;
esac

HOST_ARCH="$(uname -m)"
if [[ "$HOST_ARCH" != "$ARCH" ]]; then
  echo "Skipping updater integration test for $ARCH on $HOST_ARCH host."
  exit 0
fi

SOURCE_APP="$ROOT/macos/.artifacts/$ARCH/Codex 汉化增强工具.app"
test -d "$SOURCE_APP"
test -f "$ROOT/dist/$ASSET"
UPDATE_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/CodexZhLauncher-update-test.XXXXXX")"
TARGET_PARENT="$(mktemp -d "${TMPDIR:-/tmp}/CodexZhLauncher-target-test.XXXXXX")"
TARGET_PARENT="$(cd "$TARGET_PARENT" && pwd -P)"
TARGET_APP="$TARGET_PARENT/Codex 汉化增强工具.app"
EXTRACTED="$UPDATE_ROOT/extracted"
STAGED_APP="$EXTRACTED/Codex 汉化增强工具.app"
HELPER="$UPDATE_ROOT/CodexZhLauncherUpdater"

cleanup() {
  [[ -d "$UPDATE_ROOT" ]] && rm -rf "$UPDATE_ROOT"
  [[ -d "$TARGET_PARENT" ]] && rm -rf "$TARGET_PARENT"
}
trap cleanup EXIT

ditto "$SOURCE_APP" "$TARGET_APP"
mkdir -p "$EXTRACTED"
ditto -x -k "$ROOT/dist/$ASSET" "$EXTRACTED"
test -d "$STAGED_APP"
cp "$STAGED_APP/Contents/MacOS/CodexZhLauncherMac" "$HELPER"
chmod 755 "$HELPER"

"$HELPER" \
  --apply-update-macos-test \
  999999999 \
  "$TARGET_APP" \
  "$STAGED_APP" \
  "$UPDATE_ROOT" \
  0.7.3

test -d "$TARGET_APP"
test -z "$(find "$TARGET_PARENT" -maxdepth 1 -name '.CodexZhLauncher-update-backup-*' -print -quit)"
codesign --verify --deep --strict "$TARGET_APP"
VERSION="$(plutil -extract CFBundleShortVersionString raw -o - "$TARGET_APP/Contents/Info.plist")"
test "$VERSION" = "0.7.3"
test ! -e "$UPDATE_ROOT"
echo "macos_updater=ok arch=$ARCH"
