#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
mode=${1:-bmd}
if [[ "$mode" != bmd && "$mode" != dt ]]; then
  echo 'Usage: ./scripts/linux/repack_linux.sh bmd|dt [XML-directory] [--force]' >&2
  exit 2
fi
if pgrep -ix 'p3r.exe' >/dev/null; then
  echo 'Close Persona 3 Reload before building.' >&2
  exit 1
fi
command -v wine >/dev/null || { echo 'Wine is required.' >&2; exit 1; }
command -v winepath >/dev/null || { echo 'winepath is required.' >&2; exit 1; }
if [[ "$mode" == bmd ]]; then
  tool="$root/tools/bmd/P3RBmdTool.exe"
  source_dir="$root/tools/dds_l10n/source"
  input_dir=${2:-"$root/xml"}
else
  tool="$root/tools/dt/P3RDtTool.exe"
  source_dir="$root/tools/dt/source"
  input_dir=${2:-"$root/dt_xml"}
fi
[[ -d "$input_dir" && -d "$source_dir" && -f "$tool" ]] || { echo 'Missing XML directory, source templates or compiler.' >&2; exit 1; }
# Keep the compiler environment and logs inside this project.
export WINEPREFIX=${P3R_BUILD_WINEPREFIX:-"$root/artifacts/wine-build-prefix"}
export WINEDEBUG=-all
out="$root/UnrealEssentials/P3R/Content/L10N/en"
mkdir -p "$out" "$root/artifacts/build-logs"
args=()
if [[ ${3:-} == --force ]]; then args+=(--force); elif [[ -n ${3:-} ]]; then echo 'Unknown option' >&2; exit 2; fi
# Address-space limits cannot safely be used with Wine's large virtual reservations.
# Stop each build after ten minutes instead; start with a small XML subset.
timeout --kill-after=10s 600s wine "$tool" --batch-import-xml \
  "$(winepath -w "$input_dir")" "$(winepath -w "$source_dir")" \
  "$(winepath -w "$out")" "${args[@]}" 2>&1 | tee "$root/artifacts/build-logs/$mode-$(date +%Y%m%d-%H%M%S).log"
