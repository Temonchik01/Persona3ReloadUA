"""Check the supplied nested DT fixtures without modifying mod outputs."""
from pathlib import Path
import json, struct, xml.etree.ElementTree as E
import os, subprocess
ROOT=Path(__file__).resolve().parents[1]
def run(args):
    r=subprocess.run(['prlimit','--core=0','--as=8589934592','--cpu=30','--','timeout','40s','dotnet',str(ROOT/'tools/dt/linux/P3RDtTool.dll'),*map(str,args)],capture_output=True,text=True,env={**os.environ,'DOTNET_GCHeapHardLimit':'0x10000000','DOTNET_gcServer':'0'})
    if r.returncode: raise AssertionError(r.stdout+r.stderr)
    return r
OUT=ROOT/'build/tests/dt-nested';OUT.mkdir(parents=True,exist_ok=True);XML=OUT/'xml';rows=[]
# Fresh test exports must never overwrite a translator's workspace XML.
import shutil
available=Path(os.environ.get('P3R_DT_TEST_SOURCE', str(ROOT/'dt test')))
if not available.is_dir(): available=ROOT/'workspace/dt-fmodel-en-20261008/source'
if not available.is_dir(): raise SystemExit('Set P3R_DT_TEST_SOURCE to a directory of original DT test assets.')
SOURCE=OUT/'source';SOURCE.mkdir(exist_ok=True)
for asset in available.rglob('*.uasset'):
    rel=asset.relative_to(available)
    if 'Blueprints' not in rel.parts and 'UI/Tables/' not in rel.as_posix():continue
    target=SOURCE/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(asset,target)
run(['--batch-export-xml',SOURCE,XML])
def i32(b,p):return struct.unpack_from('<i',b,p)[0]
for src in sorted(SOURCE.rglob('*.uasset')):
 rel=src.relative_to(SOURCE);work=OUT/'checks'/rel.with_suffix('');work.mkdir(parents=True,exist_ok=True)
 xml=work/'source.xml';run(['--export-text-xml',src,xml]);tree=E.parse(xml);nodes=tree.getroot().findall('Text')
 noop=work/'noop.uasset';run(['--import-text-xml',src,xml,noop]);original=src.read_bytes();assert noop.read_bytes()==original,rel
 candidates=[e for e in nodes if e.get('role')=='value']
 row={'file':str(rel),'strings':len(nodes),'editable':len(candidates),'noop_identical':True}
 if candidates:
  chosen=next((e for e in candidates if e.findtext('Source')=='Pursue the target'),candidates[0])
  chosen.find('Translation').text='Переслідуйте ціль 😀';edited=work/'edited.xml';tree.write(edited,encoding='utf-8',xml_declaration=True)
  target=work/'edited.uasset';run(['--import-text-xml',src,edited,target]);b=target.read_bytes();newxml=work/'edited-export.xml';run(['--export-text-xml',target,newxml]);new=E.parse(newxml).getroot().findall('Text');assert len(new)==len(nodes),rel
  for before,after in zip(nodes,new):assert after.findtext('Source')==before.findtext('Translation'),(rel,before.get('offset'))
  # Independently find every Array/StructProperty tag enclosing this string.
  names=[];p=i32(original,24)+1
  while original[p]:n=original[p];names.append(original[p+1:p+1+n].decode());p+=n+2
  start=i32(original,52)+i32(original,56);stringpos=int(chosen.get('offset'),16);oldn=i32(original,stringpos);oldbytes=abs(oldn)*(2 if oldn<0 else 1);delta=len(b)-len(original);containers=0
  for pos in range(start,len(original)-49):
   name,an,typ,tn,size,ix=struct.unpack_from('<6i',original,pos)
   if not(0<=name<len(names) and 0<=typ<len(names)) or an or tn or ix:continue
   extra={'ArrayProperty':8,'StructProperty':24}.get(names[typ])
   if extra is None:continue
   payload=pos+25+extra
   if original[payload-1] or size<=0 or payload+size>len(original):continue
   if payload<=stringpos-25 and stringpos+4+oldbytes<=payload+size:
    assert i32(b,pos+16)==size+delta,(rel,hex(pos),size,delta,i32(b,pos+16));containers+=1
  row.update(unicode_roundtrip=True,container_sizes_checked=containers,delta=delta)
 rows.append(row);print(rel,row.get('container_sizes_checked','no editable tagged strings'),flush=True)
(OUT/'report.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2))
print('All nested DT fixture checks passed.')
arrays=[]
for xml in sorted((XML/'fstrings').rglob('*.xml')):
 rel=xml.relative_to(XML/'fstrings');src=SOURCE/rel.with_suffix('.uasset');work=OUT/'array-checks'/rel.with_suffix('');work.mkdir(parents=True,exist_ok=True)
 tree=E.parse(xml);assert tree.getroot().get('layout') in ('array-str','array-text'),rel
 original=src.read_bytes();noop=work/'noop.uasset';run(['--import-fstrings-xml',src,xml,noop]);assert noop.read_bytes()==original,rel
 nodes=tree.getroot().findall('String');assert nodes,rel
 nodes[0].find('Translation').text='Українська назва 😀';edit=work/'edited.xml';tree.write(edit,encoding='utf-8',xml_declaration=True);target=work/'edited.uasset';run(['--import-fstrings-xml',src,edit,target]);newxml=work/'edited-export.xml';run(['--export-fstrings-xml',target,newxml]);new=E.parse(newxml).getroot().findall('String');assert len(nodes)==len(new),rel
 for before,after in zip(nodes,new):assert before.findtext('Translation')==after.findtext('Source'),rel
 b=target.read_bytes();start=i32(original,52)+i32(original,56);delta=len(b)-len(original);assert i32(b,start+16)==i32(original,start+16)+delta,rel
 assert struct.unpack_from('<q',b,i32(b,44)+8)[0]==len(b)-start
 arrays.append({'file':str(rel),'layout':tree.getroot().get('layout'),'strings':len(nodes),'noop_identical':True,'unicode_roundtrip':True,'array_size_checked':True})
(OUT/'array-report.json').write_text(json.dumps(arrays,ensure_ascii=False,indent=2));print('All structured array checks passed:',len(arrays))
# Several edits in one structure must accumulate, including shrinking a value.
src=SOURCE/'Xrd777/UI/Tables/DatSuggestionTextDataAsset.uasset'
xml=XML/'text/Xrd777/UI/Tables/DatSuggestionTextDataAsset.xml';tree=E.parse(xml)
values=[e for e in tree.getroot().findall('Text') if e.get('role')=='value']
for e,value in zip(values[:3],['А','Дуже довга українська підказка 😀'*4,'X']):e.find('Translation').text=value
work=OUT/'multi-edit';work.mkdir(exist_ok=True);edit=work/'input.xml';tree.write(edit,encoding='utf-8',xml_declaration=True);target=work/'output.uasset';run(['--import-text-xml',src,edit,target]);export=work/'output.xml';run(['--export-text-xml',target,export])
for before,after in zip(tree.getroot().findall('Text'),E.parse(export).getroot().findall('Text')):assert before.findtext('Translation')==after.findtext('Source')
b=target.read_bytes();old=src.read_bytes();start=i32(old,52)+i32(old,56);delta=len(b)-len(old)
assert i32(b,start+16)==i32(old,start+16)+delta
assert i32(b,start+37+16)==i32(old,start+37+16)+delta
# Labels and comments may never be changed by the translation importer.
tree=E.parse(xml);node=next(e for e in tree.getroot().findall('Text') if e.get('property')=='TextLabel');node.find('Translation').text='НЕ МІНЯТИ';bad=work/'technical.xml';tree.write(bad,encoding='utf-8',xml_declaration=True)
try:run(['--import-text-xml',src,bad,work/'unsafe.uasset'])
except AssertionError:pass
else:raise AssertionError('Technical label edit accepted')
print('Accumulated nested sizes and technical-label protection passed.')
