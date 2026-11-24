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

        // палитра готова (обложка из интернета догрузилась) — плеер перекрашивается
        public static event Action<string> Ready;
        static readonly HashSet<string> pending = new HashSet<string>();

        public static Color[] Of(Track t)
        {
            if (t == null || string.IsNullOrEmpty(t.Cover)) return Fallback;
            var key = t.Id + ":" + t.Cover.Length;
            if (cache.TryGetValue(key, out var c0)) return c0;
            if (t.Cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // BitmapImage с адресом грузится асинхронно — пиксели ещё пустые. Качаем сами и считаем потом.
                if (pending.Add(key)) Fetch(t.Id, key, t.Cover);
                return Fallback;
            }
            Color[] res = Fallback;
            try { res = Compute(Img.FromData(t.Id, t.Cover, 64)) ?? Fallback; }
            catch (Exception ex) { Log.W("palette: " + ex.Message); }
            cache[key] = res;
            return res;
        }
        public static bool Has(Track t)
        {
            return t == null || string.IsNullOrEmpty(t.Cover) || cache.ContainsKey(t.Id + ":" + t.Cover.Length);
        }
        static async void Fetch(string id, string key, string url)
        {
            Color[] res = Fallback;
            try
            {
                var bytes = await Net.Http.GetByteArrayAsync(url);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.StreamSource = new System.IO.MemoryStream(bytes);
                bi.DecodePixelWidth = 64;
                bi.EndInit();
                bi.Freeze();
                res = Compute(bi) ?? Fallback;
            }
            catch (Exception ex) { Log.W("palette http: " + ex.Message); }
            cache[key] = res;
            pending.Remove(key);
            try { Application.Current?.Dispatcher.BeginInvoke(new Action(() => Ready?.Invoke(id))); } catch { }
        }

        static Color[] Compute(BitmapSource src)
        {
            if (src == null || src.PixelWidth < 2) return null;
            Color[] res = null;
            {
                {
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
                        double wt = (.12 + s) * (1 - Math.Abs(l - .5) * 1.1) + .01;
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
                        int third = order.Skip(1).FirstOrDefault(i => i != second && wsum[i] > wsum[order[0]] * .08 && Math.Min(Math.Abs(i - order[0]), Bins - Math.Abs(i - order[0])) >= 2);
                        var c3 = third != order[0] && third != second && wsum[third] > 0 ? col(third) : Shift(c1, -24);
                        res = new[] { Natural(c1), Natural(c2), Natural(c3) };
                    }
                    else if (an > 0)
                    {
                        // почти чёрно-белая обложка — её же нейтральный серый, без подмешивания синего
                        var g0 = Natural(Color.FromRgb((byte)(ar / an), (byte)(ag / an), (byte)(ab / an)));
                        res = new[] { g0, Mix(g0, Colors.White, .18), Mix(g0, Colors.Black, .35) };
                    }
                }
            }
            }
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
        // цвет обложки без «кислотности»: насыщенность почти как на обложке, яркость средняя
        public static Color Natural(Color c)
        {
            double h, s, l; Hsl(c.R, c.G, c.B, out h, out s, out l);
            return FromHsl(h, Math.Min(.9, s * 1.08), Math.Max(.32, Math.Min(.64, l)));
        }
        // заливка плеера: тот же оттенок обложки, заданная яркость (как у Spotify)
        public static Color Surface(Color c, double l)
        {
            double h, s, ll; Hsl(c.R, c.G, c.B, out h, out s, out ll);
            return FromHsl(h, Math.Min(.78, s * 1.05), l);
        }
        // светлый вариант для полосок и активных кнопок
        public static Color Light(Color c)
        {
            double h, s, l; Hsl(c.R, c.G, c.B, out h, out s, out l);
            return FromHsl(h, Math.Min(.9, Math.Max(.45, s)), .72);
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
        // тёмный, но цветной вариант для фона
        public static Color Deep(Color c, double l)
        {
            double h, s, ll; Hsl(c.R, c.G, c.B, out h, out s, out ll);
            return FromHsl(h, Math.Min(.85, s * .95), Math.Min(ll, l) * .62 + .03);
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

    // Живой фон в цветах обложки: плавные градиенты палитры медленно перетекают друг в друга (без «пятен»)
    public sealed class Liquid : Grid
    {
        readonly Rectangle baseRect = new Rectangle { IsHitTestVisible = false };
        readonly Rectangle flowRect = new Rectangle { IsHitTestVisible = false };
        readonly Rectangle shadeRect = new Rectangle { IsHitTestVisible = false };
        readonly Rectangle pulse = new Rectangle { IsHitTestVisible = false, Opacity = 0 };
        readonly LinearGradientBrush baseBrush = new LinearGradientBrush(), flowBrush = new LinearGradientBrush(), pulseBrush = new LinearGradientBrush();
        readonly GradientStop b0 = new GradientStop(Colors.Black, 0), b1 = new GradientStop(Colors.Black, .55), b2 = new GradientStop(Colors.Black, 1);
        readonly GradientStop f0 = new GradientStop(Colors.Transparent, 0), f1 = new GradientStop(Colors.Transparent, .5), f2 = new GradientStop(Colors.Transparent, 1);
        readonly GradientStop p0 = new GradientStop(Colors.Transparent, 0), p1 = new GradientStop(Colors.Transparent, 1);
        readonly DispatcherTimer timer;
        readonly bool big;
        double t, energy, slow;
        DateTime last = DateTime.Now;
        Color[] pal = Palette.Fallback;

        public Liquid(bool bigMode)
        {
            big = bigMode;
            IsHitTestVisible = false;
            baseBrush.GradientStops.Add(b0); baseBrush.GradientStops.Add(b1); baseBrush.GradientStops.Add(b2);
            flowBrush.GradientStops.Add(f0); flowBrush.GradientStops.Add(f1); flowBrush.GradientStops.Add(f2);
            pulseBrush.GradientStops.Add(p0); pulseBrush.GradientStops.Add(p1);
            pulseBrush.StartPoint = big ? new Point(.5, 0) : new Point(0, .5);
            pulseBrush.EndPoint = big ? new Point(.5, 1) : new Point(1, .5);
            baseRect.Fill = baseBrush;
            flowRect.Fill = flowBrush;
            pulse.Fill = pulseBrush;
            // мягкое затемнение для читаемости текста
            var sh = big
                ? new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(30, 0, 0, 0), .45), new GradientStop(Color.FromArgb(150, 0, 0, 0), 1) }, new Point(.5, 0), new Point(.5, 1))
                : new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(10, 0, 0, 0), 0), new GradientStop(Color.FromArgb(55, 0, 0, 0), 1) }, new Point(0, .5), new Point(1, .5));
            sh.Freeze();
            shadeRect.Fill = sh;
            Children.Add(baseRect);
            Children.Add(flowRect);
            Children.Add(pulse);
            Children.Add(shadeRect);
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += (s, e) => Frame();
            IsVisibleChanged += (s, e) => Sync();
            Loaded += (s, e) => Sync();
            Unloaded += (s, e) => timer.Stop();
            SetColors(pal, false);
            Frame(true);
        }

        public void Sync()
        {
            bool on = IsVisible && Library.D.LiquidBg && !(Shell.I != null && Shell.I.ShotMode);
            if (on && !timer.IsEnabled) { last = DateTime.Now; timer.Start(); }
            else if (!on && timer.IsEnabled) timer.Stop();
            if (!on) Frame(true);
        }

        public void SetColors(Color[] p, bool animate = true)
        {
            pal = p ?? Palette.Fallback;
            // фон — приглушённые цвета палитры, поверх — более яркий «перелив»
            Anim(b0, Palette.Surface(pal[0], big ? .40 : .34), animate);
            Anim(b1, Palette.Surface(pal[2], big ? .28 : .26), animate);
            Anim(b2, Palette.Surface(pal[1], big ? .33 : .30), animate);
            Anim(f0, Palette.A(Palette.Surface(pal[1], .46), big ? .45 : .5), animate);
            Anim(f1, Palette.A(pal[0], 0), animate);
            Anim(f2, Palette.A(Palette.Surface(pal[0], .48), big ? .40 : .46), animate);
            Anim(p0, Palette.A(Palette.Mix(pal[0], Colors.White, .2), big ? .32 : .4), animate);
            Anim(p1, Palette.A(pal[0], 0), animate);
        }
        static void Anim(GradientStop s, Color c, bool animate)
        {
            if (!animate) { s.BeginAnimation(GradientStop.ColorProperty, null); s.Color = c; return; }
            s.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(1200)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        static void Angle(LinearGradientBrush b, double a)
        {
            double c = Math.Cos(a) * .5, s = Math.Sin(a) * .5;
            b.StartPoint = new Point(.5 - c, .5 - s);
            b.EndPoint = new Point(.5 + c, .5 + s);
        }

        void Frame(bool force = false)
        {
            var now = DateTime.Now;
            double dt = Math.Min(.1, (now - last).TotalSeconds);
            last = now;
            var win = Application.Current?.MainWindow;
            if (!force && win != null && win.WindowState == WindowState.Minimized) return;
            var p = Player.I;
            bool playing = p != null && p.IsPlaying && !p.Loading;
            double target = 0;
            if (playing && Library.D.BeatGlow)
            {
                var lv = Beat.Level(p.Current, p.Position + .06);
                target = lv >= 0 ? lv : .14 + .08 * Math.Sin(t * 2.0);
            }
            energy += (target - energy) * (target > energy ? Math.Min(1, dt * 18) : Math.Min(1, dt * 4));
            slow += (energy - slow) * Math.Min(1, dt * 1.2);
            t += dt * (playing ? 1 + slow * 1.2 : .35);
            // медленное вращение двух слоёв навстречу друг другу — цвета перетекают
            Angle(baseBrush, .8 + t * .05);
            Angle(flowBrush, 2.4 - t * .07 + Math.Sin(t * .11) * .6);
            b1.Offset = .5 + .18 * Math.Sin(t * .09);
            f1.Offset = .5 + .22 * Math.Sin(t * .13 + 1);
            flowRect.Opacity = .75 + .25 * Math.Sin(t * .17);
            pulse.Opacity = Math.Min(.9, energy * (big ? .8 : 1));
        }
    }
}
