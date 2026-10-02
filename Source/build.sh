#!/usr/bin/env sh
# Builds the mod DLL for every supported RimWorld version into <version>/Assemblies.
set -e
cd "$(dirname "$0")"
for version in 1.5 1.6; do
    dotnet build BuildersDontCutPlants.csproj -c Release -p:RimWorldVersion="$version"
done
