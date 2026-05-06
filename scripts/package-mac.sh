#!/usr/bin/env bash
# Run this from worktree root. Outputs publish/mac/Kasir.app — open in Finder for green-bolt Dock icon.
set -euo pipefail

WORKTREE_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$WORKTREE_ROOT"

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
  RID="osx-arm64"
else
  RID="osx-x64"
fi

echo "==> Publishing for $RID..."
dotnet publish Kasir.Avalonia -c Release -r "$RID" --self-contained -o publish/mac-bin

# Paths
APP_DIR="publish/mac/Kasir.app"
CONTENTS="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"
ICON_SRC="Kasir.Avalonia/Assets/icon-256.png"
ICONSET="publish/mac-tmp/Kasir.iconset"

echo "==> Building .app bundle structure..."
rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "publish/mac-tmp"

# Copy binary and rename to match CFBundleExecutable
cp "publish/mac-bin/Kasir.Avalonia" "$MACOS_DIR/Kasir"
chmod +x "$MACOS_DIR/Kasir"

# Copy remaining published files (dylibs, etc.) alongside the binary
rsync -a --exclude="Kasir.Avalonia" publish/mac-bin/ "$MACOS_DIR/"

echo "==> Generating .icns from $ICON_SRC..."
mkdir -p "$ICONSET"
sips -z 16   16   "$ICON_SRC" --out "$ICONSET/icon_16x16.png"
sips -z 32   32   "$ICON_SRC" --out "$ICONSET/icon_16x16@2x.png"
sips -z 32   32   "$ICON_SRC" --out "$ICONSET/icon_32x32.png"
sips -z 64   64   "$ICON_SRC" --out "$ICONSET/icon_32x32@2x.png"
sips -z 128  128  "$ICON_SRC" --out "$ICONSET/icon_128x128.png"
sips -z 256  256  "$ICON_SRC" --out "$ICONSET/icon_128x128@2x.png"
sips -z 256  256  "$ICON_SRC" --out "$ICONSET/icon_256x256.png"
# icon-256.png is used as source; upscaling to 512 may be low-res but is acceptable
sips -z 512  512  "$ICON_SRC" --out "$ICONSET/icon_256x256@2x.png"
iconutil -c icns "$ICONSET" -o "$RESOURCES_DIR/Kasir.icns"

echo "==> Writing Info.plist..."
cat > "$CONTENTS/Info.plist" << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Kasir</string>
  <key>CFBundleDisplayName</key><string>Kasir POS</string>
  <key>CFBundleIdentifier</key><string>id.sinarmakmur.kasir</string>
  <key>CFBundleVersion</key><string>2.0.0</string>
  <key>CFBundleShortVersionString</key><string>2.0.0</string>
  <key>CFBundleExecutable</key><string>Kasir</string>
  <key>CFBundleIconFile</key><string>Kasir</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

# Cleanup temp files
rm -rf "publish/mac-tmp" "publish/mac-bin"

echo ""
echo "Done! Bundle at: $WORKTREE_ROOT/$APP_DIR"
echo "Open in Finder or run: open '$WORKTREE_ROOT/$APP_DIR'"
