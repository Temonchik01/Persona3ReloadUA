using System.Text;
using System.Xml.Linq;

namespace P3RBmdTool;

// ── XLIFF 1.2 export / import for Crowdin workflow ────────────────────────────
//
// Each BMD file → one <file> element.
// Speaker names → <trans-unit id="speaker.N">
// Message pages  → <trans-unit id="{di}.{name}.p{pi}">
// Selection opts → <trans-unit id="{di}.{name}.o{oi}">
//
// Function calls, newlines, CR → locked <ph> pills:
//   <ph id="N" ctype="x-p3r" disp="{f 0 5 254}" x-raw="FEF205FFFF"/>
//   x-raw stores the EXACT bytes to write back — no re-encoding ever.
//
// Import: text nodes → UTF-8, <ph> x-raw → ParseTokens → same writer.
// If <target> is empty the source content is used (untranslated fall-through).

public static class P3RXliffTool
{
    static readonly XNamespace Ns = "urn:oasis:names:tc:xliff:document:1.2";

    // ── Export ────────────────────────────────────────────────────────────────
    public static string Export(P3RBmdFile bmd,
                                 string sourcePath   = "",
                                 string sourceLang   = "en",
                                 string targetLang   = "uk")
    {
        var fileElem = new XElement(Ns + "file",
            new XAttribute("original",        Path.GetFileName(sourcePath)),
            new XAttribute("source-language", sourceLang),
            new XAttribute("target-language", targetLang),
            new XAttribute("datatype",        "x-p3r-bmd"));

        // Header — stores the source path so import can find the original BMD
        var hdr = new XElement(Ns + "header");
        if (sourcePath.Length > 0)
        {
            var pg = new XElement(Ns + "prop-group", new XAttribute("name", "x-p3r"));
            pg.Add(new XElement(Ns + "prop", new XAttribute("name", "source-path"), Path.GetFullPath(sourcePath)));
            pg.Add(new XElement(Ns + "prop", new XAttribute("name", "dialogs"), bmd.Dialogs.Length));
            hdr.Add(pg);
        }
        fileElem.Add(hdr);

        var body = new XElement(Ns + "body");

        // Speaker names
        for (int i = 0; i < bmd.Speakers.Names.Length; i++)
        {
            body.Add(BuildSpeakerUnit(i, bmd.Speakers.Names[i].Text));
        }

        // Dialogs
        for (int di = 0; di < bmd.Dialogs.Length; di++)
        {
            switch (bmd.Dialogs[di])
            {
                case P3RMessageDialog msg:
                    for (int pi = 0; pi < msg.Pages.Length; pi++)
                    {
                        string id   = $"{di}.{msg.Name}.p{pi}";
                        string note = $"Speaker: {SpeakerLabel(bmd, msg.SpeakerId)} | {msg.Name} | Page {pi + 1}/{msg.Pages.Length}";
                        body.Add(BuildPageUnit(id, msg.Pages[pi].Tokens, note));
                    }
                    break;

                case P3RSelectionDialog sel:
                    for (int oi = 0; oi < sel.Options.Length; oi++)
                    {
                        string id   = $"{di}.{sel.Name}.o{oi}";
                        string note = $"Selection option {oi + 1}/{sel.Options.Length} | {sel.Name}";
                        body.Add(BuildPageUnit(id, sel.Options[oi].Tokens, note));
                    }
                    break;
            }
        }

        fileElem.Add(body);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "xliff",
                new XAttribute("version", "1.2"),
                fileElem));

        using var ms = new MemoryStream();
        doc.Save(ms, SaveOptions.DisableFormatting);
        var bytes = ms.ToArray();
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    // ── Build trans-unit elements ─────────────────────────────────────────────
    static XElement BuildSpeakerUnit(int index, string name)
    {
        var tu = new XElement(Ns + "trans-unit",
            new XAttribute("id",      $"speaker.{index}"),
            new XAttribute("resname", $"speaker.{index}"));
        tu.Add(new XElement(Ns + "source", name));
        tu.Add(EmptyTarget());
        tu.Add(new XElement(Ns + "note", "Speaker name"));
        return tu;
    }

    static XElement BuildPageUnit(string id, P3RToken[] tokens, string note)
    {
        var tu = new XElement(Ns + "trans-unit",
            new XAttribute("id",      id),
            new XAttribute("resname", id));

        tu.Add(TokensToSourceElem(tokens));
        tu.Add(EmptyTarget());
        tu.Add(new XElement(Ns + "note", note));
        return tu;
    }

    static XElement TokensToSourceElem(P3RToken[] tokens)
    {
        var src = new XElement(Ns + "source");
        src.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
        FillMixedContent(src, tokens);
        return src;
    }

    static XElement EmptyTarget()
    {
        var tgt = new XElement(Ns + "target");
        tgt.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
        return tgt;
    }

    // ── Token → mixed XML content ─────────────────────────────────────────────
    static void FillMixedContent(XElement parent, P3RToken[] tokens)
    {
        int phId = 1;
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length > 0) { parent.Add(new XText(text.ToString())); text.Clear(); }
        }

        foreach (var tok in tokens)
        {
            switch (tok)
            {
                case P3RTextToken t:
                    text.Append(t.Text);
                    break;

                case P3RNewlineToken:
                    FlushText();
                    parent.Add(MakePh(phId++, "[n]", "0A"));
                    break;

                case P3RCrToken:
                    FlushText();
                    parent.Add(MakePh(phId++, "[cr]", "0D"));
                    break;

                case P3RFunctionToken f:
                {
                    FlushText();
                    var raw = new List<byte> { 0xFE, f.Signifier, f.FunctionId };
                    raw.AddRange(f.RawArgBytes);
                    string rawHex = BytesToHex(raw.ToArray());
                    string disp   = FuncDisp(f);
                    parent.Add(MakePh(phId++, disp, rawHex));
                    break;
                }

                case P3RRawByteToken r:
                    FlushText();
                    parent.Add(MakePh(phId++, $"[raw:{r.Value:X2}]", r.Value.ToString("X2")));
                    break;
            }
        }

        FlushText();
    }

    static XElement MakePh(int id, string disp, string rawHex) =>
        new XElement(Ns + "ph",
            new XAttribute("id",     id.ToString()),
            new XAttribute("ctype",  "x-p3r"),
            new XAttribute("disp",   disp),
            new XAttribute("x-raw",  rawHex));

    static string FuncDisp(P3RFunctionToken f)
    {
        var sb = new StringBuilder("{f ").Append(f.TableIndex).Append(' ').Append(f.FuncIndex);
        for (int a = 0; a < f.ArgCount; a++)
            sb.Append(' ').Append(f.GetArgValue(a));
        return sb.Append('}').ToString();
    }

    // ── Import ────────────────────────────────────────────────────────────────
    public static bool HasTranslations(string xliffText)
    {
        // True if any <target> element contains real content (text node or <ph>).
        try
        {
            var doc = XDocument.Parse(xliffText);
            return doc.Descendants(Ns + "trans-unit").Any(tu =>
            {
                var tgt = tu.Element(Ns + "target");
                if (tgt == null) return false;
                return tgt.Nodes().Any(n =>
                    n is XElement ||
                    (n is XText t && t.Value.Length > 0));
            });
        }
        catch { return false; }
    }

    public static string GetSourcePath(string xliffText)
    {
        try
        {
            return XDocument.Parse(xliffText)
                .Descendants(Ns + "prop")
                .FirstOrDefault(p => (string?)p.Attribute("name") == "source-path")
                ?.Value ?? "";
        }
        catch { return ""; }
    }

    public static P3RBmdFile Apply(P3RBmdFile original, string xliffText)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xliffText); }
        catch (Exception ex) { throw new InvalidDataException($"XLIFF parse error: {ex.Message}"); }

        // Build id → effective nodes (target if non-empty, else source)
        var units = new Dictionary<string, List<XNode>>(StringComparer.Ordinal);
        foreach (var tu in doc.Descendants(Ns + "trans-unit"))
        {
            string id  = (string?)tu.Attribute("id") ?? "";
            var src    = tu.Element(Ns + "source");
            var tgt    = tu.Element(Ns + "target");
            var nodes  = EffectiveNodes(tgt, src);
            if (id.Length > 0) units[id] = nodes;
        }

        // Speaker names
        var newSpeakerNames = original.Speakers.Names.ToArray();
        for (int i = 0; i < newSpeakerNames.Length; i++)
        {
            if (units.TryGetValue($"speaker.{i}", out var nodes))
            {
                string text = string.Concat(nodes.OfType<XText>().Select(t => t.Value));
                if (text.Length > 0)
                    newSpeakerNames[i] = new P3RSpeakerName(Encoding.UTF8.GetBytes(text));
            }
        }

        // Dialogs
        var newDialogs = new P3RDialog[original.Dialogs.Length];
        for (int di = 0; di < original.Dialogs.Length; di++)
        {
            var orig = original.Dialogs[di];
            switch (orig)
            {
                case P3RMessageDialog msg:
                {
                    var newMsg = new P3RMessageDialog { Name = msg.Name, SpeakerId = msg.SpeakerId };
                    var pages  = new P3RPage[msg.Pages.Length];
                    for (int pi = 0; pi < msg.Pages.Length; pi++)
                    {
                        string id = $"{di}.{msg.Name}.p{pi}";
                        pages[pi] = units.TryGetValue(id, out var nodes)
                            ? new P3RPage { Tokens = NodesToTokens(nodes) }
                            : msg.Pages[pi];
                    }
                    newMsg.Pages   = pages;
                    newDialogs[di] = newMsg;
                    break;
                }
                case P3RSelectionDialog sel:
                {
                    var newSel = new P3RSelectionDialog
                    {
                        Name = sel.Name, Field18 = sel.Field18,
                        Field1C = sel.Field1C, Field1E = sel.Field1E,
                    };
                    var opts = new P3RPage[sel.Options.Length];
                    for (int oi = 0; oi < sel.Options.Length; oi++)
                    {
                        string id = $"{di}.{sel.Name}.o{oi}";
                        opts[oi] = units.TryGetValue(id, out var nodes)
                            ? new P3RPage { Tokens = NodesToTokens(nodes) }
                            : sel.Options[oi];
                    }
                    newSel.Options = opts;
                    newDialogs[di] = newSel;
                    break;
                }
                default:
                    newDialogs[di] = orig;
                    break;
            }
        }

        return new P3RBmdFile
        {
            IsCompressed = original.IsCompressed,
            UserId       = original.UserId,
            Field0C      = original.Field0C,
            Field1E      = original.Field1E,
            Speakers     = new P3RSpeakerTable
            {
                Field08 = original.Speakers.Field08,
                Field0C = original.Speakers.Field0C,
                Names   = newSpeakerNames,
            },
            Dialogs = newDialogs,
        };
    }

    // ── Mixed XML content → tokens ────────────────────────────────────────────
    static P3RToken[] NodesToTokens(List<XNode> nodes)
    {
        var tokens  = new List<P3RToken>();
        var textBuf = new StringBuilder();

        void FlushText()
        {
            if (textBuf.Length > 0)
            {
                tokens.Add(new P3RTextToken(textBuf.ToString()));
                textBuf.Clear();
            }
        }

        foreach (var node in nodes)
        {
            if (node is XText xt)
            {
                // Allow translators to write [n] and [cr] inline in text instead of <ph> elements.
                var parts = System.Text.RegularExpressions.Regex.Split(xt.Value, @"(\[n\]|\[cr\])");
                foreach (var part in parts)
                {
                    if (part == "[n]")      { FlushText(); tokens.Add(new P3RNewlineToken()); }
                    else if (part == "[cr]") { FlushText(); tokens.Add(new P3RCrToken()); }
                    else                     textBuf.Append(part);
                }
            }
            else if (node is XElement elem && elem.Name.LocalName == "ph")
            {
                FlushText();
                string rawHex = (string?)elem.Attribute("x-raw") ?? "";
                if (rawHex.Length > 0)
                {
                    var raw  = HexToBytes(rawHex);
                    // Feed raw bytes back through the same reader ParseTokens to
                    // reconstruct the correct token type (function, newline, etc.)
                    var parsed = P3RBmdReader.ParseTokens(raw);
                    tokens.AddRange(parsed);
                }
            }
        }

        FlushText();
        return tokens.ToArray();
    }

    // Picks target nodes if non-empty, falls back to source.
    static List<XNode> EffectiveNodes(XElement? target, XElement? source)
    {
        if (target != null)
        {
            var nodes = target.Nodes().ToList();
            bool hasContent = nodes.Any(n =>
                n is XElement ||
                (n is XText t && t.Value.Length > 0));
            if (hasContent) return nodes;
        }
        return source?.Nodes().ToList() ?? new List<XNode>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static string SpeakerLabel(P3RBmdFile bmd, ushort speakerId)
    {
        if (speakerId == 0xFFFF) return "(none)";
        if ((speakerId & 0x8000) != 0) return $"(variable {speakerId & 0x7FFF})";
        int idx = speakerId & 0x7FFF;
        if (idx < bmd.Speakers.Names.Length)
            return bmd.Speakers.Names[idx].Text;
        return $"#{speakerId}";
    }

    static string BytesToHex(byte[] bytes) =>
        BitConverter.ToString(bytes).Replace("-", "");

    static byte[] HexToBytes(string hex)
    {
        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return result;
    }
}
