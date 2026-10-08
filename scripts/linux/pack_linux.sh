#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
if [[ ${1:-} == --help ]]; then
  echo 'Usage: ./scripts/linux/pack_linux.sh [UnrealEssentials-directory]'
  echo 'Environment: RETOC, GAME_PAKS, FONT_PAK, P3R_PACK_MEMORY_MB (default 1536), P3R_PACK_TIMEOUT (default 900 seconds)'
  echo 'Outputs: dist/<time>/mod (three files), ZIP and reports. Temporary work: build/pak-builds; cleaned after verification.'
  exit 0
fi
if [[ -f "$root/config.local.env" ]]; then source "$root/config.local.env"; fi
export RETOC=${RETOC:-$root/tools/iostore/bin/retoc}
export GAME_PAKS=${GAME_PAKS:-$HOME/.var/app/com.valvesoftware.Steam/data/Steam/steamapps/common/P3R/P3R/Content/Paks}
export FONT_PAK=${FONT_PAK:-$root/tools/iostore/fonts.pak}
export RAYON_NUM_THREADS=1
exec python3 "$root/tools/iostore/pack_linux.py" "${1:-$root/UnrealEssentials}"
