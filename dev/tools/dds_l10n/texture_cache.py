"""Content-addressed PNG conversion cache shared by CLI and GUI builds."""
from pathlib import Path
import contextlib
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import importlib.metadata


def digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda:f.read(1024*1024), b''):
            h.update(block)
    return h.hexdigest()


def convert(root, png, relative, *, allow_resize=False):
    root, png, relative = Path(root), Path(png), Path(relative).with_suffix('.dds')
    if relative.is_absolute() or '..' in relative.parts:
        raise ValueError('Invalid texture resource path')
    asset = (root/'dev/tools/dds_l10n/source'/relative).with_suffix('.uasset')
    if not asset.is_file():
        raise ValueError(f'[PNG ERROR] Missing original UAsset for {relative}: {asset}')
    deps = [asset]
    deps += [asset.with_suffix(ext) for ext in ('.ubulk','.uptnl','.uexp') if asset.with_suffix(ext).exists()]
    src = root/'dev/tools/UE4-DDS-Tools/src'
    deps += [p for p in [root/'dev/tools/dds_l10n/image_to_dds.py',src/'p3rtex.py',
                         src/'directx/texconv.py',src/'directx/dds.py',
                         src/'directx'/('texconv.dll' if os.name=='nt' else 'libtexconv.so')] if p.is_file()]
    key_data = {'version':2,'quality':'normal','allow_resize':allow_resize,'platform':os.name,'pillow':importlib.metadata.version('Pillow'),'resource':relative.as_posix(),
                'png':digest(png),'dependencies':[[str(p.relative_to(root)),digest(p)] for p in deps]}
    key = hashlib.sha256(json.dumps(key_data,sort_keys=True).encode()).hexdigest()
    cache = root/'workspace/texture-dds-cache'
    cache.mkdir(parents=True,exist_ok=True)
    output, metadata = cache/(key+'.dds'), cache/(key+'.json')
    try:
        data = json.loads(metadata.read_text())
        if data['key']==key_data and output.is_file() and data['dds_sha256']==digest(output):
            print(f'[PNG cache] {relative}',flush=True)
            return output
    except (OSError,ValueError,KeyError):
        pass
    sys.path.insert(0,str(src))
    import p3rtex
    with tempfile.TemporaryDirectory(prefix='pending-',dir=cache) as tmp:
        tmp = Path(tmp)
        snapshot = tmp/'input.png'
        shutil.copyfile(png,snapshot)
        if digest(snapshot)!=key_data['png']:
            raise RuntimeError(f'[PNG ERROR] File changed during conversion: {png}')
        reference, generated = tmp/'reference.dds', tmp/'result.dds'
        with contextlib.redirect_stdout(io.StringIO()):
            _, tex = p3rtex._load_texture(str(asset))
            if tex is None:
                raise ValueError(f'[PNG ERROR] Not a texture: {asset}')
            tex.get_dds().save(str(reference))
        print(f'[PNG -> DDS] {relative}',flush=True)
        try:
            subprocess.run([sys.executable,'-u',str(root/'dev/tools/dds_l10n/image_to_dds.py'),
                            '--quality','normal',*(['--allow-resize'] if allow_resize else []),
                            str(snapshot),str(reference),str(generated)],check=True,
                           timeout=int(os.environ.get('P3R_TEXTURE_TIMEOUT','600')),
                           env=dict(os.environ,OMP_NUM_THREADS='1'))
        except (subprocess.CalledProcessError,subprocess.TimeoutExpired) as error:
            raise RuntimeError(f'[PNG ERROR] Conversion failed for {png} ({relative}): {error}') from error
        data = {'key':key_data,'dds_sha256':digest(generated)}
        if digest(png)!=key_data['png']:
            raise RuntimeError(f'[PNG ERROR] File changed during conversion: {png}')
        pending = tmp/'metadata.json'
        pending.write_text(json.dumps(data,ensure_ascii=False,indent=2))
        os.replace(generated,output)
        os.replace(pending,metadata)
    return output


def precompile(root):
    """Explicitly publish compiled repository PNGs into texture/dds."""
    root = Path(root)
    files = sorted((root/'texture/png').rglob('*.png'))
    for index, png in enumerate(files,1):
        relative = png.relative_to(root/'texture/png').with_suffix('.dds')
        print(f'PNG {index}/{len(files)}: {relative}',flush=True)
        cached = convert(root,png,relative)
        target = root/'texture/dds'/relative
        target.parent.mkdir(parents=True,exist_ok=True)
        # Replace only after a successful conversion; never truncate a DDS.
        with tempfile.NamedTemporaryFile(dir=target.parent,delete=False,suffix='.tmp') as f:
            pending = Path(f.name)
        try:
            shutil.copyfile(cached,pending)
            os.replace(pending,target)
        finally:
            pending.unlink(missing_ok=True)
    print(f'PNG_PRECOMPILE_DONE: {len(files)}',flush=True)
    return len(files)
