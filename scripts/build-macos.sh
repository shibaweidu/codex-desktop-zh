#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="${1:-$(uname -m)}"
case "$ARCH" in
  arm64) ASSET="Codex-Zh-Launcher-macOS-arm64.zip" ;;
  x86_64|x64) ARCH="x86_64"; ASSET="Codex-Zh-Launcher-macOS-x64.zip" ;;
  *) echo "Unsupported architecture: $ARCH" >&2; exit 2 ;;
esac

STAGE="$ROOT/macos/.artifacts/$ARCH"
APP="$STAGE/Codex 汉化增强工具.app"
CONTENTS="$APP/Contents"
rm -rf "$STAGE"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources/shared" "$ROOT/dist"

cd "$ROOT/macos"
swift build -c release --arch "$ARCH"
BUILD="$(swift build -c release --arch "$ARCH" --show-bin-path)"
cp "$BUILD/CodexZhLauncherMac" "$CONTENTS/MacOS/CodexZhLauncherMac"
cp "$ROOT/shared/i18n-bootstrap.js" "$ROOT/shared/locale-script.js" "$ROOT/shared/menu-script.js" "$ROOT/shared/menu-translations.json" "$CONTENTS/Resources/shared/"

cat > "$CONTENTS/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDevelopmentRegion</key><string>zh_CN</string>
  <key>CFBundleDisplayName</key><string>Codex 汉化增强工具</string>
  <key>CFBundleExecutable</key><string>CodexZhLauncherMac</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleIdentifier</key><string>com.codexzh.launcher</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>Codex 汉化增强工具</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.7.6</string>
  <key>CFBundleVersion</key><string>0.7.6</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

ICONSET="$STAGE/AppIcon.iconset"
swift "$ROOT/scripts/generate-macos-icon.swift" "$ICONSET"
iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/AppIcon.icns"
rm -rf "$ICONSET"

plutil -lint "$CONTENTS/Info.plist"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
test "$(lipo -archs "$CONTENTS/MacOS/CodexZhLauncherMac")" = "$ARCH"
test -x "$CONTENTS/MacOS/CodexZhLauncherMac"
test -f "$CONTENTS/Resources/shared/menu-translations.json"

rm -f "$ROOT/dist/$ASSET"
ditto -c -k --keepParent "$APP" "$ROOT/dist/$ASSET"
TMP_CHECK="$(mktemp -d)"
trap 'rm -rf "$TMP_CHECK"' EXIT
ditto -x -k "$ROOT/dist/$ASSET" "$TMP_CHECK"
test -x "$TMP_CHECK/Codex 汉化增强工具.app/Contents/MacOS/CodexZhLauncherMac"
shasum -a 256 "$ROOT/dist/$ASSET" | sed "s#  .*#  $ASSET#" > "$ROOT/dist/$ASSET.sha256"
echo "Built $ROOT/dist/$ASSET"
