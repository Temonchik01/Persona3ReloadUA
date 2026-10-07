"""Rebuild original containers with selected Zen replacements using retoc raw mode.
Never synthesizes or edits package-store tables. Originals remain unchanged.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
KEY = '0x92BADFE2921B376069D3DE8541696D230BA06B5E4320084DD34A26D117D2FFEE'

def main():
    source = Path(sys.argv[1]).resolve()
    tool = Path(os.environ['RETOC']).resolve()
    game = Path(os.environ['GAME_PAKS']).resolve()
    font = Path(os.environ['FONT_PAK']).resolve()
    budget = int(os.environ.get('P3R_PACK_MEMORY_MB', '1536')) * 1024 * 1024
    seconds = int(os.environ.get('P3R_PACK_TIMEOUT', '900'))
    if budget < 256 * 1024 * 1024 or seconds < 1:
        raise ValueError('Invalid memory/time limit')
    if not tool.is_file() or not game.is_dir() or not font.is_file():
        raise ValueError('Missing retoc, game Paks directory or companion FONT_PAK; see PACK_LINUX.md')
    files = {p.relative_to(source).as_posix(): p for p in source.rglob('*') if p.is_file() and p.suffix in ('.uasset', '.ubulk', '.uptnl')}
    if not files or any(not k.startswith('P3R/Content/') for k in files):
        raise ValueError('Input must contain P3R/Content/.../*.uasset')
    loose = [p for p in source.rglob('*') if p.is_file() and p.suffix not in ('.uasset', '.ubulk', '.uptnl')]
    if font == (ROOT / 'tools/iostore/fonts.pak').resolve():
        validated = []
        for p in loose:
            original = ROOT / 'tools/iostore/font-reference' / p.relative_to(source)
            if original.is_file() and original.read_bytes() == p.read_bytes():
                validated.append(p)
        loose = [p for p in loose if p not in validated]
        if validated:
            print(f'{len(validated)} unchanged fonts supplied by the tested companion PAK.', flush=True)
    if loose:
        raise ValueError('Input contains unsupported or changed loose files. This script uses the existing font PAK; supply an asset-only input directory.')
    out = ROOT / 'artifacts/pak-builds' / time.strftime('%Y%m%d-%H%M%S')
    out.mkdir(parents=True, exist_ok=False)
    report = []
    def run(args, name, encrypted=False):
        cmd = ['prlimit', f'--as={budget}', f'--cpu={seconds}', '--',
               'timeout', '--kill-after=5s', str(seconds), str(tool)]
        if encrypted:
            cmd += ['--aes-key', KEY]
        cmd += list(map(str, args))
        with (out / (name + '.log')).open('wb') as log:
            r = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT)
        if r.returncode:
            raise RuntimeError(f'{name} failed ({r.returncode}); see {out / (name + ".log")}')
        return (out / (name + '.log')).read_text(errors='replace')
    groups = []
    found = set()
    for toc in sorted(game.glob('pakchunk*.utoc')):
        listing = run(['list', '--path', toc], toc.stem + '-list', True)
        matches = {}
        for line in listing.splitlines():
            fields = line.split(maxsplit=3)
            if len(fields) != 4:
                continue
            name = fields[3].removeprefix('../../../')
            if name in files:
                matches[fields[1]] = name
                found.add(name)
        if matches:
            groups.append((toc, matches))
    missing = sorted(files.keys() - found)
    if missing:
        (out / 'missing-assets.json').write_text(json.dumps(missing, indent=2))
        raise ValueError(f'{len(missing)} assets not found in original containers; see missing-assets.json')
    for index, (toc, matches) in enumerate(groups):
        print(f'Container {index + 1}/{len(groups)}: {toc.name}, {len(matches)} replacements', flush=True)
        raw = out / ('raw-' + toc.stem)
        run(['unpack-raw', toc, raw], toc.stem + '-unpack', True)
        manifest = json.loads((raw / 'manifest.json').read_text())
        for cid, name in matches.items():
            expected = manifest['chunk_paths'].get(cid, '').removeprefix('../../../')
            if expected != name:
                raise ValueError(f'Manifest path mismatch: {name}')
            shutil.copy2(files[name], raw / 'chunks' / cid)
        target = out / f'pakchunk999-UA{index:02d}_P.utoc'
        run(['pack-raw', raw, target], toc.stem + '-pack')
        run(['verify', target], toc.stem + '-verify')
        run(['info', target], toc.stem + '-info')
        for cid, name in matches.items():
            extracted = out / 'verify-resource.bin'
            run(['get', target, cid, extracted], 'verify-resource')
            actual = hashlib.sha256(extracted.read_bytes()).hexdigest()
            expected = hashlib.sha256(files[name].read_bytes()).hexdigest()
            if actual != expected:
                raise ValueError(f'Packed resource mismatch: {name}')
            report.append({'path': name, 'chunk': cid, 'sha256': actual, 'container': target.name})
        # A companion PAK is needed for normal mounting; retain the tested font PAK.
        shutil.copy2(font, target.with_suffix('.pak'))
        # Keep raw data for repeatable diagnosis; no automatic deletion.
    (out / 'replacements.json').write_text(json.dumps(report, indent=2))
    print(f'Completed: {len(report)} replacements. Install ONLY .pak/.utoc/.ucas from {out}', flush=True)
    print('Raw folders and logs are build data; game loading must still be tested.')

if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        sys.exit(1)
