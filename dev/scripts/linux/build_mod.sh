#!/usr/bin/env bash
# Build repository translations and textures, then publish a three-file mod.
set -Eeuo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)
if [[ ${1:-} == --help ]]; then
  cat <<'EOF'
Використання: ./dev/scripts/linux/build_mod.sh
Джерела: translations/uk/dt_xml, translations/uk/xml та texture/dds і texture/png у репозиторії.
Послідовність: DT-інструмент → DT → BMD → DDS → пакування.
При помилці збирання зупиняється; етап і його журнал показуються в повідомленні.
Результат: dist/<час>/Persona3ReloadUA.zip і mod/ із трьома файлами.
EOF
  exit 0
fi
if [[ $# -ne 0 ]]; then
  echo 'Невідомі аргументи. Використайте --help.' >&2
  exit 2
fi
cd -- "$root"
mkdir -p "$root/build/logs"
log_dir=$(mktemp -d "$root/build/logs/full-build-$(date +%Y%m%d-%H%M%S).XXXXXX")
stage='Підготовка'
stage_log="$log_dir/preflight.log"
: > "$stage_log"
trap 'printf "\nЗбирання перервано. Етап: %s. Журнали: %s\n" "$stage" "$log_dir" >&2; exit 130' INT
trap 'printf "\nЗбирання припинено. Етап: %s. Журнали: %s\n" "$stage" "$log_dir" >&2; exit 143' TERM
fail() {
  printf '\nПОМИЛКА — %s\n%s\nЖурнал: %s\nНаступні етапи не запускалися.\n' "$stage" "$1" "$stage_log" >&2
  exit "${2:-1}"
}
trap 'status=$?; fail "Помилка виконання (код $status)." "$status"' ERR
# Ordinary builds must generate both save-localization converters.
unset P3R_DIAGNOSTIC_ASSETS_FILE P3R_EXTRA_EXCLUSIONS_FILE P3R_EXCLUSIONS_FILE P3R_INCLUDE_DT_TEXT
if pgrep -ix p3r.exe >/dev/null; then
  fail 'Закрийте Persona 3 Reload перед збиранням.'
fi
for executable in python3 dotnet wine winepath timeout prlimit tee; do
  command -v "$executable" >/dev/null || fail "Не знайдено команду: $executable"
done
for directory in translations/uk/dt_xml translations/uk/xml texture/dds dev/tools/dt/source dev/tools/dds_l10n/source; do
  [[ -d "$root/$directory" ]] || fail "Відсутня папка: $root/$directory"
done
# Validate every input XML before changing cooked resources.
if ! python3 - "$root" >"$stage_log" 2>&1 <<'PY'
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
root = Path(sys.argv[1])
for relative in ('translations/uk/dt_xml', 'translations/uk/xml'):
    files = list((root/relative).rglob('*.xml'))
    if not files:
        raise SystemExit('Немає XML у '+relative)
    for path in files:
        try:
            ET.parse(path)
        except ET.ParseError as error:
            raise SystemExit(f'Некоректний XML: {path}: {error}')
    print(f'{relative}: {len(files)} XML перевірено')
if not any(p.is_file() and p.suffix.lower() in ('.dds', '.png')
           for folder in ('texture/dds', 'texture/png') for p in (root/folder).rglob('*')):
    raise SystemExit('У texture/dds та texture/png немає текстур')
PY
then
  cat "$stage_log" >&2
  fail 'Перевірка вхідних файлів не пройшла.'
fi
cat "$stage_log"
run_stage() {
  stage=$1
  local key=$2
  shift 2
  stage_log="$log_dir/$key.log"
  printf '\n=== %s ===\n' "$stage"
  local status=0
  if "$@" 2>&1 | tee "$stage_log"; then
    status=0
  else
    status=$?
  fi
  [[ $status -eq 0 ]] || fail "Команда завершилася з кодом $status." "$status"
  # Some legacy tools return zero despite individual import failures.
  if ! python3 - "$stage_log" <<'PY'
from pathlib import Path
import re
import sys
text = Path(sys.argv[1]).read_text(errors='replace')
for line in text.splitlines():
    if re.search(r'\b[1-9][0-9]*\s+errors?\b', line, re.I) or re.match(r'\s*(?:ERROR:|FAIL:|READ FAIL:|PARSE FAIL:|EXPORT FAIL:)', line):
        print('Інструмент повідомив про помилку: '+line, file=sys.stderr)
        raise SystemExit(1)
PY
  then
    fail 'Інструмент повідомив про помилки, попри нульовий код завершення.'
  fi
}
run_stage '1/5 — Збирання DT-інструмента' dt-tool "$root/dev/scripts/linux/build_dt_tool.sh"
# Force the freshly compiled tool, including when the caller exported an override.
export P3R_DT_TOOL_DLL="$root/dev/tools/dt/linux/P3RDtTool.dll"
run_stage '2/5 — Імпорт DT XML' dt "$root/dev/scripts/linux/repack_linux.sh" dt "$root/translations/uk/dt_xml" --force
run_stage '3/5 — Імпорт BMD XML' bmd "$root/dev/scripts/linux/repack_linux.sh" bmd "$root/translations/uk/xml" --force
run_stage '4/5 — Імпорт DDS-текстур' dds "$root/dev/scripts/linux/repack_dds.sh" "$root/texture/dds"
run_stage '5/5 — Пакування мода' pack "$root/dev/scripts/linux/pack_linux.sh"
if ! python3 - "$stage_log" <<'PY'
from pathlib import Path
import sys
import zipfile
text = Path(sys.argv[1]).read_text(errors='replace')
prefix = 'Completed: THREE mod files in '
paths = [Path(line[len(prefix):].strip()) for line in text.splitlines() if line.startswith(prefix)]
if len(paths) != 1:
    raise SystemExit('Не знайдено підтвердження успішної збірки в журналі')
mod = paths[0]
files = [p for p in mod.iterdir() if p.is_file()]
if len(files) != 3 or {p.suffix for p in files} != {'.pak', '.ucas', '.utoc'}:
    raise SystemExit('Папка mod не містить очікуваних трьох файлів')
archive = mod.parent/'Persona3ReloadUA.zip'
with zipfile.ZipFile(archive) as z:
    names = [name for name in z.namelist() if not name.endswith('/')]
    if len(names) != 3 or {Path(name).name for name in names} != {p.name for p in files}:
        raise SystemExit('Вміст архіву не відповідає трьом файлам мода')
print(f'\nГОТОВО: {archive}\nФайли мода: {mod}')
PY
then
  fail 'Фінальна перевірка комплекту не пройшла.'
fi
printf 'Журнали всіх етапів: %s\n' "$log_dir"
