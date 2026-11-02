#!/usr/bin/env python3
"""Export the original community FName display fields, preserving row identity.

This exports Crowdin sources only. NameProperty import is a separate operation;
these are deliberately not presented as FString or FText assets.
"""
import argparse
import hashlib
from pathlib import Path
import struct
import xml.etree.ElementTree as ET


FIELDS = (
    'CommunityName', 'CampDispCommunityCharacterNameA',
    'CampDispCommunityCharacterNameB', 'NPCFirstNameA', 'NPCLastNameA',
    'NPCFirstNameB', 'NPCLastNameB',
)


def export(source: Path, output: Path):
    data = source.read_bytes()

    def u32(offset):
        return struct.unpack_from('<I', data, offset)[0]

    def fname(offset):
        index, number = struct.unpack_from('<II', data, offset)
        if number or index >= len(names):
            raise ValueError(f'Unsupported FName at {offset:#x}')
        return names[index]

    names = []
    pos = u32(24)
    count = (u32(36) - 8) // 8
    for _ in range(count):
        header = int.from_bytes(data[pos:pos + 2], 'big')
        pos += 2
        wide, length = bool(header & 0x8000), header & 0x7fff
        if wide:
            pos += pos % 2  # UTF-16 entries are aligned, including after odd ASCII lengths.
        size = length * (2 if wide else 1)
        names.append(data[pos:pos + size].decode('utf-16-le' if wide else 'utf-8'))
        pos += size
    if pos > u32(32) or u32(32) - pos > 7:
        raise ValueError('Unexpected name-map boundaries')

    # This particular cooked table has one root ObjectProperty (RowStruct),
    # followed by None, an empty object GUID flag, and the row count.
    pos = u32(52)
    if u32(pos) != 0:
        raise ValueError('Unexpected export prefix')
    pos += 4
    if (fname(pos), fname(pos + 8), u32(pos + 16), u32(pos + 20), data[pos + 24]) != (
        'RowStruct', 'ObjectProperty', 4, 0, 0
    ):
        raise ValueError('Unexpected root properties')
    pos += 29
    if fname(pos) != 'None' or u32(pos + 8) != 0:
        raise ValueError('Unexpected table prefix')
    pos += 12
    rows = u32(pos)
    pos += 4
    root = ET.Element('P3RDataTableNames', source=source.name,
                      layout='community-name-properties',
                      sourceSha256=hashlib.sha256(data).hexdigest())
    entries = 0
    for _ in range(rows):
        row = fname(pos)
        pos += 8
        for field in FIELDS:
            if (fname(pos), fname(pos + 8), u32(pos + 16), u32(pos + 20), data[pos + 24]) != (
                field, 'NameProperty', 8, 0, 0
            ):
                raise ValueError(f'Unexpected property in {row}: {field}')
            pos += 25
            value_offset = pos
            value = fname(pos)
            pos += 8
            if value in ('None', 'ブランク'):
                continue
            if not value.isascii():
                raise ValueError(f'Non-English source value in {row}/{field}: {value}')
            node = ET.SubElement(root, 'Name', row=row, field=field,
                                 offset=f'0x{value_offset:X}')
            ET.SubElement(node, 'Source').text = value
            ET.SubElement(node, 'Translation').text = value
            entries += 1
        if fname(pos) != 'None':
            raise ValueError(f'Unexpected row terminator: {row}')
        pos += 8
    if pos != len(data):
        raise ValueError('Unexpected bytes after table rows')
    root.set('nameCount', str(entries))
    ET.indent(root, space='  ')
    output.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(root).write(output, encoding='utf-8', xml_declaration=True)
    print(f'Exported {entries} display fields from {rows} rows: {output}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    export(args.source, args.output)
