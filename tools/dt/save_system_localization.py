"""Preserve save-system localization and append aliases for English replacements."""
from pathlib import Path
import struct
import hashlib
import json

ENGLISH_BIT = 2


def i32(data, offset):
    if not 0 <= offset <= len(data) - 4:
        raise ValueError('Integer outside asset boundaries')
    return struct.unpack_from('<i', data, offset)[0]


def fstring(data, offset=0):
    length = i32(data, offset)
    if length == 0:
        return '', offset + 4
    if length == -2147483648:
        raise ValueError('Invalid FString length')
    terminator = 2 if length < 0 else 1
    end = offset + 4 + abs(length) * terminator
    if end > len(data) or data[end-terminator:end] != bytes(terminator):
        raise ValueError('Invalid FString payload')
    value = data[offset+4:end-terminator].decode('utf-16le' if length < 0 else 'utf-8')
    return value, end


def ftext(data):
    if len(data) < 5 or data[4] != 0:
        raise ValueError('Only Base FText history is supported')
    namespace, pos = fstring(data, 5)
    key, pos = fstring(data, pos)
    value, pos = fstring(data, pos)
    if pos != len(data):
        raise ValueError('Trailing FText data')
    return namespace, key, value


def read_table(data):
    if not 64 <= len(data) <= 16*1024*1024:
        raise ValueError('Invalid system table size')
    names = []
    pos = i32(data, 24) + 1
    while True:
        if not 0 <= pos < len(data):
            raise ValueError('Invalid name table')
        size = data[pos]
        if not size:
            break
        if pos + size + 2 > len(data) or data[pos+size+1] != 0:
            raise ValueError('Invalid name entry')
        names.append(data[pos+1:pos+size+1].decode('utf-8'))
        pos += size + 2
    export = i32(data, 44)
    if (i32(data, 48) - export) != 72:
        raise ValueError('Expected exactly one export')
    start = i32(data, 52) + i32(data, 56)

    def properties(pos):
        result = {}
        while True:
            key, number = i32(data, pos), i32(data, pos+4)
            if not 0 <= key < len(names):
                raise ValueError('Invalid property name')
            name = names[key]
            if name == 'None':
                return pos + 8, result
            typ, type_number, size, index = [i32(data, pos+n) for n in (8,12,16,20)]
            if not 0 <= typ < len(names) or number or type_number or index or size < 0:
                raise ValueError('Unsupported property tag')
            kind = names[typ]
            if kind not in ('ObjectProperty','IntProperty','StrProperty','TextProperty','BoolProperty'):
                raise ValueError('Unsupported property type: '+kind)
            header = 26 if kind == 'BoolProperty' else 25
            payload = pos + header
            if payload + size > len(data) or data[payload-1] != 0 or name in result:
                raise ValueError('Invalid property boundaries')
            if kind == 'BoolProperty' and (size or data[pos+24] not in (0,1)):
                raise ValueError('Invalid BoolProperty')
            result[name] = {'type':kind, 'tag':pos, 'payload':payload,
                            'raw':data[payload:payload+size]}
            pos = payload + size
    pos, root = properties(start)
    if set(root) != {'RowStruct'} or i32(data, pos) != 0:
        raise ValueError('Unsupported table prefix')
    count_offset = pos+4
    count = i32(data, count_offset)
    if not 0 < count < 100000:
        raise ValueError('Invalid row count')
    pos = count_offset+4
    rows = []
    keys = set()
    for _ in range(count):
        begin = pos
        key = (i32(data,pos), i32(data,pos+4))
        if not 0 <= key[0] < len(names) or key in keys:
            raise ValueError('Invalid or duplicate row key')
        keys.add(key)
        pos, props = properties(pos+8)
        rows.append({'key':key,'props':props,'begin':begin,'bytes':data[begin:pos]})
    if pos != len(data):
        raise ValueError('Unexpected trailing table data')
    return names, count_offset, export, rows


def build_system_converter(original_system, translated_system, original_converter, output_path):
    names, _, _, old = read_table(Path(original_system).read_bytes())
    new_names, _, _, new = read_table(Path(translated_system).read_bytes())
    if names != new_names or len(old) != len(new):
        raise ValueError('System table identity changed')
    edits = {}
    for a, b in zip(old, new):
        if a['key'] != b['key'] or set(a['props']) != set(b['props']):
            raise ValueError('System row identity changed')
        for key in a['props']:
            if key != 'Name' and a['props'][key]['raw'] != b['props'][key]['raw']:
                raise ValueError('Technical system property changed: '+key)
        if a['props']['Name']['type'] != 'TextProperty' or b['props']['Name']['type'] != 'TextProperty':
            raise ValueError('Unsupported system Name type')
        source = ftext(a['props']['Name']['raw'])
        target = ftext(b['props']['Name']['raw'])
        if source[:2] != target[:2]:
            raise ValueError('FText namespace/key changed')
        if source[2] != target[2]:
            index = int(names[a['key'][0]])
            if index in edits:
                raise ValueError('Duplicate system index')
            edits[index] = (source[2], target[2])
    template_path = Path(original_converter)
    data = template_path.read_bytes()
    provenance = template_path.with_suffix('.json')
    if provenance.is_file():
        expected = json.loads(provenance.read_text()).get('sha256')
        if expected != hashlib.sha256(data).hexdigest():
            raise ValueError('Original system converter template checksum mismatch')
    _, offset, export, rows = read_table(data)
    keys = {r['key'] for r in rows}
    aliases = []
    details = []
    counter = 1
    english_names = set()
    for row in rows:
        props = row['props']
        if set(props) != {'Name','Ext','LangBits','Index'} or props['Name']['type'] != 'StrProperty':
            raise ValueError('Unsupported system converter schema')
        index = i32(props['Index']['raw'],0)
        mask = i32(props['LangBits']['raw'],0)
        value, end = fstring(props['Name']['raw'])
        if end != len(props['Name']['raw']):
            raise ValueError('Malformed converter Name')
        if mask & ENGLISH_BIT:
            english_names.add((index,value))
    for row in rows:
        props = row['props']
        index = i32(props['Index']['raw'],0)
        if index not in edits or not (i32(props['LangBits']['raw'],0) & ENGLISH_BIT):
            continue
        source, value = edits[index]
        if (index,value) in english_names:
            continue
        # Retain each English template's metadata, including Ext variations.
        raw = (value+'\0').encode('utf-16le')
        payload = struct.pack('<i',-(len(raw)//2))+raw
        template = bytearray(row['bytes'])
        tag = props['Name']['tag']-row['begin']
        pos = props['Name']['payload']-row['begin']
        size = len(props['Name']['raw'])
        struct.pack_into('<i',template,tag+16,len(payload))
        while (rows[0]['key'][0],counter) in keys:
            counter += 1
        key = (rows[0]['key'][0],counter)
        struct.pack_into('<ii',template,0,*key)
        keys.add(key)
        counter += 1
        aliases.append(bytes(template[:pos])+payload+bytes(template[pos+size:]))
        details.append({'index':index,'source':source,'translation':value})
    result = bytearray(data[:offset]+struct.pack('<i',len(rows)+len(aliases))+data[offset+4:]+b''.join(aliases))
    struct.pack_into('<q',result,export+8,struct.unpack_from('<q',data,export+8)[0]+len(result)-len(data))
    _, _, _, checked = read_table(result)
    if [r['bytes'] for r in checked[:len(rows)]] != [r['bytes'] for r in rows]:
        raise ValueError('Original converter rows changed')
    if any(not (i32(r['props']['LangBits']['raw'],0)&ENGLISH_BIT) for r in checked[len(rows):]):
        raise ValueError('Generated alias missing English language bit')
    dest = Path(output_path)
    dest.parent.mkdir(parents=True,exist_ok=True)
    dest.write_bytes(result)
    return {'original_rows':len(rows),'translated_mappings_added':len(aliases),
            'original_rows_byte_identical':True,'english_language_bit':ENGLISH_BIT,
            'mapped':details,'translated_values_without_converter_id':[
                {'index':k,'source':v[0],'translation':v[1]} for k,v in edits.items()
                if k not in {d['index'] for d in details}]}
