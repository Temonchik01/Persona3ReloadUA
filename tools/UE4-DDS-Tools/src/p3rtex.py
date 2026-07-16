"""P3R texture batch tool — export/import DDS files mirroring the source tree.

Commands:
  --batch-export  <uasset-src-dir> <dds-out-dir>
      Walk all T_*.uasset files under uasset-src-dir, export each as DDS
      into dds-out-dir, preserving the relative directory tree.

  --batch-import  <dds-dir> <uasset-src-dir> <mod-assets-dir>
      Walk all .dds files under dds-dir, find the matching source uasset
      (same relative path), inject the DDS, and write the result under
      mod-assets-dir (preserving the relative directory tree).

  --batch-roundtrip  <uasset-src-dir>
      Export every T_*.uasset to DDS in memory, inject it back, byte-compare.
      Reports perfect / size-change / error counts.
"""

import argparse
import contextlib
import io
import os
import sys
import time
import tempfile
import traceback

def _quiet(func, *args, **kwargs):
    """Call func silencing its stdout."""
    with contextlib.redirect_stdout(io.StringIO()):
        return func(*args, **kwargs)

# Run from next to this file — add src root to path so imports work.
sys.path.insert(0, os.path.dirname(__file__))

from unreal.uasset import Uasset
from directx.dds import DDS
from directx.texconv import Texconv

VERSION = "4.27"   # P3R uses UE 4.27

FORCE_INLINE_RELS = {
    os.path.normcase(os.path.normpath(path))
    for path in (
        r"Xrd777\UI\Title\Material\Textures\T_UI_Title_PRESSANYBUTTON_Line.uasset",
        r"Xrd777\UI\Title\Material\Textures\T_UI_Title_PRESSANYBUTTON_Shadow.uasset",
        r"Xrd777\UI\Title\Material\Textures\T_UI_Title_PRESSANYBUTTON_Text.uasset",
    )
}

# ── helpers ───────────────────────────────────────────────────────────────────

def _walk_uassets(root: str):
    """Yield (abs_path, rel_path) for every T_*.uasset under root."""
    for dirpath, _, filenames in os.walk(root):
        for fn in sorted(filenames):
            if fn.startswith("T_") and fn.lower().endswith(".uasset"):
                abs_path = os.path.join(dirpath, fn)
                rel_path = os.path.relpath(abs_path, root)
                yield abs_path, rel_path


def _walk_dds(root: str):
    """Yield (abs_path, rel_path) for every .dds file under root."""
    for dirpath, _, filenames in os.walk(root):
        for fn in sorted(filenames):
            if fn.lower().endswith(".dds"):
                abs_path = os.path.join(dirpath, fn)
                rel_path = os.path.relpath(abs_path, root)
                yield abs_path, rel_path


def _load_texture(uasset_path: str):
    import io, contextlib
    with contextlib.redirect_stdout(io.StringIO()):
        asset = Uasset(uasset_path, version=VERSION)
    if not asset.has_textures():
        return None, None
    textures = asset.get_texture_list()
    if not textures:
        return None, None
    return asset, textures[0]


# ── batch export ──────────────────────────────────────────────────────────────

def batch_export(src_dir: str, dds_dir: str):
    src_dir = os.path.abspath(src_dir)
    dds_dir = os.path.abspath(dds_dir)

    print(f"Scanning T_* uassets in: {src_dir}", flush=True)
    files = list(_walk_uassets(src_dir))
    print(f"Exporting {len(files)} T_* uassets -> DDS ...\n", flush=True)

    ok = skip = fail = processed = 0
    for uasset_path, rel in files:
        try:
            asset, tex = _load_texture(uasset_path)
            if tex is None:
                skip += 1
                processed += 1
                if processed % 500 == 0:
                    print(f"  ... {processed}/{len(files)}", flush=True)
                continue

            dds = _quiet(tex.get_dds)

            rel_dds  = os.path.splitext(rel)[0] + ".dds"
            out_path = os.path.join(dds_dir, rel_dds)
            os.makedirs(os.path.dirname(out_path), exist_ok=True)
            _quiet(dds.save, out_path)
            ok += 1
        except Exception as e:
            print(f"  FAIL: {rel}: {e}")
            fail += 1

        processed += 1
        if processed % 500 == 0:
            print(f"  ... {processed}/{len(files)}", flush=True)

    print(f"\nDone: {ok} exported  |  {skip} skipped (no texture)  |  {fail} errors", flush=True)
    print(f"DDS files at: {dds_dir}", flush=True)


# ── batch import ──────────────────────────────────────────────────────────────

def batch_import(dds_dir: str, src_dir: str, out_dir: str):
    dds_dir = os.path.abspath(dds_dir)
    src_dir = os.path.abspath(src_dir)
    out_dir = os.path.abspath(out_dir)

    dds_files = list(_walk_dds(dds_dir))
    print(f"Importing {len(dds_files)} DDS files -> uassets ...\n")

    ok = fail = no_src = 0
    for dds_path, rel_dds in dds_files:
        rel_uasset  = os.path.splitext(rel_dds)[0] + ".uasset"
        src_uasset  = os.path.join(src_dir, rel_uasset)

        if not os.path.exists(src_uasset):
            print(f"  NO SRC: {rel_uasset}")
            no_src += 1
            continue

        try:
            asset, tex = _load_texture(src_uasset)
            if tex is None:
                print(f"  NO TEX: {rel_uasset}")
                fail += 1
                continue

            force_inline = os.path.normcase(os.path.normpath(rel_uasset)) in FORCE_INLINE_RELS
            if force_inline:
                tex.has_ubulk = False

            dds = _quiet(DDS.load, dds_path)
            if dds.header.dxgi_format != tex.dxgi_format:
                with tempfile.TemporaryDirectory() as tmp_dir:
                    converted = _quiet(
                        Texconv().convert_to_dds,
                        dds_path,
                        tex.dxgi_format,
                        out=tmp_dir,
                        no_mip=(len(tex.mipmaps) == 1),
                        verbose=False,
                        allow_slow_codec=True,
                    )
                    dds = _quiet(DDS.load, converted)
            _quiet(tex.inject_dds, dds)

            out_path = os.path.join(out_dir, rel_uasset)
            os.makedirs(os.path.dirname(out_path), exist_ok=True)
            asset.update_package_source(is_official=False)
            _quiet(asset.save, out_path)
            if force_inline:
                for ext in (".ubulk", ".uptnl"):
                    sidecar = os.path.splitext(out_path)[0] + ext
                    if os.path.exists(sidecar):
                        os.remove(sidecar)
            ok += 1
        except Exception as e:
            print(f"  FAIL: {rel_uasset}: {e}")
            fail += 1

        if (ok + fail + no_src) % 200 == 0 and (ok + fail + no_src) > 0:
            print(f"  ... {ok + fail + no_src}/{len(dds_files)}")

    print(f"\nDone: {ok} injected  |  {no_src} skipped (no source)  |  {fail} errors")


# ── batch roundtrip ───────────────────────────────────────────────────────────

def batch_roundtrip(src_dir: str):
    import io, tempfile, shutil

    src_dir  = os.path.abspath(src_dir)
    files    = list(_walk_uassets(src_dir))
    print(f"Roundtrip check: {len(files)} T_* uassets ...\n")

    perfect = size_change = fail = 0

    for uasset_path, rel in files:
        try:
            # read original bytes
            with open(uasset_path, "rb") as f:
                orig = f.read()

            # export -> inject -> save to temp
            with tempfile.TemporaryDirectory() as tmp:
                tmp_out = os.path.join(tmp, os.path.basename(uasset_path))

                asset, tex = _load_texture(uasset_path)
                if tex is None:
                    fail += 1
                    continue

                dds = _quiet(tex.get_dds)

                # reload fresh to inject
                asset2, tex2 = _load_texture(uasset_path)
                _quiet(tex2.inject_dds, dds)
                asset2.update_package_source(is_official=True)
                _quiet(asset2.save, tmp_out)

                with open(tmp_out, "rb") as f:
                    rebuilt = f.read()

            if orig == rebuilt:
                perfect += 1
            else:
                size_change += 1
                if (size_change + fail) <= 10:
                    print(f"  DIFF: {rel}  orig={len(orig)}  rebuilt={len(rebuilt)}")

        except Exception as e:
            fail += 1
            if (size_change + fail) <= 10:
                print(f"  FAIL: {rel}: {e}")

    total = perfect + size_change + fail
    print(f"\nResults ({total} files):")
    print(f"  Byte-identical:  {perfect:6d}  ({perfect * 100.0 / max(total,1):.1f}%)")
    print(f"  Different size:  {size_change:6d}")
    print(f"  Errors:          {fail:6d}")


# ── entry point ───────────────────────────────────────────────────────────────

USAGE = """P3R texture batch tool

Usage:
  p3rtex.py --batch-export    <uasset-src-dir> <dds-out-dir>
  p3rtex.py --batch-import    <dds-dir> <uasset-src-dir> <mod-assets-dir>
  p3rtex.py --batch-roundtrip <uasset-src-dir>
"""

def main():
    if len(sys.argv) < 2:
        print(USAGE)
        sys.exit(0)

    cmd  = sys.argv[1].lstrip("-")
    rest = sys.argv[2:]

    t0 = time.time()

    if cmd == "batch-export":
        if len(rest) < 2:
            print("Usage: --batch-export <uasset-src-dir> <dds-out-dir>")
            sys.exit(1)
        batch_export(rest[0], rest[1])

    elif cmd == "batch-import":
        if len(rest) < 3:
            print("Usage: --batch-import <dds-dir> <uasset-src-dir> <mod-assets-dir>")
            sys.exit(1)
        batch_import(rest[0], rest[1], rest[2])

    elif cmd == "batch-roundtrip":
        if len(rest) < 1:
            print("Usage: --batch-roundtrip <uasset-src-dir>")
            sys.exit(1)
        batch_roundtrip(rest[0])

    else:
        print(f"Unknown command: {sys.argv[1]}")
        print(USAGE)
        sys.exit(1)

    print(f"\nRun time: {time.time() - t0:.1f}s", flush=True)


if __name__ == "__main__":
    main()

