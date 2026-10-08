using System.Text;
using System.Xml.Linq;

namespace P3RBmdTool;

// ── XML export / import for P3R BMD localization ──────────────────────────────
//
// Export format:
//   <BMD source="name.bmd" sourcePath="C:\..." dialogs="N">
//     <Speakers>
//       <Speaker id="0" rawBytes="E3 82 A4 ...">Igor</Speaker>
//     </Speakers>
//     <Dialog id="0" name="MSG_000_0_0" type="Message">
//       <Page spk="FFFF"><![CDATA[{f 0 5 254}{f 2 1}Text[n]More[f 1 1]]]></Page>
//     </Dialog>
//     <Dialog id="1" name="SEL_001" type="Selection">
//       <Option>{f 0 5 254}{f 2 1}Option A</Option>
//     </Dialog>
//   </BMD>
//
// Token notation inside CDATA / text:
//   {f tableIdx funcIdx arg1 arg2 ...}  function call (args are decoded int16 values)
//   [n]                                  newline (0x0A)
//   [cr]                                 carriage return (0x0D, rare)
//   {raw XX}                             raw single byte (fallback)
//   All other text is plain UTF-8.
//
// Import rebuilds each page from the notation.  Function call arguments are
// re-encoded using the standard formula (value+1 per byte).

public static class P3RXmlTool
{
    // ── Export ────────────────────────────────────────────────────────────────
    public static string Export(P3RBmdFile bmd, string sourcePath = "")
    {
        var root = new XElement("BMD");
        if (sourcePath.Length > 0)
        {
            root.Add(new XAttribute("source",     Path.GetFileName(sourcePath)));
            root.Add(new XAttribute("sourcePath", Path.GetFullPath(sourcePath)));
        }
        root.Add(new XAttribute("dialogs", bmd.Dialogs.Length));

        // Speakers
        if (bmd.Speakers.Names.Length > 0)
        {
            var spkElem = new XElement("Speakers");
            for (int i = 0; i < bmd.Speakers.Names.Length; i++)
            {
                var n = bmd.Speakers.Names[i];
                var e = new XElement("Speaker",
                    new XAttribute("id", i),
                    new XAttribute("rawBytes", BitConverter.ToString(n.RawBytes).Replace('-', ' ')),
                    n.Text);
                spkElem.Add(e);
            }
            root.Add(spkElem);
        }

        // Dialogs
        for (int di = 0; di < bmd.Dialogs.Length; di++)
        {
            var dialog = bmd.Dialogs[di];
            var dElem  = new XElement("Dialog",
                new XAttribute("id",   di),
                new XAttribute("name", dialog.Name),
                new XAttribute("type", dialog.Kind.ToString()));

            switch (dialog)
            {
                case P3RMessageDialog msg:
                    foreach (var page in msg.Pages)
                    {
                        var pElem = new XElement("Page");
                        pElem.Add(new XAttribute("spk", msg.SpeakerId.ToString("X4")));
                        pElem.Add(new XCData(TokensToText(page.Tokens)));
                        dElem.Add(pElem);
                    }
                    break;

                case P3RSelectionDialog sel:
                    for (int oi = 0; oi < sel.Options.Length; oi++)
                    {
                        var oElem = new XElement("Option");
                        oElem.Add(new XCData(TokensToText(sel.Options[oi].Tokens)));
                        dElem.Add(oElem);
                    }
                    break;
            }

            root.Add(dElem);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        using var ms = new MemoryStream();
        doc.Save(ms);
        var bytes = ms.ToArray();
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    public static bool HasChanges(P3RBmdFile original, string xmlText)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xmlText); }
        catch (Exception ex) { throw new InvalidDataException($"XML parse error: {ex.Message}"); }

        var root = doc.Root ?? throw new InvalidDataException("Missing XML root.");

        var speakerElem = root.Element("Speakers");
        if (speakerElem is not null)
        {
            foreach (var elem in speakerElem.Elements("Speaker"))
            {
                if (!int.TryParse((string?)elem.Attribute("id"), out int id))
                    return true;
                if ((uint)id >= (uint)original.Speakers.Names.Length)
                    return true;
                if (elem.Value != original.Speakers.Names[id].Text)
                    return true;
            }
        }

        foreach (var dElem in root.Elements("Dialog"))
        {
            if (!int.TryParse((string?)dElem.Attribute("id"), out int id))
                return true;
            if ((uint)id >= (uint)original.Dialogs.Length)
                return true;

            var orig = original.Dialogs[id];
            string xmlName = (string?)dElem.Attribute("name") ?? "";
            if (!xmlName.Equals(orig.Name, StringComparison.Ordinal))
                return true;

            switch (orig)
            {
                case P3RMessageDialog msg:
                {
                    var pages = dElem.Elements("Page").Select(e => e.Value).ToList();
                    if (pages.Count != msg.Pages.Length)
                        return true;
                    for (int i = 0; i < pages.Count; i++)
                    {
                        if (pages[i] != TokensToText(msg.Pages[i].Tokens))
                            return true;
                    }
                    break;
                }
                case P3RSelectionDialog sel:
                {
                    var options = dElem.Elements("Option").Select(e => e.Value).ToList();
                    if (options.Count != sel.Options.Length)
                        return true;
                    for (int i = 0; i < options.Count; i++)
                    {
                        if (options[i] != TokensToText(sel.Options[i].Tokens))
                            return true;
                    }
                    break;
                }
            }
        }

        return false;
    }
    // ── Token → text ─────────────────────────────────────────────────────────
    static string TokensToText(P3RToken[] tokens)
    {
        var sb = new StringBuilder();
        foreach (var tok in tokens)
        {
            switch (tok)
            {
                case P3RTextToken t:
                    sb.Append(t.Text.Replace("{", "{{").Replace("}", "}}").Replace("[", "[[").Replace("]", "]]"));
                    break;
                case P3RNewlineToken:
                    sb.Append("[n]");
                    break;
                case P3RCrToken:
                    sb.Append("[cr]");
                    break;
                case P3RFunctionToken f:
                    sb.Append("{f ").Append(f.TableIndex).Append(' ').Append(f.FuncIndex);
                    for (int a = 0; a < f.ArgCount; a++)
                        sb.Append(' ').Append(f.GetArgValue(a));
                    sb.Append('}');
                    break;
                case P3RRawByteToken r:
                    sb.Append($"{{raw {r.Value:X2}}}");
                    break;
            }
        }
        return sb.ToString();
    }

    // ── Import ────────────────────────────────────────────────────────────────
    // Returns the stored sourcePath from the XML (empty if absent).
    public static string GetSourcePath(string xml)
    {
        try { return (string?)XDocument.Parse(xml).Root?.Attribute("sourcePath") ?? ""; }
        catch { return ""; }
    }

    // Apply XML translations onto an existing P3RBmdFile.
    // Only dialog/page text is modified; all structural fields are preserved.
    public static P3RBmdFile Apply(P3RBmdFile original, string xmlText)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xmlText); }
        catch (Exception ex) { throw new InvalidDataException($"XML parse error: {ex.Message}"); }

        var speakers = new P3RSpeakerTable
        {
            Field08 = original.Speakers.Field08,
            Field0C = original.Speakers.Field0C,
            Names = original.Speakers.Names.ToArray(),
        };

        var speakerElem = doc.Root!.Element("Speakers");
        if (speakerElem is not null)
        {
            foreach (var elem in speakerElem.Elements("Speaker"))
            {
                if (!int.TryParse((string?)elem.Attribute("id"), out int id))
                    continue;
                if ((uint)id >= (uint)speakers.Names.Length)
                    continue;

                speakers.Names[id] = new P3RSpeakerName(Encoding.UTF8.GetBytes(elem.Value));
            }
        }

        // Build translation map: "dialogIdx/name" → list-of-page-texts
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var dElem in doc.Root.Elements("Dialog"))
        {
            string id   = (string?)dElem.Attribute("id")   ?? "";
            string name = (string?)dElem.Attribute("name") ?? "";
            string type = (string?)dElem.Attribute("type") ?? "Message";
            string key  = $"{id}/{name}";

            var pageTexts = type.Equals("Selection", StringComparison.OrdinalIgnoreCase)
                ? dElem.Elements("Option").Select(e => e.Value).ToList()
                : dElem.Elements("Page").Select(e => e.Value).ToList();

            if (pageTexts.Count > 0)
                map[key] = pageTexts;
        }

        // Clone the file, applying translations
        var result = new P3RBmdFile
        {
            IsCompressed = original.IsCompressed,
            UserId       = original.UserId,
            Field0C      = original.Field0C,
            Field1E      = original.Field1E,
            Speakers     = speakers,
        };

        var newDialogs = new P3RDialog[original.Dialogs.Length];
        for (int di = 0; di < original.Dialogs.Length; di++)
        {
            var orig = original.Dialogs[di];
            string key = $"{di}/{orig.Name}";

            if (!map.TryGetValue(key, out var pageTexts))
            {
                newDialogs[di] = orig;  // untranslated — keep original
                continue;
            }

            switch (orig)
            {
                case P3RMessageDialog msg:
                {
                    var newMsg = new P3RMessageDialog
                    {
                        Name      = msg.Name,
                        SpeakerId = msg.SpeakerId,
                    };
                    var pages = new P3RPage[msg.Pages.Length];
                    for (int pi = 0; pi < msg.Pages.Length; pi++)
                    {
                        var text = pi < pageTexts.Count ? pageTexts[pi] : "";
                        pages[pi] = new P3RPage { Tokens = ParseText(text) };
                    }
                    newMsg.Pages = pages;
                    newDialogs[di] = newMsg;
                    break;
                }
                case P3RSelectionDialog sel:
                {
                    var newSel = new P3RSelectionDialog
                    {
                        Name    = sel.Name,
                        Field18 = sel.Field18,
                        Field1C = sel.Field1C,
                        Field1E = sel.Field1E,
                    };
                    var opts = new P3RPage[sel.Options.Length];
                    for (int oi = 0; oi < sel.Options.Length; oi++)
                    {
                        var text = oi < pageTexts.Count ? pageTexts[oi] : "";
                        opts[oi] = new P3RPage { Tokens = ParseText(text) };
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

        result.Dialogs = newDialogs;
        return result;
    }

    // ── Text → tokens ─────────────────────────────────────────────────────────
    // Parses the notation used in the XML export back to tokens.
    //
    //   {f tableIdx funcIdx arg1 arg2 ...}  function call
    //   [n]                                  newline
    //   [cr]                                 carriage return
    //   {raw XX}                             raw byte
    //   {{  →  {   }}  →  }   [[  →  [   ]]  →  ]   (escapes for literal chars)
    //   anything else: plain UTF-8 text

    public static P3RToken[] ParseText(string text)
    {
        var tokens = new List<P3RToken>();
        var textBuf = new StringBuilder();
        int i = 0;

        void FlushText()
        {
            if (textBuf.Length > 0) { tokens.Add(new P3RTextToken(textBuf.ToString())); textBuf.Clear(); }
        }

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { textBuf.Append('{'); i += 2; continue; }
            if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { textBuf.Append('}'); i += 2; continue; }
            if (c == '[' && i + 1 < text.Length && text[i + 1] == '[') { textBuf.Append('['); i += 2; continue; }
            if (c == ']' && i + 1 < text.Length && text[i + 1] == ']') { textBuf.Append(']'); i += 2; continue; }

            if (c == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close < 0) { textBuf.Append(c); i++; continue; }

                string tag = text.Substring(i + 1, close - i - 1).Trim().ToLowerInvariant();
                FlushText();
                if (tag == "n")       tokens.Add(new P3RNewlineToken());
                else if (tag == "cr") tokens.Add(new P3RCrToken());
                else                  textBuf.Append('[' + tag + ']');  // unknown — keep as text
                i = close + 1;
                continue;
            }

            if (c == '{')
            {
                int close = text.IndexOf('}', i + 1);
                if (close < 0) { textBuf.Append(c); i++; continue; }

                string inner = text.Substring(i + 1, close - i - 1).Trim();
                FlushText();

                if (inner.StartsWith("f ", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(ParseFunctionToken(inner));
                }
                else if (inner.StartsWith("raw ", StringComparison.OrdinalIgnoreCase))
                {
                    if (byte.TryParse(inner.AsSpan(4).Trim(), System.Globalization.NumberStyles.HexNumber, null, out byte rb))
                        tokens.Add(new P3RRawByteToken(rb));
                }
                else
                {
                    // Unknown brace content — pass through as text
                    textBuf.Append('{').Append(inner).Append('}');
                }
                i = close + 1;
                continue;
            }

            textBuf.Append(c);
            i++;
        }

        FlushText();
        return tokens.ToArray();
    }

    // Parse "{f tableIdx funcIdx arg1 arg2 ...}" inner content (without the outer braces)
    static P3RFunctionToken ParseFunctionToken(string inner)
    {
        // inner = "f tableIdx funcIdx arg1 arg2 ..."
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // parts[0] = "f", parts[1] = tableIdx, parts[2] = funcIdx, parts[3+] = args

        int tableIdx = parts.Length > 1 ? ParseInt(parts[1]) : 0;
        int funcIdx  = parts.Length > 2 ? ParseInt(parts[2]) : 0;

        int argCount = Math.Max(0, parts.Length - 3);
        var argBytes = new byte[argCount * 2];
        for (int a = 0; a < argCount; a++)
        {
            short val = (short)ParseInt(parts[3 + a]);
            byte hi = (byte)((val >> 8) & 0xFF);
            byte lo = (byte)((val & 0xFF) + 1);
            argBytes[a * 2]     = lo;
            // Atlus convention: when the encoded low byte is 0xFF (arg low = 0xFE),
            // the high byte is also stored as 0xFF.  All other cases use standard (hi+1).
            argBytes[a * 2 + 1] = (lo == 0xFF && hi == 0) ? (byte)0xFF : (byte)(hi + 1);
        }

        byte signifier  = (byte)(0xF0 | ((argCount + 1) & 0x0F));
        byte functionId = (byte)(((tableIdx & 0x07) << 5) | (funcIdx & 0x1F));

        return new P3RFunctionToken(signifier, functionId, argBytes);
    }

    static int ParseInt(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(s, 16);
        if (short.TryParse(s, out var sv)) return sv;
        if (int.TryParse(s, out var iv)) return iv;
        return 0;
    }

    // ── Debug: dump parsed BMD structure to console ───────────────────────────
    public static void DumpInfo(P3RBmdFile bmd)
    {
        Console.WriteLine($"P3R BMD  dialogs={bmd.Dialogs.Length}  speakers={bmd.Speakers.Names.Length}");
        for (int i = 0; i < bmd.Speakers.Names.Length; i++)
            Console.WriteLine($"  Speaker[{i}]: {bmd.Speakers.Names[i].Text}");

        foreach (var (dialog, di) in bmd.Dialogs.Select((d, i) => (d, i)))
        {
            switch (dialog)
            {
                case P3RMessageDialog msg:
                    Console.WriteLine($"  Dialog[{di}] Message name=\"{msg.Name}\" spk=0x{msg.SpeakerId:X4} pages={msg.Pages.Length}");
                    foreach (var (page, pi) in msg.Pages.Select((p, i) => (p, i)))
                        Console.WriteLine($"    Page[{pi}]: {TokensToText(page.Tokens).Replace("\n", "\\n")}");
                    break;
                case P3RSelectionDialog sel:
                    Console.WriteLine($"  Dialog[{di}] Selection name=\"{sel.Name}\" opts={sel.Options.Length}");
                    foreach (var (opt, oi) in sel.Options.Select((o, i) => (o, i)))
                        Console.WriteLine($"    Option[{oi}]: {TokensToText(opt.Tokens).Replace("\n", "\\n")}");
                    break;
            }
        }
    }
}


