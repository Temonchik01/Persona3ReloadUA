"""Bounded UE4 Initial package-store subset; retoc writes archive structures."""
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'UE4-DDS-Tools/src'))
from unreal.city_hash import city_hash_64

MAX_PACKAGES = 100000

def subset_header(data, selected, container_name, payloads):
    """Retain store records/import IDs and culture redirects only for selected IDs."""
    if len(data) > 32 * 1024 * 1024 or len(data) < 32:
        raise ValueError('Unexpected header size')
    def count(pos, limit=MAX_PACKAGES):
        if pos < 0 or pos + 4 > len(data): raise ValueError('Header offset out of bounds')
        value = struct.unpack_from('<I', data, pos)[0]
        if value > limit: raise ValueError('Header count exceeds limit')
        return value
    pos = 12
    names = count(pos, len(data)); pos += 4 + names
    hashes = count(pos, len(data)); pos += 4 + hashes
    prefix = bytearray(data[:pos])
    n = count(pos); pos += 4
    if pos + n * 8 + 4 > len(data): raise ValueError('Truncated package IDs')
    ids = struct.unpack_from('<' + 'Q' * n, data, pos); pos += n * 8
    size = count(pos, len(data)); pos += 4
    end = pos + size
    if size < n * 32 or end > len(data): raise ValueError('Invalid store-entry buffer')
    lookup = {pid: i for i, pid in enumerate(ids)}
    if not selected <= lookup.keys(): raise ValueError('Missing original store entries')
    ordered = sorted(selected)
    records = bytearray(); imports = bytearray()
    for pid in ordered:
        start = pos + lookup[pid] * 32
        entry = bytearray(data[start:start + 32])
        num, rel = struct.unpack_from('<ii', entry, 24)
        pointer = start + 24 + rel
        if num < 0 or num > MAX_PACKAGES or (num and not pos <= pointer <= end - num * 8):
            raise ValueError('Invalid imported-package view')
        values = data[pointer:pointer + num * 8] if num else b''
        relative = len(ordered) * 32 + len(imports) - len(records) - 24
        struct.pack_into('<i', entry, 28, relative if num else 0)
        payload = payloads[pid].read_bytes()
        if len(payload) < 64: raise ValueError('Truncated Zen payload')
        export, bundles, graph = struct.unpack_from('<III', payload, 44)
        if not 64 <= export <= bundles <= graph <= len(payload) or (bundles - export) % 72:
            raise ValueError('Unsupported Zen export layout')
        exports = (bundles - export) // 72
        remainder = graph - bundles - exports * 16
        if exports < 1 or exports > 10000 or remainder < 8 or remainder % 8 or remainder // 8 > exports * 2:
            raise ValueError('Unsupported UE4 export bundles')
        serial_size = sum(struct.unpack_from('<Q', payload, export + i * 72 + 8)[0] for i in range(exports))
        struct.pack_into('<QII', entry, 0, serial_size, exports, remainder // 8)
        records += entry; imports += values
    # Read UE4 culture map. Preserve original source IDs; filter localized targets.
    pos = end
    cultures = count(pos, 1000); pos += 4; culture_records = []
    for _ in range(cultures):
        if pos + 4 > len(data): raise ValueError('Truncated culture name')
        length = struct.unpack_from('<i', data, pos)[0]
        byte_count = abs(length) * (2 if length < 0 else 1)
        if byte_count > 4096 or pos + 4 + byte_count > len(data): raise ValueError('Invalid culture name')
        name = data[pos:pos + 4 + byte_count]; pos += 4 + byte_count
        pairs = count(pos); pos += 4
        if pos + pairs * 16 > len(data): raise ValueError('Truncated culture map')
        retained = []
        for i in range(pairs):
            pair = data[pos + i * 16:pos + (i + 1) * 16]
            if struct.unpack_from('<Q', pair, 8)[0] in selected: retained.append(pair)
        pos += pairs * 16
        if retained: culture_records.append(name + struct.pack('<I', len(retained)) + b''.join(retained))
    redirects = count(pos); pos += 4
    if pos + redirects * 16 > len(data): raise ValueError('Truncated redirect table')
    retained = [data[pos + i * 16:pos + (i + 1) * 16] for i in range(redirects)
                if struct.unpack_from('<Q', data, pos + i * 16 + 8)[0] in selected]
    cid = city_hash_64(container_name.lower().encode('utf-16le'))
    struct.pack_into('<QI', prefix, 0, cid, len(ordered))
    result = prefix + struct.pack('<I', len(ordered)) + struct.pack('<' + 'Q' * len(ordered), *ordered)
    result += struct.pack('<I', len(records) + len(imports)) + records + imports
    result += struct.pack('<I', len(culture_records)) + b''.join(culture_records)
    result += struct.pack('<I', len(retained)) + b''.join(retained)
    return bytes(result), cid.to_bytes(8, 'little').hex() + '0000000a'
