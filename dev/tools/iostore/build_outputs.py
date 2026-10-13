"""Publish verified compact builds, then remove their temporary working data."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import zipfile

MOD_FILES = {f'pakchunk999-Ukrainian_P.{ext}' for ext in ('pak', 'utoc', 'ucas')}


def file_hash(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def publish_build(work, destination_root):
    """Only a completed merged build may be published and automatically cleaned.

    Failures retain working data (or the pending destination after publication
    starts). Nothing outside the explicit build directory is removed.
    """
    work = Path(work).resolve()
    destination_root = Path(destination_root).resolve()
    if not re.fullmatch(r'\d{8}-\d{6}(?:-\d+)?', work.name):
        raise ValueError('Expected a timestamped build directory')
    if work == destination_root or work in destination_root.parents or destination_root in work.parents:
        raise ValueError('Working and published directories must be separate')
    if not (work/'BUILD_COMPLETE.json').is_file() or not (work/'MERGED_BUILD.json').is_file():
        raise ValueError('Refusing to clean an incomplete or unmerged build')
    complete = json.loads((work/'BUILD_COMPLETE.json').read_text())
    merged = json.loads((work/'MERGED_BUILD.json').read_text())
    legacy_verified = (complete.get('mode') == 'compact' and complete.get('container_verification') == 'passed'
                       and len(merged.get('files', [])) == 3)
    if (complete.get('mode') != 'compact-merged' and not legacy_verified) or merged.get('file_count') != 3:
        raise ValueError('Unexpected build verification record')
    mod = work/'mod'
    if not mod.is_dir() or mod.is_symlink() or {p.name for p in mod.iterdir()} != MOD_FILES:
        raise ValueError('The verified mod must contain exactly three expected files')
    if any(not (mod/name).is_file() or (mod/name).is_symlink() or (mod/name).stat().st_size == 0 for name in MOD_FILES):
        raise ValueError('Invalid final mod file')
    hashes = {name: file_hash(mod/name) for name in sorted(MOD_FILES)}
    if legacy_verified and {item['name']: item['sha256'] for item in merged['files']} != hashes:
        raise ValueError('Historical merged build hashes do not match')
    destination_root.mkdir(parents=True, exist_ok=True)
    final = destination_root/work.name
    pending = destination_root/(work.name+'.pending')
    if final.exists() or pending.exists():
        raise FileExistsError(f'Output already exists: {final} or {pending}')
    pending.mkdir()
    reports = pending/'reports'
    reports.mkdir()
    # Keep diagnostic metadata and logs, never intermediate asset payloads.
    for path in work.rglob('*'):
        if path.is_file() and not path.is_symlink() and path.suffix.lower() in ('.json', '.log', '.md', '.txt'):
            target = reports/path.relative_to(work)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
    # Keep existing hand-named release ZIPs as well when migrating old builds.
    for path in work.glob('*.zip'):
        shutil.move(str(path), pending/path.name)
    shutil.move(str(mod), pending/'mod')
    archive = pending/'Persona3ReloadUA.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as package:
        for name in sorted(MOD_FILES):
            package.write(pending/'mod'/name, name)
    with zipfile.ZipFile(archive) as package:
        if set(package.namelist()) != MOD_FILES:
            raise ValueError('Unexpected ZIP contents')
        for name, expected in hashes.items():
            digest = hashlib.sha256()
            with package.open(name) as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(block)
            if digest.hexdigest() != expected:
                raise ValueError(f'ZIP verification failed: {name}')
    complete.update(mode='compact-merged', output=str(final/'mod'), archive=str(final/archive.name), sha256=hashes,
                    intermediates_removed=True)
    (pending/'BUILD_COMPLETE.json').write_text(json.dumps(complete, indent=2))
    pending.rename(final)
    shutil.rmtree(work)
    return final/'mod'
