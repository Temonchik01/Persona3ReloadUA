using System.Text.Json;
using System.Text.Json.Serialization;

namespace P3RPlgTool;

/// <summary>
/// Patches PLG uasset binary data by replacing one entry's geometry with new data.
///
/// Binary layout recap (from PlgParser empirical reverse-engineering):
///
///   [countPos]       int32 vertexCount           ← BinaryOffset stored in PlgDataEntry
///   [+4 .. +52]      49 bytes of header (prop tag)
///   [+53 ..]         vertexCount × 12 bytes (float X, Y, Z)
///   [vertEnd+0..+35] 36-byte index array header; index byte count at [+16]
///   [vertEnd+36..]   indexByteCount bytes of big-endian uint16 indices
///   [idxEnd+0..+33]  color array header; color count int32 at [+30]
///   [idxEnd+34..]    colorCount × 4 bytes (uint32 LE colors)
///   [afterColors..]  149 bytes of tagged properties (Name + 4 floats)
///
/// The patcher:
/// 1. Locates the entry in the binary using its BinaryOffset (= countPos).
/// 2. Slices out the old entry bytes.
/// 3. Writes new entry bytes using the same header bytes verbatim (copied from original).
/// 4. Splices them back and writes the output file.
/// </summary>
public static class PlgPacker
{
    // Constants mirrored from PlgParser
    const int VertCountOffset  = -53;
    const int AfterVertHdrSize = 36;
    const int IdxSizeFieldOff  = 16;
    const int ColorCountOff    = 30;
    const int AfterColorsBlobSize = 149;

    // -----------------------------------------------------------------------
    // JSON-driven full-file patcher
    // -----------------------------------------------------------------------

    /// <summary>
    /// Read a PLG JSON file (as produced by PlgJson.Export) together with the original
    /// uasset, replace the entry named <paramref name="entryName"/> with new geometry,
    /// and write the result to <paramref name="outputPath"/>.
    /// </summary>
    public static void Pack(string inputJsonPath, string originalUassetPath, string outputPath)
    {
        var jsonText = File.ReadAllText(inputJsonPath);
        var doc = JsonSerializer.Deserialize<PlgJsonDoc>(jsonText)
            ?? throw new Exception("Failed to deserialize PLG JSON");

        if (doc.Entries == null || doc.Entries.Count == 0)
            throw new Exception("JSON contains no entries");

        byte[] data = File.ReadAllBytes(originalUassetPath);
        var (names, _, _) = PlgHeader.Read(data);
        var entries = PlgParser.Parse(data, names);

        byte[] result = data;

        // Apply each JSON entry as a patch if geometry differs
        foreach (var je in doc.Entries)
        {
            if (je.Name == null) continue;
            var orig = entries.FirstOrDefault(e => e.Name == je.Name);
            if (orig == null)
            {
                Console.WriteLine($"  [SKIP] entry '{je.Name}' not found in binary");
                continue;
            }

            var newVerts  = je.Vertices?.Select(v => (v[0], v[1], v.Length > 2 ? v[2] : 0f)).ToList()
                          ?? orig.Vertices;
            var newIdxs   = je.Indices?.Select(i => (ushort)i).ToList() ?? orig.Indices;
            var newColors = je.Colors?.Select(c => (uint)c).ToList() ?? orig.Colors;
            float minX = je.MinX, minY = je.MinY, maxX = je.MaxX, maxY = je.MaxY;

            result = PatchEntryInBytes(result, names, orig, newVerts, newIdxs, newColors,
                                       minX, minY, maxX, maxY);
            // Re-parse after each patch (offsets shift)
            var (names2, _, _) = PlgHeader.Read(result);
            entries = PlgParser.Parse(result, names2);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, result);
        Console.WriteLine($"Packed -> {outputPath}  ({result.Length} bytes)");
    }

    // -----------------------------------------------------------------------
    // Single-entry targeted patcher
    // -----------------------------------------------------------------------

    /// <summary>
    /// Locate <paramref name="entryName"/> in the binary and replace its geometry.
    /// </summary>
    public static void PatchEntry(
        string uassetPath, string outputPath, string entryName,
        List<(float X, float Y, float Z)> newVerts,
        List<ushort> newIndices,
        List<uint> newColors,
        float minX, float minY, float maxX, float maxY)
    {
        byte[] data = File.ReadAllBytes(uassetPath);
        var (names, _, _) = PlgHeader.Read(data);
        var entries = PlgParser.Parse(data, names);

        var sorted = entries.OrderBy(e => e.BinaryOffset).ToList();
        var target = sorted.FirstOrDefault(e => e.Name == entryName)
            ?? throw new Exception($"Entry '{entryName}' not found in {uassetPath}");

        int targetIdx   = sorted.IndexOf(target);
        // Real entry end = next parsed entry's countPos (or EOF)
        int nextEntryStart = targetIdx + 1 < sorted.Count
            ? sorted[targetIdx + 1].BinaryOffset
            : data.Length;

        byte[] result = PatchEntryInBytes(data, names, target, newVerts, newIndices, newColors,
                                          minX, minY, maxX, maxY, nextEntryStart);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, result);
        Console.WriteLine($"Patched entry '{entryName}' -> {outputPath}  ({result.Length} bytes)");
    }

    // -----------------------------------------------------------------------
    // Core binary patcher
    // -----------------------------------------------------------------------

    static byte[] PatchEntryInBytes(
        byte[] data,
        List<string> names,
        PlgDataEntry entry,
        List<(float X, float Y, float Z)> newVerts,
        List<ushort> newIndices,
        List<uint> newColors,
        float minX, float minY, float maxX, float maxY,
        int nextEntryStart = -1)
    {
        // ── Locate all section boundaries ─────────────────────────────────
        int countPos   = entry.BinaryOffset;
        int vertStart  = countPos + 53;            // 49 bytes of header after count field  (+4 for the int itself = 53 total gap)
        int vertEnd    = vertStart + entry.Vertices.Count * 12;

        int idxHdrStart    = vertEnd;
        int idxByteSizePos = idxHdrStart + IdxSizeFieldOff;
        int idxByteCount   = RI32(data, idxByteSizePos);
        int idxDataStart   = idxHdrStart + AfterVertHdrSize;
        int idxDataEnd     = idxDataStart + idxByteCount;

        int colHdrStart  = idxDataEnd;
        int colCountPos  = colHdrStart + ColorCountOff;
        int colCount     = RI32(data, colCountPos);
        int colDataStart = colCountPos + 4;
        int afterColors  = colDataStart + colCount * 4;

        // Real entry end = next parsed entry's countPos (passed in by caller).
        // Falls back to afterColors+149 if not provided (old behaviour).
        int oldEntryEnd = nextEntryStart > afterColors
            ? nextEntryStart
            : afterColors + 149;
        if (oldEntryEnd > data.Length) oldEntryEnd = data.Length;

        Console.WriteLine($"  Entry 0x{countPos:X} afterColors=0x{afterColors:X} oldEntryEnd=0x{oldEntryEnd:X} blobSize={oldEntryEnd-afterColors}");

        // ── Capture header/footer blobs from the original ──────────────────
        // The 53 bytes from countPos to vertStart (4 for count + 49 header bytes)
        // We keep the 49-byte blob verbatim; only overwrite the count int32.
        byte[] vertHdrBlob    = data[countPos..(countPos + 53)];       // 53 bytes incl. old count
        byte[] afterVertHdr   = data[idxHdrStart..idxDataStart];       // 36 bytes
        byte[] afterIdxHdr    = data[colHdrStart..colDataStart];       // 34 bytes (up to and incl. count int32)
        byte[] afterColorsBlob = data[afterColors..oldEntryEnd];       // 149 bytes

        // ── Build replacement bytes ─────────────────────────────────────────
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);

        // [0..3]   new vertex count
        w.Write(newVerts.Count);

        // [4..52]  the 49-byte header blob (copy from original, skip old count, patch vert byte size)
        byte[] vertBlob49 = vertHdrBlob[4..53];
        // Position 16 within the 49-byte blob stores vertex array byte size (vertCount * 12).
        // Must be updated or the engine reads the wrong number of bytes for the vertex array.
        BitConverter.TryWriteBytes(vertBlob49.AsSpan(16), newVerts.Count * 12);
        w.Write(vertBlob49);

        // vertex data
        foreach (var (vx, vy, vz) in newVerts)
        {
            w.Write(vx); w.Write(vy); w.Write(vz);
        }

        // 36-byte index array header — copy original but patch index byte count at [+16]
        byte[] newAfterVertHdr = (byte[])afterVertHdr.Clone();
        int newIdxByteCount = newIndices.Count * 2;
        BitConverter.TryWriteBytes(newAfterVertHdr.AsSpan(IdxSizeFieldOff), newIdxByteCount);
        w.Write(newAfterVertHdr);

        // index data (big-endian uint16)
        foreach (ushort idx in newIndices)
        {
            w.Write((byte)(idx >> 8));
            w.Write((byte)(idx & 0xFF));
        }

        // 34-byte color array header — copy original but patch:
        //   [+13]: int32 = colorCount * 4 + 4  (color byte size field, empirically determined)
        //   [+30]: int32 = colorCount
        byte[] newAfterIdxHdr = (byte[])afterIdxHdr.Clone();
        BitConverter.TryWriteBytes(newAfterIdxHdr.AsSpan(13), newColors.Count * 4 + 4);
        BitConverter.TryWriteBytes(newAfterIdxHdr.AsSpan(ColorCountOff), newColors.Count);
        w.Write(newAfterIdxHdr);

        // color data
        foreach (uint col in newColors)
            w.Write(col);

        // 149-byte after-colors blob — patch the 4 float props in-place
        // Layout (from spec):
        //   [0..32]   Name property  — nameIdx at +25, number at +29
        //   [33..61]  MaxY  float    — float at +33+25 = +58
        //   [62..90]  MinX  float    — float at +62+25 = +87
        //   [91..119] MinY/LevelUp   — float at +91+25 = +116
        //   [120..148] MaxX float    — float at +120+25 = +145
        byte[] acBlob = (byte[])afterColorsBlob.Clone();
        if (acBlob.Length >= AfterColorsBlobSize)
        {
            BitConverter.TryWriteBytes(acBlob.AsSpan(58),  maxY);
            BitConverter.TryWriteBytes(acBlob.AsSpan(87),  minX);
            BitConverter.TryWriteBytes(acBlob.AsSpan(116), minY);
            BitConverter.TryWriteBytes(acBlob.AsSpan(145), maxX);
        }
        w.Write(acBlob);

        byte[] newEntryBytes = ms.ToArray();

        // ── Splice into original file ──────────────────────────────────────
        var result = new byte[data.Length - (oldEntryEnd - countPos) + newEntryBytes.Length];
        Array.Copy(data, 0,           result, 0,                   countPos);
        Array.Copy(newEntryBytes, 0,  result, countPos,            newEntryBytes.Length);
        Array.Copy(data, oldEntryEnd, result, countPos + newEntryBytes.Length,
                   data.Length - oldEntryEnd);

        // ── Update SerialSize in export table (int64 at 0xA78) ─────────────
        // UE5 stores the export's serial byte count here; must match actual data size.
        const int SerialSizeOff = 0xA78;
        if (SerialSizeOff + 8 <= result.Length)
        {
            long oldSerial = BitConverter.ToInt64(result, SerialSizeOff);
            long newSerial = oldSerial + (result.Length - data.Length);
            BitConverter.TryWriteBytes(result.AsSpan(SerialSizeOff), newSerial);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    public static void PatchCluster(
        string uassetPath, string outputPath, int clusterStart,
        List<(float X, float Y, float Z)> newVerts,
        List<ushort> newIndices,
        List<uint> newColors,
        float minX, float minY, float maxX, float maxY)
    {
        byte[] data = File.ReadAllBytes(uassetPath);
        var (names, exportStart, _) = PlgHeader.Read(data);
        var clusters = PlgParser.FindVertexClusters(data, exportStart).OrderBy(c => c.Start).ToList();
        var matches = clusters.Where(c => c.Start == clusterStart).ToList();
        if (matches.Count == 0)
            throw new Exception($"Raw cluster 0x{clusterStart:X} not found in {uassetPath}");
        var cluster = matches[0];

        int countPos = FindCountPos(data, exportStart, cluster.Start, cluster.Count);
        if (countPos < 0)
            throw new Exception($"Could not find vertex count for cluster 0x{cluster.Start:X6}; this cluster is not safely patchable yet.");

        int nextCountPos = -1;
        foreach (var next in clusters.Where(c => c.Start > cluster.Start))
        {
            int cp = FindCountPos(data, exportStart, next.Start, next.Count);
            if (cp > countPos) { nextCountPos = cp; break; }
        }
        if (nextCountPos < 0) nextCountPos = data.Length;

        var verts = new List<(float X, float Y, float Z)>();
        for (int i = 0; i < cluster.Count; i++)
        {
            int p = cluster.Start + i * 12;
            verts.Add((BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8)));
        }

        int idxHdrStart = cluster.End;
        int idxByteCount = RI32(data, idxHdrStart + IdxSizeFieldOff);
        int idxDataStart = idxHdrStart + AfterVertHdrSize;
        var indices = new List<ushort>();
        for (int i = 0; i < idxByteCount / 2; i++)
        {
            int p = idxDataStart + i * 2;
            indices.Add((ushort)((data[p] << 8) | data[p + 1]));
        }

        int idxDataEnd = idxDataStart + idxByteCount;
        int colCountPos = idxDataEnd + ColorCountOff;
        int colCount = RI32(data, colCountPos);
        var colors = new List<uint>();
        for (int i = 0; i < colCount; i++)
            colors.Add(BitConverter.ToUInt32(data, colCountPos + 4 + i * 4));

        var entry = new PlgDataEntry(countPos, $"cluster_0x{cluster.Start:X6}", minX, minY, maxX, maxY, verts, indices, colors);
        byte[] result = PatchEntryInBytes(data, names, entry, newVerts, newIndices, newColors, minX, minY, maxX, maxY, nextCountPos);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, result);
        Console.WriteLine($"Patched cluster 0x{cluster.Start:X6} -> {outputPath}  ({result.Length} bytes)");
    }

    public static void PatchAtVertexStart(
        string uassetPath, string outputPath, int vertexStart, int oldVertexCount,
        List<(float X, float Y, float Z)> newVerts,
        List<ushort> newIndices,
        List<uint> newColors,
        float minX, float minY, float maxX, float maxY)
    {
        byte[] data = File.ReadAllBytes(uassetPath);
        var (names, exportStart, _) = PlgHeader.Read(data);
        int countPos = FindCountPos(data, exportStart, vertexStart, oldVertexCount);
        if (countPos < 0)
            throw new Exception($"Could not find vertex count for vertex array 0x{vertexStart:X}; cannot patch safely.");

        var oldVerts = new List<(float X, float Y, float Z)>();
        for (int i = 0; i < oldVertexCount; i++)
        {
            int p = vertexStart + i * 12;
            oldVerts.Add((BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8)));
        }

        int idxHdrStart = vertexStart + oldVertexCount * 12;
        int idxByteCount = RI32(data, idxHdrStart + IdxSizeFieldOff);
        int idxDataStart = idxHdrStart + AfterVertHdrSize;
        var oldIndices = new List<ushort>();
        for (int i = 0; i < idxByteCount / 2; i++)
        {
            int p = idxDataStart + i * 2;
            oldIndices.Add((ushort)((data[p] << 8) | data[p + 1]));
        }

        int idxDataEnd = idxDataStart + idxByteCount;
        int colCountPos = idxDataEnd + ColorCountOff;
        int colCount = RI32(data, colCountPos);
        var oldColors = new List<uint>();
        for (int i = 0; i < colCount; i++)
            oldColors.Add(BitConverter.ToUInt32(data, colCountPos + 4 + i * 4));

        var entry = new PlgDataEntry(countPos, $"vertex_0x{vertexStart:X}", minX, minY, maxX, maxY, oldVerts, oldIndices, oldColors);
        byte[] result = PatchEntryInBytes(data, names, entry, newVerts, newIndices, newColors, minX, minY, maxX, maxY);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, result);
        Console.WriteLine($"Patched vertex array 0x{vertexStart:X} -> {outputPath}  ({result.Length} bytes)");
    }

    public static void PatchSameSizeAtVertexStart(
        string uassetPath, string outputPath, int vertexStart, int oldVertexCount,
        List<(float X, float Y, float Z)> newVerts,
        List<uint> newColors)
    {
        if (newVerts.Count != oldVertexCount)
            throw new ArgumentException($"Same-size patch requires {oldVertexCount} vertices, got {newVerts.Count}.");

        byte[] data = File.ReadAllBytes(uassetPath);

        for (int i = 0; i < newVerts.Count; i++)
        {
            int p = vertexStart + i * 12;
            BitConverter.TryWriteBytes(data.AsSpan(p), newVerts[i].X);
            BitConverter.TryWriteBytes(data.AsSpan(p + 4), newVerts[i].Y);
            BitConverter.TryWriteBytes(data.AsSpan(p + 8), newVerts[i].Z);
        }

        int idxHdrStart = vertexStart + oldVertexCount * 12;
        int idxByteCount = RI32(data, idxHdrStart + IdxSizeFieldOff);
        int idxDataEnd = idxHdrStart + AfterVertHdrSize + idxByteCount;
        int colCountPos = idxDataEnd + ColorCountOff;
        int colCount = RI32(data, colCountPos);
        if (newColors.Count != colCount)
            throw new ArgumentException($"Same-size patch requires {colCount} colors, got {newColors.Count}.");

        int colDataStart = colCountPos + 4;
        for (int i = 0; i < newColors.Count; i++)
            BitConverter.TryWriteBytes(data.AsSpan(colDataStart + i * 4), newColors[i]);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, data);
        Console.WriteLine($"Patched in-place vertex array 0x{vertexStart:X} -> {outputPath}  ({data.Length} bytes)");
    }


    public static void PatchSameLayoutAtVertexStart(
        string uassetPath, string outputPath, int vertexStart, int oldVertexCount,
        List<(float X, float Y, float Z)> newVerts,
        List<ushort> newIndices,
        List<uint> newColors)
    {
        if (newVerts.Count != oldVertexCount)
            throw new ArgumentException($"Same-layout patch requires {oldVertexCount} vertices, got {newVerts.Count}.");

        byte[] data = File.ReadAllBytes(uassetPath);

        for (int i = 0; i < newVerts.Count; i++)
        {
            int p = vertexStart + i * 12;
            BitConverter.TryWriteBytes(data.AsSpan(p), newVerts[i].X);
            BitConverter.TryWriteBytes(data.AsSpan(p + 4), newVerts[i].Y);
            BitConverter.TryWriteBytes(data.AsSpan(p + 8), newVerts[i].Z);
        }

        int idxHdrStart = vertexStart + oldVertexCount * 12;
        int idxByteCount = RI32(data, idxHdrStart + IdxSizeFieldOff);
        int oldIndexCount = idxByteCount / 2;
        if (newIndices.Count > oldIndexCount)
            newIndices = newIndices.Take(oldIndexCount).ToList();
        if (newIndices.Count == 0)
            throw new ArgumentException($"Same-layout patch requires indices, got 0.");

        // Some PLG index buffers contain trailing bytes that CUE4Parse does not expose as real indices.
        // Preserve that tail verbatim; overwriting it can corrupt the following FName stream.
        Console.WriteLine($"Writing {newIndices.Count}/{oldIndexCount} raw index slots; preserving {(oldIndexCount - newIndices.Count) * 2} trailing bytes");

        int idxDataStart = idxHdrStart + AfterVertHdrSize;
        for (int i = 0; i < newIndices.Count; i++)
        {
            int p = idxDataStart + i * 2;
            data[p] = (byte)(newIndices[i] >> 8);
            data[p + 1] = (byte)(newIndices[i] & 0xFF);
        }

        int idxDataEnd = idxDataStart + idxByteCount;
        int colCountPos = idxDataEnd + ColorCountOff;
        int colCount = RI32(data, colCountPos);
        if (newColors.Count != colCount)
            throw new ArgumentException($"Same-layout patch requires {colCount} colors, got {newColors.Count}.");

        int colDataStart = colCountPos + 4;
        for (int i = 0; i < newColors.Count; i++)
            BitConverter.TryWriteBytes(data.AsSpan(colDataStart + i * 4), newColors[i]);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, data);
        Console.WriteLine($"Patched in-place vertex/index/color arrays 0x{vertexStart:X} -> {outputPath}  ({data.Length} bytes)");
    }
    static int FindCountPos(byte[] data, int minPos, int vertStart, int vertCount)
    {
        int from = Math.Max(minPos, vertStart - 256);
        int to = Math.Max(from, vertStart - 4);
        for (int pos = to; pos >= from; pos--)
            if (RI32(data, pos) == vertCount)
                return pos;
        return -1;
    }
    // JSON schema (mirrors PlgJson internal records)
    // -----------------------------------------------------------------------

    record PlgJsonDoc(
        [property: JsonPropertyName("Source")]    string? Source,
        [property: JsonPropertyName("EntryCount")] int EntryCount,
        [property: JsonPropertyName("Entries")]   List<PlgJsonEntry>? Entries);

    record PlgJsonEntry(
        [property: JsonPropertyName("Name")]     string? Name,
        [property: JsonPropertyName("MinX")]     float MinX,
        [property: JsonPropertyName("MinY")]     float MinY,
        [property: JsonPropertyName("MaxX")]     float MaxX,
        [property: JsonPropertyName("MaxY")]     float MaxY,
        [property: JsonPropertyName("Vertices")] List<float[]>? Vertices,
        [property: JsonPropertyName("Indices")]  List<int>? Indices,
        [property: JsonPropertyName("Colors")]   List<long>? Colors);

    static int RI32(byte[] d, int o) => o + 3 < d.Length ? BitConverter.ToInt32(d, o) : 0;
}







