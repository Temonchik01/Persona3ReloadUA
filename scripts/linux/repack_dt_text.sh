#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
if pgrep -ix p3r.exe >/dev/null; then echo 'Close Persona 3 Reload first.' >&2; exit 1; fi
if [[ ! -f "$root/tools/dt/linux/P3RDtTool.dll" ]]; then
  "$root/scripts/linux/build_dt_tool.sh"
fi
mkdir -p "$root/build"
input=$(mktemp -d "$root/build/dt-text-input.XXXXXX")
trap 'rm -rf -- "$input"' EXIT
cp -a "$root/dt_xml/text" "$input/text"
"$root/scripts/linux/repack_linux.sh" dt "$input" --force
echo 'DT text built; packaging exclusions remain active until an explicit game test.'
