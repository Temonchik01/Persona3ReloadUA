using CUE4Parse.UE4.Assets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace P3RPlgTool;

internal static class PlgCueInspector
{
    public static void ListGameEntries(string gameDir, string assetPath, string aesKey)
    {
        var package = CuePackageReader.LoadGamePackage(gameDir, assetPath, aesKey);
        var root = ToJsonRoot(package);
        var entries = root.SelectToken("Exports[0].Properties.PlgData.PlgDatas") as JArray;
        if (entries == null)
            throw new InvalidDataException("Could not find Exports[0].Properties.PlgData.PlgDatas in CUE4Parse output.");

        Console.WriteLine($"Package: {package.Name}");
        Console.WriteLine($"PlgDatas: {entries.Count}");
        Console.WriteLine();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var name = entry.Value<string>("Name") ?? "<no name>";
            var vertices = (entry["Vertices"] as JArray)?.Count ?? 0;
            var indices = (entry["Indices"] as JArray)?.Count ?? 0;
            var colors = (entry["Colors"] as JArray)?.Count ?? 0;
            Console.WriteLine($"[{i:000}] {name,-36} verts={vertices,5} tris={indices / 3,5} colors={colors,5}");
        }
    }

    private static JObject ToJsonRoot(IPackage package)
    {
        var data = new { Exports = package.GetExports() };
        return JObject.Parse(JsonConvert.SerializeObject(data));
    }
}
