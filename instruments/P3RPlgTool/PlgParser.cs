using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P3RPlgTool;

// -------------------------------------------------------------------
// PLG uasset binary layout (empirically reverse-engineered):
//
// Header: same as DT (null magic)
// At 0xAE8:  int32=0, int32=entryCount (e.g. 72)
// At 0xAF0:  sequential PlgData entries
//
// Each PlgData entry layout (byte-aligned, no padding):
//   FName(name, 0)          [8 bytes]
//   float MinX,MinY,MaxX,MaxY [16 bytes]
//   -- vertex array header (28 bytes of property tag) --
//   int32 vertexCount
//   -- 45 bytes of type/struct header --
//   [vertexCount × 12 bytes: 3× float32 (X,Y,Z)]
//   -- index array header (36 bytes) --
//   int32 indexByteCount
//   -- 4 bytes --
//   [indexByteCount bytes: uint16-BE indices]
//   -- color array header (variable) --
//   int32 colorCount
//   -- 8 bytes --
//   [colorCount × 4 bytes: uint32 LE colors]
//
// NOTE: For EMPTY entries (vertCount=0) the layout collapses to just
//       FName + 16 float bytes (MinX..MaxY) + zero counts.
//
// -------------------------------------------------------------------

public static class PlgParser
{
    const int VertCountOffset    = -53;   // bytes from vertex cluster start
    const int AfterVertHdrSize   = 36;    // header between vertex end and index size field
    const int IdxSizeFieldOff    = 16;    // within the after-vert header: index byte count
    const int AfterIdxHdrSize    = 36;    // header between index end and color count
    const int ColorCountOff      = 30;    // within the after-idx header: color element count (int32 at +30)
    const int AfterColorHdr      = 0;     // bytes after color count before color data

    // ---- Entry count and start ----
    const int EntryCountOffset  = 0xAEC; // int32 at this position = number of entries
    const int EntriesStart      = 0xAF0; // first entry byte

    public static List<PlgDataEntry> Parse(byte[] data, List<string> names, bool debug = false)
    {
        var clusters = FindVertexClusters(data, EntriesStart);

        var clusterEntries = new Dictionary<int, PlgDataEntry>();
        foreach (var cl in clusters)
        {
            if (TryExtractEntry(data, names, cl, out var entry, debug))
                clusterEntries[entry.BinaryOffset] = entry;
            else if (debug)
                Console.WriteLine($"  [SKIP] cluster at 0x{cl.Start:X6} (verts={cl.Count})");
        }

        return clusterEntries.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    static bool TryExtractEntry(byte[] data, List<string> names,
        (int Start, int Count, int End) cl,
        out PlgDataEntry entry, bool debug = false)
    {
        entry = default!;
        int vertStart = cl.Start;
        int vertCount = cl.Count;
        int vertEnd   = cl.End;

        // Vertex count is at a fixed offset before the cluster
        // Vertex count may be at slightly varying offsets — scan ±20 bytes around expected
        int countPos = -1;
        for (int delta = 0; delta <= 20; delta++)
        {
            foreach (int sign in new[] { -1, 1 })
            {
                int tryPos = vertStart + VertCountOffset + sign * delta;
                if (tryPos < EntriesStart) continue;
                if (RI32(data, tryPos) == vertCount) { countPos = tryPos; break; }
            }
            if (countPos >= 0) break;
        }
        if (countPos < 0)
        {
            if (debug) Console.WriteLine($"    verts=0x{vertStart:X6}: count={vertCount} not found near offset {VertCountOffset}");
            return false;
        }

        // --- Read vertices ---
        var verts = new List<(float X, float Y, float Z)>(vertCount);
        for (int i = 0; i < vertCount; i++)
        {
            int p = vertStart + i * 12;
            verts.Add((
                BitConverter.ToSingle(data, p),
                BitConverter.ToSingle(data, p + 4),
                BitConverter.ToSingle(data, p + 8)));
        }

        // --- Read index section ---
        // After vertex data: 36-byte header; index byte count is at header+16
        int idxHdrStart    = vertEnd;
        int idxByteSizePos = idxHdrStart + IdxSizeFieldOff;
        if (idxByteSizePos + 4 > data.Length) return false;
        int idxByteCount = RI32(data, idxByteSizePos);
        if (idxByteCount < 0 || idxByteCount > 1_000_000)
        {
            if (debug) Console.WriteLine($"    idxByteCount={idxByteCount} invalid");
            return false;
        }

        int idxDataStart = idxHdrStart + AfterVertHdrSize;
        int idxDataEnd   = idxDataStart + idxByteCount;
        if (idxDataEnd > data.Length) return false;

        // Indices are uint16 big-endian
        int numIdx = idxByteCount / 2;
        var indices = new List<ushort>(numIdx);
        for (int i = 0; i < numIdx; i++)
        {
            int p = idxDataStart + i * 2;
            ushort val = (ushort)((data[p] << 8) | data[p + 1]);
            indices.Add(val);
        }

        // --- Read color section ---
        // After index data: 36-byte header; color count at header+ColorCountOff
        int colHdrStart   = idxDataEnd;
        int colCountPos   = colHdrStart + ColorCountOff;
        if (colCountPos + 4 > data.Length) return false;
        int colCount = RI32(data, colCountPos);
        if (debug) Console.WriteLine($"    0x{vertStart:X6}: idxBytes={idxByteCount} colCount={colCount} (at 0x{colCountPos:X6})");
        if (colCount < 0 || colCount > 100_000)
        {
            if (debug) Console.WriteLine($"    colCount={colCount} invalid");
            return false;
        }
        // sanity: colors should match vertices (or be 0)
        if (colCount != vertCount && colCount != 0)
        {
            if (debug) Console.WriteLine($"    colCount={colCount} != vertCount={vertCount}");
            return false;
        }

        int colDataStart = colCountPos + 4 + AfterColorHdr;
        if (colDataStart + colCount * 4 > data.Length) return false;

        var colors = new List<uint>(colCount);
        for (int i = 0; i < colCount; i++)
            colors.Add(BitConverter.ToUInt32(data, colDataStart + i * 4));

        int afterColors = colDataStart + colCount * 4;

        string entryName = "";
        float minX = 0, minY = 0, maxX = 0, maxY = 0;

        // After color data: tagged property layout (empirically verified):
        //   [0..32]   Name property: FName(propName)[8]+FName(type)[8]+int32 size[4]+int32 arrayIdx[4]+byte hasGuid[1]+4 bytes+nameIdx[4]
        //   [33..61]  Float property 1: FName(propName)[8]+FName(type)[8]+int32[4]+int32[4]+byte[1]+float value[4]
        //   [62..90]  Float property 2
        //   [91..119] Float property 3
        //   [120..148] Float property 4
        if (afterColors + 149 <= data.Length)
        {
            // FName value: (nameIndex:int32, number:int32) — standard UE5 FName serialization
            // number=0 → base name, number=N>0 → baseName + "_" + (N-1)
            int ni  = RI32(data, afterColors + 25);
            int num = RI32(data, afterColors + 29);
            entryName = (ni >= 0 && ni < names.Count) ? names[ni] : "";
            if (num > 0 && !string.IsNullOrEmpty(entryName))
                entryName += "_" + (num - 1);

            int[] propStarts = { 33, 62, 91, 120 };
            foreach (int ps in propStarts)
            {
                int pni = RI32(data, afterColors + ps);
                if (pni < 0 || pni >= names.Count) continue;
                float val = BitConverter.ToSingle(data, afterColors + ps + 25);
                switch (names[pni])
                {
                    case "MinX": minX = val; break;
                    case "MinY": case "LevelUp": minY = val; break;
                    case "MaxX": maxX = val; break;
                    case "MaxY": maxY = val; break;
                }
            }
        }

        entry = new PlgDataEntry(
            BinaryOffset: countPos,
            Name:    entryName,
            MinX:    minX, MinY:    minY,
            MaxX:    maxX, MaxY:    maxY,
            Vertices: verts,
            Indices:  indices,
            Colors:   colors);
        return true;
    }

    public static List<(int Start, int Count, int End)> FindVertexClusters(byte[] data, int from)
    {
        var result = new List<(int, int, int)>();
        int i = from;
        while (i + 11 < data.Length)
        {
            float x = BitConverter.ToSingle(data, i);
            float y = BitConverter.ToSingle(data, i + 4);
            float z = BitConverter.ToSingle(data, i + 8);

            if (IsCoord(x) && IsCoord(y) && z == 0f && (MathF.Abs(x) > 0.01f || MathF.Abs(y) > 0.01f))
            {
                int start = i;
                int count = 0;
                while (i + 11 < data.Length)
                {
                    float cx = BitConverter.ToSingle(data, i);
                    float cy = BitConverter.ToSingle(data, i + 4);
                    float cz = BitConverter.ToSingle(data, i + 8);
                    if (!IsCoord(cx) || !IsCoord(cy) || cz != 0f) break;
                    count++;
                    i += 12;
                }
                if (count >= 3)
                    result.Add((start, count, i));
            }
            else
                i += 4;
        }
        return result;
    }

    static bool IsCoord(float f)    => float.IsFinite(f) && MathF.Abs(f) < 15_000f;
    static bool IsReasonableFloat(float f) => float.IsFinite(f) && MathF.Abs(f) < 15_000f;
    // Normalized float: finite, not subnormal (i.e. exponent bits not all zero unless value is exactly 0)
    static bool IsNormalFloat(float f) =>
        float.IsFinite(f) && MathF.Abs(f) < 15_000f &&
        (!float.IsSubnormal(f) || f == 0f);

    static bool IsPropertyTypeName(string name) =>
        name is "ArrayProperty" or "StructProperty" or "FloatProperty"
             or "NameProperty" or "UInt16Property" or "UInt32Property"
             or "ObjectProperty" or "BoolProperty" or "IntProperty"
             or "StrProperty" or "ByteProperty" or "None" or "Vector";

    static int RI32(byte[] d, int o) => o + 3 < d.Length ? BitConverter.ToInt32(d, o) : 0;
}

public sealed record PlgDataEntry(
    int BinaryOffset,
    string Name,
    float MinX, float MinY, float MaxX, float MaxY,
    List<(float X, float Y, float Z)> Vertices,
    List<ushort> Indices,
    List<uint> Colors);
