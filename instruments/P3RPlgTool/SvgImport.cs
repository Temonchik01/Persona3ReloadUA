using System.Globalization;
using System.Xml.Linq;

namespace P3RPlgTool;

/// <summary>
/// Imports an SVG file and triangulates all paths into a PLG-compatible mesh.
/// </summary>
public static class SvgImport
{
    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public static (
        List<(float X, float Y, float Z)> Vertices,
        List<ushort> Indices,
        List<uint> Colors,
        float MinX, float MinY, float MaxX, float MaxY)
    Import(string svgPath, uint defaultColor = 0xFFFFFFFF)
    {
        var doc = XDocument.Load(svgPath);
        XNamespace svg = "http://www.w3.org/2000/svg";

        // Collect all <path> elements anywhere in the document
        var paths = doc.Descendants(svg + "path")
                       .Concat(doc.Descendants("path"))   // no-namespace fallback
                       .ToList();

        // Each SVG path may produce multiple sub-contours (M...Z segments)
        var allContours = new List<List<(float X, float Y)>>();
        foreach (var pathEl in paths)
        {
            string? d = pathEl.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d)) continue;
            allContours.AddRange(ParseSvgPath(d));
        }

        if (allContours.Count == 0)
            return (new(), new(), new(), 0, 0, 0, 0);

        // Separate outer rings (CCW winding in SVG Y-down space → positive area) from holes (CW)
        // SVG uses Y-down: positive signed area = CCW winding = outer shape
        var outers = new List<List<(float X, float Y)>>();
        var holes  = new List<List<(float X, float Y)>>();
        foreach (var c in allContours)
        {
            if (c.Count < 3) continue;
            double area = SignedArea(c);
            if (area >= 0)
                outers.Add(c);
            else
                holes.Add(c);
        }

        // If no outers found, treat everything as outers
        if (outers.Count == 0)
        {
            outers.AddRange(holes);
            holes.Clear();
        }

        // Assign each hole to its containing outer
        var groups = new List<(List<(float X, float Y)> Outer, List<List<(float X, float Y)>> Holes)>();
        foreach (var outer in outers)
            groups.Add((outer, new List<List<(float X, float Y)>>()));

        foreach (var hole in holes)
        {
            // Pick the smallest outer that contains the hole's first point
            List<(float X, float Y)>? bestOuter = null;
            double bestArea = double.MaxValue;
            foreach (var (outer, _) in groups)
            {
                if (PointInPolygon(hole[0], outer))
                {
                    double a = Math.Abs(SignedArea(outer));
                    if (a < bestArea) { bestArea = a; bestOuter = outer; }
                }
            }
            if (bestOuter != null)
                groups.First(g => g.Outer == bestOuter).Holes.Add(hole);
            else
                groups[0].Holes.Add(hole);  // fallback: attach to first outer
        }

        // Triangulate each group
        var finalVerts   = new List<(float X, float Y, float Z)>();
        var finalIndices = new List<ushort>();

        foreach (var (outer, groupHoles) in groups)
        {
            // Merge holes into outer polygon via bridge edges
            var poly = MergeHoles(outer, groupHoles);

            int baseIndex = finalVerts.Count;
            foreach (var (x, y) in poly)
                finalVerts.Add((x, y, 0f));

            var tris = EarClip(poly);
            foreach (int idx in tris)
                finalIndices.Add((ushort)(baseIndex + idx));
        }

        // Colors: one per vertex
        var finalColors = Enumerable.Repeat(defaultColor, finalVerts.Count).ToList();

        // Bounds
        float minX = finalVerts.Min(v => v.X);
        float minY = finalVerts.Min(v => v.Y);
        float maxX = finalVerts.Max(v => v.X);
        float maxY = finalVerts.Max(v => v.Y);

        return (finalVerts, finalIndices, finalColors, minX, minY, maxX, maxY);
    }

    // -----------------------------------------------------------------------
    // SVG path parser
    // -----------------------------------------------------------------------


    public static (
        List<(float X, float Y, float Z)> Vertices,
        List<ushort> Indices,
        List<uint> Colors,
        float MinX, float MinY, float MaxX, float MaxY)
    ImportPunchout(string svgPath, float width, float height, uint defaultColor = 0xFFFFFFFF)
    {
        var doc = XDocument.Load(svgPath);
        XNamespace svg = "http://www.w3.org/2000/svg";

        var paths = doc.Descendants(svg + "path")
                       .Concat(doc.Descendants("path"))
                       .ToList();

        var contours = new List<List<(float X, float Y)>>();
        foreach (var pathEl in paths)
        {
            var d = pathEl.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d))
                continue;
            contours.AddRange(ParseSvgPath(d).Where(c => c.Count >= 3));
        }

        if (contours.Count == 0)
            return (new(), new(), new(), 0, 0, 0, 0);

        var depths = new int[contours.Count];
        for (var i = 0; i < contours.Count; i++)
        {
            var p = contours[i][0];
            var depth = 0;
            for (var j = 0; j < contours.Count; j++)
            {
                if (i == j)
                    continue;
                if (PointInPolygon(p, contours[j]))
                    depth++;
            }
            depths[i] = depth;
        }

        List<(float X, float Y)> EnsureCcw(List<(float X, float Y)> c)
        {
            var copy = c.ToList();
            if (SignedArea(copy) < 0)
                copy.Reverse();
            return copy;
        }

        List<(float X, float Y)> EnsureCw(List<(float X, float Y)> c)
        {
            var copy = c.ToList();
            if (SignedArea(copy) > 0)
                copy.Reverse();
            return copy;
        }

        var rect = new List<(float X, float Y)>
        {
            (0f, 0f),
            (width, 0f),
            (width, height),
            (0f, height)
        };

        var groups = new List<(List<(float X, float Y)> Outer, List<List<(float X, float Y)>> Holes)>
        {
            (rect, new List<List<(float X, float Y)>>())
        };

        for (var i = 0; i < contours.Count; i++)
        {
            if ((depths[i] % 2) == 0)
                groups[0].Holes.Add(EnsureCw(contours[i]));
            else
                groups.Add((EnsureCcw(contours[i]), new List<List<(float X, float Y)>>()));
        }

        var finalVerts = new List<(float X, float Y, float Z)>();
        var finalIndices = new List<ushort>();

        foreach (var (outer, groupHoles) in groups)
        {
            var poly = MergeHoles(outer, groupHoles);
            var baseIndex = finalVerts.Count;
            foreach (var (x, y) in poly)
                finalVerts.Add((x, y, 0f));

            var tris = EarClip(poly);
            foreach (var idx in tris)
                finalIndices.Add((ushort)(baseIndex + idx));
        }

        var finalColors = Enumerable.Repeat(defaultColor, finalVerts.Count).ToList();
        var minX = finalVerts.Min(v => v.X);
        var minY = finalVerts.Min(v => v.Y);
        var maxX = finalVerts.Max(v => v.X);
        var maxY = finalVerts.Max(v => v.Y);
        return (finalVerts, finalIndices, finalColors, minX, minY, maxX, maxY);
    }
    static List<List<(float X, float Y)>> ParseSvgPath(string d)
    {
        var contours = new List<List<(float X, float Y)>>();
        var current  = new List<(float X, float Y)>();

        float cx = 0, cy = 0;       // current pen
        float sx = 0, sy = 0;       // start of current sub-path (for Z)
        float lastCpX = 0, lastCpY = 0; // last bezier control point (for smooth curves)
        char  lastCmd = ' ';

        var tokens = TokenizePath(d);
        int ti = 0;

        char cmd = 'M';
        while (ti < tokens.Count)
        {
            string tok = tokens[ti];
            if (tok.Length == 1 && char.IsLetter(tok[0]))
            {
                cmd = tok[0];
                ti++;
                // After reading a new command letter, if we are at end or next is also a letter,
                // loop back to re-check (handles Z followed immediately by M etc.)
                if (char.ToUpper(cmd) == 'Z')
                {
                    // Z has no parameters — execute immediately then loop
                    if (current.Count >= 3)
                    {
                        var first0 = current[0]; var last0 = current[^1];
                        if (MathF.Abs(last0.X - first0.X) > 0.001f || MathF.Abs(last0.Y - first0.Y) > 0.001f)
                            current.Add((first0.X, first0.Y));
                        contours.Add(new List<(float, float)>(current));
                    }
                    current.Clear(); cx = sx; cy = sy; lastCmd = 'Z';
                    cmd = 'M'; // reset to M so next coord pair starts a new move (will be overridden by token)
                    continue;
                }
            }

            bool rel = char.IsLower(cmd);
            char upper = char.ToUpper(cmd);

            switch (upper)
            {
                case 'M':
                {
                    float x = NextFloat(tokens, ref ti);
                    float y = NextFloat(tokens, ref ti);
                    if (rel) { x += cx; y += cy; }
                    if (current.Count >= 3) contours.Add(new List<(float, float)>(current));
                    current.Clear();
                    cx = sx = x; cy = sy = y;
                    current.Add((cx, cy));
                    // Implicit lineto for subsequent coordinates
                    cmd = rel ? 'l' : 'L';
                    lastCmd = upper;
                    continue;
                }
                case 'L':
                {
                    if (ti >= tokens.Count || !IsNumber(tokens[ti])) { lastCmd = upper; continue; }
                    float x = NextFloat(tokens, ref ti);
                    float y = NextFloat(tokens, ref ti);
                    if (rel) { x += cx; y += cy; }
                    cx = x; cy = y;
                    current.Add((cx, cy));
                    lastCmd = upper;
                    continue;
                }
                case 'H':
                {
                    if (ti >= tokens.Count || !IsNumber(tokens[ti])) { lastCmd = upper; continue; }
                    float x = NextFloat(tokens, ref ti);
                    if (rel) x += cx;
                    cx = x;
                    current.Add((cx, cy));
                    lastCmd = upper;
                    continue;
                }
                case 'V':
                {
                    if (ti >= tokens.Count || !IsNumber(tokens[ti])) { lastCmd = upper; continue; }
                    float y = NextFloat(tokens, ref ti);
                    if (rel) y += cy;
                    cy = y;
                    current.Add((cx, cy));
                    lastCmd = upper;
                    continue;
                }
                case 'C': // cubic bezier
                {
                    if (ti >= tokens.Count || !IsNumber(tokens[ti])) { lastCmd = upper; continue; }
                    float x1 = NextFloat(tokens, ref ti); float y1 = NextFloat(tokens, ref ti);
                    float x2 = NextFloat(tokens, ref ti); float y2 = NextFloat(tokens, ref ti);
                    float x  = NextFloat(tokens, ref ti); float y  = NextFloat(tokens, ref ti);
                    if (rel) { x1+=cx; y1+=cy; x2+=cx; y2+=cy; x+=cx; y+=cy; }
                    SubdivideCubic(cx, cy, x1, y1, x2, y2, x, y, current);
                    lastCpX = x2; lastCpY = y2;
                    cx = x; cy = y;
                    lastCmd = upper;
                    continue;
                }
                case 'S': // smooth cubic
                {
                    if (ti >= tokens.Count || !IsNumber(tokens[ti])) { lastCmd = upper; continue; }
                    float x2 = NextFloat(tokens, ref ti); float y2 = NextFloat(tokens, ref ti);
                    float x  = NextFloat(tokens, ref ti); float y  = NextFloat(tokens, ref ti);
                    if (rel) { x2+=cx; y2+=cy; x+=cx; y+=cy; }
                    float x1 = lastCmd is 'C' or 'S' ? 2*cx - lastCpX : cx;
                    float y1 = lastCmd is 'C' or 'S' ? 2*cy - lastCpY : cy;
                    SubdivideCubic(cx, cy, x1, y1, x2, y2, x, y, current);
                    lastCpX = x2; lastCpY = y2;
                    cx = x; cy = y;
                    lastCmd = upper;
                    continue;
                }
                case 'Q': // quadratic bezier
                {
                    if (ti >= tokens.Count || !IsNumber(tokens[ti])) { lastCmd = upper; continue; }
                    float x1 = NextFloat(tokens, ref ti); float y1 = NextFloat(tokens, ref ti);
                    float x  = NextFloat(tokens, ref ti); float y  = NextFloat(tokens, ref ti);
                    if (rel) { x1+=cx; y1+=cy; x+=cx; y+=cy; }
                    SubdivideQuad(cx, cy, x1, y1, x, y, current);
                    lastCpX = x1; lastCpY = y1;
                    cx = x; cy = y;
                    lastCmd = upper;
                    continue;
                }
                case 'T': // smooth quadratic
                {
                    if (ti >= tokens.Count || !IsNumber(tokens[ti])) { lastCmd = upper; continue; }
                    float x = NextFloat(tokens, ref ti); float y = NextFloat(tokens, ref ti);
                    if (rel) { x+=cx; y+=cy; }
                    float x1 = lastCmd is 'Q' or 'T' ? 2*cx - lastCpX : cx;
                    float y1 = lastCmd is 'Q' or 'T' ? 2*cy - lastCpY : cy;
                    SubdivideQuad(cx, cy, x1, y1, x, y, current);
                    lastCpX = x1; lastCpY = y1;
                    cx = x; cy = y;
                    lastCmd = upper;
                    continue;
                }
                default:
                    ti++; // unknown / skip
                    continue;
            }
        }

        if (current.Count >= 3)
            contours.Add(current);

        return contours;
    }

    // -----------------------------------------------------------------------
    // Bezier subdivision
    // -----------------------------------------------------------------------

    const float FlatnessSq = 0.5f * 0.5f; // max deviation 0.5 units

    static void SubdivideCubic(
        float x0, float y0, float x1, float y1,
        float x2, float y2, float x3, float y3,
        List<(float X, float Y)> pts)
    {
        // Check flatness: max distance of control points from the chord
        float dx = x3 - x0, dy = y3 - y0;
        float lenSq = dx*dx + dy*dy;
        float d1Sq, d2Sq;
        if (lenSq < 1e-6f)
        {
            d1Sq = Sq(x1-x0, y1-y0);
            d2Sq = Sq(x2-x0, y2-y0);
        }
        else
        {
            float inv = 1f / lenSq;
            float t1 = ((x1-x0)*dx + (y1-y0)*dy) * inv;
            float t2 = ((x2-x0)*dx + (y2-y0)*dy) * inv;
            t1 = Math.Clamp(t1, 0, 1);
            t2 = Math.Clamp(t2, 0, 1);
            d1Sq = Sq(x1-(x0+t1*dx), y1-(y0+t1*dy));
            d2Sq = Sq(x2-(x0+t2*dx), y2-(y0+t2*dy));
        }

        if (d1Sq <= FlatnessSq && d2Sq <= FlatnessSq)
        {
            pts.Add((x3, y3));
            return;
        }

        // de Casteljau subdivision at t=0.5
        float m01x=(x0+x1)/2, m01y=(y0+y1)/2;
        float m12x=(x1+x2)/2, m12y=(y1+y2)/2;
        float m23x=(x2+x3)/2, m23y=(y2+y3)/2;
        float m012x=(m01x+m12x)/2, m012y=(m01y+m12y)/2;
        float m123x=(m12x+m23x)/2, m123y=(m12y+m23y)/2;
        float mx=(m012x+m123x)/2, my=(m012y+m123y)/2;

        SubdivideCubic(x0,y0, m01x,m01y, m012x,m012y, mx,my, pts);
        SubdivideCubic(mx,my, m123x,m123y, m23x,m23y, x3,y3, pts);
    }

    static void SubdivideQuad(
        float x0, float y0, float x1, float y1, float x2, float y2,
        List<(float X, float Y)> pts)
    {
        float dx = x2-x0, dy = y2-y0;
        float lenSq = dx*dx+dy*dy;
        float dSq;
        if (lenSq < 1e-6f)
            dSq = Sq(x1-x0, y1-y0);
        else
        {
            float t = Math.Clamp(((x1-x0)*dx+(y1-y0)*dy)/lenSq, 0, 1);
            dSq = Sq(x1-(x0+t*dx), y1-(y0+t*dy));
        }
        if (dSq <= FlatnessSq)
        {
            pts.Add((x2, y2));
            return;
        }
        float m01x=(x0+x1)/2, m01y=(y0+y1)/2;
        float m12x=(x1+x2)/2, m12y=(y1+y2)/2;
        float mx=(m01x+m12x)/2, my=(m01y+m12y)/2;
        SubdivideQuad(x0,y0, m01x,m01y, mx,my, pts);
        SubdivideQuad(mx,my, m12x,m12y, x2,y2, pts);
    }

    static float Sq(float a, float b) => a*a + b*b;

    // -----------------------------------------------------------------------
    // Winding / containment
    // -----------------------------------------------------------------------

    // Returns signed area in SVG (Y-down) space.
    // Positive = CCW = outer; negative = CW = hole.
    static double SignedArea(List<(float X, float Y)> poly)
    {
        double area = 0;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
            area += ((double)poly[j].X * poly[i].Y) - ((double)poly[i].X * poly[j].Y);
        return area / 2.0;
    }

    static bool PointInPolygon((float X, float Y) pt, List<(float X, float Y)> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = poly[i].X, yi = poly[i].Y;
            float xj = poly[j].X, yj = poly[j].Y;
            if ((yi > pt.Y) != (yj > pt.Y) &&
                pt.X < (xj - xi) * (pt.Y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    // -----------------------------------------------------------------------
    // Hole merging via bridge edges
    // -----------------------------------------------------------------------

    static List<(float X, float Y)> MergeHoles(
        List<(float X, float Y)> outer,
        List<List<(float X, float Y)>> holes)
    {
        if (holes.Count == 0) return outer;

        // Sort holes by rightmost vertex (descending X) for deterministic bridge insertion
        var sortedHoles = holes
            .OrderByDescending(h => h.Max(p => p.X))
            .ToList();

        var poly = new List<(float X, float Y)>(outer);

        foreach (var hole in sortedHoles)
        {
            // Find rightmost vertex of the hole
            int hIdx = 0;
            for (int i = 1; i < hole.Count; i++)
                if (hole[i].X > hole[hIdx].X)
                    hIdx = i;

            // Find the bridge vertex on the outer polygon (Mθller–Trumbore style)
            var bridgePt = hole[hIdx];
            int bridgeOuterIdx = FindBridgeVertex(poly, bridgePt);

            // Splice the hole into the outer polygon
            // Result: ... poly[bridgeOuterIdx], holeVerts (starting at hIdx, full loop), bridgePt, poly[bridgeOuterIdx], ...
            var merged = new List<(float X, float Y)>(poly.Count + hole.Count + 2);
            for (int i = 0; i <= bridgeOuterIdx; i++)
                merged.Add(poly[i]);
            // Insert hole vertices starting from hIdx, going around
            for (int i = 0; i <= hole.Count; i++)
                merged.Add(hole[(hIdx + i) % hole.Count]);
            // Duplicate the bridge outer vertex to close the seam
            merged.Add(poly[bridgeOuterIdx]);
            for (int i = bridgeOuterIdx + 1; i < poly.Count; i++)
                merged.Add(poly[i]);

            poly = merged;
        }

        return poly;
    }

    static int FindBridgeVertex(List<(float X, float Y)> poly, (float X, float Y) holePt)
    {
        // Cast a ray from holePt to the right; find the nearest outer edge intersection
        // then pick the outer vertex that is most visible (the "mutually visible" vertex).
        float bestX = float.MinValue;
        int bestEdgeStart = -1;

        for (int i = 0; i < poly.Count; i++)
        {
            int j = (i + 1) % poly.Count;
            var a = poly[i]; var b = poly[j];
            // Horizontal ray from holePt.X to +infinity at holePt.Y
            if ((a.Y <= holePt.Y && b.Y > holePt.Y) ||
                (b.Y <= holePt.Y && a.Y > holePt.Y))
            {
                float t = (holePt.Y - a.Y) / (b.Y - a.Y);
                float ix = a.X + t * (b.X - a.X);
                if (ix >= holePt.X && ix > bestX)
                {
                    bestX = ix;
                    bestEdgeStart = i;
                }
            }
        }

        if (bestEdgeStart < 0)
        {
            // Fallback: pick nearest outer vertex by distance
            int nearest = 0;
            float minDist = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                float dx = poly[i].X - holePt.X;
                float dy = poly[i].Y - holePt.Y;
                float d  = dx*dx + dy*dy;
                if (d < minDist) { minDist = d; nearest = i; }
            }
            return nearest;
        }

        // Among edge start/end, pick the rightmost (largest X) to get a shorter bridge
        var edgeA = poly[bestEdgeStart];
        var edgeB = poly[(bestEdgeStart + 1) % poly.Count];
        return edgeA.X >= edgeB.X ? bestEdgeStart : (bestEdgeStart + 1) % poly.Count;
    }

    // -----------------------------------------------------------------------
    // Ear-clipping triangulation
    // -----------------------------------------------------------------------

    static List<int> EarClip(List<(float X, float Y)> poly)
    {
        var result = new List<int>(Math.Max(0, (poly.Count - 2) * 3));
        if (poly.Count < 3) return result;

        // Remove duplicate closing vertex if present
        while (poly.Count > 3)
        {
            var first = poly[0]; var last = poly[^1];
            if (MathF.Abs(first.X - last.X) < 1e-5f && MathF.Abs(first.Y - last.Y) < 1e-5f)
                poly.RemoveAt(poly.Count - 1);
            else break;
        }

        if (poly.Count < 3) return result;

        // Ensure CCW winding (positive area) for the ear-clip to work correctly
        if (SignedArea(poly) < 0)
            poly.Reverse();

        // Build linked list of indices
        var indices = Enumerable.Range(0, poly.Count).ToList();

        int n = indices.Count;
        int maxIter = n * n + n; // safety
        int iter = 0;
        int i = 0;

        while (n > 3 && iter++ < maxIter)
        {
            int prev = indices[(i - 1 + n) % n]; // modular index into original poly
            int curr = indices[i % n];
            int next = indices[(i + 1) % n];

            if (IsEar(poly, indices, n, i))
            {
                result.Add(prev);
                result.Add(curr);
                result.Add(next);
                indices.RemoveAt(i % n);
                n--;
                if (i >= n) i = 0;
            }
            else
            {
                i = (i + 1) % n;
            }
        }

        // Last triangle
        if (n == 3)
        {
            result.Add(indices[0]);
            result.Add(indices[1]);
            result.Add(indices[2]);
        }

        return result;
    }

    static bool IsEar(List<(float X, float Y)> poly, List<int> indices, int n, int i)
    {
        int prev = indices[(i - 1 + n) % n];
        int curr = indices[i];
        int next = indices[(i + 1) % n];

        var a = poly[prev];
        var b = poly[curr];
        var c = poly[next];

        // The triangle must be CCW (convex ear) relative to the polygon
        if (Cross(a, b, c) <= 0) return false;

        // No other vertex should be inside this triangle
        for (int j = 0; j < n; j++)
        {
            int vi = indices[j];
            if (vi == prev || vi == curr || vi == next) continue;
            var p = poly[vi];
            if (PointInTriangle(p, a, b, c)) return false;
        }
        return true;
    }

    static float Cross((float X, float Y) o, (float X, float Y) a, (float X, float Y) b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    static bool PointInTriangle(
        (float X, float Y) p,
        (float X, float Y) a,
        (float X, float Y) b,
        (float X, float Y) c)
    {
        float d1 = Cross(a, b, p);
        float d2 = Cross(b, c, p);
        float d3 = Cross(c, a, p);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    // -----------------------------------------------------------------------
    // Path tokenizer helpers
    // -----------------------------------------------------------------------

    static List<string> TokenizePath(string d)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c)) { tokens.Add(c.ToString()); i++; continue; }

            // Number: optional sign, digits, optional decimal, optional exponent
            int start = i;
            if (c == '-' || c == '+') i++;
            while (i < d.Length && char.IsDigit(d[i])) i++;
            if (i < d.Length && d[i] == '.') { i++; while (i < d.Length && char.IsDigit(d[i])) i++; }
            if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
            {
                i++;
                if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
                while (i < d.Length && char.IsDigit(d[i])) i++;
            }
            if (i > start)
                tokens.Add(d[start..i]);
            else
                i++; // skip unknown
        }
        return tokens;
    }

    static float NextFloat(List<string> tokens, ref int ti)
    {
        if (ti >= tokens.Count) return 0;
        if (float.TryParse(tokens[ti], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
        { ti++; return v; }
        ti++;
        return 0;
    }

    static bool IsNumber(string tok)
    {
        if (tok.Length == 0) return false;
        char first = tok[0];
        return char.IsDigit(first) || first == '-' || first == '+' || first == '.';
    }
}


