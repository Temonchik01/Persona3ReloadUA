#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
command -v dotnet >/dev/null || { echo '.NET 8 SDK or newer is required.' >&2; exit 1; }
export DOTNET_CLI_HOME="$root/.cache/dotnet-home"
export NUGET_PACKAGES="$root/.cache/nuget"
export DOTNET_GCHeapHardLimit=0x20000000
mkdir -p "$root/tools/dt/linux" "$root/build/dt-tool/obj"
timeout --kill-after=5s 300s dotnet build \
  "$root/instruments/P3RDtTool/P3RDtTool.Linux.csproj" \
  --configuration Release --output "$root/tools/dt/linux" \
  --ignore-failed-sources -p:NuGetAudit=false \
  -p:BaseIntermediateOutputPath="$root/build/dt-tool/obj/"
echo "Built native DT tool: $root/tools/dt/linux/P3RDtTool.dll"
