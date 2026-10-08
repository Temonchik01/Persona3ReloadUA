from pathlib import Path
import os,subprocess,xml.etree.ElementTree as E,struct,json
ROOT=Path(__file__).resolve().parents[1];os.chdir(ROOT);DLL=Path(os.environ.get('P3R_DT_TOOL_DLL',str(ROOT/'tools/dt/linux/P3RDtTool.dll')));OUT=ROOT/'build/tests/dt-text-regression';OUT.mkdir(parents=True,exist_ok=True);rows=[]
def run(args,ok=True):
 r=subprocess.run(['prlimit','--core=0','--as=8589934592','--cpu=30','--','timeout','40s','dotnet',str(DLL),*map(str,args)],capture_output=True,text=True,env={**os.environ,'DOTNET_GCHeapHardLimit':'0x10000000','DOTNET_gcServer':'0'})
 if ok and r.returncode:raise AssertionError(r.stdout+r.stderr)
 if not ok:assert r.returncode!=0,'Unsafe edit unexpectedly accepted'
 return r
for xml in sorted((ROOT/'dt_xml/text').rglob('*.xml')):
 rel=xml.relative_to(ROOT/'dt_xml/text');src=ROOT/'tools/dt/source'/rel.with_suffix('.uasset');work=OUT/rel.with_suffix('');work.mkdir(parents=True,exist_ok=True)
 original=work/'original.xml';run(['--export-text-xml',src,original]);noop=work/'noop.uasset';run(['--import-text-xml',src,original,noop]);assert noop.read_bytes()==src.read_bytes(),f'No-op changed {rel}'
 target=work/'translated.uasset';r=run(['--import-text-xml',src,xml,target]);exported=work/'translated.xml';run(['--export-text-xml',target,exported]);new=E.parse(exported).getroot();old=E.parse(xml).getroot();assert len(new.findall('Text'))>=len(old.findall('Text'))
 changed=0;b=target.read_bytes()
 actuals={int(e.get('offset'),16):e for e in new.findall('Text')}
 delta=0
 for wanted in old.findall('Text'):
  value=wanted.findtext('Translation') or wanted.findtext('Source')
  expected_offset=int(wanted.get('offset'),16)+delta
  assert expected_offset in actuals,(rel,wanted.get('index'),'missing output block',hex(expected_offset))
  actual=actuals[expected_offset]
  assert actual.findtext('Source')==value,(rel,wanted.get('index'),actual.findtext('Source'),value)
  if value!=wanted.findtext('Source'):
   changed+=1;pos=expected_offset;n=struct.unpack_from('<i',b,pos)[0];size=abs(n)*(2 if n<0 else 1)
   assert struct.unpack_from('<i',b,pos-9)[0]==4+size
   assert actual.get('role')=='value'
   decoded=b[pos+4:pos+4+size-(2 if n<0 else 1)].decode('utf-16le' if n<0 else 'ascii');assert decoded==value
   if any(ord(c)>127 for c in value):assert n<0
   following=pos+4+size;fname=struct.unpack_from('<I',b,following)[0];namecount=(struct.unpack_from('<I',b,36)[0]-8)//8;assert fname<namecount
   oldlength=int(wanted.get('length'));delta+=size-abs(oldlength)*(2 if oldlength<0 else 1)
 export_offset=struct.unpack_from('<I',b,44)[0];graph,graph_size=struct.unpack_from('<II',b,52);assert struct.unpack_from('<Q',b,export_offset+8)[0]==len(b)-graph-graph_size
 rows.append({'xml':str(rel),'changed':changed,'no_op_identical':True,'translated_roundtrip':True,'original_bytes':src.stat().st_size,'new_bytes':len(b)})
 print(rel,changed,'changed; no-op and serialization checks passed',flush=True)
# Boundary tests on the exact resource from the crash report.
rel=Path('Xrd777/Field/Data/DataTable/Texts/DT_FldShortcutName');src=ROOT/'tools/dt/source'/rel.with_suffix('.uasset');base=OUT/rel/'original.xml'
for i,text in enumerate(['A','A much longer English name','Мапа міста','ЇЄҐі їєґ','Місто 😀', 'A'*600, 'Мапа\nміста', '-']):
 t=E.parse(base);t.getroot().find('Text/Translation').text=text;x=OUT/f'boundary-{i}.xml';t.write(x,encoding='utf-8',xml_declaration=True);u=OUT/f'boundary-{i}.uasset';run(['--import-text-xml',src,x,u]);e=OUT/f'boundary-{i}-export.xml';run(['--export-text-xml',u,e]);assert E.parse(e).getroot().findtext('Text/Source')==text
# Scanner false-positive (row/technical string) must be blocked.
t=E.parse(base);technical=next(e for e in t.getroot().findall('Text') if e.get('role')=='technical');technical.find('Translation').text='НЕ РЕДАГУВАТИ';x=OUT/'unsafe.xml';t.write(x,encoding='utf-8',xml_declaration=True);run(['--import-text-xml',src,x,OUT/'unsafe.uasset'],False)
# Wrong offsets and modified Source must not silently pass.
for case in ['offset','source']:
 t=E.parse(base);e=t.getroot().find('Text')
 if case=='offset':e.set('offset','0x1')
 else:e.find('Source').text='wrong original'
 x=OUT/f'invalid-{case}.xml';t.write(x,encoding='utf-8',xml_declaration=True)
 run(['--import-text-xml',src,x,OUT/f'invalid-{case}.uasset'],False)
(OUT/'report.json').write_text(json.dumps({'tables':rows,'boundary_cases':8,'technical_edit_rejected':True,'game_tested':False},indent=2));print('All regression checks passed.')
