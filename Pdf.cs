using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tempo;

/// <summary>
/// Tempo's own minimal PDF 1.5 writer. Text is shaped with WPF font metrics and embedded as subsetted TrueType
/// (Type0 / Identity-H), so accents, CJK and symbols render correctly and stay searchable via a ToUnicode map.
/// Coordinates are top-down points; (0,0) is the top-left of the page.
/// </summary>
public sealed class PdfWriter
{
    static readonly string[] Families = { "Segoe UI", "Microsoft YaHei UI", "Yu Gothic UI", "Malgun Gothic", "Segoe UI Symbol" };

    internal sealed class Font
    {
        public GlyphTypeface Face = null!;
        public string Name = "";
        public string Tag = "";
        public SortedDictionary<ushort, int> Used = new() { [0] = 0 };
    }
    internal sealed class Seg { public Font Font = null!; public List<ushort> Glyphs = new(); }

    public sealed class Img { internal byte[] Rgb = null!; internal byte[]? Alpha; internal int W, H; internal string Name = ""; public double Width => W; public double Height => H; }

    public sealed class Page
    {
        internal readonly PdfWriter Doc;
        internal readonly StringBuilder C = new();
        internal readonly HashSet<Font> Fonts = new();
        internal readonly HashSet<Img> Images = new();
        public double Width { get; }
        public double Height { get; }
        internal Page(PdfWriter d, double w, double h) { Doc = d; Width = w; Height = h; }

        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string Col(Color c) => $"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)}";

        /// <summary>Draws text with its line box starting at <paramref name="top"/>.</summary>
        public void Text(double x, double top, string s, double size, bool bold, Color color)
        {
            if (string.IsNullOrEmpty(s)) return;
            var segs = Doc.Shape(s, bold);
            double baseline = Height - (top + size * 0.98);
            C.Append($"{Col(color)} rg BT {N(x)} {N(baseline)} Td ");
            foreach (var seg in segs)
            {
                Fonts.Add(seg.Font);
                C.Append($"/{seg.Font.Name} {N(size)} Tf <");
                foreach (var g in seg.Glyphs) C.Append(g.ToString("X4"));
                C.Append("> Tj ");
            }
            C.Append("ET\n");
        }

        public void TextRight(double right, double top, string s, double size, bool bold, Color color) =>
            Text(right - Doc.Measure(s, size, bold), top, s, size, bold, color);

        public void Line(double x1, double y1, double x2, double y2, double width, Color color) =>
            C.Append($"{Col(color).Replace(' ', ' ')} RG {N(width)} w {N(x1)} {N(Height - y1)} m {N(x2)} {N(Height - y2)} l S\n");

        public void Image(Img img, double x, double top, double w, double h)
        {
            Images.Add(img);
            C.Append($"q {N(w)} 0 0 {N(h)} {N(x)} {N(Height - top - h)} cm /{img.Name} Do Q\n");
        }
    }

    readonly Dictionary<bool, List<Font>> chains = new();
    readonly List<Font> allFonts = new();
    readonly List<Page> pages = new();
    readonly List<Img> images = new();

    public Page AddPage(double w, double h) { var p = new Page(this, w, h); pages.Add(p); return p; }
    public int PageCount => pages.Count;
    public Page PageAt(int i) => pages[i];

    List<Font> Chain(bool bold)
    {
        if (chains.TryGetValue(bold, out var c)) return c;
        c = new List<Font>();
        foreach (var fam in Families)
        {
            var tf = new Typeface(new FontFamily(fam), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
            if (!tf.TryGetGlyphTypeface(out var gt)) continue;
            var existing = allFonts.FirstOrDefault(f => f.Face.FontUri == gt.FontUri && f.Face.Weight == gt.Weight);
            if (existing == null)
            {
                existing = new Font { Face = gt, Name = "F" + (allFonts.Count + 1), Tag = Tag(allFonts.Count) };
                allFonts.Add(existing);
            }
            c.Add(existing);
        }
        if (c.Count == 0) throw new InvalidOperationException("No usable fonts were found for PDF export.");
        chains[bold] = c;
        return c;
    }

    static string Tag(int i) { var s = new char[6]; for (int k = 5; k >= 0; k--) { s[k] = (char)('A' + i % 26); i /= 26; } return new string(s); }

    List<Seg> Shape(string text, bool bold)
    {
        var chain = Chain(bold);
        var segs = new List<Seg>();
        foreach (var r in text.EnumerateRunes())
        {
            int cp = r.Value;
            if (cp < 32 && cp != '\t') continue;
            if (cp == '\t') cp = ' ';
            Font? f = null; ushort g = 0;
            foreach (var cand in chain) if (cand.Face.CharacterToGlyphMap.TryGetValue(cp, out g)) { f = cand; break; }
            if (f == null) { f = chain[0]; g = 0; cp = 0; }
            f.Used[g] = cp;
            if (segs.Count == 0 || segs[^1].Font != f) segs.Add(new Seg { Font = f });
            segs[^1].Glyphs.Add(g);
        }
        return segs;
    }

    public double Measure(string text, double size, bool bold)
    {
        double w = 0;
        foreach (var seg in Shape(text, bold))
            foreach (var g in seg.Glyphs) w += seg.Font.Face.AdvanceWidths[g];
        return w * size;
    }

    /// <summary>Word-wraps (and hard-wraps long words / CJK) to the given width. Honours explicit newlines.</summary>
    public List<string> Wrap(string text, double width, double size, bool bold)
    {
        var lines = new List<string>();
        foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
        {
            if (para.Length == 0) { lines.Add(""); continue; }
            var cur = new StringBuilder();
            foreach (var word in para.Split(' '))
            {
                var trial = cur.Length == 0 ? word : cur + " " + word;
                if (Measure(trial, size, bold) <= width) { cur.Clear(); cur.Append(trial); continue; }
                if (cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); }
                var piece = new StringBuilder();
                foreach (var rune in word.EnumerateRunes())
                {
                    var t = piece + rune.ToString();
                    if (piece.Length > 0 && Measure(t, size, bold) > width) { lines.Add(piece.ToString()); piece.Clear(); }
                    piece.Append(rune.ToString());
                }
                cur.Append(piece);
            }
            lines.Add(cur.ToString());
        }
        return lines;
    }

    /// <summary>Loads PNG/JPEG (downscaled if huge) for embedding. Returns null if the file isn't a readable image.</summary>
    public Img? LoadImage(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(path); bi.CacheOption = BitmapCacheOption.OnLoad;
            if (bi.PixelWidth == 0) { }
            bi.EndInit();
            BitmapSource src = bi;
            if (src.PixelWidth > 600) src = new TransformedBitmap(src, new ScaleTransform(600.0 / src.PixelWidth, 600.0 / src.PixelWidth));
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            var rgb = new byte[w * h * 3]; var a = new byte[w * h]; bool hasAlpha = false;
            for (int i = 0; i < w * h; i++)
            {
                rgb[i * 3] = px[i * 4 + 2]; rgb[i * 3 + 1] = px[i * 4 + 1]; rgb[i * 3 + 2] = px[i * 4];
                a[i] = px[i * 4 + 3]; if (a[i] != 255) hasAlpha = true;
            }
            var img = new Img { Rgb = rgb, Alpha = hasAlpha ? a : null, W = w, H = h, Name = "Im" + (images.Count + 1) };
            images.Add(img);
            return img;
        }
        catch { return null; }
    }

    // ---------------- serialisation ----------------
    static byte[] Z(byte[] b)
    {
        using var o = new MemoryStream();
        using (var z = new ZLibStream(o, CompressionLevel.Optimal)) z.Write(b, 0, b.Length);
        return o.ToArray();
    }
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    static string Hex16(int cp)
    {
        if (cp <= 0xFFFF) return cp.ToString("X4");
        cp -= 0x10000; return (0xD800 + (cp >> 10)).ToString("X4") + (0xDC00 + (cp & 0x3FF)).ToString("X4");
    }

    public void Save(string path, string title)
    {
        var ms = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        int next = 1; int Alloc() => next++;
        int cat = Alloc(), pagesObj = Alloc(), info = Alloc();

        var usedFonts = allFonts.Where(f => pages.Any(p => p.Fonts.Contains(f))).ToList();
        var fontNums = usedFonts.ToDictionary(f => f, f => (type0: Alloc(), cid: Alloc(), desc: Alloc(), file: Alloc(), uni: Alloc()));
        var usedImages = images.Where(i => pages.Any(p => p.Images.Contains(i))).ToList();
        var imgNums = usedImages.ToDictionary(i => i, i => (img: Alloc(), mask: i.Alpha != null ? Alloc() : 0));
        var pageNums = pages.Select(_ => (page: Alloc(), content: Alloc())).ToList();

        var off = new long[next];
        void Begin(int n) { off[n] = ms.Position; W($"{n} 0 obj\n"); }
        void End() => W("endobj\n");
        void Obj(int n, string body) { Begin(n); W(body + "\n"); End(); }
        void Stream(int n, string dict, byte[] data) { Begin(n); W($"<< {dict} /Length {data.Length} >>\nstream\n"); ms.Write(data, 0, data.Length); W("\nendstream\n"); End(); }

        W("%PDF-1.5\n%âãÏÓ\n");
        Obj(cat, $"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
        Obj(pagesObj, $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", pageNums.Select(p => p.page + " 0 R"))}] >>");
        Obj(info, $"<< /Title ({Esc(title)}) /Producer (Tempo) /Creator (Tempo) >>");

        foreach (var f in usedFonts)
        {
            var n = fontNums[f];
            var baseName = f.Tag + "+" + new string(f.Face.Win32FamilyNames.Values.FirstOrDefault()?.Where(char.IsLetterOrDigit).ToArray() ?? "Font".ToCharArray());
            Obj(n.type0, $"<< /Type /Font /Subtype /Type0 /BaseFont /{baseName} /Encoding /Identity-H /DescendantFonts [{n.cid} 0 R] /ToUnicode {n.uni} 0 R >>");
            var widths = new StringBuilder();
            foreach (var g in f.Used.Keys) widths.Append($"{g} [{Math.Round(f.Face.AdvanceWidths[g] * 1000)}] ");
            Obj(n.cid, $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{baseName} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {n.desc} 0 R /DW 1000 /W [{widths}] /CIDToGIDMap /Identity >>");
            int asc = (int)Math.Round(f.Face.Baseline * 1000), dsc = -(int)Math.Round((f.Face.Height - f.Face.Baseline) * 1000);
            Obj(n.desc, $"<< /Type /FontDescriptor /FontName /{baseName} /Flags 4 /FontBBox [-300 {dsc} 1500 {asc}] /ItalicAngle 0 /Ascent {asc} /Descent {dsc} /CapHeight {(int)Math.Round(f.Face.CapsHeight * 1000)} /StemV 80 /FontFile2 {n.file} 0 R >>");
            var subset = f.Face.ComputeSubset(f.Used.Keys.ToList());
            Stream(n.file, $"/Filter /FlateDecode /Length1 {subset.Length}", Z(subset));

            var cm = new StringBuilder();
            cm.Append("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def /CMapName /Adobe-Identity-UCS def /CMapType 2 def\n1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
            var map = f.Used.Where(kv => kv.Value > 0).ToList();
            for (int i = 0; i < map.Count; i += 100)
            {
                var chunk = map.Skip(i).Take(100).ToList();
                cm.Append($"{chunk.Count} beginbfchar\n");
                foreach (var kv in chunk) cm.Append($"<{kv.Key:X4}> <{Hex16(kv.Value)}>\n");
                cm.Append("endbfchar\n");
            }
            cm.Append("endcmap CMapName currentdict /CMap defineresource pop end end");
            Stream(n.uni, "/Filter /FlateDecode", Z(Encoding.ASCII.GetBytes(cm.ToString())));
        }

        foreach (var im in usedImages)
        {
            var n = imgNums[im];
            Stream(n.img, $"/Type /XObject /Subtype /Image /Width {im.W} /Height {im.H} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode" + (n.mask != 0 ? $" /SMask {n.mask} 0 R" : ""), Z(im.Rgb));
            if (n.mask != 0) Stream(n.mask, $"/Type /XObject /Subtype /Image /Width {im.W} /Height {im.H} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", Z(im.Alpha!));
        }

        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            var fonts = string.Join(" ", p.Fonts.Select(f => $"/{f.Name} {fontNums[f].type0} 0 R"));
            var xo = string.Join(" ", p.Images.Select(im => $"/{im.Name} {imgNums[im].img} 0 R"));
            Obj(pageNums[i].page, $"<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [0 0 {p.Width.ToString("0.##", CultureInfo.InvariantCulture)} {p.Height.ToString("0.##", CultureInfo.InvariantCulture)}] /Resources << /Font << {fonts} >> /XObject << {xo} >> >> /Contents {pageNums[i].content} 0 R >>");
            Stream(pageNums[i].content, "/Filter /FlateDecode", Z(Encoding.Latin1.GetBytes(p.C.ToString())));
        }

        long xref = ms.Position;
        W($"xref\n0 {next}\n0000000000 65535 f \n");
        for (int i = 1; i < next; i++) W($"{off[i]:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {next} /Root {cat} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, ms.ToArray());
    }
}
