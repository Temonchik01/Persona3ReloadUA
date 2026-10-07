#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
if [[ ${1:-} == --help ]]; then
  echo 'Usage: ./scripts/linux/repack_dds.sh [edited-DDS-directory]'
  echo 'Default: dds. Writes base Content and L10N/en only.'
  echo 'DDS must retain original DXGI format; conversion requires Linux libtexconv.so.'
  exit 0
fi
if pgrep -ix 'p3r.exe' >/dev/null; then echo 'Close Persona 3 Reload first.' >&2; exit 1; fi
export P3R_DDS_INPUT=${1:-"$root/dds"}
[[ -d "$P3R_DDS_INPUT" ]] || { echo 'DDS directory does not exist.' >&2; exit 1; }
mkdir -p "$root/UnrealEssentials/P3R/Content/L10N/en" "$root/artifacts/build-logs"
prlimit --as="${P3R_DDS_MEMORY_BYTES:-1073741824}" --cpu=900 -- \
  timeout --kill-after=5s 900s python3 -u -B -X utf8 -E "$root/tools/dds_l10n/repack_dds_all_l10n.py" \
  2>&1 | tee "$root/artifacts/build-logs/dds-import-$(date +%Y%m%d-%H%M%S).log"
