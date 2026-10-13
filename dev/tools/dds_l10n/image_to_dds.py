#!/usr/bin/env python3
"""Convert an edited PNG/TGA (or PSD stored composite) using a reference DDS."""
import argparse
import contextlib
import io
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile

from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'dev/tools/UE4-DDS-Tools/src'))
from directx.dds import DDS
from directx.texconv import Texconv


def read_dds(path):
    with contextlib.redirect_stdout(io.StringIO()):
        return DDS.load(str(path))


def signature(h):
    return (h.width, h.height, h.mipmap_num, h.dxgi_format.name,
            h.depth, h.get_array_size(), h.is_cube())


def preserve_transparent_bc7(result, reference, image):
    """Reuse a validated transparent BC7 block away from visible pixels.

    Mode 6's shared endpoint parity can turn alpha 0 into 1. Only empty
    blocks, with a conservative margin around visible input, are replaced.
    Text, antialiasing and partially transparent blocks are left untouched.
    """
    dds, original = read_dds(result), read_dds(reference)
    if 'BC7' not in dds.header.dxgi_format.name:
        return 0
    with Image.open(reference) as im:
        alpha = im.convert('RGBA').getchannel('A')
    block = None
    blocks_wide = (alpha.width + 3) // 4
    for y in range(0, alpha.height - 3, 4):
        for x in range(0, alpha.width - 3, 4):
            if alpha.crop((x, y, x + 4, y + 4)).getextrema() == (0, 0):
                pos = ((y // 4) * blocks_wide + x // 4) * 16
                block = original.slice_bin_list[0][pos:pos + 16]
                break
        if block is not None:
            break
    if block is None:
        return 0
    payload = bytearray(dds.slice_bin_list[0])
    mask = image.getchannel('A').filter(ImageFilter.MaxFilter(5))
    offset = count = 0
    for level, (padded_w, padded_h) in enumerate(dds.mipmap_size_list):
        w, h = max(1, image.width >> level), max(1, image.height >> level)
        mip = mask.resize((w, h), Image.Resampling.BOX)
        for y in range(0, padded_h, 4):
            for x in range(0, padded_w, 4):
                if mip.crop((x, y, min(x + 4, w), min(y + 4, h))).getextrema() == (0, 0):
                    pos = offset + ((y // 4) * (padded_w // 4) + x // 4) * 16
                    payload[pos:pos + 16] = block
                    count += 1
        offset += (padded_w // 4) * (padded_h // 4) * 16
    dds.slice_bin_list[0] = bytes(payload)
    with contextlib.redirect_stdout(io.StringIO()):
        dds.save(str(result))
    return count


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('image', type=Path)
    p.add_argument('reference', type=Path)
    p.add_argument('output', type=Path)
    p.add_argument('--quality', choices=['quick', 'normal'], default='quick')
    p.add_argument('--psd-composite', action='store_true', help='Use PSD stored merged image; does not render layers')
    a = p.parse_args()
    if a.output.suffix.lower() != '.dds':
        p.error('Output must end in .dds')
    if a.output.exists():
        p.error('Output exists; choose a new path')
    if a.image.suffix.lower() == '.psd' and not a.psd_composite:
        p.error('Export PNG from your editor, or explicitly use --psd-composite')
    ref = read_dds(a.reference).header
    if ref.is_cube() or ref.get_array_size() != 1 or ref.depth > 1:
        p.error('Only single 2D textures are supported')
    with Image.open(a.image) as im:
        if im.size != (ref.width, ref.height):
            p.error(f'Image size {im.size} differs from reference {(ref.width, ref.height)}')
        if im.mode not in ('RGB', 'RGBA', 'P', 'L', 'LA'):
            p.error('Export an 8-bit RGB/RGBA PNG first')
        rgba = im.convert('RGBA')
        with Image.open(a.reference) as original:
            # BC7 can represent an opaque source with alpha 254. Do not mistake
            # that one-step compression error for meaningful transparency.
            transparent = original.convert('RGBA').getchannel('A').getextrema()[0] < 254
        if transparent and rgba.getchannel('A').getextrema() == (255, 255):
            p.error('Reference uses transparency but input is fully opaque; export PNG with alpha')
    a.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=a.output.parent) as tmp:
        tmp = Path(tmp)
        tga = tmp / 'image.tga'
        rgba.save(tga)
        args = ['-f', ref.dxgi_format.name, '-m', str(ref.mipmap_num), '-tgazeroalpha']
        if 'SRGB' in ref.dxgi_format.name:
            args.append('-srgb')
        if a.quality == 'quick' and 'BC7' in ref.dxgi_format.name:
            args.extend(['-bc', 'q'])
        print(f'[convert] {ref.width}x{ref.height}, {ref.dxgi_format.name}, {ref.mipmap_num} mips, {a.quality}', flush=True)
        converter = Texconv()
        converter._Texconv__texconv(str(tga), args, out=str(tmp), verbose=False, allow_slow_codec=True)
        result = tmp / 'image.dds'
        if signature(read_dds(result).header) != signature(ref):
            raise ValueError('DDS metadata does not match reference')
        restored = preserve_transparent_bc7(result, a.reference, rgba)
        print(f'[alpha] {restored} fully transparent BC7 blocks preserved', flush=True)
        shutil.move(result, a.output)
    report = {'image': str(a.image.resolve()), 'reference': str(a.reference.resolve()),
              'output': str(a.output.resolve()), 'format': ref.dxgi_format.name,
              'width': ref.width, 'height': ref.height, 'mips': ref.mipmap_num,
              'quality': a.quality, 'psd_composite': a.psd_composite,
              'transparent_blocks_restored': restored}
    a.output.with_suffix('.dds.json').write_text(json.dumps(report, indent=2))
    print(f'[done] {a.output}', flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        print(f'[convert ERROR] {e}', file=sys.stderr)
        sys.exit(1)
