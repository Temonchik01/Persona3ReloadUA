using System.Xml.Linq;
using System.Xml.XPath;

namespace P3RDtTool;

public static class P3RDtBatch
{
    public static void ExportXml(string sourceRoot, string xmlRoot)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        xmlRoot = Path.GetFullPath(xmlRoot);

        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException(sourceRoot);

        Directory.CreateDirectory(xmlRoot);

        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.uasset", SearchOption.AllDirectories).OrderBy(p => p).ToList();
        Console.WriteLine($"Scanning {sourceFiles.Count} uassets -> XML ...");
        Console.WriteLine();

        int files = 0;
        int fstrings = 0;
        int text = 0;

        foreach (string sourcePath in sourceFiles)
        {
            files++;
            string rel = Path.GetRelativePath(sourceRoot, sourcePath);
            string xmlRel = Path.ChangeExtension(rel, ".xml");

            string fstringXml = Path.Combine(xmlRoot, "fstrings", xmlRel);
            RunQuietly(() => P3RDtFStringScanner.ExportXml(sourcePath, fstringXml));
            bool hasFstrings = CountNodes(fstringXml, "//String[@role='value']") > 0;
            bool looseFstrings = hasFstrings && IsLooseFStringXml(fstringXml);

            string textXml = Path.Combine(xmlRoot, "text", xmlRel);
            RunQuietly(() => P3RDtTextScanner.ExportXml(sourcePath, textXml));
            bool hasTextBlocks = CountNodes(textXml, "//Text") > 0;
            if (hasTextBlocks)
            {
                text++;
                Console.WriteLine($"[text]     {rel}");
            }
            else
            {
                File.Delete(textXml);
                DeleteEmptyParents(Path.GetDirectoryName(textXml)!, Path.Combine(xmlRoot, "text"));
            }

            if (hasFstrings && !(hasTextBlocks && (looseFstrings || CountNodes(textXml, "//Text[@role='value']") > 0)))
            {
                fstrings++;
                Console.WriteLine($"[fstrings] {rel}");
            }
            else
            {
                File.Delete(fstringXml);
                DeleteEmptyParents(Path.GetDirectoryName(fstringXml)!, Path.Combine(xmlRoot, "fstrings"));
            }

            if (files % 200 == 0)
                Console.WriteLine($"  ... {files}/{sourceFiles.Count}");
        }

        Console.WriteLine();
        Console.WriteLine($"Done. Scanned {files} uassets. Exported {fstrings} FString XML and {text} text-block XML files.");
    }

    public static void ImportXml(string xmlRoot, string sourceRoot, string outputRoot, bool force = false, bool verbose = false)
    {
        xmlRoot = Path.GetFullPath(xmlRoot);
        sourceRoot = Path.GetFullPath(sourceRoot);
        outputRoot = Path.GetFullPath(outputRoot);

        if (!Directory.Exists(xmlRoot))
            throw new DirectoryNotFoundException(xmlRoot);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException(sourceRoot);

        // Report all missing templates before writing any replacement resources.
        var missing = new List<string>();
        foreach (string mode in new[] { "fstrings", "text" })
        {
            string directory = Path.Combine(xmlRoot, mode);
            if (!Directory.Exists(directory)) continue;
            foreach (string xml in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories))
            {
                string relative = Path.ChangeExtension(Path.GetRelativePath(directory, xml), ".uasset");
                if (!File.Exists(Path.Combine(sourceRoot, relative))) missing.Add(relative);
            }
        }
        if (missing.Count > 0)
            throw new FileNotFoundException("Missing DT source templates (no resources imported):\n" +
                string.Join("\n", missing.Distinct().OrderBy(path => path)) +
                "\nPlace the original FModel uassets under: " + sourceRoot);

        // Validate sources, offsets and supported text edits before writing any
        // output, including --force runs. Force means "include unchanged", not
        // "bypass validation".
        foreach (string mode in new[] { "fstrings", "text" })
        {
            string directory = Path.Combine(xmlRoot, mode);
            if (!Directory.Exists(directory)) continue;
            foreach (string xml in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories))
            {
                string source = Path.Combine(sourceRoot,
                    Path.ChangeExtension(Path.GetRelativePath(directory, xml), ".uasset"));
                try
                {
                    if (mode == "text") P3RDtTextScanner.CountChanges(source, xml);
                    else P3RDtFStringScanner.CountChanges(source, xml);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Invalid DT XML (no resources imported): {xml}\n{ex.Message}", ex);
                }
            }
        }

        var fstringResult = ImportMode(
            Path.Combine(xmlRoot, "fstrings"),
            sourceRoot,
            outputRoot,
            P3RDtFStringScanner.ImportXml,
            P3RDtFStringScanner.CountChanges,
            force,
            verbose);

        var textResult = ImportMode(
            Path.Combine(xmlRoot, "text"),
            sourceRoot,
            outputRoot,
            P3RDtTextScanner.ImportXml,
            P3RDtTextScanner.CountChanges,
            force,
            verbose);

        Console.WriteLine();
        int imported = fstringResult.imported + textResult.imported;
        int removedStale = fstringResult.removedStale + textResult.removedStale;
        Console.WriteLine($"Done. Imported {fstringResult.imported} FString XML and {textResult.imported} text-block XML files ({(force ? "force" : "changed-only")}) | removed stale: {removedStale}.");
    }

    private static (int imported, int removedStale) ImportMode(
        string modeXmlRoot,
        string sourceRoot,
        string outputRoot,
        Action<string, string, string> import,
        Func<string, string, int> countChanges,
        bool force,
        bool verbose)
    {
        if (!Directory.Exists(modeXmlRoot))
            return (0, 0);

        int count = 0;
        int removedStale = 0;
        foreach (string xmlPath in Directory.EnumerateFiles(modeXmlRoot, "*.xml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(modeXmlRoot, xmlPath);
            string assetRel = Path.ChangeExtension(rel, ".uasset");
            string sourcePath = Path.Combine(sourceRoot, assetRel);
            string outputPath = Path.Combine(outputRoot, assetRel);

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Missing DT source for XML: {xmlPath}", sourcePath);

            int changes = force ? -1 : countChanges(sourcePath, xmlPath);
            if (!force && changes == 0)
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                    removedStale++;
                }
                continue;
            }

            if (verbose)
                import(sourcePath, xmlPath, outputPath);
            else
                RunQuietly(() => import(sourcePath, xmlPath, outputPath));

            count++;
        }

        return (count, removedStale);
    }

    private static int CountNodes(string xmlPath, string xpath)
    {
        var doc = XDocument.Load(xmlPath);
        return doc.XPathSelectElements(xpath).Count();
    }

    private static bool IsLooseFStringXml(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        return string.Equals((string?)doc.Root?.Attribute("layout"), "loose-fstrings", StringComparison.Ordinal);
    }

    private static void RunQuietly(Action action)
    {
        TextWriter oldOut = Console.Out;
        try
        {
            Console.SetOut(TextWriter.Null);
            action();
        }
        finally
        {
            Console.SetOut(oldOut);
        }
    }

    private static void DeleteEmptyParents(string path, string stopAt)
    {
        path = Path.GetFullPath(path);
        stopAt = Path.GetFullPath(stopAt);

        while (path.StartsWith(stopAt, StringComparison.OrdinalIgnoreCase) &&
               !path.Equals(stopAt, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(path) &&
               !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
            path = Path.GetDirectoryName(path)!;
        }
    }
}






