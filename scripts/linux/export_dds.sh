#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
if [[ ${1:-} == --help ]]; then
  echo 'Usage: ./scripts/linux/export_dds.sh [source-directory] [output-directory]'
  echo 'Defaults: tools/dds_l10n/source -> workspace/dds-exports/<time> (existing edited DDS are preserved).'
  exit 0
fi
if pgrep -ix 'p3r.exe' >/dev/null; then echo 'Close Persona 3 Reload first.' >&2; exit 1; fi
src=${1:-"$root/tools/dds_l10n/source"}
out=${2:-"$root/workspace/dds-exports/$(date +%Y%m%d-%H%M%S)"}
[[ -d "$src" ]] || { echo 'Source directory does not exist.' >&2; exit 1; }
if [[ -d "$out" ]] && [[ -n $(find "$out" -mindepth 1 -print -quit) ]]; then
  echo 'Output is not empty. Choose a new folder to protect edited DDS.' >&2; exit 1
fi
mkdir -p "$out" "$root/build/logs"
prlimit --as="${P3R_DDS_MEMORY_BYTES:-1073741824}" --cpu=900 -- \
  timeout --kill-after=5s 900s python3 -u -B -E "$root/tools/UE4-DDS-Tools/src/p3rtex.py" \
  --batch-export "$src" "$out" 2>&1 | tee "$root/build/logs/dds-export-$(date +%Y%m%d-%H%M%S).log"
