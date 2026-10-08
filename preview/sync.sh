#!/bin/bash
# Syncs the MAUI app's UI into the preview harness (excludes MAUI-only Routes.razor).
set -e
SRC="$(cd "$(dirname "$0")/../src/EmpireSim" && pwd)"
DST="$(cd "$(dirname "$0")" && pwd)"
rm -rf "$DST/Components" "$DST/wwwroot" "$DST/_Imports.razor"
mkdir -p "$DST/Components"
cp -r "$SRC/Components/Layout" "$SRC/Components/Pages" "$DST/Components/"
rm -f "$DST/Components/Layout/Routes.razor" "$DST/Components/Pages/Routes.razor"
cp "$SRC/Components/_Imports.razor" "$DST/_Imports.razor"
cp -r "$SRC/wwwroot" "$DST/wwwroot"
echo "synced"
