"""Merge bounded UE4 Initial headers and raw chunks into one compact container."""
import hashlib
import json
from pathlib import Path
import shutil
import struct
from compact_container import city_hash_64

def parse(data):
    if not 32 <= len(data) <= 32 * 1024 * 1024: raise ValueError('Invalid header size')
    def count(pos, limit=100000):
        if not 0 <= pos <= len(data)-4: raise ValueError('Offset outside header')
        n=struct.unpack_from('<I',data,pos)[0]
        if n>limit: raise ValueError('Count exceeds limit')
        return n
    pos=12
    pos+=4+count(pos,len(data));pos+=4+count(pos,len(data))
    prefix=data[12:pos]
    n=count(pos);pos+=4
    if pos+n*8+4>len(data):raise ValueError('Truncated IDs')
    ids=struct.unpack_from('<'+'Q'*n,data,pos);pos+=n*8
    size=count(pos,len(data));pos+=4;end=pos+size
    if size<n*32 or end>len(data):raise ValueError('Invalid records')
    records={}
    for i,pid in enumerate(ids):
        start=pos+i*32;e=bytearray(data[start:start+32]);num,rel=struct.unpack_from('<ii',e,24);ptr=start+24+rel
        if num<0 or num>100000 or (num and not pos<=ptr<=end-num*8):raise ValueError('Invalid imports')
        imports=data[ptr:ptr+num*8] if num else b''
        struct.pack_into('<i',e,28,0);records[pid]=(bytes(e),imports)
    pos=end;cultures={}
    nc=count(pos,1000);pos+=4
    for _ in range(nc):
        if pos+4>len(data):raise ValueError('Truncated culture')
        length=struct.unpack_from('<i',data,pos)[0];sz=abs(length)*(2 if length<0 else 1)
        if sz>4096 or pos+4+sz>len(data):raise ValueError('Invalid culture')
        name=data[pos:pos+4+sz];pos+=4+sz
        np=count(pos);pos+=4
        if pos+np*16>len(data):raise ValueError('Truncated culture pairs')
        cultures[name]=[struct.unpack_from('<QQ',data,pos+i*16) for i in range(np)];pos+=np*16
    nr=count(pos);pos+=4
    if pos+nr*16>len(data):raise ValueError('Truncated redirects')
    redirects=[struct.unpack_from('<QQ',data,pos+i*16) for i in range(nr)]
    return prefix,records,cultures,redirects

def merge(out, font, run):
    inputs=sorted(out.glob('raw-*'))
    if (out/'additional-raw').is_dir():inputs.append(out/'additional-raw')
    if not inputs:raise ValueError('No raw compact inputs')
    raw=out/'merged-raw';(raw/'chunks').mkdir(parents=True, exist_ok=True)
    entries={};cultures={};redirects={};prefix=None;paths={};digests={};versions=set()
    for folder in inputs:
        manifest=json.loads((folder/'manifest.json').read_text());versions.add(manifest['version'])
        for chunk in (folder/'chunks').iterdir():
            if chunk.name.endswith('0a'):
                p,records,cs,rs=parse(chunk.read_bytes())
                if folder.name == 'additional-raw':
                    # Legacy template package names retain the donor culture.
                    # Register each alias using its real mounted path instead.
                    cs = {}; rs = []
                    for alias_id, alias_path in manifest['chunk_paths'].items():
                        if not alias_id.endswith('02') or '/Content/L10N/' not in alias_path:
                            continue
                        culture, relative = alias_path.split('/Content/L10N/', 1)[1].split('/', 1)
                        source_name = '/Game/' + str(Path(relative).with_suffix(''))
                        source_id = city_hash_64(source_name.lower().encode('utf-16le'))
                        target_id = int.from_bytes(bytes.fromhex(alias_id)[:8], 'little')
                        culture_name = culture.encode('utf-8') + b'\0'
                        key = struct.pack('<i', len(culture_name)) + culture_name
                        cs.setdefault(key, []).append((source_id, target_id))
                if prefix is not None and prefix!=p:raise ValueError('Different container name maps')
                prefix=p
                for pid,value in records.items():
                    if pid in entries and entries[pid]!=value:raise ValueError('Conflicting package metadata')
                    entries[pid]=value
                for culture,pairs in cs.items():
                    mapping=cultures.setdefault(culture,{})
                    for source,target in pairs:
                        if source in mapping and mapping[source]!=target:raise ValueError(f'Conflicting culture redirect in {folder.name}: {culture!r}, {source:x} -> {mapping[source]:x} / {target:x}')
                        mapping[source]=target
                for source,target in rs:
                    if source in redirects and redirects[source]!=target:raise ValueError('Conflicting package redirect')
                    redirects[source]=target
            else:
                digest=hashlib.sha256(chunk.read_bytes()).hexdigest()
                if chunk.name in digests and digests[chunk.name]!=digest:raise ValueError('Conflicting chunk payload')
                if chunk.name not in digests:shutil.copy2(chunk,raw/'chunks'/chunk.name)
                digests[chunk.name]=digest
                path=manifest['chunk_paths'].get(chunk.name)
                if path:
                    if chunk.name in paths and paths[chunk.name]!=path:raise ValueError('Conflicting chunk path')
                    paths[chunk.name]=path
    if versions!={'PartitionSize'}:raise ValueError('Unsupported TOC version')
    ids=sorted(entries);records=bytearray();imports=bytearray()
    for pid in ids:
        record,values=entries[pid];record=bytearray(record)
        rel=len(ids)*32+len(imports)-len(records)-24
        struct.pack_into('<i',record,28,rel if values else 0);records+=record;imports+=values
    dist=out/'mod';dist.mkdir();base=dist/'pakchunk999-Ukrainian_P';cid=city_hash_64(base.name.lower().encode('utf-16le'))
    header=struct.pack('<QI',cid,len(ids))+prefix+struct.pack('<I',len(ids))+struct.pack('<'+'Q'*len(ids),*ids)
    header+=struct.pack('<I',len(records)+len(imports))+records+imports+struct.pack('<I',len(cultures))
    for name,pairs in sorted(cultures.items()):header+=name+struct.pack('<I',len(pairs))+b''.join(struct.pack('<QQ',*p) for p in sorted(pairs.items()))
    header+=struct.pack('<I',len(redirects))+b''.join(struct.pack('<QQ',*p) for p in sorted(redirects.items()))
    parsed=parse(header)
    if parsed[1]!=entries:raise ValueError('Merged metadata roundtrip mismatch')
    (raw/'chunks'/(cid.to_bytes(8,'little').hex()+'0000000a')).write_bytes(header)
    (raw/'manifest.json').write_text(json.dumps({'version':'PartitionSize','mount_point':'../../../','chunk_paths':paths},indent=2))
    if {p.name for p in (raw/'chunks').iterdir()} != set(digests) | {cid.to_bytes(8,'little').hex()+'0000000a'}:
        raise ValueError('Stale chunks in merge folder; use a new build directory')
    toc=base.with_suffix('.utoc');run(['pack-raw',raw,toc],'merged-pack');run(['verify',toc],'merged-verify');run(['info',toc],'merged-info')
    # Re-extract only this compact container, not any original game containers.
    check=out/'merged-validation';run(['unpack-raw',toc,check],'merged-extract-check')
    for cid,digest in digests.items():
        if hashlib.sha256((check/'chunks'/cid).read_bytes()).hexdigest()!=digest:raise ValueError('Merged resource hash mismatch')
    shutil.copy2(font,base.with_suffix('.pak'))
    result={'package_count':len(ids),'resource_chunks':len(digests),'file_count':3,'total_bytes':sum(p.stat().st_size for p in dist.iterdir()),'game_loading_verified':False}
    (out/'MERGED_BUILD.json').write_text(json.dumps(result,indent=2));return dist,result
