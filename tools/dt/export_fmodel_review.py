#!/usr/bin/env python3
"""Export a separate, auditable DT translation workspace from FModel assets."""
import argparse, hashlib, json, os, shutil, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    source, output = args.source.resolve(), args.output.resolve()
    if not source.is_dir():
        parser.error('Source directory does not exist')
    if output.exists():
        parser.error('Choose a new output directory; existing translations are never overwritten')
    output.mkdir(parents=True)
    dll = ROOT/'tools/dt/linux/P3RDtTool.dll'
    env = {**os.environ, 'DOTNET_GCHeapHardLimit': '0x10000000', 'DOTNET_gcServer': '0'}
    def run(*command):
        result = subprocess.run(['prlimit', '--core=0', '--as=8589934592', '--cpu=30', '--',
            'timeout', '40s', 'dotnet', str(dll), *map(str,command)],
            capture_output=True, text=True, env=env, timeout=45)
        if result.returncode:
            raise RuntimeError((result.stdout+result.stderr)[-4000:])
    selected = sorted(p for p in source.rglob('*.uasset') if
        p.name.startswith(('DT_', 'Dat')) or 'UI/Tables/' in p.relative_to(source).as_posix())
    rows = []
    for index, asset in enumerate(selected, 1):
        rel = asset.relative_to(source)
        row = {'file': str(rel), 'sha256': hashlib.sha256(asset.read_bytes()).hexdigest()}
        copied = output/'source'/rel
        copied.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(asset, copied)
        work = output/'checks'/rel.with_suffix('')
        work.mkdir(parents=True)
        try:
            candidates = []
            for mode, tag in [('text', 'Text'), ('fstrings', 'String')]:
                xml = work/f'{mode}.xml'
                run(f'--export-{mode}-xml', copied, xml)
                tree = ET.parse(xml)
                nodes = tree.getroot().findall(tag)
                values = [e for e in nodes if e.get('role') == 'value']
                candidates.append((mode,tag,xml,tree,nodes,values))
            choice = next((c for c in candidates if c[0]=='text' and c[5]),
                next((c for c in candidates if c[5]), candidates[0]))
            # A loose byte scan must not override recognized technical fields
            # such as Font glyphs merely because their text is not editable.
            if choice[0]=='fstrings' and choice[3].getroot().get('layout')=='loose-fstrings' and candidates[0][4]:
                choice=candidates[0]
            mode, tag, xml, tree, nodes, values = choice
            row.update(mode=mode, layout=tree.getroot().get('layout'), strings=len(nodes), editable=len(values))
            status = 'editable' if values else 'no-editable-text'
            if mode=='fstrings' and row['layout'] not in ('array-str','array-text'):
                status='needs-review'
            if asset.stem.startswith('DT_SystemTextName'):
                status='system-text-review'
            if values:
                noop=work/'noop.uasset'
                run(f'--import-{mode}-xml',copied,xml,noop)
                if noop.read_bytes()!=copied.read_bytes():
                    raise RuntimeError('No-op import changed original bytes')
                row['noop_identical']=True
                values[0].find('Translation').text='Тест перекладу їєґ 😀'
                probe=work/'probe.xml';tree.write(probe,encoding='utf-8',xml_declaration=True)
                compiled=work/'probe.uasset'
                run(f'--import-{mode}-xml',copied,probe,compiled)
                recovered=work/'probe-export.xml'
                run(f'--export-{mode}-xml',compiled,recovered)
                actual=ET.parse(recovered).getroot().findall(tag)
                if len(actual)!=len(nodes) or any(a.findtext('Source') != b.findtext('Translation') for a,b in zip(actual,nodes)):
                    raise RuntimeError('Unicode probe did not round-trip all strings')
                row['unicode_roundtrip']=True
            row['status']=status
            dest=output/('xml' if status=='editable' else 'review')/mode/rel.with_suffix('.xml')
            dest.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(xml,dest)
            row['xml']=str(dest.relative_to(output))
        except Exception as exc:
            row.update(status='error',error=str(exc))
        rows.append(row)
        (output/'report.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2))
        if index%20==0 or index==len(selected):
            print(f'{index}/{len(selected)} processed',flush=True)
    from collections import Counter
    counts=dict(Counter(r['status'] for r in rows))
    (output/'summary.json').write_text(json.dumps(counts,ensure_ascii=False,indent=2))
    print(json.dumps(counts,ensure_ascii=False),flush=True)

if __name__=='__main__':
    main()
