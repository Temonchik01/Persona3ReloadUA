using CUE4Parse.UE4.Assets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace P3RPlgTool;

internal static class PlgCueEditor
{
    public static void ExportGameSvg(string gameDir, string assetPath, string aesKey, string selector, string outSvg)
    {
        var package = CuePackageReader.LoadGamePackage(gameDir, assetPath, aesKey);
        var root = ToJsonRoot(package);
        var entry = SelectEntry(root, selector);
        PlgSvg.ExportEntry(entry.ToPlgDataEntry(), outSvg);
    }

    public static void ExportJsonSvg(string cueJsonPath, string selector, string outSvg)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var entry = SelectEntry(root, selector);
        PlgSvg.ExportEntry(entry.ToPlgDataEntry(), outSvg);
    }

    public static void PatchFromSvg(string rawUassetPath, string cueJsonPath, string selector, string svgPath, string outUassetPath, uint color)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var original = SelectEntry(root, selector);
        if (original.Vertices.Count == 0)
            throw new InvalidOperationException($"Entry '{selector}' is empty and cannot be patched safely.");

        var (importedVerts, importedIndices, importedColors, _, _, _, _) = SvgImport.Import(svgPath, color);
        if (importedVerts.Count == 0 || importedIndices.Count == 0)
            throw new InvalidOperationException("Imported SVG produced no geometry.");

        var convertedVerts = importedVerts
            .Select(v => (X: original.MinX + v.X, Y: original.MaxY - v.Y, Z: v.Z))
            .ToList();

        var minX = convertedVerts.Min(v => v.X);
        var minY = convertedVerts.Min(v => v.Y);
        var maxX = convertedVerts.Max(v => v.X);
        var maxY = convertedVerts.Max(v => v.Y);

        var clusterStart = FindVertexArrayStart(rawUassetPath, original.Vertices);
        Console.WriteLine($"Matched '{original.Name}' at vertex array 0x{clusterStart:X}");

        PlgPacker.PatchAtVertexStart(
            rawUassetPath,
            outUassetPath,
            clusterStart,
            original.Vertices.Count,
            convertedVerts,
            importedIndices,
            importedColors,
            minX,
            minY,
            maxX,
            maxY);
    }


    public static void PatchInPlaceFromSvg(string rawUassetPath, string cueJsonPath, string selector, string svgPath, string outUassetPath, uint color)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var original = SelectEntry(root, selector);
        if (original.Vertices.Count == 0)
            throw new InvalidOperationException($"Entry '{selector}' is empty and cannot be patched safely.");

        var (importedVerts, importedIndices, _, importMinX, importMinY, importMaxX, importMaxY) = SvgImport.Import(svgPath, color);
        if (importedVerts.Count == 0 || importedIndices.Count == 0)
            throw new InvalidOperationException("Imported SVG produced no geometry.");

        var srcW = Math.Max(1f, importMaxX - importMinX);
        var srcH = Math.Max(1f, importMaxY - importMinY);
        var dstW = Math.Max(1f, original.MaxX - original.MinX);
        var dstH = Math.Max(1f, original.MaxY - original.MinY);

        var fitted = importedVerts
            .Select(v =>
            {
                var nx = (v.X - importMinX) / srcW;
                var ny = (v.Y - importMinY) / srcH;
                return (X: original.MinX + nx * dstW, Y: original.MaxY - ny * dstH, Z: v.Z);
            })
            .ToList();

        var verts = PadVertices(fitted, original.Vertices.Count);
        var indices = PadIndices(importedIndices, original.Indices.Count);
        var colors = Enumerable.Repeat(color, original.Colors.Count).ToList();

        var vertexStart = FindVertexArrayStart(rawUassetPath, original.Vertices);
        Console.WriteLine($"Matched '{original.Name}' at vertex array 0x{vertexStart:X}");
        Console.WriteLine($"SVG {importedVerts.Count} verts/{importedIndices.Count} indices -> in-place {verts.Count} verts/{indices.Count} indices; raw tail preserved");
        PlgPacker.PatchSameLayoutAtVertexStart(rawUassetPath, outUassetPath, vertexStart, original.Vertices.Count, verts, indices, colors);
    }




    public static void PatchInPlaceFromTemplateSvg(string rawUassetPath, string cueJsonPath, string selector, string svgPath, string outUassetPath, uint color)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var original = SelectEntry(root, selector);
        if (original.Vertices.Count == 0)
            throw new InvalidOperationException($"Entry '{selector}' is empty and cannot be patched safely.");

        var (importedVerts, importedIndices, _, _, _, _, _) = SvgImport.Import(svgPath, color);
        if (importedVerts.Count == 0 || importedIndices.Count == 0)
            throw new InvalidOperationException("Imported SVG produced no geometry.");

        const float templateW = 2400f;
        const float templateH = 1746f;

        // The NEWGAME entry contains long perspective/helper triangles, so the raw
        // Min/Max is much larger than the visible text. Use the dense vertex area
        // as the template target and keep Illustrator's absolute artboard layout.
        var xs = original.Vertices.Select(v => v.X).OrderBy(v => v).ToList();
        var ys = original.Vertices.Select(v => v.Y).OrderBy(v => v).ToList();
        var targetMinX = Percentile(xs, 0.05f);
        var targetMaxX = Percentile(xs, 0.95f);
        var targetMinY = Percentile(ys, 0.05f);
        var targetMaxY = Percentile(ys, 0.95f);
        var dstW = Math.Max(1f, targetMaxX - targetMinX);
        var dstH = Math.Max(1f, targetMaxY - targetMinY);

        var fitted = importedVerts
            .Select(v =>
            {
                var nx = v.X / templateW;
                var ny = v.Y / templateH;
                return (X: targetMinX + nx * dstW, Y: targetMaxY - ny * dstH, Z: v.Z);
            })
            .ToList();

        var verts = PadVertices(fitted, original.Vertices.Count);
        var indices = PadIndices(importedIndices, original.Indices.Count);
        var colors = Enumerable.Repeat(color, original.Colors.Count).ToList();

        var vertexStart = FindVertexArrayStart(rawUassetPath, original.Vertices);
        Console.WriteLine($"Matched '{original.Name}' at vertex array 0x{vertexStart:X}");
        Console.WriteLine($"Template SVG {importedVerts.Count} verts/{importedIndices.Count} indices -> in-place {verts.Count} verts/{indices.Count} indices; raw tail preserved");
        PlgPacker.PatchSameLayoutAtVertexStart(rawUassetPath, outUassetPath, vertexStart, original.Vertices.Count, verts, indices, colors);
    }
    public static void PatchInPlaceFromTemplateSvgExtrude(string rawUassetPath, string cueJsonPath, string selector, string svgPath, string outUassetPath)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var original = SelectEntry(root, selector);
        if (original.Vertices.Count == 0)
            throw new InvalidOperationException($"Entry '{selector}' is empty and cannot be patched safely.");

        const float templateW = 2400f;
        const float templateH = 1746f;
        var frontColor = original.Colors.Count > 0 ? original.Colors[0] : 0xFFFFFFFF;
        var sideColor = original.Colors.Count > original.Colors.Count / 2 ? original.Colors[original.Colors.Count / 2] : frontColor;

        var (importedVerts, importedIndices, _, _, _, _, _) = SvgImport.Import(svgPath, frontColor);
        if (importedVerts.Count == 0 || importedIndices.Count == 0)
            throw new InvalidOperationException("Imported SVG produced no geometry.");
        if (importedVerts.Count * 2 > original.Vertices.Count)
            throw new InvalidOperationException($"Extruded SVG has too many vertices: {importedVerts.Count * 2}/{original.Vertices.Count}. Simplify the SVG paths.");

        var xs = original.Vertices.Select(v => v.X).OrderBy(v => v).ToList();
        var ys = original.Vertices.Select(v => v.Y).OrderBy(v => v).ToList();
        var targetMinX = Percentile(xs, 0.05f);
        var targetMaxX = Percentile(xs, 0.95f);
        var targetMinY = Percentile(ys, 0.05f);
        var targetMaxY = Percentile(ys, 0.95f);
        var dstW = Math.Max(1f, targetMaxX - targetMinX);
        var dstH = Math.Max(1f, targetMaxY - targetMinY);

        var front = importedVerts
            .Select(v =>
            {
                var nx = v.X / templateW;
                var ny = v.Y / templateH;
                return (X: targetMinX + nx * dstW, Y: targetMaxY - ny * dstH, Z: v.Z);
            })
            .ToList();

        var depthX = (original.MaxX - original.MinX) / templateW;
        var depthY = -(original.MaxY - original.MinY) / templateH;
        var back = front.Select(v => (X: v.X + depthX, Y: v.Y + depthY, Z: v.Z)).ToList();

        var edges = new Dictionary<(ushort A, ushort B), int>();
        void AddEdge(ushort a, ushort b)
        {
            var key = a < b ? (a, b) : (b, a);
            edges.TryGetValue(key, out var count);
            edges[key] = count + 1;
        }

        for (var i = 0; i + 2 < importedIndices.Count; i += 3)
        {
            AddEdge(importedIndices[i], importedIndices[i + 1]);
            AddEdge(importedIndices[i + 1], importedIndices[i + 2]);
            AddEdge(importedIndices[i + 2], importedIndices[i]);
        }

        var outVerts = new List<(float X, float Y, float Z)>(front.Count + back.Count);
        outVerts.AddRange(front);
        outVerts.AddRange(back);
        var backBase = checked((ushort)front.Count);

        var outIndices = new List<ushort>(original.Indices.Count);
        outIndices.AddRange(importedIndices);

        foreach (var ((a, b), count) in edges.OrderBy(e => e.Key.A).ThenBy(e => e.Key.B))
        {
            if (count != 1)
                continue;

            var ab = checked((ushort)(a + backBase));
            var bb = checked((ushort)(b + backBase));
            if (outIndices.Count + 6 > original.Indices.Count)
                break;

            outIndices.Add(a); outIndices.Add(b); outIndices.Add(bb);
            outIndices.Add(a); outIndices.Add(bb); outIndices.Add(ab);
        }

        var verts = PadVertices(outVerts, original.Vertices.Count);
        var indices = PadIndices(outIndices, original.Indices.Count);
        var colors = new List<uint>(original.Colors.Count);
        for (var i = 0; i < original.Colors.Count; i++)
            colors.Add(i < front.Count ? frontColor : sideColor);

        var vertexStart = FindVertexArrayStart(rawUassetPath, original.Vertices);
        Console.WriteLine($"Matched '{original.Name}' at vertex array 0x{vertexStart:X}");
        Console.WriteLine($"Template SVG extrude {importedVerts.Count} front verts/{importedIndices.Count} front indices -> {outVerts.Count} verts/{outIndices.Count} indices before padding");
        Console.WriteLine($"Boundary edges: {edges.Count(e => e.Value == 1)}; depth=({depthX:F2},{depthY:F2})");
        PlgPacker.PatchSameLayoutAtVertexStart(rawUassetPath, outUassetPath, vertexStart, original.Vertices.Count, verts, indices, colors);
    }
    public static void PatchInPlaceFromTemplateSvgMask(string rawUassetPath, string cueJsonPath, string selector, string svgPath, string outUassetPath, uint color)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var original = SelectEntry(root, selector);
        if (original.Vertices.Count == 0)
            throw new InvalidOperationException($"Entry '{selector}' is empty and cannot be patched safely.");

        const float templateW = 2400f;
        const float templateH = 1746f;
        var (importedVerts, importedIndices, _, _, _, _, _) = SvgImport.ImportPunchout(svgPath, templateW, templateH, color);
        if (importedVerts.Count == 0 || importedIndices.Count == 0)
            throw new InvalidOperationException("Imported SVG mask produced no geometry.");

        var xs = original.Vertices.Select(v => v.X).OrderBy(v => v).ToList();
        var ys = original.Vertices.Select(v => v.Y).OrderBy(v => v).ToList();
        var targetMinX = Percentile(xs, 0.05f);
        var targetMaxX = Percentile(xs, 0.95f);
        var targetMinY = Percentile(ys, 0.05f);
        var targetMaxY = Percentile(ys, 0.95f);
        var dstW = Math.Max(1f, targetMaxX - targetMinX);
        var dstH = Math.Max(1f, targetMaxY - targetMinY);

        var fitted = importedVerts
            .Select(v =>
            {
                var nx = v.X / templateW;
                var ny = v.Y / templateH;
                return (X: targetMinX + nx * dstW, Y: targetMaxY - ny * dstH, Z: v.Z);
            })
            .ToList();

        var verts = PadVertices(fitted, original.Vertices.Count);
        var indices = PadIndices(importedIndices, original.Indices.Count);
        var colors = Enumerable.Repeat(color, original.Colors.Count).ToList();

        var vertexStart = FindVertexArrayStart(rawUassetPath, original.Vertices);
        Console.WriteLine($"Matched '{original.Name}' at vertex array 0x{vertexStart:X}");
        Console.WriteLine($"Template SVG mask {importedVerts.Count} verts/{importedIndices.Count} indices -> in-place {verts.Count} verts/{indices.Count} indices; raw tail preserved");
        PlgPacker.PatchSameLayoutAtVertexStart(rawUassetPath, outUassetPath, vertexStart, original.Vertices.Count, verts, indices, colors);
    }
    public static void PatchInPlaceFromPngGrid(string rawUassetPath, string cueJsonPath, string selector, string pngPath, string outUassetPath, uint color)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var original = SelectEntry(root, selector);
        if (original.Vertices.Count == 0)
            throw new InvalidOperationException($"Entry '{selector}' is empty and cannot be patched safely.");

        using var bmp = System.Drawing.Bitmap.FromFile(pngPath) as System.Drawing.Bitmap
            ?? throw new InvalidOperationException("Could not load PNG mask.");

        const int cols = 36;
        const int rows = 18;
        const int alphaThreshold = 48;
        const float fillThreshold = 0.08f;

        var verts = new List<(float X, float Y, float Z)>();
        var indices = new List<ushort>();
        var vertexByGrid = new Dictionary<(int X, int Y), ushort>();

        ushort AddVertex(int gx, int gy)
        {
            var key = (gx, gy);
            if (vertexByGrid.TryGetValue(key, out var existing))
                return existing;

            if (verts.Count >= original.Vertices.Count)
                throw new InvalidOperationException("PNG grid produced too many vertices for this PLG entry.");

            var nx = (float)gx / cols;
            var ny = (float)gy / rows;
            var x = original.MinX + nx * (original.MaxX - original.MinX);
            var y = original.MaxY - ny * (original.MaxY - original.MinY);
            var idx = checked((ushort)verts.Count);
            verts.Add((x, y, 0f));
            vertexByGrid[key] = idx;
            return idx;
        }

        for (var cy = 0; cy < rows; cy++)
        {
            for (var cx = 0; cx < cols; cx++)
            {
                var x0 = cx * bmp.Width / cols;
                var x1 = (cx + 1) * bmp.Width / cols;
                var y0 = cy * bmp.Height / rows;
                var y1 = (cy + 1) * bmp.Height / rows;

                var total = 0;
                var filled = 0;
                var stepX = Math.Max(1, (x1 - x0) / 5);
                var stepY = Math.Max(1, (y1 - y0) / 5);
                for (var y = y0; y < y1; y += stepY)
                {
                    for (var x = x0; x < x1; x += stepX)
                    {
                        total++;
                        var c = bmp.GetPixel(Math.Min(x, bmp.Width - 1), Math.Min(y, bmp.Height - 1));
                        if (c.A >= alphaThreshold)
                            filled++;
                    }
                }

                if (total == 0 || (float)filled / total < fillThreshold)
                    continue;

                if (indices.Count + 6 > original.Indices.Count)
                    continue;

                var a = AddVertex(cx, cy);
                var b = AddVertex(cx + 1, cy);
                var c0 = AddVertex(cx + 1, cy + 1);
                var d = AddVertex(cx, cy + 1);

                indices.Add(a); indices.Add(b); indices.Add(c0);
                indices.Add(a); indices.Add(c0); indices.Add(d);
            }
        }

        if (verts.Count == 0 || indices.Count == 0)
            throw new InvalidOperationException("PNG mask produced no grid geometry.");

        var paddedVerts = PadVertices(verts, original.Vertices.Count);
        var paddedIndices = PadIndices(indices, original.Indices.Count);
        var colors = Enumerable.Repeat(color, original.Colors.Count).ToList();

        var vertexStart = FindVertexArrayStart(rawUassetPath, original.Vertices);
        Console.WriteLine($"Matched '{original.Name}' at vertex array 0x{vertexStart:X}");
        Console.WriteLine($"PNG grid {cols}x{rows}: {verts.Count} verts/{indices.Count} indices -> in-place {paddedVerts.Count} verts/{paddedIndices.Count} indices; raw tail preserved");
        PlgPacker.PatchSameLayoutAtVertexStart(rawUassetPath, outUassetPath, vertexStart, original.Vertices.Count, paddedVerts, paddedIndices, colors);
    }
    private static float Percentile(List<float> sortedValues, float p)
    {
        if (sortedValues.Count == 0)
            return 0f;
        var idx = (int)MathF.Round((sortedValues.Count - 1) * Math.Clamp(p, 0f, 1f));
        return sortedValues[Math.Clamp(idx, 0, sortedValues.Count - 1)];
    }
    private static List<(float X, float Y, float Z)> PadVertices(List<(float X, float Y, float Z)> source, int count)
    {
        if (source.Count == 0)
            return Enumerable.Repeat((0f, 0f, 0f), count).ToList();

        var result = new List<(float X, float Y, float Z)>(count);
        for (var i = 0; i < count; i++)
            result.Add(i < source.Count ? source[i] : source[0]);
        return result;
    }

    private static List<ushort> PadIndices(List<ushort> source, int count)
    {
        var result = new List<ushort>(count);
        for (var i = 0; i < count; i++)
            result.Add(i < source.Count ? source[i] : (ushort)0);
        return result;
    }
    private static List<(float X, float Y, float Z)> ResampleVertices(List<(float X, float Y, float Z)> source, int count)
    {
        if (source.Count == count)
            return source.ToList();
        if (source.Count == 1)
            return Enumerable.Repeat(source[0], count).ToList();

        var result = new List<(float X, float Y, float Z)>(count);
        for (var i = 0; i < count; i++)
        {
            var pos = (double)i * (source.Count - 1) / Math.Max(1, count - 1);
            var a = (int)Math.Floor(pos);
            var b = Math.Min(source.Count - 1, a + 1);
            var t = (float)(pos - a);
            var va = source[a];
            var vb = source[b];
            result.Add((
                va.X + (vb.X - va.X) * t,
                va.Y + (vb.Y - va.Y) * t,
                va.Z + (vb.Z - va.Z) * t));
        }
        return result;
    }

    private static List<ushort> ResampleIndices(List<ushort> source, int sourceVertexCount, int indexCount, int targetVertexCount)
    {
        var result = new List<ushort>(indexCount);
        if (source.Count == 0 || sourceVertexCount <= 0)
            return Enumerable.Repeat((ushort)0, indexCount).ToList();

        for (var i = 0; i < indexCount; i++)
        {
            var srcIndex = source[i % source.Count];
            var mapped = sourceVertexCount <= 1
                ? 0
                : (int)Math.Round((double)srcIndex * (targetVertexCount - 1) / (sourceVertexCount - 1));
            result.Add((ushort)Math.Clamp(mapped, 0, targetVertexCount - 1));
        }
        return result;
    }
    public static void PatchTestInPlace(string rawUassetPath, string cueJsonPath, string selector, string outUassetPath)
    {
        var root = JObject.Parse(File.ReadAllText(cueJsonPath));
        var original = SelectEntry(root, selector);
        if (original.Vertices.Count == 0)
            throw new InvalidOperationException($"Entry '{selector}' is empty and cannot be patched safely.");

        var centerX = (original.MinX + original.MaxX) * 0.5f;
        var centerY = (original.MinY + original.MaxY) * 0.5f;
        const float scale = 2.25f;

        var verts = original.Vertices
            .Select(v => (
                X: centerX + (v.X - centerX) * scale,
                Y: centerY + (v.Y - centerY) * scale,
                Z: v.Z))
            .ToList();

        var colors = new List<uint>(original.Colors.Count);
        for (var i = 0; i < original.Colors.Count; i++)
            colors.Add(i % 3 == 0 ? 0xFFFFFFFFu : 0xFF2020FFu); // FColor BGRA: red with white streaks

        var vertexStart = FindVertexArrayStart(rawUassetPath, original.Vertices);
        Console.WriteLine($"Matched '{original.Name}' at vertex array 0x{vertexStart:X}");
        PlgPacker.PatchSameSizeAtVertexStart(rawUassetPath, outUassetPath, vertexStart, original.Vertices.Count, verts, colors);
    }

    private static int FindVertexArrayStart(string rawUassetPath, List<(float X, float Y, float Z)> vertices)
    {
        var data = File.ReadAllBytes(rawUassetPath);
        var needle = BuildVertexNeedle(vertices);
        var matches = FindAll(data, needle).ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException("Could not find original CUE vertex sequence in raw uasset.");
        if (matches.Count > 1)
            throw new InvalidOperationException($"Original vertex sequence matched {matches.Count} places. Use a larger/non-empty entry or inspect manually.");

        return matches[0];
    }

    private static byte[] BuildVertexNeedle(List<(float X, float Y, float Z)> vertices)
    {
        var take = Math.Min(vertices.Count, vertices.Count < 8 ? vertices.Count : 16);
        using var ms = new MemoryStream(take * 12);
        using var bw = new BinaryWriter(ms);
        for (var i = 0; i < take; i++)
        {
            bw.Write(vertices[i].X);
            bw.Write(vertices[i].Y);
            bw.Write(vertices[i].Z);
        }
        return ms.ToArray();
    }

    private static IEnumerable<int> FindAll(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            yield break;

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                ok = false;
                break;
            }

            if (ok) yield return i;
        }
    }

    private static JObject ToJsonRoot(IPackage package)
    {
        var data = new { Exports = package.GetExports() };
        return JObject.Parse(JsonConvert.SerializeObject(data));
    }

    private static CuePlgEntry SelectEntry(JObject root, string selector)
    {
        var entries = root.SelectToken("Exports[0].Properties.PlgData.PlgDatas") as JArray;
        if (entries == null)
            throw new InvalidDataException("Could not find Exports[0].Properties.PlgData.PlgDatas.");

        JToken? token = null;
        if (int.TryParse(selector, out var index))
        {
            if (index < 0 || index >= entries.Count)
                throw new IndexOutOfRangeException($"Entry index {index} is outside 0..{entries.Count - 1}.");
            token = entries[index];
        }
        else
        {
            token = entries.FirstOrDefault(e =>
                string.Equals(e.Value<string>("Name"), selector, StringComparison.OrdinalIgnoreCase));
        }

        if (token == null)
            throw new InvalidOperationException($"PLG entry '{selector}' was not found.");

        return CuePlgEntry.FromToken(token);
    }

    private sealed record CuePlgEntry(
        string Name,
        List<(float X, float Y, float Z)> Vertices,
        List<ushort> Indices,
        List<uint> Colors)
    {
        public float MinX => Vertices.Count == 0 ? 0 : Vertices.Min(v => v.X);
        public float MinY => Vertices.Count == 0 ? 0 : Vertices.Min(v => v.Y);
        public float MaxX => Vertices.Count == 0 ? 0 : Vertices.Max(v => v.X);
        public float MaxY => Vertices.Count == 0 ? 0 : Vertices.Max(v => v.Y);

        public PlgDataEntry ToPlgDataEntry() =>
            new(0, Name, MinX, MinY, MaxX, MaxY, Vertices, Indices, Colors);

        public static CuePlgEntry FromToken(JToken token)
        {
            var name = token.Value<string>("Name") ?? "None";
            var vertices = new List<(float X, float Y, float Z)>();
            foreach (var v in token["Vertices"] as JArray ?? [])
            {
                vertices.Add((
                    v.Value<float>("X"),
                    v.Value<float>("Y"),
                    v.Value<float>("Z")));
            }

            var indices = (token["Indices"] as JArray ?? [])
                .Select(i => checked((ushort)i.Value<int>()))
                .ToList();

            var colors = (token["Colors"] as JArray ?? [])
                .Select(c => unchecked((uint)c.Value<long>()))
                .ToList();

            return new CuePlgEntry(name, vertices, indices, colors);
        }
    }
}















