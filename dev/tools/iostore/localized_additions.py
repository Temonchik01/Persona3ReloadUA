"""Let retoc generate metadata for absent localized aliases; preserve mod payloads."""
import hashlib
import json
from pathlib import Path
import shutil
import struct


def build_additions(files, missing, available, game, out, font, run):
    donors = {}
    for target in missing:
        if not target.endswith('.uasset') or '/Content/L10N/' not in target:
            raise ValueError(f'Cannot safely create this missing resource: {target}')
        prefix, localized = target.split('/Content/L10N/', 1)
        culture, tail = localized.split('/', 1)
        candidates = [prefix + '/Content/L10N/en/' + tail, prefix + '/Content/' + tail]
        donor = next((available.get(x.casefold()) for x in candidates if available.get(x.casefold())), None)
        if donor is None:
            raise ValueError(f'No matching original template for {target}')
        donors[target] = donor
    legacy = out / 'additional-legacy-originals'
    args = ['to-legacy', game, legacy, '--version', 'UE4_27', '--no-shaders', '--no-script-objects', '--no-parallel']
    for donor in sorted(set(donors.values())):
        args += ['--filter', donor]
    run(args, 'additional-extract-templates', True)
    renamed = out / 'additional-legacy-aliases'
    mappings = []
    for target, donor in donors.items():
        src = legacy / donor
        if not src.is_file():
            raise ValueError(f'retoc did not export template: {donor}')
        dst = renamed / target
        dst.parent.mkdir(parents=True, exist_ok=True)
        for ext in ('.uasset', '.uexp', '.ubulk', '.uptnl'):
            if src.with_suffix(ext).is_file():
                shutil.copy2(src.with_suffix(ext), dst.with_suffix(ext))
        mappings.append({'path': target, 'template': donor})
    toc = out / 'pakchunk999-UAAdded_P.utoc'
    run(['to-zen', renamed, toc, '--version', 'UE4_27', '--no-parallel'], 'additional-create-metadata')
    raw = out / 'additional-raw'
    run(['unpack-raw', toc, raw], 'additional-unpack')
    manifest = json.loads((raw / 'manifest.json').read_text())
    paths = {path.removeprefix('../../../').casefold(): cid for cid, path in manifest['chunk_paths'].items()}
    for item in mappings:
        target = item['path']
        if target.casefold() not in paths:
            raise ValueError(f'No generated chunk for {target}')
        cid = paths[target.casefold()]
        shutil.copy2(files[target], raw / 'chunks' / cid)
        item['chunk'] = cid
        item['sha256'] = hashlib.sha256(files[target].read_bytes()).hexdigest()
        for ext in ('.ubulk', '.uptnl'):
            sidecar = str(Path(target).with_suffix(ext))
            if sidecar in files:
                sid = paths.get(sidecar.casefold())
                if sid is None:
                    raise ValueError(f'No generated metadata for sidecar {sidecar}')
                shutil.copy2(files[sidecar], raw / 'chunks' / sid)
    # The source compiler preserves UE4 export bundles; retoc reconstruction may
    # choose a different bundle layout. Derive bounded counts from each payload.
    # Keep retoc's package IDs, imports and culture mappings; patch only size/counts.
    headers = [p for p in (raw / 'chunks').iterdir() if p.name.endswith('0a')]
    if len(headers) != 1:
        raise ValueError('Expected exactly one generated container header')
    hp = headers[0]
    header = bytearray(hp.read_bytes())
    def u32(pos):
        if pos < 0 or pos + 4 > len(header):
            raise ValueError('Container header offset out of bounds')
        return struct.unpack_from('<I', header, pos)[0]
    pos = 12
    names = u32(pos); pos += 4 + names
    hashes = u32(pos); pos += 4 + hashes
    count = u32(pos); pos += 4
    if count != len(mappings) or count > 10000 or pos + count * 8 + 4 > len(header):
        raise ValueError('Unexpected generated package count')
    package_ids = list(struct.unpack_from('<' + 'Q' * count, header, pos)); pos += count * 8
    store_size = u32(pos); pos += 4
    if store_size < count * 32 or pos + store_size > len(header):
        raise ValueError('Invalid generated store-entry buffer')
    for item in mappings:
        payload = files[item['path']].read_bytes()
        if len(payload) < 64:
            raise ValueError('Truncated Zen payload')
        export, bundles, graph = struct.unpack_from('<III', payload, 44)
        if not 64 <= export <= bundles <= graph <= len(payload) or (bundles - export) % 72:
            raise ValueError('Unsupported Zen export layout: ' + item['path'])
        exports = (bundles - export) // 72
        remainder = graph - bundles - exports * 16
        if exports < 1 or exports > 10000 or remainder < 8 or remainder % 8:
            raise ValueError('Unsupported UE4 bundle layout: ' + item['path'])
        bundle_count = remainder // 8
        if bundle_count > exports * 2:
            raise ValueError('Unreasonable bundle count')
        serial_size = sum(struct.unpack_from('<Q', payload, export + i * 72 + 8)[0] for i in range(exports))
        package_id = int.from_bytes(bytes.fromhex(item['chunk'])[:8], 'little')
        entry = pos + package_ids.index(package_id) * 32
        struct.pack_into('<QII', header, entry, serial_size, exports, bundle_count)
        item['export_count'] = exports
        item['export_bundle_count'] = bundle_count
    hp.write_bytes(header)
    run(['pack-raw', raw, toc], 'additional-pack')
    run(['verify', toc], 'additional-verify')
    run(['info', toc], 'additional-info')
    # Re-parse every mod payload with retoc using generated template metadata.
    # Failure blocks the build instead of silently dropping localized aliases.
    validation_input = out / 'additional-validation-input'
    validation_input.mkdir()
    for original in (toc, toc.with_suffix('.ucas'), game / 'global.utoc', game / 'global.ucas'):
        shutil.copy2(original, validation_input / original.name)
    run(['to-legacy', validation_input, out / 'additional-validation', '--version', 'UE4_27', '--no-shaders', '--no-script-objects', '--no-parallel'], 'additional-validate-payloads')
    for item in mappings:
        extracted = out / 'additional-check.bin'
        run(['get', toc, item['chunk'], extracted], 'additional-check-resource')
        if hashlib.sha256(extracted.read_bytes()).hexdigest() != item['sha256']:
            raise ValueError(f'Additional payload mismatch: {item["path"]}')
    shutil.copy2(font, toc.with_suffix('.pak'))
    (out / 'additional-assets.json').write_text(json.dumps(mappings, indent=2))
    return mappings
