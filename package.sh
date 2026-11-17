#!/bin/sh
# Builds the Release mod, runs the tests, and writes the release files to dist/:
#   0LoadOrderFix-<version>.zip         the .dll and .json at the top of the zip, the layout the
#                                       Android StS2 Launcher's "add mod" installs from
#   0LoadOrderFix-<version>.zip.sha256  its SHA-256, to publish next to it
#
#   ./package.sh                          uses the game at the default macOS Steam location
#   ./package.sh -p:Sts2DataPath=<dir>    the folder holding your copy of the game's sts2.dll
set -eu
cd "$(dirname "$0")"

manifest=src/LoadOrderFix/0LoadOrderFix.json
project=src/LoadOrderFix/LoadOrderFix.csproj
manifest_version="$(sed -n 's/.*"version": *"v\([^"]*\)".*/\1/p' "$manifest")"
project_version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project")"
if [ -z "$manifest_version" ] || [ "$manifest_version" != "$project_version" ]; then
    echo "Version mismatch: $manifest says v$manifest_version, $project says $project_version." >&2
    exit 1
fi

dotnet test tests/LoadOrderFix.Tests > /dev/null
dotnet build -c Release "$project" "$@" > /dev/null

mkdir -p dist
zip="dist/0LoadOrderFix-v$manifest_version.zip"
rm -f "$zip" "$zip.sha256"
# -X leaves out file owners and extra timestamps.
zip -j -q -X "$zip" src/LoadOrderFix/bin/Release/net9.0/0LoadOrderFix.dll "$manifest"
(cd dist && shasum -a 256 "$(basename "$zip")" > "$(basename "$zip").sha256")
echo "$zip"
cat "$zip.sha256"
