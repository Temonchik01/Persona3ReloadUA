"""Missing templates and technical edits must fail before any batch output."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as E

ROOT=Path(__file__).resolve().parents[1]
DLL=ROOT/'dev/tools/dt/linux/P3RDtTool.dll'
ENV={**os.environ,'DOTNET_GCHeapHardLimit':'0x10000000'}
def run(*args):
    return subprocess.run(['timeout','40s','dotnet',str(DLL),*map(str,args)],capture_output=True,text=True,env=ENV)
with tempfile.TemporaryDirectory() as directory:
    base=Path(directory);sources=base/'source';sources.mkdir();xmls=base/'xml/text';xmls.mkdir(parents=True)
    source=ROOT/'dev/tools/dt/source/Astrea/Field/Data/DataTable/Texts/DT_FldShortcutName.uasset'
    shutil.copy2(source,sources/'valid.uasset')
    original=base/'original.xml';assert run('--export-text-xml',source,original).returncode==0
    shutil.copy2(original,xmls/'valid.xml');shutil.copy2(original,xmls/'missing.xml')
    result=run('--batch-import-xml',base/'xml',sources,base/'output','--force')
    assert result.returncode==1 and 'missing.uasset' in result.stderr,result.stderr
    assert 'Unhandled exception' not in result.stderr and not (base/'output').exists()
    (xmls/'missing.xml').unlink()
    tree=E.parse(original);technical=next(e for e in tree.getroot().findall('Text') if e.get('role')=='technical')
    technical.find('Translation').text='НЕБЕЗПЕЧНА ЗМІНА';tree.write(xmls/'valid.xml',encoding='utf-8',xml_declaration=True)
    result=run('--batch-import-xml',base/'xml',sources,base/'output','--force')
    assert result.returncode==1 and 'no resources imported' in result.stderr,result.stderr
    assert 'valid.xml' in result.stderr and not (base/'output').exists()
print('Missing-source and unsafe-edit preflight checks passed; no output written.')
