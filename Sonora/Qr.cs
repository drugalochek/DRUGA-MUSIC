using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Sonora
{
    // ---------- Минимальный генератор QR-кодов (байтовый режим, уровень коррекции M, версии 1–40) ----------
    // Без сторонних библиотек: по мотивам эталонной реализации Nayuki (MIT).
    public static class Qr
    {
        static readonly int[] EccPerBlockM = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 };
        static readonly int[] BlocksM = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 };
        const int FormatEcc = 0; // уровень M

        // Матрица модулей [y, x], true = тёмный
        public static bool[,] Encode(string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? "");
            int ver = 1;
            for (; ver <= 40; ver++)
            {
                int need = 4 + (ver < 10 ? 8 : 16) + data.Length * 8;
                if (need <= DataCodewords(ver) * 8) break;
            }
            if (ver > 40) throw new ArgumentException("QR: слишком длинный текст");

            var bits = new List<bool>();
            Action<int, int> put = (val, len) => { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };
            put(4, 4);
            put(data.Length, ver < 10 ? 8 : 16);
            foreach (var b in data) put(b, 8);
            int capBits = DataCodewords(ver) * 8;
            put(0, Math.Min(4, capBits - bits.Count));
            put(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capBits; pad ^= 0xEC ^ 0x11) put(pad, 8);
            var cw = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            var q = new Matrix(ver);
            q.DrawFunction();
            q.DrawCodewords(AddEcc(cw, ver));

            int best = 0; long bestPen = long.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                q.ApplyMask(m);
                q.DrawFormat(m);
                long p = q.Penalty();
                if (p < bestPen) { bestPen = p; best = m; }
                q.ApplyMask(m); // XOR обратно
            }
            q.ApplyMask(best);
            q.DrawFormat(best);
            return q.M;
        }

        static int RawModules(int ver)
        {
            int r = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int na = ver / 7 + 2;
                r -= (25 * na - 10) * na - 55;
                if (ver >= 7) r -= 36;
            }
            return r;
        }
        static int DataCodewords(int ver) { return RawModules(ver) / 8 - EccPerBlockM[ver] * BlocksM[ver]; }

        static byte[] AddEcc(byte[] data, int ver)
        {
            int nb = BlocksM[ver], ecc = EccPerBlockM[ver];
            int raw = RawModules(ver) / 8;
            int nShort = nb - raw % nb;
            int shortLen = raw / nb;
            var div = RsDivisor(ecc);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < nb; i++)
            {
                int dl = shortLen - ecc + (i < nShort ? 0 : 1);
                var dat = new byte[dl];
                Array.Copy(data, k, dat, 0, dl);
                k += dl;
                var rem = RsRemainder(dat, div);
                var blk = new byte[shortLen + 1];
                Array.Copy(dat, blk, dl);
                Array.Copy(rem, 0, blk, shortLen + 1 - ecc, ecc);
                blocks.Add(blk);
            }
            var res = new List<byte>();
            for (int i = 0; i < shortLen + 1; i++)
                for (int j = 0; j < nb; j++)
                    if (i != shortLen - ecc || j >= nShort) res.Add(blocks[j][i]);
            return res.ToArray();
        }

        static byte[] RsDivisor(int deg)
        {
            var r = new byte[deg];
            r[deg - 1] = 1;
            int root = 1;
            for (int i = 0; i < deg; i++)
            {
                for (int j = 0; j < deg; j++)
                {
                    r[j] = Mul(r[j], root);
                    if (j + 1 < deg) r[j] ^= r[j + 1];
                }
                root = Mul(root, 2);
            }
            return r;
        }
        static byte[] RsRemainder(byte[] data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (var b in data)
            {
                int f = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= Mul(div[i], f);
            }
            return r;
        }
        static byte Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        sealed class Matrix
        {
            public readonly int N, Ver;
            public readonly bool[,] M;
            readonly bool[,] fn;
            public Matrix(int ver) { Ver = ver; N = ver * 4 + 17; M = new bool[N, N]; fn = new bool[N, N]; }
            void Set(int x, int y, bool dark) { M[y, x] = dark; fn[y, x] = true; }

            public void DrawFunction()
            {
                for (int i = 0; i < N; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
                Finder(3, 3); Finder(N - 4, 3); Finder(3, N - 4);
                var pos = AlignPos();
                int n = pos.Length;
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0)) Align(pos[i], pos[j]);
                DrawFormat(0);
                DrawVersion();
            }
            void Finder(int x, int y)
            {
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
                        if (xx >= 0 && xx < N && yy >= 0 && yy < N) Set(xx, yy, d != 2 && d != 4);
                    }
            }
            void Align(int x, int y)
            {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
            int[] AlignPos()
            {
                if (Ver == 1) return new int[0];
                int na = Ver / 7 + 2;
                int step = Ver == 32 ? 26 : (Ver * 4 + na * 2 + 1) / (na * 2 - 2) * 2;
                var r = new int[na];
                r[0] = 6;
                for (int i = na - 1, p = N - 7; i >= 1; i--, p -= step) r[i] = p;
                return r;
            }
            public void DrawFormat(int mask)
            {
                int data = FormatEcc << 3 | mask;
                int rem = data;
                for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
                int bits = (data << 10 | rem) ^ 0x5412;
                Func<int, bool> b = i => ((bits >> i) & 1) != 0;
                for (int i = 0; i <= 5; i++) Set(8, i, b(i));
                Set(8, 7, b(6)); Set(8, 8, b(7)); Set(7, 8, b(8));
                for (int i = 9; i < 15; i++) Set(14 - i, 8, b(i));
                for (int i = 0; i < 8; i++) Set(N - 1 - i, 8, b(i));
                for (int i = 8; i < 15; i++) Set(8, N - 15 + i, b(i));
                Set(8, N - 8, true);
            }
            void DrawVersion()
            {
                if (Ver < 7) return;
                int rem = Ver;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = Ver << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = N - 11 + i % 3, c = i / 3;
                    Set(a, c, bit); Set(c, a, bit);
                }
            }
            public void DrawCodewords(byte[] data)
            {
                int i = 0, total = data.Length * 8;
                for (int right = N - 1; right >= 1; right -= 2)
                {
                    if (right == 6) right = 5;
                    for (int vert = 0; vert < N; vert++)
                        for (int j = 0; j < 2; j++)
                        {
                            int x = right - j;
                            bool upward = ((right + 1) & 2) == 0;
                            int y = upward ? N - 1 - vert : vert;
                            if (!fn[y, x] && i < total)
                            {
                                M[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                                i++;
                            }
                        }
                }
            }
            public void ApplyMask(int m)
            {
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        if (fn[y, x]) continue;
                        bool inv;
                        switch (m)
                        {
                            case 0: inv = (x + y) % 2 == 0; break;
                            case 1: inv = y % 2 == 0; break;
                            case 2: inv = x % 3 == 0; break;
                            case 3: inv = (x + y) % 3 == 0; break;
                            case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                            case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                            case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                            default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                        }
                        if (inv) M[y, x] = !M[y, x];
                    }
            }
            bool At(int pass, int a, int b) { return pass == 0 ? M[a, b] : M[b, a]; }
            // Штрафы по стандарту (упрощённо): длинные ряды, квадраты 2×2, шаблоны «как у искателя», баланс цвета
            public long Penalty()
            {
                long r = 0;
                for (int pass = 0; pass < 2; pass++)
                    for (int a = 0; a < N; a++)
                    {
                        bool prev = false; int run = 0;
                        for (int b = 0; b < N; b++)
                        {
                            bool c = At(pass, a, b);
                            if (b > 0 && c == prev) { run++; if (run == 5) r += 3; else if (run > 5) r++; }
                            else { prev = c; run = 1; }
                        }
                        for (int b = 0; b + 6 < N; b++)
                        {
                            if (!(At(pass, a, b) && !At(pass, a, b + 1) && At(pass, a, b + 2) && At(pass, a, b + 3) && At(pass, a, b + 4) && !At(pass, a, b + 5) && At(pass, a, b + 6))) continue;
                            bool before = true, after = true;
                            for (int k = 1; k <= 4; k++)
                            {
                                int p = b - k, q = b + 6 + k;
                                if (p >= 0 && At(pass, a, p)) before = false;
                                if (q < N && At(pass, a, q)) after = false;
                            }
                            if (before || after) r += 40;
                        }
                    }
                int dark = 0;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        if (M[y, x]) dark++;
                        if (x + 1 < N && y + 1 < N && M[y, x] == M[y, x + 1] && M[y, x] == M[y + 1, x] && M[y, x] == M[y + 1, x + 1]) r += 3;
                    }
                int total = N * N;
                int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
                r += Math.Max(0, k2) * 10;
                return r;
            }
        }

        // Для проверки в CI: матрицы для набора строк разной длины (строка: длина текста, размер, биты)
        public static void SelfTest(string file)
        {
            var sb = new StringBuilder();
            var rnd = new Random(7);
            const string abc = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:/?&=.-_";
            var tests = new List<string> { Plus.PayUrl("AC", "preview-uid-000000000000000000"), "SONORA", "Привет, SONORA Plus!" };
            foreach (var len in new[] { 1, 10, 17, 25, 40, 70, 100, 134, 150, 200, 270, 400, 600, 900, 1300, 1800, 2300 })
            {
                var c = new char[len];
                for (int i = 0; i < len; i++) c[i] = abc[rnd.Next(abc.Length)];
                tests.Add(new string(c));
            }
            foreach (var t in tests)
            {
                var m = Encode(t);
                int n = m.GetLength(0);
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(t))).Append(' ').Append(n).Append(' ');
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) sb.Append(m[y, x] ? '1' : '0');
                sb.Append('\n');
            }
            System.IO.File.WriteAllText(file, sb.ToString());
        }

        // Картинка для WPF: тёмные модули на белом с «тихой зоной» в 4 модуля. Ширина картинки = число модулей + 8.
        public static DrawingImage Image(string text)
        {
            var m = Encode(text);
            int n = m.GetLength(0);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        if (!m[y, x]) continue;
                        int x2 = x;
                        while (x2 + 1 < n && m[y, x2 + 1]) x2++;
                        ctx.BeginFigure(new Point(x + 4, y + 4), true, true);
                        ctx.LineTo(new Point(x2 + 5, y + 4), false, false);
                        ctx.LineTo(new Point(x2 + 5, y + 5), false, false);
                        ctx.LineTo(new Point(x + 4, y + 5), false, false);
                        x = x2;
                    }
            }
            geo.Freeze();
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, n + 8, n + 8))));
            var ink = new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x12));
            ink.Freeze();
            g.Children.Add(new GeometryDrawing(ink, null, geo));
            g.Freeze();
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }
    }
}
