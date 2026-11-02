"""Texture filtering; discovery time is explicitly distinct from creation time."""
from pathlib import Path
from datetime import datetime, timedelta
import json
import os
import tempfile
import time


def catalog(root, filenames):
    path = Path(root)/'workspace/texture-discovery.json'
    try:
        seen = json.loads(path.read_text(encoding='utf-8'))
        if not isinstance(seen, dict):
            seen = {}
    except (OSError, ValueError):
        seen = {}
    result = {}
    for filename in filenames:
        file = Path(filename)
        try:
            stat = file.stat()
        except OSError:
            continue
        key = str(file.resolve())
        stamp = seen.get(key)
        if not isinstance(stamp, (int, float)):
            stamp = time.time()
        seen[key] = stamp
        result[str(filename)] = {'seen': stamp, 'modified': stat.st_mtime}
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=path.parent, delete=False) as f:
        pending = Path(f.name)
        json.dump(seen, f, ensure_ascii=False)
    try:
        os.replace(pending, path)
    finally:
        pending.unlink(missing_ok=True)
    return result


def select(entries, overrides, metadata, search='', fmt='Усі', days=0,
           date_kind='modified', newest=False, now=None):
    now = now or datetime.now()
    cutoff = (now.replace(hour=0, minute=0, second=0, microsecond=0)
              if days == 1 else now-timedelta(days=days)).timestamp() if days else None
    rows = []
    for relative, original in entries.items():
        source = overrides.get(relative, original)
        info = metadata.get(str(source))
        if info is None:
            continue
        if search and search.casefold() not in relative.casefold():
            continue
        if fmt != 'Усі' and Path(source).suffix.upper() != '.'+fmt:
            continue
        stamp = info[date_kind]
        if cutoff is not None and stamp < cutoff:
            continue
        rows.append((relative, source, stamp))
    return sorted(rows, key=(lambda row: (-row[2], row[0])) if newest else lambda row: row[0])
