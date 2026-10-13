using CUE4Parse.Encryption.Aes;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace P3RPlgTool;

internal static class CuePackageReader
{
    private static bool _compressionReady;

    public static Package LoadLoosePackage(string uassetPath)
    {
        if (!File.Exists(uassetPath))
            throw new FileNotFoundException("Input uasset was not found.", uassetPath);

        var basePath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(uassetPath))!,
            Path.GetFileNameWithoutExtension(uassetPath));

        var versions = new VersionContainer(EGame.GAME_UE4_27, ETexturePlatform.DesktopMobile);
        var uasset = File.ReadAllBytes(basePath + ".uasset");
        var uexp = ReadOptional(basePath + ".uexp");
        var ubulk = ReadOptional(basePath + ".ubulk");
        var uptnl = ReadOptional(basePath + ".uptnl");

        var name = Path.GetFileNameWithoutExtension(uassetPath);
        var package = new Package(
            new FByteArchive($"{name}.uasset", uasset, versions),
            uexp != null ? new FByteArchive($"{name}.uexp", uexp, versions) : null,
            ubulk != null ? new FByteArchive($"{name}.ubulk", ubulk, versions) : null,
            uptnl != null ? new FByteArchive($"{name}.uptnl", uptnl, versions) : null,
            provider: null,
            useLazySerialization: false);

        return package;
    }

    public static IPackage LoadGamePackage(string gameDir, string assetPath, string aesKey)
    {
        InitializeCompression();

        var versions = new VersionContainer(EGame.GAME_UE4_27, ETexturePlatform.DesktopMobile);
        var provider = new DefaultFileProvider(gameDir, SearchOption.AllDirectories, versions, StringComparer.OrdinalIgnoreCase);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey(aesKey));
        provider.PostMount();

        var gameFile = ResolveGameFile(provider, assetPath);
        return provider.LoadPackage(gameFile);
    }

    public static void Audit(string uassetPath)
    {
        var package = LoadLoosePackage(uassetPath);
        AuditPackage(package);
    }

    public static void AuditGame(string gameDir, string assetPath, string aesKey)
    {
        var package = LoadGamePackage(gameDir, assetPath, aesKey);
        AuditPackage(package);
    }

    private static void AuditPackage(IPackage package)
    {
        Console.WriteLine($"Package: {package.Name}");
        Console.WriteLine($"Names:   {package.NameMap.Length}");
        Console.WriteLine($"Imports: {package.ImportMapLength}");
        Console.WriteLine($"Exports: {package.ExportMapLength}");
        Console.WriteLine($"Flags:   {package.Summary.PackageFlags}");
        Console.WriteLine();

        if (package is Package classicPackage)
            PrintClassicExportMap(classicPackage);

        Console.WriteLine();
        Console.WriteLine("Parsed exports:");
        var exports = package.GetExports().ToList();
        for (var i = 0; i < exports.Count; i++)
        {
            var export = exports[i];
            Console.WriteLine($"[{i}] {export.ExportType} {export.Name} props={export.Properties.Count}");
            foreach (var prop in export.Properties)
            {
                Console.WriteLine($"    {prop.Name.Text} : {prop.PropertyType.Text} = {Describe(prop.Tag?.GenericValue)}");
            }
        }
    }

    public static void ExportJson(string uassetPath, string outPath)
    {
        var package = LoadLoosePackage(uassetPath);
        ExportPackageJson(package, Path.GetFullPath(uassetPath), outPath);
    }

    public static void ExportGameJson(string gameDir, string assetPath, string aesKey, string outPath)
    {
        var package = LoadGamePackage(gameDir, assetPath, aesKey);
        ExportPackageJson(package, assetPath, outPath);
    }

    private static void ExportPackageJson(IPackage package, string source, string outPath)
    {
        var data = new
        {
            Source = source,
            Package = package.Name,
            NameCount = package.NameMap.Length,
            ImportCount = package.ImportMapLength,
            ExportCount = package.ExportMapLength,
            Exports = package.GetExports()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonConvert.SerializeObject(data, Formatting.Indented));
        Console.WriteLine($"Exported CUE4Parse JSON -> {outPath}");
    }

    private static byte[]? ReadOptional(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static void InitializeCompression()
    {
        if (_compressionReady) return;
        ZlibHelper.Initialize();
        _compressionReady = true;
    }

    private static GameFile ResolveGameFile(DefaultFileProvider provider, string assetPath)
    {
        foreach (var candidate in BuildAssetPathCandidates(assetPath))
        {
            if (provider.TryGetGameFile(candidate, out var file))
                return file;
        }

        throw new FileNotFoundException(
            "Asset was not found in provider. Try a path like /Game/Xrd777/UI/.../AssetName or P3R/Content/Xrd777/UI/.../AssetName.uasset",
            assetPath);
    }

    private static IEnumerable<string> BuildAssetPathCandidates(string assetPath)
    {
        var p = assetPath.Replace('\\', '/').Trim();
        if (p.StartsWith("../../../", StringComparison.Ordinal)) p = p[9..];
        if (p.StartsWith("/")) p = p[1..];
        if (p.StartsWith("Game/", StringComparison.OrdinalIgnoreCase))
            p = "P3R/Content/" + p[5..];

        yield return p;
        if (!p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            yield return p + ".uasset";
    }

    private static void PrintClassicExportMap(Package package)
    {
        for (var i = 0; i < package.ExportMap.Length; i++)
        {
            var export = package.ExportMap[i];
            Console.WriteLine(
                $"[{i}] {export.ObjectName.Text} : {export.ClassName}  " +
                $"serial=0x{export.SerialOffset:X}+0x{export.SerialSize:X}");
        }
    }

    private static string Describe(object? value)
    {
        if (value is null) return "<null>";
        if (value is Array array) return $"{value.GetType().GetElementType()?.Name ?? "object"}[{array.Length}]";
        if (value is System.Collections.ICollection collection) return $"{value.GetType().Name}[{collection.Count}]";
        return value.ToString() ?? value.GetType().Name;
    }
}
