"""Preserve multilingual save-name mappings and add translated place names.

A full game test confirmed that changing PlaceName labels requires matching
entries in SaveDataLocalize/DT_FldPlaceNameConvert. Original mappings and
conversion numbers remain intact; generation starts from pristine sources.
"""
from pathlib import Path
import json
import struct

def i32(data,pos):return struct.unpack_from('<i',data,pos)[0]

def read_place_table(data):
    if not 64<=len(data)<=16*1024*1024:raise ValueError('Invalid place table size')
    names=[];pos=i32(data,24)+1
    while data[pos]:
        size=data[pos];names.append(data[pos+1:pos+1+size].decode('utf-8'));pos+=size+2
    export=i32(data,44)
    if (i32(data,48)-export)//72!=1:raise ValueError('Expected one export')
    start=i32(data,52)+i32(data,56)
    def properties(pos):
        result={}
        while True:
            key,num=struct.unpack_from('<ii',data,pos)
            if not 0<=key<len(names):raise ValueError('Bad property FName')
            if names[key]=='None':return pos+8,result
            typ,tn,size,index=struct.unpack_from('<iiii',data,pos+8)
            if num or tn or index or size<0 or data[pos+24]!=0 or pos+25+size>len(data):raise ValueError('Unsupported property tag')
            result[names[key]]=(names[typ],data[pos+25:pos+25+size]);pos+=25+size
    pos,root=properties(start)
    if set(root)!={'RowStruct'} or root['RowStruct'][0]!='ObjectProperty' or i32(data,pos)!=0:raise ValueError('Unsupported table object prefix')
    count_offset=pos+4;count=i32(data,count_offset);pos=count_offset+4
    if not 0<count<10000:raise ValueError('Invalid row count')
    rows=[]
    for _ in range(count):
        begin=pos;key,number=struct.unpack_from('<ii',data,pos);pos,props=properties(pos+8)
        if set(props)!={'Index','Name'} or props['Index'][0]!='IntProperty' or props['Name'][0]!='StrProperty':raise ValueError('Unsupported row schema')
        index=i32(props['Index'][1],0);raw=props['Name'][1];n=i32(raw,0)
        size=abs(n)*(2 if n<0 else 1)
        if n==0 or size+4!=len(raw) or raw[-1]!=0:raise ValueError('Bad FString size')
        value=raw[4:-(2 if n<0 else 1)].decode('utf-16le' if n<0 else 'ascii')
        rows.append({'key':(key,number),'index':index,'value':value,'bytes':data[begin:pos]})
    if pos!=len(data) or len({r['key'] for r in rows})!=count:raise ValueError('Unexpected trailing data or duplicate row keys')
    return count_offset,export,rows

def read_conversion_table(data):
    if not 64<=len(data)<=16*1024*1024:raise ValueError('Invalid table size')
    names=[];pos=i32(data,24)+1
    while data[pos]:
        size=data[pos];names.append(data[pos+1:pos+1+size].decode('utf-8'));pos+=size+2
    export=i32(data,44)
    if (i32(data,48)-export)//72!=1:raise ValueError('Expected one export')
    start=i32(data,52)+i32(data,56)
    def properties(pos):
        result={}
        while True:
            key,num=struct.unpack_from('<ii',data,pos)
            if not 0<=key<len(names):raise ValueError('Bad property key')
            if names[key]=='None':return pos+8,result
            typ,tn,size,array=struct.unpack_from('<iiii',data,pos+8)
            if num or tn or array or size<0 or data[pos+24]!=0 or pos+25+size>len(data):raise ValueError('Unsupported property tag')
            result[names[key]]={'type':names[typ],'tag':pos,'payload':pos+25,'raw':data[pos+25:pos+25+size]}
            pos+=25+size
    pos,root=properties(start)
    if set(root)!={'RowStruct'} or i32(data,pos)!=0:raise ValueError('Unsupported object prefix')
    offset=pos+4;count=i32(data,offset);pos=offset+4
    if not 0<count<100000:raise ValueError('Invalid row count')
    rows=[]
    for _ in range(count):
        begin=pos;key,num=struct.unpack_from('<ii',data,pos);pos,props=properties(pos+8)
        if 'Name' not in props or set(props)-{'Name','Number'} or props['Name']['type']!='StrProperty':raise ValueError('Unsupported converter schema')
        raw=props['Name']['raw'];n=i32(raw,0);size=abs(n)*(2 if n<0 else 1)
        if size+4!=len(raw):raise ValueError('Invalid FString size')
        text=raw[4:-(2 if n<0 else 1)].decode('utf-16le' if n<0 else 'utf-8') if n else ''
        number=i32(props['Number']['raw'],0) if 'Number' in props else 0
        rows.append({'key':(key,num),'number':number,'name':text,'bytes':data[begin:pos],'name_payload':props['Name']['payload']-begin,'name_tag':props['Name']['tag']-begin})
    if pos!=len(data) or len({r['key'] for r in rows})!=count:raise ValueError('Invalid row boundaries or keys')
    return offset,export,rows

def build_converter(original_place, translated_place, original_converter, output_path):
    _,_,english=read_place_table(Path(original_place).read_bytes())
    _,_,ukrainian=read_place_table(Path(translated_place).read_bytes())
    source=Path(original_converter).read_bytes()
    offset,export,rows=read_conversion_table(source)
    # Conversion numbers are not always the direct PlaceName Index. Match
    # existing English names and retain the Number supplied by the original
    # conversion table (e.g. the intro station shares its ordinary name).
    translations={}
    for old,new in zip(english,ukrainian):
        if old['key']!=new['key'] or old['index']!=new['index']:raise ValueError('Place row identity changed')
        if old['value']!=new['value']:translations.setdefault(old['value'],set()).add(new['value'])
    aliases=[];pairs=[];seen=set();matched_names=set()
    for matched in rows:
        for value in sorted(translations.get(matched['name'],set())):
            identity=(matched['number'],value)
            matched_names.add(matched['name'])
            if identity in seen:continue
            seen.add(identity)
            data=bytearray(matched['bytes']);payload=matched['name_payload'];n=i32(data,payload);oldsize=abs(n)*(2 if n<0 else 1)
            encoded=(value+'\0').encode('utf-16le');data[payload:payload+4+oldsize]=struct.pack('<i',-(len(value)+1))+encoded
            struct.pack_into('<i',data,matched['name_tag']+16,4+len(encoded));struct.pack_into('<i',data,4,matched['key'][1]+100000+len(aliases))
            aliases.append(bytes(data));pairs.append({'number':matched['number'],'original':matched['name'],'ukrainian':value})
    unmatched=sorted(set(translations)-matched_names)
    output=bytearray(source[:offset]+struct.pack('<i',len(rows)+len(aliases))+source[offset+4:]+b''.join(aliases))
    struct.pack_into('<q',output,export+8,struct.unpack_from('<q',source,export+8)[0]+len(output)-len(source))
    _,_,checked=read_conversion_table(output)
    if [r['bytes'] for r in checked[:len(rows)]]!=[r['bytes'] for r in rows]:raise ValueError('Original mappings changed')
    actual={(r['number'],r['name']) for r in checked}
    if not all((p['number'],p['ukrainian']) in actual for p in pairs):raise ValueError('Missing Ukrainian mapping')

    if unmatched:
        raise ValueError('Translated place names have no original save mapping: ' + ', '.join(unmatched))
    output_path=Path(output_path)
    output_path.parent.mkdir(parents=True,exist_ok=True)
    output_path.write_bytes(output)
    return {'original_converter_rows':len(rows),'translated_mappings_added':len(aliases),
            'total_converter_rows':len(checked),'original_rows_byte_identical':True,
            'output':str(output_path),'mappings':pairs}
