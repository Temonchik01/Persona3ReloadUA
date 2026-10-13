"""Stage DDS inputs, overriding matching textures with explicit PNG exports."""
from pathlib import Path
import contextlib
import io
import json
import os
import shutil
import subprocess
import sys


def prepare(root, work):
    root, work = Path(root), Path(work)
    dds_root, png_root = root/'texture/dds', root/'texture/png'
    staged = work/'dds'
    staged.mkdir(parents=True)
    dds_files = sorted(p for p in dds_root.rglob('*') if p.suffix.lower() == '.dds' and p.is_file())
    for source in dds_files:
        dest = staged/source.relative_to(dds_root)
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)
    png_files = sorted(p for p in png_root.rglob('*') if p.suffix.lower() == '.png' and p.is_file())
    if png_files:
        sys.path.insert(0, str(root/'dev/tools/UE4-DDS-Tools/src'))
        import p3rtex
    details = []
    for png in png_files:
        relative = png.relative_to(png_root)
        asset = (root/'dev/tools/dds_l10n/source'/relative).with_suffix('.uasset')
        if not asset.is_file():
            raise ValueError(f'[PNG ERROR] Missing original UAsset for {relative}: {asset}')
        reference = (work/'reference'/relative).with_suffix('.dds')
        reference.parent.mkdir(parents=True, exist_ok=True)
        with contextlib.redirect_stdout(io.StringIO()):
            _, tex = p3rtex._load_texture(str(asset))
            if tex is None:
                raise ValueError(f'[PNG ERROR] Not a texture: {asset}')
            tex.get_dds().save(str(reference))
        dest = (staged/relative).with_suffix('.dds')
        dest.unlink(missing_ok=True)
        print(f'[PNG -> DDS] {relative}', flush=True)
        env = dict(os.environ, OMP_NUM_THREADS='1')
        try:
            subprocess.run([sys.executable, '-u', str(root/'dev/tools/dds_l10n/image_to_dds.py'),
                            str(png), str(reference), str(dest)], check=True,
                           timeout=int(os.environ.get('P3R_TEXTURE_TIMEOUT', '600')), env=env)
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
            raise RuntimeError(f'[PNG ERROR] Conversion failed for {relative}: {error}') from error
        details.append({'png':str(relative), 'dds':str(dest.relative_to(staged))})
    # Reports are not texture inputs.
    for p in staged.rglob('*.dds.json'):
        shutil.move(p, work/('__'.join(p.relative_to(staged).parts)))
    (work/'texture-inputs.json').write_text(json.dumps({'dds_supplied':len(dds_files),
        'png_overrides':details}, ensure_ascii=False, indent=2))
    print(f'[textures] {len(dds_files)} DDS, {len(png_files)} PNG overrides', flush=True)
    return staged
