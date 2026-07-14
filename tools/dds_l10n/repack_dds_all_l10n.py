import os
import shutil
import subprocess


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MOD_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, '..', '..'))

DDS_TOOL_ROOT = os.path.join(MOD_ROOT, 'tools', 'UE4-DDS-Tools')
PYTHON = os.path.join(DDS_TOOL_ROOT, 'python', 'python.exe')
TEX_TOOL = os.path.join(DDS_TOOL_ROOT, 'src', 'p3rtex.py')
DDS_ROOT = os.path.join(MOD_ROOT, 'dds')
DEFAULT_SOURCE = os.path.join(SCRIPT_DIR, 'source')
FALLBACK_CONTENT_ROOT = os.path.join(SCRIPT_DIR, 'fallback_content')
FALLBACK_EN_ROOT = os.path.join(SCRIPT_DIR, 'fallback_l10n_en')
OUT_CONTENT_ROOT = os.path.join(MOD_ROOT, 'UnrealEssentials', 'P3R', 'Content')
OUT_EN_ROOT = os.path.join(OUT_CONTENT_ROOT, 'L10N', 'en')


def require_file(path, label):
    if not os.path.isfile(path):
        raise SystemExit(f'Missing {label}: {path}')


def require_dir(path, label):
    if not os.path.isdir(path):
        raise SystemExit(f'Missing {label}: {path}')


def run(args):
    result = subprocess.run(args)
    if result.returncode != 0:
        raise SystemExit(result.returncode)


def log(message=''):
    print(message, flush=True)


def l10n_culture_in_path(path):
    parts = os.path.normpath(os.path.abspath(path)).split(os.sep)
    for index, part in enumerate(parts[:-1]):
        if part.lower() == 'l10n':
            return parts[index + 1]
    return None


def require_allowed_write_target(path, label):
    culture = l10n_culture_in_path(path)
    if culture is not None and culture.lower() != 'en':
        raise SystemExit(f'Forbidden {label}: DDS output may write only base Content or L10N/en: {path}')


def forbid_l10n_container_source(path, label):
    l10n = os.path.join(path, 'L10N')
    if os.path.isdir(l10n):
        raise SystemExit(f'Forbidden {label}: source root must not be a culture container: {l10n}')


def copy_tree_overlay(src_root, dst_root, label, skip_existing=False):
    if not os.path.isdir(src_root):
        return

    require_allowed_write_target(dst_root, f'{label} fallback output')
    log(f'[fallback] overlay {label} from {src_root}')
    for dirpath, _, names in os.walk(src_root):
        for name in names:
            src = os.path.join(dirpath, name)
            rel = os.path.relpath(src, src_root)
            dst = os.path.join(dst_root, rel)
            require_allowed_write_target(dst, f'{label} fallback file')
            if skip_existing and os.path.exists(dst):
                continue
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(src, dst)


def invoke_texture_import(label, source_dir, out_dir):
    require_allowed_write_target(out_dir, f'{label} DDS output')
    forbid_l10n_container_source(source_dir, f'{label} DDS source')
    log(f'[import] {label} textures from {source_dir}')
    run([PYTHON, '-B', '-E', TEX_TOOL, '--batch-import', DDS_ROOT, source_dir, out_dir])


def main():
    require_file(PYTHON, 'Python')
    require_file(TEX_TOOL, 'texture tool')
    require_dir(DDS_ROOT, 'DDS root')
    require_dir(DEFAULT_SOURCE, 'source root')
    require_dir(OUT_CONTENT_ROOT, 'base Content output root')
    require_dir(OUT_EN_ROOT, 'L10N/en output root')

    invoke_texture_import('base Content', DEFAULT_SOURCE, OUT_CONTENT_ROOT)
    invoke_texture_import('L10N/en', DEFAULT_SOURCE, OUT_EN_ROOT)
    copy_tree_overlay(FALLBACK_CONTENT_ROOT, OUT_CONTENT_ROOT, 'base Content', skip_existing=True)
    copy_tree_overlay(FALLBACK_EN_ROOT, OUT_EN_ROOT, 'L10N/en', skip_existing=True)

    log()
    log('Done. DDS import wrote base Content and L10N/en only. Other L10N cultures were not touched.')


if __name__ == '__main__':
    main()
