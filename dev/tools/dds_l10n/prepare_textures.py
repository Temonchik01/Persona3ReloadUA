"""Stage full-build textures: prefer published DDS; convert PNG only if DDS is absent."""
from pathlib import Path
import contextlib
import io
import json
import os
import shutil
import subprocess
import sys
sys.path.insert(0,str(Path(__file__).resolve().parent))
from texture_cache import convert


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
    details = []
    skipped = []
    for png in png_files:
        relative = png.relative_to(png_root)
        dest = (staged/relative).with_suffix('.dds')
        if dest.is_file():
            skipped.append(str(relative))
            continue
        asset = (root/'dev/tools/dds_l10n/source'/relative).with_suffix('.uasset')
        if not asset.is_file():
            raise ValueError(f'[PNG ERROR] Missing original UAsset for {relative}: {asset}')
        dest.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(convert(root,png,relative),dest)
        details.append({'png':str(relative), 'dds':str(dest.relative_to(staged))})
    # Reports are not texture inputs.
    for p in staged.rglob('*.dds.json'):
        shutil.move(p, work/('__'.join(p.relative_to(staged).parts)))
    (work/'texture-inputs.json').write_text(json.dumps({'dds_supplied':len(dds_files),
        'png_overrides':details, 'png_skipped_for_dds':skipped}, ensure_ascii=False, indent=2))
    print(f'[textures] {len(dds_files)} DDS, {len(details)} PNG conversions, {len(skipped)} PNG skipped (DDS priority)', flush=True)
    return staged
