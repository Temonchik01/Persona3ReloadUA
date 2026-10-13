using System.Text;

namespace P3RPlgTool;

static class PlgDiag
{
    public static void Audit(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var (names, exportStart, exportOffset) = PlgHeader.Read(data);
        int expected = exportStart + 4 <= data.Length ? RI32(data, exportStart) : -1;
        var clusters = PlgParser.FindVertexClusters(data, exportStart);
        var parsed = PlgParser.Parse(data, names, debug: false);

        Console.WriteLine($"=== AUDIT {Path.GetFileName(path)} ===");
        Console.WriteLine($"Size: {data.Length} bytes");
        Console.WriteLine($"Names: {names.Count}  ExportStart: 0x{exportStart:X}  ExportOffset: 0x{exportOffset:X}");
        Console.WriteLine($"Expected PlgDatas count at exportStart: {expected}");
        Console.WriteLine($"Raw vertex clusters: {clusters.Count}");
        Console.WriteLine($"Parsed named entries: {parsed.Count}");
        Console.WriteLine();

        var suspicious = parsed.Where(e => string.IsNullOrWhiteSpace(e.Name)
            || e.Name.Contains('/')
            || e.Name is "Colors" or "MinX" or "MinY" or "MaxX" or "MaxY" or "PlgPrimitiveData")
            .ToList();
        Console.WriteLine($"Suspicious parsed names: {suspicious.Count}");
        foreach (var e in suspicious.Take(20))
            Console.WriteLine($"  0x{e.BinaryOffset:X6}: {e.Name} ({e.Vertices.Count} verts)");

        Console.WriteLine();
        Console.WriteLine("Raw clusters:");
        foreach (var (start, count, end) in clusters)
            Console.WriteLine($"  0x{start:X6}: {count,5} verts  end=0x{end:X6}");
    }

    public static void Dump(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var (names, exportStart, exportOffset) = PlgHeader.Read(data);

        Console.WriteLine($"=== {Path.GetFileName(path)}  ({data.Length} bytes) ===");
        Console.WriteLine($"Names: {names.Count}  ExportStart: 0x{exportStart:X}  ExportOffset: 0x{exportOffset:X}");
        Console.WriteLine();

        Console.WriteLine("=== TAG SCAN (FName(propName, 0) + FName(propType, 0) at 4-byte aligned positions) ===");
        ScanTags(data, names, exportStart);

        Console.WriteLine();
        Console.WriteLine("=== RAW PLGDATA ARRAY SCAN ===");
        ScanRawPlgData(data, names, exportStart);
    }

    public static void DumpRange(string path, string fromHex, string lenHex)
    {
        byte[] data = File.ReadAllBytes(path);
        var (names, _, _) = PlgHeader.Read(data);
        int from = Convert.ToInt32(fromHex, 16);
        int len  = Convert.ToInt32(lenHex, 16);
        Console.WriteLine($"=== 0x{from:X6} +0x{len:X} ===");
        HexDumpWithNames(data, names, from, len);
    }

    public static void DumpBeforeVerts(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var (names, _, _) = PlgHeader.Read(data);

        const int EntriesStart    = 0xAF0;
        const int VertCountOffset = -53;

        var clusters = FindClusters(data, EntriesStart);

        int shown = 0;
        foreach (var (vStart, vCount, vEnd) in clusters)
        {
            if (vCount < 10) continue;
            if (shown >= 4) break;
            shown++;

            int countPos = vStart + VertCountOffset;
            int dumpFrom = Math.Max(EntriesStart, countPos - 128);

            Console.WriteLine($"\n=== Cluster @ 0x{vStart:X6}  verts={vCount}  countPos=0x{countPos:X6} ===");
            Console.WriteLine($"  Dumping {countPos - dumpFrom + 8} bytes before+at countPos (from 0x{dumpFrom:X6}):");
            HexDumpWithNames(data, names, dumpFrom, countPos - dumpFrom + 8);
        }
    }

    public static void DumpEntryLayout(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var (names, _, _) = PlgHeader.Read(data);

        const int EntriesStart   = 0xAF0;
        const int IdxSizeFieldOff = 16;
        const int AfterVertHdrSize = 36;
        const int ColorCountOff  = 30;

        var clusters = FindClusters(data, EntriesStart);

        int shown = 0;
        foreach (var (vStart, vCount, vEnd) in clusters)
        {
            if (vCount < 10) continue;
            if (shown >= 3) break;
            shown++;

            // -- index section --
            int idxHdrStart      = vEnd;
            int idxByteSizePos   = idxHdrStart + IdxSizeFieldOff;
            int idxByteCount     = RI32(data, idxByteSizePos);
            int idxDataStart     = idxHdrStart + AfterVertHdrSize;
            int idxDataEnd       = idxDataStart + idxByteCount;

            // -- color section header (between idxDataEnd and color data) --
            int colHdrStart = idxDataEnd;
            int colCountPos = colHdrStart + ColorCountOff;
            int colCount    = RI32(data, colCountPos);
            int colDataStart = colCountPos + 4;
            int afterColors = colDataStart + colCount * 4;

            Console.WriteLine($"\n=== Cluster @ 0x{vStart:X6}  verts={vCount} ===");
            Console.WriteLine($"  idxHdr=0x{idxHdrStart:X6}  idxData=0x{idxDataStart:X6}  idxDataEnd=0x{idxDataEnd:X6}  ({idxByteCount} bytes)");
            Console.WriteLine($"  colHdr=0x{colHdrStart:X6}  colCountAt=0x{colCountPos:X6}  colData=0x{colDataStart:X6}  colCount={colCount}  afterColors=0x{afterColors:X6}");

            Console.WriteLine($"\n  [A] idxHdr bytes (36 bytes from 0x{idxHdrStart:X6}):");
            HexDumpWithNames(data, names, idxHdrStart, 36);

            Console.WriteLine($"\n  [B] colHdr bytes (34 bytes from 0x{colHdrStart:X6}):");
            HexDumpWithNames(data, names, colHdrStart, 34);

            Console.WriteLine($"\n  [C] after-colors bytes (128 bytes from 0x{afterColors:X6}):");
            HexDumpWithNames(data, names, afterColors, 128);
        }
    }

    public static void DumpAfterColors(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var (names, exportStart, _) = PlgHeader.Read(data);

        Console.WriteLine($"=== DUMP AFTER-COLORS  {Path.GetFileName(path)} ===");
        Console.WriteLine($"Name table ({names.Count} entries):");
        for (int i = 0; i < Math.Min(names.Count, 130); i++)
            if (!string.IsNullOrEmpty(names[i]))
                Console.WriteLine($"  [{i,3}] = \"{names[i]}\"");
        Console.WriteLine();

        const int EntriesStart = 0xAF0;
        const int IdxSizeFieldOff = 16;
        const int AfterVertHdrSize = 36;
        const int ColorCountOff = 30;

        var clusters = FindClusters(data, EntriesStart);
        Console.WriteLine($"Found {clusters.Count} vertex clusters.\n");

        int shown = 0;
        foreach (var (vStart, vCount, vEnd) in clusters)
        {
            if (vCount < 10) continue;   // skip tiny false-positives
            if (shown >= 6) break;
            shown++;

            // Reconstruct the layout exactly as PlgParser does
            int idxByteSizePos = vEnd + IdxSizeFieldOff;
            if (idxByteSizePos + 4 > data.Length) continue;
            int idxByteCount = RI32(data, idxByteSizePos);
            if (idxByteCount < 0 || idxByteCount > 1_000_000) { Console.WriteLine($"0x{vStart:X6}: idxByteCount={idxByteCount} INVALID"); continue; }

            int idxDataStart = vEnd + AfterVertHdrSize;
            int idxDataEnd   = idxDataStart + idxByteCount;

            int colCountPos  = idxDataEnd + ColorCountOff;
            if (colCountPos + 4 > data.Length) continue;
            int colCount = RI32(data, colCountPos);
            if (colCount < 0 || colCount > 100_000) { Console.WriteLine($"0x{vStart:X6}: colCount={colCount} INVALID"); continue; }

            int colDataStart = colCountPos + 4;
            int afterColors  = colDataStart + colCount * 4;

            Console.WriteLine($"=== Cluster 0x{vStart:X6} verts={vCount} idxBytes={idxByteCount} cols={colCount} ===");
            Console.WriteLine($"  vertEnd=0x{vEnd:X6}  idxDataEnd=0x{idxDataEnd:X6}  colCountAt=0x{colCountPos:X6}  afterColors=0x{afterColors:X6}");
            Console.WriteLine($"  Dumping 256 bytes from 0x{afterColors:X6}:");
            Console.WriteLine();

            HexDumpWithNames(data, names, afterColors, 256);
            Console.WriteLine();
        }
    }

    static void HexDumpWithNames(byte[] data, List<string> names, int from, int length)
    {
        int to = Math.Min(from + length, data.Length);
        for (int pos = from; pos < to; pos += 16)
        {
            int rowEnd = Math.Min(pos + 16, to);
            // raw hex
            var hex = new System.Text.StringBuilder();
            var ascii = new System.Text.StringBuilder();
            for (int b = pos; b < rowEnd; b++)
            {
                hex.Append($"{data[b]:X2} ");
                ascii.Append(data[b] >= 32 && data[b] < 127 ? (char)data[b] : '.');
            }

            // FName interpretation at this row start (every 4 bytes)
            var fnames = new System.Text.StringBuilder();
            for (int off = 0; off + 8 <= rowEnd - pos; off += 4)
            {
                int ni  = pos + off + 3 < data.Length ? RI32(data, pos + off) : -1;
                int num = pos + off + 7 < data.Length ? RI32(data, pos + off + 4) : -1;
                if (ni >= 0 && ni < names.Count && num == 0 && !string.IsNullOrEmpty(names[ni]))
                    fnames.Append($" +{off:D2}:FName[{names[ni]}]");
            }

            Console.WriteLine($"  {pos:X6}: {hex,-48} |{ascii}|{fnames}");
        }
    }

    static int RI32(byte[] d, int o) => PlgHeader.RI32(d, o);

    static List<(int Start, int Count, int End)> FindClusters(byte[] data, int from)
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
                int start = i, count = 0;
                while (i + 11 < data.Length)
                {
                    float cx = BitConverter.ToSingle(data, i);
                    float cy = BitConverter.ToSingle(data, i + 4);
                    float cz = BitConverter.ToSingle(data, i + 8);
                    if (!IsCoord(cx) || !IsCoord(cy) || cz != 0f) break;
                    count++; i += 12;
                }
                if (count >= 3) result.Add((start, count, i));
            }
            else i += 4;
        }
        return result;
    }

    static void ScanTags(byte[] data, List<string> names, int from)
    {
        // Known property types
        var propTypes = new HashSet<string>
        {
            "ArrayProperty", "StructProperty", "FloatProperty", "NameProperty",
            "UInt16Property", "UInt32Property", "ObjectProperty", "BoolProperty",
            "ByteProperty", "IntProperty", "StrProperty"
        };
        var propNames = new HashSet<string>
        {
            "PlgData", "PlgDatas", "Vertices", "Indices", "Colors",
            "Name", "MinX", "MinY", "MaxX", "MaxY"
        };

        int hits = 0;
        for (int pos = from; pos + 15 < data.Length; pos += 4)
        {
            int ni  = PlgHeader.RI32(data, pos);
            int num = PlgHeader.RI32(data, pos + 4);
            int ti  = PlgHeader.RI32(data, pos + 8);
            int tn  = PlgHeader.RI32(data, pos + 12);

            if (ni < 0 || ni >= names.Count) continue;
            if (ti < 0 || ti >= names.Count) continue;
            if (num != 0 || tn != 0) continue;

            string pn = names[ni];
            string pt = names[ti];

            if (!propNames.Contains(pn) || !propTypes.Contains(pt)) continue;

            long sz   = pos + 23 < data.Length ? BitConverter.ToInt64(data, pos + 16) : -1;
            int  aIdx = pos + 27 < data.Length ? PlgHeader.RI32(data, pos + 24) : -1;
            Console.WriteLine($"  0x{pos:X5}: [{pn}] type=[{pt}] sz={sz} aIdx={aIdx}");
            hits++;
            if (hits > 100) { Console.WriteLine("  ... (truncated)"); break; }
        }

        if (hits == 0)
            Console.WriteLine("  No tagged property pairs found at 4-byte alignment from exportStart.");
    }

    static void ScanRawPlgData(byte[] data, List<string> names, int from)
    {
        ScanVertexClusters(data, from);
        Console.WriteLine();
        ScanByKnownNames(data, names, from);
    }

    static void ScanVertexClusters(byte[] data, int from)
    {
        // Find clusters of consecutive (X, Y, 0.0) vertex triples by scanning every byte offset
        Console.WriteLine("=== VERTEX CLUSTER SCAN (3 floats: X, Y, Z=0.0 at any byte offset) ===");

        // Scan at every 4-byte aligned offset for Z=0 (00 00 00 00) preceded/followed by non-trivial floats
        var clusters = new List<(int Start, int Count, int Stride)>();
        int i = from;
        while (i + 11 < data.Length)
        {
            // Try 12-byte stride (Vector = float X + float Y + float Z=0)
            float x = BitConverter.ToSingle(data, i);
            float y = BitConverter.ToSingle(data, i + 4);
            float z = BitConverter.ToSingle(data, i + 8);

            if (IsCoord(x) && IsCoord(y) && z == 0f && (MathF.Abs(x) > 0.01f || MathF.Abs(y) > 0.01f))
            {
                // Count consecutive vertices
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
                {
                    float ex = BitConverter.ToSingle(data, start);
                    float ey = BitConverter.ToSingle(data, start + 4);
                    Console.WriteLine($"  0x{start:X6}: {count} vertices  first=({ex:F2},{ey:F2})  end=0x{i:X6}");
                }
            }
            else
                i += 4;
        }
    }

    static bool IsCoord(float f) => float.IsFinite(f) && MathF.Abs(f) < 15_000f;

    static void ScanByKnownNames(byte[] data, List<string> names, int from)
    {
        // Search by known PLG element name FNames: find FName(idx, 0) then check surroundings.
        // Known UI names in PLG_UI_Camp name table:
        var knownNames = new HashSet<string>
        {
            "BGM", "CALENDAR1", "CALENDAR2", "CALENDAR3", "CALENDAR4", "CALENDAR5",
            "CALENDAR6", "CALENDAR7", "CALENDAR8", "CALENDAR9", "CALENDAR10",
            "CALENDAR11", "CALENDAR12", "CALENDARDetailBG", "CampDetailBG",
            "COMMU", "COMPLETE", "CONFIG", "Equip", "Item", "Persona",
            "QUEST", "Ripples", "Status", "StatusLine", "SYSTEM", "THEURGIA",
            "TUTRIAL", "Wipe_In_Mask_A", "Wipe_In_Mask_B", "blue", "white",
            "MISSING", "MISSINGBG", "JOBBG", "LevelUp", "NewSkill", "POINTUP",
            "REQUEST", "RANKUP", "COMPLETE_BG", "DICTIONARY", "BlueBoard", "WhiteBoard",
        };

        // Build nameIdx → name map for known names
        var targetIdxs = new Dictionary<int, string>();
        for (int i = 0; i < names.Count; i++)
            if (knownNames.Contains(names[i]))
                targetIdxs[i] = names[i];

        Console.WriteLine($"  Known name indices found: {string.Join(", ", targetIdxs.Select(kv => $"{kv.Value}={kv.Key}"))}");
        Console.WriteLine();

        // Search for each FName(idx, 0) in the binary after exportStart
        var hitOffsets = new SortedDictionary<int, string>();
        foreach (var (idx, nm) in targetIdxs)
        {
            byte[] pattern = BitConverter.GetBytes(idx)   // nameIndex (4 bytes LE)
                .Concat(BitConverter.GetBytes(0))          // number = 0 (4 bytes)
                .ToArray();

            for (int pos = from; pos + 8 <= data.Length; pos++)
            {
                if (data[pos] == pattern[0] && data[pos+1] == pattern[1] &&
                    data[pos+2] == pattern[2] && data[pos+3] == pattern[3] &&
                    data[pos+4] == 0 && data[pos+5] == 0 && data[pos+6] == 0 && data[pos+7] == 0)
                {
                    hitOffsets[pos] = nm;
                }
            }
        }

        Console.WriteLine($"  Found {hitOffsets.Count} FName hits for known names:");
        foreach (var (pos, nm) in hitOffsets.Take(30))
        {
            // Try to interpret as PlgData where FName is the Name field:
            // Entry layout: int32 vertCount, [verts], int32 idxCount, [idxs], int32 colCount, [cols], FName(8), float*4
            // Try to find the entry START by going backwards from the FName position.
            // After FName: 4 floats = 16 bytes
            // Before FName: cols array, before that idxs array, before that verts array
            // We don't know the counts, so let's just show the floats after the FName
            int fnamePos = pos;
            if (fnamePos + 8 + 16 > data.Length) continue;
            float minX = BitConverter.ToSingle(data, fnamePos + 8);
            float minY = BitConverter.ToSingle(data, fnamePos + 12);
            float maxX = BitConverter.ToSingle(data, fnamePos + 16);
            float maxY = BitConverter.ToSingle(data, fnamePos + 20);

            bool floatsOk = IsReasonableFloat(minX) && IsReasonableFloat(minY) &&
                            IsReasonableFloat(maxX) && IsReasonableFloat(maxY) &&
                            maxX >= minX - 0.01f && maxY >= minY - 0.01f;

            string floatStr = floatsOk
                ? $"min=({minX:F1},{minY:F1}) max=({maxX:F1},{maxY:F1})"
                : $"BAD_FLOATS: {minX:G} {minY:G} {maxX:G} {maxY:G}";

            Console.WriteLine($"  0x{pos:X6}: FName=[{nm}] after→ {floatStr}");
        }

        if (hitOffsets.Count == 0)
            Console.WriteLine("  No hits found for known PLG element names.");
    }

    static bool TryParsePlgEntry(byte[] data, List<string> names, int pos, out PlgEntry entry, out int endPos)
    {
        entry = default;
        endPos = pos;

        int vertCount = PlgHeader.RI32(data, pos);
        if (vertCount < 0 || vertCount > 5000) return false;
        int afterVerts = pos + 4 + vertCount * 12;
        if (afterVerts + 4 >= data.Length) return false;

        int idxCount = PlgHeader.RI32(data, afterVerts);
        if (idxCount < 0 || idxCount > 30000) return false;
        // indices are uint16, but may need padding to 4-byte boundary
        int idxBytes = idxCount * 2;
        int afterIdxRaw = afterVerts + 4 + idxBytes;
        // try with and without padding
        foreach (int afterIdx in new[] { afterIdxRaw, afterIdxRaw + (idxBytes % 4 == 0 ? 0 : 4 - idxBytes % 4) })
        {
            if (afterIdx + 4 >= data.Length) continue;

            int colCount = PlgHeader.RI32(data, afterIdx);
            if (colCount < 0 || colCount > 5000) continue;
            // colors should match verts (or be 0)
            if (colCount != vertCount && colCount != 0 && vertCount != 0) continue;
            int afterCols = afterIdx + 4 + colCount * 4;
            if (afterCols + 24 >= data.Length) continue;

            // FName = (nameIdx, number)
            int nameIdx = PlgHeader.RI32(data, afterCols);
            int nameNum = PlgHeader.RI32(data, afterCols + 4);
            if (nameIdx < 0 || nameIdx >= names.Count) continue;
            if (nameNum != 0) continue;

            int afterName = afterCols + 8;
            if (afterName + 16 >= data.Length) continue;

            float minX = BitConverter.ToSingle(data, afterName);
            float minY = BitConverter.ToSingle(data, afterName + 4);
            float maxX = BitConverter.ToSingle(data, afterName + 8);
            float maxY = BitConverter.ToSingle(data, afterName + 12);

            // UI coordinates sanity check
            if (!IsReasonableFloat(minX) || !IsReasonableFloat(minY) ||
                !IsReasonableFloat(maxX) || !IsReasonableFloat(maxY))
                continue;

            if (maxX < minX - 0.01f || maxY < minY - 0.01f) continue;

            // Build vertices
            var verts = new List<(float X, float Y, float Z)>();
            for (int i = 0; i < vertCount; i++)
            {
                int vp = pos + 4 + i * 12;
                verts.Add((BitConverter.ToSingle(data, vp),
                           BitConverter.ToSingle(data, vp + 4),
                           BitConverter.ToSingle(data, vp + 8)));
            }

            // Build indices
            var idxs = new List<ushort>();
            for (int i = 0; i < idxCount; i++)
                idxs.Add(BitConverter.ToUInt16(data, afterVerts + 4 + i * 2));

            // Build colors
            var cols = new List<uint>();
            for (int i = 0; i < colCount; i++)
                cols.Add(BitConverter.ToUInt32(data, afterIdx + 4 + i * 4));

            entry = new PlgEntry(names[nameIdx], verts, idxs, cols, minX, minY, maxX, maxY);
            endPos = afterName + 16;
            return true;
        }
        return false;
    }

    static bool IsReasonableFloat(float f)
    {
        if (!float.IsFinite(f)) return false;
        float abs = MathF.Abs(f);
        return abs < 100_000f;
    }
}


