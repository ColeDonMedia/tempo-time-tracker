using System.Text;

namespace Tempo;

/// <summary>
/// Minimal QR Code encoder written for Tempo's invoices ("scan to pay"): byte mode, error-correction level M,
/// versions 1 to 10 (up to 213 bytes). Returns the module matrix, [row, column], true = dark.
/// </summary>
public static class QrCode
{
    // Level M, versions 1..10: { data codewords, EC codewords per block, blocks in group 1, data cw per block (g1), blocks in group 2, data cw per block (g2) }
    static readonly int[][] Table =
    {
        new[] { 16, 10, 1, 16, 0, 0 },  new[] { 28, 16, 1, 28, 0, 0 },  new[] { 44, 26, 1, 44, 0, 0 },
        new[] { 64, 18, 2, 32, 0, 0 },  new[] { 86, 24, 2, 43, 0, 0 },  new[] { 108, 16, 4, 27, 0, 0 },
        new[] { 124, 18, 4, 31, 0, 0 }, new[] { 154, 22, 2, 38, 2, 39 }, new[] { 182, 22, 3, 36, 2, 37 },
        new[] { 216, 26, 4, 43, 1, 44 },
    };
    static readonly int[][] Align =
    {
        Array.Empty<int>(), new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
        new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
    };

    public static bool[,]? Encode(string text, int forceMask = -1)
    {
        var data = Encoding.UTF8.GetBytes(text);
        int ver = 0;
        for (int v = 1; v <= 10; v++)
            if (data.Length * 8 + (v < 10 ? 12 : 20) <= Table[v - 1][0] * 8) { ver = v; break; }
        if (ver == 0) return null;
        var t = Table[ver - 1];
        int dataCw = t[0];

        // ---- bit stream: mode (byte), length, data, terminator, padding
        var bits = new List<bool>();
        void Put(int val, int n) { for (int i = n - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); }
        Put(0b0100, 4); Put(data.Length, ver < 10 ? 8 : 16);
        foreach (var b in data) Put(b, 8);
        Put(0, Math.Min(4, dataCw * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        var cw = new List<byte>();
        for (int i = 0; i < bits.Count; i += 8) { int v = 0; for (int j = 0; j < 8; j++) v = (v << 1) | (bits[i + j] ? 1 : 0); cw.Add((byte)v); }
        for (bool alt = false; cw.Count < dataCw; alt = !alt) cw.Add(alt ? (byte)0x11 : (byte)0xEC);

        // ---- split into blocks, add Reed-Solomon error correction, interleave
        var blocks = new List<byte[]>(); int pos = 0;
        for (int i = 0; i < t[2]; i++) { blocks.Add(cw.GetRange(pos, t[3]).ToArray()); pos += t[3]; }
        for (int i = 0; i < t[4]; i++) { blocks.Add(cw.GetRange(pos, t[5]).ToArray()); pos += t[5]; }
        var divisor = Divisor(t[1]);
        var ecs = blocks.Select(b => Remainder(b, divisor)).ToList();
        var all = new List<byte>();
        for (int i = 0; i < blocks.Max(b => b.Length); i++) foreach (var b in blocks) if (i < b.Length) all.Add(b[i]);
        for (int i = 0; i < t[1]; i++) foreach (var e in ecs) all.Add(e[i]);

        // ---- matrix
        int size = ver * 4 + 17;
        var baseMod = new bool[size, size];
        var fn = new bool[size, size];
        DrawFunctionPatterns(baseMod, fn, ver, size);
        PlaceData(baseMod, fn, all, size);

        bool[,]? best = null; int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            if (forceMask >= 0 && mask != forceMask) continue;
            var m = (bool[,])baseMod.Clone();
            var f2 = (bool[,])fn.Clone();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (!fn[y, x] && Mask(mask, x, y)) m[y, x] = !m[y, x];
            DrawFormat(m, f2, mask, size);
            int p = Penalty(m, size);
            if (p < bestPenalty) { bestPenalty = p; best = m; }
        }
        return best;
    }

    // ---------------- Reed-Solomon over GF(256), polynomial 0x11D ----------------
    static int Mul(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
        return z;
    }

    static byte[] Divisor(int degree)
    {
        var r = new byte[degree]; r[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                r[j] = (byte)Mul(r[j], root);
                if (j + 1 < degree) r[j] ^= r[j + 1];
            }
            root = Mul(root, 2);
        }
        return r;
    }

    static byte[] Remainder(byte[] data, byte[] divisor)
    {
        var r = new byte[divisor.Length];
        foreach (var b in data)
        {
            int factor = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1); r[^1] = 0;
            for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(divisor[i], factor);
        }
        return r;
    }

    // ---------------- layout ----------------
    static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark) { if (x >= 0 && x < m.GetLength(1) && y >= 0 && y < m.GetLength(0)) { m[y, x] = dark; fn[y, x] = true; } }

    static void DrawFunctionPatterns(bool[,] m, bool[,] fn, int ver, int size)
    {
        for (int i = 0; i < size; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }
        void Finder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(m, fn, cx + dx, cy + dy, dist != 2 && dist != 4);
                }
        }
        Finder(3, 3); Finder(size - 4, 3); Finder(3, size - 4);
        var al = Align[ver - 1];
        for (int i = 0; i < al.Length; i++)
            for (int j = 0; j < al.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == al.Length - 1) || (i == al.Length - 1 && j == 0)) continue;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(m, fn, al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        DrawFormat(m, fn, 0, size);               // reserve the format areas
        if (ver >= 7)
        {
            int rem = ver;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (ver << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(m, fn, a, b, bit); Set(m, fn, b, a, bit);
            }
        }
    }

    static void DrawFormat(bool[,] m, bool[,] fn, int mask, int size)
    {
        int data = (0 << 3) | mask;               // error-correction level M = 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;
        bool B(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Set(m, fn, 8, i, B(i));
        Set(m, fn, 8, 7, B(6)); Set(m, fn, 8, 8, B(7)); Set(m, fn, 7, 8, B(8));
        for (int i = 9; i < 15; i++) Set(m, fn, 14 - i, 8, B(i));
        for (int i = 0; i < 8; i++) Set(m, fn, size - 1 - i, 8, B(i));
        for (int i = 8; i < 15; i++) Set(m, fn, 8, size - 15 + i, B(i));
        Set(m, fn, 8, size - 8, true);            // the always-dark module
    }

    static void PlaceData(bool[,] m, bool[,] fn, List<byte> data, int size)
    {
        int i = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (!fn[y, x] && i < data.Count * 8)
                    {
                        m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
        }
    }

    static bool Mask(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => (x / 3 + y / 2) % 2 == 0,
        5 => x * y % 2 + x * y % 3 == 0,
        6 => (x * y % 2 + x * y % 3) % 2 == 0,
        _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
    };

    // ---------------- mask choice: lowest penalty wins ----------------
    static int Penalty(bool[,] m, int size)
    {
        int score = 0;
        // N1: runs of 5+ same-colour modules in rows and columns
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++)
            {
                int run = 1;
                for (int b = 1; b < size; b++)
                {
                    bool cur = pass == 0 ? m[a, b] : m[b, a], prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) score += 3; else if (run > 5) score++; }
                    else run = 1;
                }
            }
        // N2: 2x2 blocks
        for (int y = 0; y < size - 1; y++)
            for (int x = 0; x < size - 1; x++)
                if (m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) score += 3;
        // N3: finder-like 1:1:3:1:1 patterns with a light margin
        int[] p1 = { 1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0 }, p2 = { 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1 };
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++)
                for (int b = 0; b <= size - 11; b++)
                {
                    bool m1 = true, m2 = true;
                    for (int k = 0; k < 11 && (m1 || m2); k++)
                    {
                        int v = (pass == 0 ? m[a, b + k] : m[b + k, a]) ? 1 : 0;
                        if (v != p1[k]) m1 = false;
                        if (v != p2[k]) m2 = false;
                    }
                    if (m1) score += 40;
                    if (m2) score += 40;
                }
        // N4: dark/light balance
        int dark = 0;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (m[y, x]) dark++;
        int k4 = (int)Math.Ceiling(Math.Abs(dark * 20 - size * size * 10) / (double)(size * size)) - 1;
        return score + Math.Max(0, k4) * 10;
    }
}
