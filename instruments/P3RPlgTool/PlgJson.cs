using System.Text.Json;
using System.Text.Json.Serialization;

namespace P3RPlgTool;

public static class PlgJson
{
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static void Export(string inputPath, string outputPath, bool debug = false)
    {
        byte[] data = File.ReadAllBytes(inputPath);
        var (names, exportStart, _) = PlgHeader.Read(data);
        var entries = PlgParser.Parse(data, names, debug);

        var doc = new PlgDocument(
            Source: Path.GetFileName(inputPath),
            EntryCount: entries.Count,
            Entries: entries.Select(e => new PlgEntryJson(
                Name: e.Name,
                MinX: e.MinX, MinY: e.MinY, MaxX: e.MaxX, MaxY: e.MaxY,
                Vertices: e.Vertices.Select(v => new float[] { v.X, v.Y, v.Z }).ToList(),
                Indices: e.Indices.Select(v => (int)v).ToList(),
                Colors: e.Colors.Select(c => (long)c).ToList()
            )).ToList()
        );

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(doc, Opts));
        Console.WriteLine($"Exported {entries.Count} PlgData entries -> {outputPath}");
    }

    record PlgDocument(
        string Source,
        int EntryCount,
        List<PlgEntryJson> Entries);

    record PlgEntryJson(
        string Name,
        float MinX, float MinY, float MaxX, float MaxY,
        List<float[]> Vertices,
        List<int> Indices,
        [property: JsonPropertyName("Colors")]
        List<long> Colors);
}
