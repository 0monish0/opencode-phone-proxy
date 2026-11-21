#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

export PATH="${HOME}/.dotnet:${PATH}"

VERSION="1.1.0"
DIST="dist/win-x64"

rm -rf "$DIST"
dotnet publish src/OpenCodePhoneProxy/OpenCodePhoneProxy.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:EnableWindowsTargeting=true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$DIST"

cp install.ps1 install.cmd uninstall.ps1 uninstall.cmd README.txt README.md "$DIST/"
rm -f "dist/OpenCodePhoneProxy-v${VERSION}-win-x64.zip"
(
  cd "$DIST"
  zip -q -r "../OpenCodePhoneProxy-v${VERSION}-win-x64.zip" .
)

(
  cd dist
  sha256sum "OpenCodePhoneProxy-v${VERSION}-win-x64.zip" \
    > "OpenCodePhoneProxy-v${VERSION}-win-x64.zip.sha256"
)

echo "Built dist/OpenCodePhoneProxy-v${VERSION}-win-x64.zip"
cat "dist/OpenCodePhoneProxy-v${VERSION}-win-x64.zip.sha256"
