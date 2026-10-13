"""GUI build worker. Every texture test uses an isolated input directory."""
from pathlib import Path
import argparse
import contextlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def textures(root):
    entries = {}
    for folder, suffix in [('texture/dds', '.dds'), ('texture/png', '.png')]:
        for p in (root/folder).rglob('*'):
            if p.is_file() and p.suffix.lower() == suffix:
                entries[p.relative_to(root/folder).with_suffix('.dds').as_posix()] = str(p)
    return dict(sorted(entries.items()))


def run(args, root, env):
    print('[command] '+str(args[0]), flush=True)
    proc = subprocess.Popen(list(map(str, args)), cwd=root, env=env,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding='utf-8', errors='replace',
                            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    timer = threading.Timer(1800, proc.kill)
    timer.start()
    lines = []
    try:
        for line in proc.stdout:
            lines.append(line)
            print(line, end='', flush=True)
        proc.wait()
    finally:
        timer.cancel()
        proc.stdout.close()
    if proc.returncode:
        raise RuntimeError(f'Command failed ({proc.returncode})')
    import re
    if re.search(r'\b[1-9][0-9]*\s+errors?\b|^\s*(?:FAIL:|ERROR:)', ''.join(lines), re.M | re.I):
        raise RuntimeError('Tool reported import errors')


def environment(job):
    env = dict(os.environ)
    for key in ('P3R_DIAGNOSTIC_ASSETS_FILE','P3R_EXTRA_EXCLUSIONS_FILE',
                'P3R_EXCLUSIONS_FILE','P3R_INCLUDE_DT_TEXT','P3R_DDS_INPUT'):
        env.pop(key, None)
    env.update(RETOC=job['retoc'], GAME_PAKS=job['game_paks'],
               FONT_PAK=job['font_pak'], RAYON_NUM_THREADS='1', OMP_NUM_THREADS='1',
               PYTHONIOENCODING='utf-8')
    return env


def preflight(job):
    if job['mode'] not in ('textures','full'):
        raise ValueError('Невідомий режим збирання')
    root = Path(job['root']).resolve()
    if not (root/'dev/tools/dds_l10n/source').is_dir():
        raise ValueError('Не знайдено інструментарій у вибраному репозиторії')
    game = Path(job['game_paks'])
    if not game.is_dir() or not list(game.glob('pakchunk*.utoc')):
        raise ValueError('Вкажіть папку P3R/Content/Paks із pakchunk*.utoc')
    for key in ('retoc','font_pak'):
        if not Path(job[key]).is_file():
            raise ValueError(f'Не знайдено {key}: {job[key]}')
    if os.name == 'nt':
        active = subprocess.run(['tasklist','/FI','IMAGENAME eq P3R.exe','/FO','CSV','/NH'],
                                capture_output=True, text=True).stdout
        if 'p3r.exe' in active.lower():
            raise ValueError('Закрийте Persona 3 Reload перед збиранням')
    elif subprocess.run(['pgrep','-ix','p3r.exe'], stdout=subprocess.DEVNULL).returncode == 0:
        raise ValueError('Закрийте Persona 3 Reload перед збиранням')
    if job['mode'] == 'textures':
        if not job['textures']:
            raise ValueError('Виберіть хоча б одну текстуру')
        for relative, filename in job['textures'].items():
            rel = Path(relative)
            if rel.is_absolute() or '..' in rel.parts or rel.suffix.lower() != '.dds':
                raise ValueError('Некоректний шлях текстури: '+relative)
            if not Path(filename).is_file() or Path(filename).suffix.lower() not in ('.png','.dds'):
                raise ValueError('Не знайдено PNG/DDS: '+filename)
            if not (root/'dev/tools/dds_l10n/source'/rel.with_suffix('.uasset')).is_file():
                raise ValueError('Не знайдено базовий ресурс: '+relative)
    return root


def texture_build(job, root, env):
    sys.path.insert(0, str(root/'dev/tools/UE4-DDS-Tools/src'))
    import p3rtex
    work = Path(tempfile.mkdtemp(prefix='gui-textures-', dir=root/'build'))
    assets, inputs, refs = work/'assets', work/'dds', work/'reference'
    print('Етап 1/3: підготовка вибраних текстур', flush=True)
    for relative, filename in job['textures'].items():
        rel, image = Path(relative), Path(filename)
        dest = inputs/rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        if image.suffix.lower() == '.png':
            reference = refs/rel
            reference.parent.mkdir(parents=True, exist_ok=True)
            with contextlib.redirect_stdout(io.StringIO()):
                _, tex = p3rtex._load_texture(str(root/'dev/tools/dds_l10n/source'/rel.with_suffix('.uasset')))
                tex.get_dds().save(str(reference))
            print('[PNG] '+relative, flush=True)
            run([sys.executable, '-u', root/'dev/tools/dds_l10n/image_to_dds.py', image, reference, dest], root, env)
        else:
            shutil.copy2(image, dest)
    print('Етап 2/3: імпорт текстур у ресурси гри', flush=True)
    for prefix in ('P3R/Content', 'P3R/Content/L10N/en'):
        run([sys.executable, '-u', root/'dev/tools/UE4-DDS-Tools/src/p3rtex.py',
             '--batch-import', inputs, root/'dev/tools/dds_l10n/source', assets/prefix], root, env)
    overrides = work/'overrides.json'
    overrides.write_text(json.dumps({'assets':{p.relative_to(assets).as_posix():str(p)
                          for p in assets.rglob('*.uasset')}}, ensure_ascii=False))
    env['P3R_DIAGNOSTIC_ASSETS_FILE'] = str(overrides)
    print('Етап 3/3: компактне пакування', flush=True)
    run([sys.executable, '-u', root/'dev/tools/iostore/pack_linux.py', assets], root, env)
    shutil.rmtree(work)


def full_windows(root, env):
    for folder in ('translations/uk/xml','translations/uk/dt_xml'):
        files = list((root/folder).rglob('*.xml'))
        if not files:
            raise ValueError('Немає перекладів: '+folder)
        for p in files:
            ET.parse(p)
    if not shutil.which('dotnet'):
        raise ValueError('Для повного білда встановіть .NET 8 SDK')
    print('Етап 1/5: збирання DT-інструмента', flush=True)
    dll_dir = root/'dev/tools/dt/linux'
    run(['dotnet','build',root/'dev/instruments/P3RDtTool/P3RDtTool.Linux.csproj',
         '-c','Release','-o',dll_dir,'--ignore-failed-sources','-p:NuGetAudit=false',
         '-p:BaseIntermediateOutputPath='+str(root/'build/dt-tool/obj')+os.sep], root, env)
    output = root/'UnrealEssentials/P3R/Content/L10N/en'
    output.mkdir(parents=True, exist_ok=True)
    print('Етап 2/5: імпорт DT', flush=True)
    run(['dotnet',dll_dir/'P3RDtTool.dll','--batch-import-xml',root/'translations/uk/dt_xml',
         root/'dev/tools/dt/source',output,'--force'], root, env)
    print('Етап 3/5: імпорт BMD', flush=True)
    run([root/'dev/tools/bmd/P3RBmdTool.exe','--batch-import-xml',root/'translations/uk/xml',
         root/'dev/tools/dds_l10n/source',output,'--force'], root, env)
    print('Етап 4/5: імпорт текстур', flush=True)
    run([sys.executable,'-u',root/'dev/tools/dds_l10n/repack_dds_all_l10n.py'], root, env)
    print('Етап 5/5: пакування', flush=True)
    run([sys.executable,'-u',root/'dev/tools/iostore/pack_linux.py',root/'UnrealEssentials'], root, env)


def main(job):
    root = preflight(job)
    (root/'build').mkdir(exist_ok=True)
    env = environment(job)
    if job['mode'] == 'textures':
        texture_build(job, root, env)
    elif os.name == 'nt':
        full_windows(root, env)
    else:
        run(['bash',root/'dev/scripts/linux/build_mod.sh'], root, env)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('job', type=Path)
    args = parser.parse_args()
    try:
        main(json.loads(args.job.read_text()))
    except Exception as error:
        print('[ПОМИЛКА] '+str(error), flush=True)
        sys.exit(1)
