using P3RPlgTool;

try
{
    if (args.Length == 0)
    {
        PrintUsage();
        return;
    }

    switch (args[0].ToLowerInvariant())
    {
        case "audit" when args.Length >= 2:
            CuePackageReader.Audit(args[1]);
            return;

        case "json" when args.Length >= 3:
            CuePackageReader.ExportJson(args[1], args[2]);
            return;

        case "game-audit" when args.Length >= 4:
            CuePackageReader.AuditGame(args[1], args[2], args[3]);
            return;

        case "game-json" when args.Length >= 5:
            CuePackageReader.ExportGameJson(args[1], args[2], args[3], args[4]);
            return;

        case "game-list" when args.Length >= 4:
            PlgCueInspector.ListGameEntries(args[1], args[2], args[3]);
            return;

        case "game-svg" when args.Length >= 6:
            PlgCueEditor.ExportGameSvg(args[1], args[2], args[3], args[4], args[5]);
            return;

        case "cue-svg" when args.Length >= 4:
            PlgCueEditor.ExportJsonSvg(args[1], args[2], args[3]);
            return;

        case "cue-patch" when args.Length >= 6:
        {
            var color = args.Length >= 7 && args[6].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(args[6], 16)
                : 0xFFFFFFFF;
            PlgCueEditor.PatchFromSvg(args[1], args[2], args[3], args[4], args[5], color);
            return;
        }
        case "cue-patch-inplace-svg" when args.Length >= 6:
        {
            var color = args.Length >= 7 && args[6].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(args[6], 16)
                : 0xFFFFFFFF;
            PlgCueEditor.PatchInPlaceFromSvg(args[1], args[2], args[3], args[4], args[5], color);
            return;
        }
        case "cue-patch-template-svg" when args.Length >= 6:
        {
            var color = args.Length >= 7 && args[6].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(args[6], 16)
                : 0xFFFFFFFF;
            PlgCueEditor.PatchInPlaceFromTemplateSvg(args[1], args[2], args[3], args[4], args[5], color);
            return;
        }
        case "cue-patch-template-svg-extrude" when args.Length >= 6:
            PlgCueEditor.PatchInPlaceFromTemplateSvgExtrude(args[1], args[2], args[3], args[4], args[5]);
            return;        case "cue-patch-template-svg-mask" when args.Length >= 6:
        {
            var color = args.Length >= 7 && args[6].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(args[6], 16)
                : 0xFFFFFFFF;
            PlgCueEditor.PatchInPlaceFromTemplateSvgMask(args[1], args[2], args[3], args[4], args[5], color);
            return;
        }
        case "cue-patch-png-grid" when args.Length >= 6:
        {
            var color = args.Length >= 7 && args[6].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(args[6], 16)
                : 0xFFFFFFFF;
            PlgCueEditor.PatchInPlaceFromPngGrid(args[1], args[2], args[3], args[4], args[5], color);
            return;
        }

        case "cue-test-inplace" when args.Length >= 5:
            PlgCueEditor.PatchTestInPlace(args[1], args[2], args[3], args[4]);
            return;

        case "legacy-audit" when args.Length >= 2:
            PlgDiag.Audit(args[1]);
            return;

        case "legacy-diag" when args.Length >= 2:
            PlgDiag.Dump(args[1]);
            return;

        case "legacy-export-json" when args.Length >= 3:
            PlgJson.Export(args[1], args[2], args.Contains("--debug"));
            return;

        case "legacy-raw-svg" when args.Length >= 2:
        {
            var outDir = args.Length >= 3
                ? args[2]
                : Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(args[1]))!,
                    Path.GetFileNameWithoutExtension(args[1]) + "_clusters");
            PlgSvg.ExportAllRaw(args[1], outDir);
            return;
        }

        case "legacy-patch-cluster" when args.Length >= 5:
        {
            var clusterStart = Convert.ToInt32(
                args[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[2][2..] : args[2],
                16);
            var color = args.Length >= 6 && args[5].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(args[5], 16)
                : 0xFFFFFFFF;

            var (verts, indices, colors, minX, minY, maxX, maxY) = SvgImport.Import(args[3], color);
            Console.WriteLine($"SVG imported: {verts.Count} verts, {indices.Count / 3} tris");
            PlgPacker.PatchCluster(args[1], args[4], clusterStart, verts, indices, colors, minX, minY, maxX, maxY);
            return;
        }

        default:
            Console.Error.WriteLine($"Unknown or incomplete command: {args[0]}");
            PrintUsage();
            Environment.ExitCode = 2;
            return;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERROR: " + ex.Message);
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

static void PrintUsage()
{
    Console.WriteLine("P3RPlgTool");
    Console.WriteLine();
    Console.WriteLine("CUE4Parse-first commands:");
    Console.WriteLine("  audit <file.uasset>");
    Console.WriteLine("      Parse package with CUE4Parse and print exports/properties.");
    Console.WriteLine();
    Console.WriteLine("  json <file.uasset> <out.json>");
    Console.WriteLine("      Export CUE4Parse UObject JSON, similar to FModel.");
    Console.WriteLine();
    Console.WriteLine("  game-audit <gameDir> <assetPath> <aesKey>");
    Console.WriteLine("      Parse an IoStore asset through the full game provider.");
    Console.WriteLine();
    Console.WriteLine("  game-json <gameDir> <assetPath> <aesKey> <out.json>");
    Console.WriteLine("      Export an IoStore asset through the full game provider.");
    Console.WriteLine();
    Console.WriteLine("  game-list <gameDir> <assetPath> <aesKey>");
    Console.WriteLine("      Print PlgData entry names and mesh sizes.");
    Console.WriteLine();
    Console.WriteLine("  game-svg <gameDir> <assetPath> <aesKey> <entryNameOrIndex> <out.svg>");
    Console.WriteLine("      Export one PlgData entry from the game provider to SVG.");
    Console.WriteLine();
    Console.WriteLine("  cue-svg <cue.json> <entryNameOrIndex> <out.svg>");
    Console.WriteLine("      Export one PlgData entry from an already exported CUE JSON to SVG.");
    Console.WriteLine();
    Console.WriteLine("  cue-patch <raw.uasset> <cue.json> <entryNameOrIndex> <edited.svg> <out.uasset> [0xAARRGGBB]");
    Console.WriteLine("      Replace one PlgData entry in a raw IoStore uasset using edited SVG geometry.");    Console.WriteLine();
    Console.WriteLine("  cue-patch-inplace-svg <raw.uasset> <cue.json> <entryNameOrIndex> <edited.svg> <out.uasset> [0xAARRGGBB]");
    Console.WriteLine("      Safe SVG patch: keeps original vertex/index/color byte counts and file size.");
    Console.WriteLine();
    Console.WriteLine("  cue-test-inplace <raw.uasset> <cue.json> <entryNameOrIndex> <out.uasset>");
    Console.WriteLine("      Safe test patch: no file-size change, scales and recolors one existing entry.");
    Console.WriteLine();
    Console.WriteLine("Legacy binary helpers:");
    Console.WriteLine("  legacy-audit <file.uasset>");
    Console.WriteLine("  legacy-diag <file.uasset>");
    Console.WriteLine("  legacy-export-json <file.uasset> <out.json> [--debug]");
    Console.WriteLine("  legacy-raw-svg <file.uasset> [outDir]");
    Console.WriteLine("  legacy-patch-cluster <orig.uasset> <hexStart> <svg> <out.uasset> [0xAARRGGBB]");
}







