using System.Text;
using System.Text.Json;

namespace P3RPlgTool;

public static class FModelSvg
{
    public static void Export(string fmodelJsonPath, string entryName, string outputPath)
    {
        var json = File.ReadAllText(fmodelJsonPath);
        using var doc = JsonDocument.Parse(json);

        // Structure: [ { "Properties": { "PlgData": { "PlgDatas": [ {...}, ... ] } } } ]
        var root       = doc.RootElement;
        var plgDatas   = root[0]
            .GetProperty("Properties")
            .GetProperty("PlgData")
            .GetProperty("PlgDatas");

        JsonElement? found = null;
        foreach (var entry in plgDatas.EnumerateArray())
        {
            if (entry.GetProperty("Name").GetString() == entryName)
            {
                found = entry;
                break;
            }
        }

        if (found is null)
        {
            Console.WriteLine($"Entry '{entryName}' not found. Available:");
            foreach (var e in plgDatas.EnumerateArray())
                Console.WriteLine($"  {e.GetProperty("Name").GetString()}");
            return;
        }

        var entry2 = found.Value;

        // Read vertices
        var vertsArr = entry2.GetProperty("Vertices");
        var verts = new List<(float X, float Y)>();
        foreach (var v in vertsArr.EnumerateArray())
            verts.Add((v.GetProperty("X").GetSingle(), v.GetProperty("Y").GetSingle()));

        // Read indices
        var idxArr = entry2.GetProperty("Indices");
        var indices = new List<int>();
        foreach (var idx in idxArr.EnumerateArray())
            indices.Add(idx.GetInt32());

        // Read colors
        var colArr = entry2.GetProperty("Colors");
        var colors = new List<uint>();
        foreach (var c in colArr.EnumerateArray())
            colors.Add(c.GetUInt32());

        if (verts.Count == 0)
        {
            Console.WriteLine($"Entry '{entryName}' has no vertices.");
            return;
        }

        float minX = verts.Min(v => v.X);
        float maxX = verts.Max(v => v.X);
        float minY = verts.Min(v => v.Y);
        float maxY = verts.Max(v => v.Y);
        float w    = maxX - minX;
        float h    = maxY - minY;

        // Group triangles by fill color into one <path> per color.
        // PLG Y-axis: up = positive. SVG Y-axis: down = positive → flip Y.
        var groups = new Dictionary<string, StringBuilder>();

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            if (i0 >= verts.Count || i1 >= verts.Count || i2 >= verts.Count) continue;

            string fill = GetFill(colors, i0);
            if (fill == "none") continue;

            (float x0, float y0) = Tr(verts[i0], minX, minY);
            (float x1, float y1) = Tr(verts[i1], minX, minY);
            (float x2, float y2) = Tr(verts[i2], minX, minY);

            if (!groups.TryGetValue(fill, out var sb))
                groups[fill] = sb = new StringBuilder();

            sb.Append($"M{x0:F2},{y0:F2}L{x1:F2},{y1:F2}L{x2:F2},{y2:F2}Z");
        }

        var svg = new StringBuilder();
        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w:F2} {h:F2}\" width=\"{(int)w}\" height=\"{(int)h}\">");
        svg.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#1a1a2e\"/>");
        foreach (var (fill, path) in groups)
            svg.AppendLine($"  <path d=\"{path}\" fill=\"{fill}\" stroke=\"{fill}\" stroke-width=\"0.5\" stroke-linejoin=\"round\"/>");
        svg.AppendLine("</svg>");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, svg.ToString());
        Console.WriteLine($"SVG: {verts.Count} verts, {indices.Count / 3} tris -> {outputPath}");
    }

    // FModel exports colors as uint32 ARGB (0xAARRGGBB).
    static string GetFill(List<uint> colors, int idx)
    {
        if (colors.Count == 0) return "#FFD700";
        uint c = idx < colors.Count ? colors[idx] : colors[0];
        byte a = (byte)(c >> 24);
        byte r = (byte)(c >> 16);
        byte g = (byte)(c >> 8);
        byte b = (byte)(c >> 0);
        if (a == 0) return "none";
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    static (float x, float y) Tr((float X, float Y) v, float minX, float minY)
        => (v.X - minX, v.Y - minY);
}
