using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Sonora
{
    // Цвета обложки: основной, второй оттенок и тёмный
    public static class Palette
    {
        static readonly Dictionary<string, Color[]> cache = new Dictionary<string, Color[]>();
        public static readonly Color[] Fallback = { Color.FromRgb(59, 130, 246), Color.FromRgb(56, 189, 248), Color.FromRgb(30, 58, 138) };

        public static Color[] Of(Track t)
        {
            if (t == null || string.IsNullOrEmpty(t.Cover)) return Fallback;
            if (cache.TryGetValue(t.Id, out var c0)) return c0;
            Color[] res = Fallback;
            try
            {
                var src = Img.FromData(t.Id, t.Cover, 64);
                if (src != null)
                {
                    var scaled = new TransformedBitmap(src, new ScaleTransform(36.0 / src.PixelWidth, 36.0 / src.PixelHeight));
                    var bmp = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                    int w = bmp.PixelWidth, h = bmp.PixelHeight;
                    var px = new byte[w * h * 4];
                    bmp.CopyPixels(px, w * 4, 0);
                    const int Bins = 12;
                    var wsum = new double[Bins]; var rs = new double[Bins]; var gs = new double[Bins]; var bs = new double[Bins];
                    double ar = 0, ag = 0, ab = 0; int an = 0;
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        int b = px[i], g = px[i + 1], r = px[i + 2];
                        ar += r; ag += g; ab += b; an++;
                        double hh, s, l; Hsl(r, g, b, out hh, out s, out l);
                        if (l < .08 || l > .94) continue;
                        double wt = s * s * (1 - Math.Abs(l - .5) * 1.2) + .02;
                        int bin = (int)(hh / 360 * Bins) % Bins;
                        wsum[bin] += wt; rs[bin] += r * wt; gs[bin] += g * wt; bs[bin] += b * wt;
                    }
                    var order = Enumerable.Range(0, Bins).OrderByDescending(i => wsum[i]).ToList();
                    if (wsum[order[0]] > 0.05)
                    {
                        Func<int, Color> col = i => Color.FromRgb((byte)(rs[i] / wsum[i]), (byte)(gs[i] / wsum[i]), (byte)(bs[i] / wsum[i]));
                        var c1 = col(order[0]);
                        int second = order.Skip(1).FirstOrDefault(i => wsum[i] > wsum[order[0]] * .12 && Math.Min(Math.Abs(i - order[0]), Bins - Math.Abs(i - order[0])) >= 2);
                        var c2 = second != order[0] && wsum[second] > 0 ? col(second) : Shift(c1, 28);
                        var c3 = Darken(c1, .45);
                        res = new[] { Vivid(c1), Vivid(c2), c3 };
                    }
                    else if (an > 0)
                    {
                        // почти чёрно-белая обложка — мягкий серо-синий
                        var g0 = Color.FromRgb((byte)(ar / an), (byte)(ag / an), (byte)(ab / an));
                        res = new[] { Mix(g0, Color.FromRgb(96, 120, 160), .6), Mix(g0, Color.FromRgb(140, 150, 175), .5), Color.FromRgb(24, 28, 38) };
                    }
                }
            }
            catch (Exception ex) { Log.W("palette: " + ex.Message); }
            cache[t.Id] = res;
            return res;
        }

        static void Hsl(int r, int g, int b, out double h, out double s, out double l)
        {
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
            l = (mx + mn) / 2;
            if (mx == mn) { h = 0; s = 0; return; }
            double d = mx - mn;
            s = l > .5 ? d / (2 - mx - mn) : d / (mx + mn);
            if (mx == R) h = (G - B) / d + (G < B ? 6 : 0);
            else if (mx == G) h = (B - R) / d + 2;
            else h = (R - G) / d + 4;
            h *= 60;
        }
        static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360;
            Func<double, double, double, double> f = (p, q, t) =>
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < .5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            };
            double r, g, b;
            if (s == 0) r = g = b = l;
            else
            {
                double q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                r = f(p, q, h + 1.0 / 3); g = f(p, q, h); b = f(p, q, h - 1.0 / 3);
            }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }
        // делаем цвет «светящимся»: насыщенным и средней яркости
        public static Color Vivid(Color c)
        {
            double h, s, l; Hsl(c.R, c.G, c.B, out h, out s, out l);
            return FromHsl(h, Math.Max(.55, Math.Min(.95, s * 1.15)), Math.Max(.42, Math.Min(.6, l)));
        }
        public static Color Shift(Color c, double deg)
        {
            double h, s, l; Hsl(c.R, c.G, c.B, out h, out s, out l);
            return FromHsl(h + deg, s, l);
        }
        public static Color Darken(Color c, double k) { return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)); }
        public static Color Mix(Color a, Color b, double k) { return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k)); }
        public static Color A(Color c, double a) { return Color.FromArgb((byte)Math.Max(0, Math.Min(255, a * 255)), c.R, c.G, c.B); }
    }

    // Огибающая громкости трека: считается один раз в фоне, чтобы фон «дышал» в такт
    public static class Beat
    {
        public const double Step = 0.05;
        static string curId;
        static float[] env;
        static int gen;
        public static bool Available = true;

        public static void Analyze(Track t, string file)
        {
            int my = ++gen;
            env = null; curId = t?.Id;
            if (t == null || string.IsNullOrEmpty(file) || !Library.D.BeatGlow || !Available) return;
            if (file.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
            var id = t.Id;
            Task.Run(() =>
            {
                try
                {
                    var e = Audio.Envelope(file, Step);
                    if (my == gen && e != null) env = e;
                }
                catch (Exception ex) { Log.W("beat: " + ex.Message); if (ex is System.IO.FileNotFoundException || ex is TypeLoadException) Available = false; }
            });
        }
        // 0..1 или -1, если анализ недоступен
        public static double Level(Track t, double pos)
        {
            var e = env;
            if (e == null || t == null || t.Id != curId || e.Length == 0) return -1;
            double x = pos / Step;
            int i = (int)x;
            if (i < 0) return 0;
            if (i >= e.Length - 1) return e[e.Length - 1];
            double k = x - i;
            return e[i] * (1 - k) + e[i + 1] * k;
        }
    }

    // «Живая вода»: мягкие пятна цвета обложки плавают, переливаются и светятся в такт
    public sealed class Liquid : Grid
    {
        readonly Canvas canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        readonly Rectangle baseRect = new Rectangle { IsHitTestVisible = false };
        readonly Rectangle pulse = new Rectangle { IsHitTestVisible = false, Opacity = 0 };
        readonly List<Blob> blobs = new List<Blob>();
        readonly DispatcherTimer timer;
        readonly bool big;
        readonly GradientStop baseA = new GradientStop(Colors.Black, 0), baseB = new GradientStop(Colors.Black, 1);
        readonly GradientStop pulseA = new GradientStop(Colors.Transparent, 0), pulseB = new GradientStop(Colors.Transparent, 1);
        double t, energy, slow;
        DateTime last = DateTime.Now;
        Color[] pal = Palette.Fallback;

        sealed class Blob
        {
            public Ellipse E; public GradientStop S0, S1; public TranslateTransform Tr; public ScaleTransform Sc;
            public double Fx, Fy, Px, Py, Ax, Ay, Size, Alpha; public int Ci;
        }

        public Liquid(bool bigMode)
        {
            big = bigMode;
            IsHitTestVisible = false;
            var lb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            lb.GradientStops.Add(baseA); lb.GradientStops.Add(baseB);
            baseRect.Fill = lb;
            var pb = new RadialGradientBrush { Center = new Point(.5, big ? .42 : .5), GradientOrigin = new Point(.5, big ? .42 : .5), RadiusX = big ? .75 : .7, RadiusY = big ? .6 : 1.4 };
            pb.GradientStops.Add(pulseA); pb.GradientStops.Add(pulseB);
            pulse.Fill = pb;
            Children.Add(baseRect);
            Children.Add(canvas);
            Children.Add(pulse);
            var rnd = new Random(big ? 7 : 3);
            int n = big ? 6 : 5;
            for (int i = 0; i < n; i++)
            {
                var b = new Blob
                {
                    S0 = new GradientStop(Colors.Transparent, 0),
                    S1 = new GradientStop(Colors.Transparent, 1),
                    Tr = new TranslateTransform(),
                    Sc = new ScaleTransform(1, 1),
                    Fx = .05 + rnd.NextDouble() * .09,
                    Fy = .04 + rnd.NextDouble() * .08,
                    Px = rnd.NextDouble() * 6.28,
                    Py = rnd.NextDouble() * 6.28,
                    Ax = .32 + rnd.NextDouble() * .2,
                    Ay = big ? .28 + rnd.NextDouble() * .18 : .5 + rnd.NextDouble() * .4,
                    Size = big ? .55 + rnd.NextDouble() * .35 : .34 + rnd.NextDouble() * .22,
                    Alpha = .55 + rnd.NextDouble() * .35,
                    Ci = i % 3 == 2 ? 0 : i % 3
                };
                var rb = new RadialGradientBrush();
                rb.GradientStops.Add(b.S0);
                rb.GradientStops.Add(new GradientStop(Colors.Transparent, .55));
                rb.GradientStops.Add(b.S1);
                // промежуточная точка — для мягкого «водяного» края
                rb.GradientStops[1].Color = Colors.Transparent;
                b.E = new Ellipse { Fill = rb, RenderTransformOrigin = new Point(.5, .5), IsHitTestVisible = false };
                var tg = new TransformGroup(); tg.Children.Add(b.Sc); tg.Children.Add(b.Tr);
                b.E.RenderTransform = tg;
                canvas.Children.Add(b.E);
                blobs.Add(b);
            }
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (s, e) => Frame();
            SizeChanged += (s, e) => Layout();
            IsVisibleChanged += (s, e) => Sync();
            Loaded += (s, e) => Sync();
            Unloaded += (s, e) => timer.Stop();
            SetColors(pal, false);
        }

        public void Sync()
        {
            bool on = IsVisible && Library.D.LiquidBg && !(Shell.I != null && Shell.I.ShotMode);
            if (on && !timer.IsEnabled) { last = DateTime.Now; timer.Start(); }
            else if (!on && timer.IsEnabled) timer.Stop();
            if (!on) { Frame(true); }
        }

        void Layout()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            foreach (var b in blobs)
            {
                double d = big ? Math.Max(w, h) * b.Size : w * b.Size;
                b.E.Width = d;
                b.E.Height = big ? d : Math.Max(h * 2.4, d * .55);
            }
            Frame(true);
        }

        public void SetColors(Color[] p, bool animate = true)
        {
            pal = p ?? Palette.Fallback;
            var dark = Palette.Darken(pal[2], big ? .55 : .7);
            var dark2 = Palette.Darken(pal[0], big ? .16 : .22);
            Anim(baseA, Color.FromArgb(255, dark.R, dark.G, dark.B), animate);
            Anim(baseB, Color.FromArgb(255, dark2.R, dark2.G, dark2.B), animate);
            Anim(pulseA, Palette.A(Palette.Mix(pal[0], Colors.White, .25), big ? .55 : .7), animate);
            Anim(pulseB, Palette.A(pal[0], 0), animate);
            foreach (var b in blobs)
            {
                var c = pal[b.Ci];
                Anim(b.S0, Palette.A(c, b.Alpha * (big ? .85 : .9)), animate);
                Anim(b.S1, Palette.A(c, 0), animate);
            }
        }
        static void Anim(GradientStop s, Color c, bool animate)
        {
            if (!animate) { s.BeginAnimation(GradientStop.ColorProperty, null); s.Color = c; return; }
            s.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(900)) { EasingFunction = new SineEase() });
        }

        void Frame(bool force = false)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var now = DateTime.Now;
            double dt = Math.Min(.1, (now - last).TotalSeconds);
            last = now;
            var win = Application.Current?.MainWindow;
            if (!force && win != null && win.WindowState == WindowState.Minimized) return;
            var p = Player.I;
            bool playing = p.IsPlaying && !p.Loading;
            // уровень громкости в такт (если анализ доступен), иначе — спокойное «дыхание»
            double target = 0;
            if (playing && Library.D.BeatGlow)
            {
                var lv = Beat.Level(p.Current, p.Position + .06);
                target = lv >= 0 ? lv : .18 + .12 * Math.Sin(t * 2.2);
            }
            energy += (target - energy) * (target > energy ? Math.Min(1, dt * 22) : Math.Min(1, dt * 4.5));
            slow += (energy - slow) * Math.Min(1, dt * 1.5);
            t += dt * (playing ? .9 + slow * 1.6 : .25);
            foreach (var b in blobs)
            {
                double x = w * (.5 + b.Ax * Math.Sin(t * b.Fx * 6.28 + b.Px));
                double y = h * (.5 + b.Ay * Math.Cos(t * b.Fy * 6.28 + b.Py));
                double wob = 1 + .07 * Math.Sin(t * 1.3 + b.Px);
                double sc = wob + energy * (big ? .16 : .22);
                b.Sc.ScaleX = sc * (1 + .05 * Math.Sin(t * .9 + b.Py));
                b.Sc.ScaleY = sc * (1 + .05 * Math.Cos(t * .8 + b.Px));
                b.Tr.X = x - b.E.Width / 2;
                b.Tr.Y = y - b.E.Height / 2;
                b.E.Opacity = Math.Min(1, .72 + slow * .25 + energy * .2);
            }
            pulse.Opacity = Math.Min(.85, energy * (big ? .55 : .75));
        }
    }
}
