"""Build compact IO Store patches containing only changed Zen resources.
Original containers remain unchanged. Localized additions use retoc templates.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
from localized_additions import build_additions
from compact_container import subset_header
from merge_compact import merge
from build_outputs import publish_build

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
    excluded = []
    exclusion_file = ROOT / 'config/build-exclusions.json'
    if exclusion_file.is_file():
        exclusions = json.loads(exclusion_file.read_text())
        if exclusions.get('enabled'):
            blocked = {str(Path(name).with_suffix('')).casefold() for name in exclusions['resource_paths']}
            if os.environ.get('P3R_INCLUDE_DT_TEXT') == '1':
                # This opt-in must NOT restore DT_SystemTextName (missing saves).
                text_paths = {str(p.relative_to(ROOT / 'dt_xml/text').with_suffix('')).casefold()
                              for p in (ROOT / 'dt_xml/text').rglob('*.xml')}
                blocked -= text_paths
            kept = {}
            for name, path in files.items():
                relative = name.removeprefix('P3R/Content/')
                if relative.startswith('L10N/'):
                    parts = relative.split('/', 2)
                    if len(parts) == 3:
                        relative = parts[2]
                if str(Path(relative).with_suffix('')).casefold() in blocked:
                    excluded.append(name)
                else:
                    kept[name] = path
            files = kept
            print(f'Temporarily excluded {len(excluded)} resource files by exclusion rules (sources preserved).', flush=True)
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
    out = ROOT / 'build/pak-builds' / time.strftime('%Y%m%d-%H%M%S')
    out.mkdir(parents=True, exist_ok=False)
    (out / 'excluded-assets.json').write_text(json.dumps(excluded, indent=2))
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
    available = {}
    canonical = {}
    folded = {}
    headers = {}
    skipped = []
    for name in files:
        if name.casefold() in folded:
            raise ValueError(f'Conflicting case variants: {name} and {folded[name.casefold()]}')
        folded[name.casefold()] = name
    for toc in sorted(game.glob('pakchunk*.utoc')):
        listing = run(['list', '--hash', '--path', toc], toc.stem + '-list', True)
        matches = {}
        package_candidates = {}
        for line in listing.splitlines():
            fields = line.split(maxsplit=4)
            if len(fields) != 5:
                continue
            if fields[3] == 'ContainerHeader':
                headers[toc] = fields[1]
            name = fields[4].removeprefix('../../../')
            available[name.casefold()] = name
            requested = folded.get(name.casefold())
            if requested is not None:
                if fields[1].endswith('02'):
                    package_candidates[fields[1]] = requested
                canonical[requested] = name
                found.add(requested)
                if hashlib.sha1(files[requested].read_bytes()).hexdigest() == fields[2][:40]:
                    skipped.append(requested)
                else:
                    matches[fields[1]] = requested
        for cid in list(matches):
            if not cid.endswith('02'):
                owner = cid[:-2] + '02'
                if owner not in matches:
                    if owner not in package_candidates:
                        raise ValueError(f'Provide the owner uasset for changed sidecar: {matches[cid]}')
                    matches[owner] = package_candidates[owner]
                    skipped = [name for name in skipped if name != matches[owner]]
        if matches:
            groups.append((toc, matches))
    missing = sorted(files.keys() - found)
    additions = []
    if missing:
        (out / 'missing-assets.json').write_text(json.dumps(missing, indent=2))
        print(f'Preparing {len(missing)} additional localized assets without dropping them.', flush=True)
        additions = build_additions(files, missing, available, game, out, font, run)
        (out / 'missing-assets.json').rename(out / 'additional-assets-requested.json')
    (out / 'canonical-paths.json').write_text(json.dumps(canonical, indent=2))
    if os.environ.get('P3R_PACK_ADDITIONS_ONLY') == '1':
        print(f'Additional-container validation completed in {out}. Main replacement containers were not built.', flush=True)
        return
    for index, (toc, matches) in enumerate(groups):
        print(f'Container {index + 1}/{len(groups)}: {toc.name}, {len(matches)} replacements', flush=True)
        target = out / f'pakchunk999-UA{index:02d}_P.utoc'
        raw = out / ('raw-' + toc.stem)
        (raw / 'chunks').mkdir(parents=True)
        header_file = out / (toc.stem + '-header.bin')
        if toc not in headers:
            raise ValueError(f'Original header not found: {toc.name}')
        run(['get', toc, headers[toc], header_file], toc.stem + '-read-header', True)
        package_matches = {cid: name for cid, name in matches.items() if cid.endswith('02')}
        selected = {int.from_bytes(bytes.fromhex(cid)[:8], 'little') for cid in package_matches}
        payloads = {int.from_bytes(bytes.fromhex(cid)[:8], 'little'): files[name] for cid, name in package_matches.items()}
        header, header_id = subset_header(header_file.read_bytes(), selected, target.stem, payloads)
        (raw / 'chunks' / header_id).write_bytes(header)
        manifest = {'version': 'PartitionSize', 'mount_point': '../../../', 'chunk_paths': {}}
        for cid, name in matches.items():
            if not cid.endswith('02') and int.from_bytes(bytes.fromhex(cid)[:8], 'little') not in selected:
                raise ValueError(f'Sidecar changed without its uasset: {name}')
            shutil.copy2(files[name], raw / 'chunks' / cid)
            manifest['chunk_paths'][cid] = '../../../' + canonical[name]
        # Include provided sidecars for changed uassets even if their bytes are unchanged.
        for cid, name in package_matches.items():
            for ext, kind in [('.ubulk', '03'), ('.uptnl', '04')]:
                side = str(Path(name).with_suffix(ext))
                if side in files:
                    sid = cid[:-2] + kind
                    if sid not in matches:
                        shutil.copy2(files[side], raw / 'chunks' / sid)
                        manifest['chunk_paths'][sid] = '../../../' + str(Path(canonical[name]).with_suffix(ext))
        (raw / 'manifest.json').write_text(json.dumps(manifest, indent=2))
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
            report.append({'path': canonical[name], 'input_path': name, 'chunk': cid, 'sha256': actual, 'container': target.name})
        # A companion PAK is needed for normal mounting; retain the tested font PAK.
        companion = font if index == 0 and not additions else ROOT / 'tools/iostore/empty.pak'
        shutil.copy2(companion, target.with_suffix('.pak'))
        # Retain intermediates until the final merged container is verified.
    (out / 'unchanged-assets.json').write_text(json.dumps(sorted(set(skipped)), indent=2))
    (out / 'replacements.json').write_text(json.dumps(report, indent=2))
    (out / 'PARTS_VERIFIED.json').write_text(json.dumps({'replacements': len(report), 'localized_additions': len(additions), 'container_verification': 'passed', 'game_loading_verified': False, 'mode': 'compact', 'unchanged_skipped': len(set(skipped)), 'dt_text_excluded': len(excluded)}, indent=2))
    dist, merged = merge(out, font, run)
    (out / 'BUILD_COMPLETE.json').write_text(json.dumps({'mode': 'compact-merged', 'output': str(dist), 'dt_text_excluded': len(excluded), **merged}, indent=2))
    dist = publish_build(out, ROOT / 'dist')
    print(f'Completed: THREE mod files in {dist}', flush=True)
    print('ZIP and reports are in the parent directory. Verified intermediates were removed; game loading must still be tested.')

if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        sys.exit(1)
