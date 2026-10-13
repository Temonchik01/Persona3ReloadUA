"""Apply the game-tested dialogue text-area height, without altering fonts."""
from pathlib import Path
import struct


def build_dialog_layout(source, output):
    data = Path(source).read_bytes()
    def integer(pos):
        return struct.unpack_from('<i', data, pos)[0]
    names, pos = [], integer(24)+1
    while data[pos]:
        size = data[pos]
        names.append(data[pos+1:pos+size+1].decode('utf-8'))
        pos += size+2
    def properties(pos):
        fields = {}
        while True:
            key, number = integer(pos), integer(pos+4)
            if not 0 <= key < len(names):
                raise ValueError('Invalid dialogue property name')
            if names[key] == 'None':
                return pos+8, fields
            typ, tn, size, index = [integer(pos+k) for k in (8,12,16,20)]
            if number or tn or index or size != 4 or data[pos+24] != 0:
                raise ValueError('Unsupported dialogue property')
            if names[typ] not in ('ObjectProperty','FloatProperty'):
                raise ValueError('Unsupported dialogue property type')
            fields[names[key]] = (names[typ], pos+25)
            pos += 29
    pos, header = properties(integer(52)+integer(56))
    if set(header) != {'RowStruct'} or integer(pos) != 0:
        raise ValueError('Unsupported dialogue table prefix')
    count, pos = integer(pos+4), pos+8
    rows = {}
    for _ in range(count):
        key, number = integer(pos), integer(pos+4)
        if number:
            raise ValueError('Unsupported dialogue row key')
        pos, fields = properties(pos+8)
        rows[names[key]] = fields
    if pos != len(data) or set(rows) != {'MSG_TEXT_COL','MSG_OFFSET','MSG_ROW'}:
        raise ValueError('Unsupported dialogue rows')
    kind, offset = rows['MSG_TEXT_COL']['Y']
    before = struct.unpack_from('<f', data, offset)[0]
    if kind != 'FloatProperty' or before != 175.0:
        raise ValueError('Dialogue height template changed')
    result = bytearray(data)
    struct.pack_into('<f', result, offset, 199.0)
    if result[:offset] != data[:offset] or result[offset+4:] != data[offset+4:]:
        raise ValueError('Unrelated dialogue bytes changed')
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    return {'row':'MSG_TEXT_COL','property':'Y','before':175,'after':199,
            'offset':offset,'only_height_changed':True}
