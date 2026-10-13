using System.Text;

namespace P3RPlgTool;

public static class PlgSvg
{
    public static void Export(string inputPath, string? entryName, string outputPath)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        var (names, _, _) = PlgHeader.Read(data);
        var entries = PlgParser.Parse(data, names);

        if (string.IsNullOrEmpty(entryName))
        {
            Console.WriteLine("Available entries:");
            foreach (var e in entries)
                Console.WriteLine($"  {e.Name}  ({e.Vertices.Count} verts)");
            return;
        }

        var entry = entries.FirstOrDefault(e =>
            e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            Console.WriteLine($"Entry '{entryName}' not found.");
            Console.WriteLine("Available:");
            foreach (var e in entries) Console.WriteLine($"  {e.Name}");
            return;
        }

        ExportEntry(entry, outputPath);
    }

    public static void ExportEntry(PlgDataEntry e, string outputPath)
    {
        if (e.Vertices.Count == 0)
        {
            Console.WriteLine("Entry has no vertices.");
            return;
        }

        float minX = e.Vertices.Min(v => v.X);
        float maxX = e.Vertices.Max(v => v.X);
        float minY = e.Vertices.Min(v => v.Y);
        float maxY = e.Vertices.Max(v => v.Y);
        float w    = maxX - minX;
        float h    = maxY - minY;

        // PLG Y-axis: up is positive (game space). SVG Y: down is positive.
        // Flip Y so the shape appears correct in Illustrator.
        // Flipped Y for vertex v: yFlipped = maxY - (v.Y - minY) = maxY + minY - v.Y

        var verts   = e.Vertices;
        var indices = e.Indices;

        // Group triangles by color, build one <path> per color group
        // This eliminates seams between adjacent triangles.
        var groups = new Dictionary<string, StringBuilder>();

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            if (i0 >= verts.Count || i1 >= verts.Count || i2 >= verts.Count) continue;

            string fill = GetFill(e.Colors, i0);
            if (fill == "none") continue;

            (float x0, float y0) = ToSvg(verts[i0], minX, minY, maxY);
            (float x1, float y1) = ToSvg(verts[i1], minX, minY, maxY);
            (float x2, float y2) = ToSvg(verts[i2], minX, minY, maxY);

            if (!groups.TryGetValue(fill, out var gSb))
                groups[fill] = gSb = new StringBuilder();

            gSb.Append($"M{x0:F2},{y0:F2}L{x1:F2},{y1:F2}L{x2:F2},{y2:F2}Z");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w:F3} {h:F3}\" width=\"{(int)w}\" height=\"{(int)h}\">");
        sb.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#1a1a2e\"/>");

        foreach (var (fill, pathSb) in groups)
            sb.AppendLine($"  <path d=\"{pathSb}\" fill=\"{fill}\" stroke=\"{fill}\" stroke-width=\"0.5\" stroke-linejoin=\"round\"/>");

        sb.AppendLine("</svg>");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, sb.ToString());
        Console.WriteLine($"SVG exported: {e.Vertices.Count} verts, {indices.Count / 3} triangles -> {outputPath}");
    }

    // Export every raw vertex cluster found in the file — one SVG per cluster.
    // Bypasses name/property parsing entirely, same approach as the PNG preview.
    public static void ExportAllRaw(string inputPath, string outDir)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        const int EntriesStart = 0xAF0;
        var clusters = PlgParser.FindVertexClusters(data, EntriesStart);
        Directory.CreateDirectory(outDir);

        int saved = 0;
        for (int ci = 0; ci < clusters.Count; ci++)
        {
            var (start, count, end) = clusters[ci];

            // Read vertices (we know exactly where they are)
            var verts = new List<(float X, float Y, float Z)>(count);
            for (int i = 0; i < count; i++)
            {
                int p = start + i * 12;
                verts.Add((
                    BitConverter.ToSingle(data, p),
                    BitConverter.ToSingle(data, p + 4),
                    BitConverter.ToSingle(data, p + 8)));
            }

            // Read index section: 36-byte header, index byte count at header+16
            int idxHdrStart    = end;
            int idxByteSizePos = idxHdrStart + 16;
            if (idxByteSizePos + 4 > data.Length) continue;
            int idxByteCount = BitConverter.ToInt32(data, idxByteSizePos);
            if (idxByteCount <= 0 || idxByteCount > 2_000_000) continue;

            int idxDataStart = idxHdrStart + 36;
            int idxDataEnd   = idxDataStart + idxByteCount;
            if (idxDataEnd > data.Length) continue;

            int numIdx = idxByteCount / 2;
            var indices = new List<ushort>(numIdx);
            for (int i = 0; i < numIdx; i++)
            {
                int p = idxDataStart + i * 2;
                indices.Add((ushort)((data[p] << 8) | data[p + 1]));
            }

            // Read colors: after index data, 36-byte header, count at +30
            int colHdrStart = idxDataEnd;
            int colCountPos = colHdrStart + 30;
            List<uint> colors = new();
            if (colCountPos + 4 <= data.Length)
            {
                int colCount = BitConverter.ToInt32(data, colCountPos);
                if (colCount > 0 && colCount <= 200_000 && colCountPos + 4 + colCount * 4 <= data.Length)
                {
                    for (int i = 0; i < colCount; i++)
                        colors.Add(BitConverter.ToUInt32(data, colCountPos + 4 + i * 4));
                }
            }

            // Build entry and export
            var entry = new PlgDataEntry(
                BinaryOffset: start,
                Name: $"cluster_{ci:D3}_v{count}",
                MinX: 0, MinY: 0, MaxX: 0, MaxY: 0,
                Vertices: verts,
                Indices:  indices,
                Colors:   colors);

            string outPath = Path.Combine(outDir, $"cluster_{ci:D3}_v{count}_0x{start:X6}.svg");
            ExportEntry(entry, outPath);
            saved++;
        }
        Console.WriteLine($"Exported {saved}/{clusters.Count} clusters -> {outDir}");
    }

    // PLG color uint32 is stored as BGRA (UE FColor memory layout, read as LE uint32):
    //   bits [7..0]   = Blue
    //   bits [15..8]  = Green
    //   bits [23..16] = Red
    //   bits [31..24] = Alpha
    static string GetFill(List<uint> colors, int idx)
    {
        if (colors.Count == 0) return "#FFD700";
        uint c = idx < colors.Count ? colors[idx] : colors[0];
        byte b = (byte)(c >> 0);
        byte g = (byte)(c >> 8);
        byte r = (byte)(c >> 16);
        byte a = (byte)(c >> 24);
        if (a == 0) return "none";
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    static (float x, float y) ToSvg(
        (float X, float Y, float Z) v,
        float minX, float minY, float maxY)
    {
        float x = v.X - minX;
        float y = maxY - v.Y;   // flip Y
        return (x, y);
    }
}
