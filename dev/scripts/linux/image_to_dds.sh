#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)
export OMP_NUM_THREADS=1
exec prlimit --as=1073741824 -- \
  timeout --kill-after=5s "${P3R_TEXTURE_TIMEOUT:-600}s" \
  python3 -u "$root/dev/tools/dds_l10n/image_to_dds.py" "$@"
