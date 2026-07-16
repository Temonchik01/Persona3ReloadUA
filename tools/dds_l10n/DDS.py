import os
import struct
import sys

DXGI_FORMATS = {
    2:  "R32G32B32A32_FLOAT",
    10: "R16G16B16A16_FLOAT",
    28: "R8G8B8A8_UNORM",
    29: "R8G8B8A8_UNORM_SRGB",
    41: "R32_FLOAT",
    61: "R8_UNORM",
    70: "BC1_UNORM",
    71: "BC1_UNORM_SRGB",
    72: "BC2_UNORM",
    73: "BC2_UNORM_SRGB",
    74: "BC3_UNORM",
    75: "BC3_UNORM_SRGB",
    76: "BC4_UNORM",
    77: "BC4_SNORM",
    78: "BC5_UNORM",
    79: "BC5_SNORM",
    80: "BC6H_UF16",
    81: "BC6H_SF16",
    82: "BC7_UNORM",
    83: "BC7_UNORM_SRGB",
    87: "B8G8R8A8_UNORM",
    91: "B8G8R8A8_UNORM_SRGB",
    98: "BC7_UNORM",
    99: "BC7_UNORM_SRGB",
}

LEGACY_FOURCC = {
    b"DXT1": "BC1_UNORM",
    b"DXT2": "BC2_UNORM",
    b"DXT3": "BC2_UNORM",
    b"DXT4": "BC3_UNORM",
    b"DXT5": "BC3_UNORM",
    b"ATI1": "BC4_UNORM",
    b"BC4U": "BC4_UNORM",
    b"BC4S": "BC4_SNORM",
    b"ATI2": "BC5_UNORM",
    b"BC5U": "BC5_UNORM",
    b"BC5S": "BC5_SNORM",
}

def get_colorspace(fmt):
    if "SRGB" in fmt or "SRGB" in fmt.upper():
        return "sRGB"
    if fmt.startswith("DX10") or any(x in fmt for x in ["UNORM", "SNORM", "FLOAT", "UINT", "SINT", "BC1", "BC2", "BC3", "BC4", "BC5", "BC6", "BC7", "DXT", "ATI"]):
        return "Linear"
    return ""

def get_dds_format(file_path):
    try:
        with open(file_path, 'rb') as f:
            if f.read(4) != b'DDS ':
                return "Invalid DDS signature"

            f.seek(80)
            pf_flags = struct.unpack('<I', f.read(4))[0]
            four_cc = f.read(4)

            if pf_flags & 0x4:
                if four_cc == b'DX10':
                    f.seek(128)
                    dxgi = struct.unpack('<I', f.read(4))[0]
                    name = DXGI_FORMATS.get(dxgi, f"DXGI_{dxgi}")
                    return f"DX10 / {name}"

                name = LEGACY_FOURCC.get(four_cc)
                if name:
                    return name
                try:
                    return four_cc.decode('ascii').strip()
                except UnicodeDecodeError:
                    return f"Unknown FourCC: {four_cc}"

            return "Uncompressed"
    except Exception as e:
        return f"Read error: {str(e)}"

def main():
    if len(sys.argv) > 1:
        root = os.path.abspath(sys.argv[1])
    else:
        root = os.path.dirname(os.path.abspath(__file__))

    print(f"Scanning: {root}\n")
    print(f"{'File':<60} | {'Format':<25} | {'Colorspace'}")
    print("-" * 100)

    count = 0
    for path, _, files in os.walk(root):
        for file in files:
            if file.lower().endswith('.dds'):
                full_path = os.path.join(path, file)
                rel_path = os.path.relpath(full_path, root)
                fmt = get_dds_format(full_path)
                cs = get_colorspace(fmt)
                print(f"{rel_path:<60} | {fmt:<25} | {cs}")
                count += 1

    print("-" * 100)
    print(f"Done. Found {count} DDS files.")

if __name__ == "__main__":
    main()
