using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Sonora
{
    public static class Th
    {
        // Синяя палитра: старые фиолетовые/розовые/бирюзовые оттенки переводятся в синие
        static readonly Dictionary<string, string> Remap = new Dictionary<string, string>
        {
            ["8b5cf6"] = "3b82f6", ["ec4899"] = "1d4ed8", ["06b6d4"] = "60a5fa", ["a78bfa"] = "93c5fd",
            ["f472b6"] = "60a5fa", ["7c3aed"] = "2563eb", ["db2777"] = "1e40af", ["c084fc"] = "93c5fd",
            ["f59e0b"] = "0284c7", ["ef4444"] = "1e3a8a", ["10b981"] = "2563eb", ["6366f1"] = "3b82f6",
            ["14b8a6"] = "1d4ed8", ["1e1e2a"] = "1c2230", ["16161f"] = "161b26", ["0f0f1a"] = "121722",
            ["14141e"] = "161b26", ["0a0a12"] = "07090d", ["d946ef"] = "2563eb", ["a855f7"] = "3b82f6",
            ["f97316"] = "0369a1", ["e11d48"] = "1e40af", ["22c55e"] = "2563eb", ["eab308"] = "0284c7",
            // всё остальное цветное — тоже в синий
            ["ff4d94"] = "3b82f6", ["ff5fa2"] = "60a5fa", ["d6246e"] = "1d4ed8", ["c2185b"] = "1e40af",
            ["22d3ee"] = "60a5fa", ["38bdf8"] = "7ab0ff", ["0e4c5a"] = "1e3a8a", ["0ea5e9"] = "3b82f6",
            ["0d9488"] = "1d4ed8", ["0891b2"] = "2563eb", ["4f46e5"] = "3b82f6", ["0e7490"] = "1d4ed8"
        };
        public static Color C(string hex, double a = 1)
        {
            hex = hex.TrimStart('#').ToLowerInvariant();
            if (Remap.TryGetValue(hex, out var m)) hex = m;
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            return Color.FromArgb((byte)Math.Round(Math.Max(0, Math.Min(1, a)) * 255), r, g, b);
        }
        public static SolidColorBrush B(string hex, double a = 1) { var b = new SolidColorBrush(C(hex, a)); b.Freeze(); return b; }
        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static readonly SolidColorBrush
            Bg = B("#050608"), Panel = B("#101216"), Bg2 = B("#15171C"), Card = B("#1A1D23"), Card2 = B("#22262E"),
            Border = B("#FFFFFF", .06), BorderHi = B("#FFFFFF", .12), Txt = B("#F5F7FA"), Dim = B("#8C93A1"),
            Accent = B("#3B82F6"), Accent2 = B("#60A5FA"), Accent3 = B("#7AB0FF"), Soft = B("#3B82F6", .14),
            Heart = B("#5B9BFF"),
            Danger = B("#F43F5E"), Ok = B("#60A5FA"), White = B("#FFFFFF"), Clear = B("#000000", 0);
        public static LinearGradientBrush G(string a, string b, bool diagonal = true)
        {
            var g = new LinearGradientBrush(C(a), C(b), new Point(0, 0), diagonal ? new Point(1, 1) : new Point(0, 1));
            g.Freeze();
            return g;
        }
        public static LinearGradientBrush G(Color a, Color b, bool diagonal = true)
        {
            var g = new LinearGradientBrush(a, b, new Point(0, 0), diagonal ? new Point(1, 1) : new Point(0, 1));
            g.Freeze();
            return g;
        }
        public static LinearGradientBrush GH(string a, string b)
        {
            var g = new LinearGradientBrush(C(a), C(b), new Point(0, 0.5), new Point(1, 0.5));
            g.Freeze();
            return g;
        }
        public static readonly LinearGradientBrush Brand = G("#60A5FA", "#1D4ED8"), Hot = G("#60A5FA", "#1D4ED8"), Pink = G("#60A5FA", "#1D4ED8"), Ring = G("#93C5FD", "#1D4ED8");
        public static FontFamily LogoFont = new FontFamily("Segoe UI");
        // «стекло»: полупрозрачная заливка + светлая кромка сверху, гаснущая книзу
        public static readonly LinearGradientBrush GlassEdge = Edge(.16, .035), GlassEdgeSoft = Edge(.10, .02);
        public static readonly LinearGradientBrush GlassShine = Edge(.055, 0);
        static LinearGradientBrush Edge(double top, double bottom)
        {
            var g = new LinearGradientBrush(C("#FFFFFF", top), C("#FFFFFF", bottom), new Point(0, 0), new Point(0, 1));
            g.Freeze();
            return g;
        }
        public static SolidColorBrush Glass(double a = .06) { return B("#FFFFFF", a); }
        public static readonly FontFamily Body = new FontFamily("Segoe UI");
        public static FontFamily Font = new FontFamily("Segoe UI");

        public static void InitFont()
        {
            try
            {
                var dir = Paths.Sub("fonts");
                var file = System.IO.Path.Combine(dir, "Widock.otf");
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Widock.otf"))
                {
                    if (s == null) return;
                    if (!File.Exists(file) || new FileInfo(file).Length != s.Length)
                        using (var f = File.Create(file)) s.CopyTo(f);
                }
                LogoFont = new FontFamily(new Uri(dir.TrimEnd('\\') + "\\"), "./#Widock TRIAL, Segoe UI");
                Font = LogoFont;
            }
            catch (Exception ex) { Log.W("font: " + ex.Message); }
        }
        public static string Shade(string hex, int d)
        {
            var c = C(hex);
            Func<int, byte> f = v => (byte)Math.Max(0, Math.Min(255, v + d));
            return "#" + f(c.R).ToString("X2") + f(c.G).ToString("X2") + f(c.B).ToString("X2");
        }
    }

    public static class Img
    {
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();

        public static BitmapSource FromData(string key, string data, int px)
        {
            if (string.IsNullOrEmpty(data)) return null;
            px = Math.Max(32, px);
            var k = key + "|" + px;
            if (cache.TryGetValue(k, out var hit)) return hit;
            BitmapSource result = null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (data.StartsWith("http", StringComparison.OrdinalIgnoreCase)) bi.UriSource = new Uri(data);
                else
                {
                    int c = data.IndexOf(',');
                    var b64 = data.StartsWith("data:") && c > 0 ? data.Substring(c + 1) : data;
                    bi.StreamSource = new MemoryStream(Convert.FromBase64String(b64));
                }
                bi.DecodePixelWidth = px;
                bi.EndInit();
                if (bi.CanFreeze) bi.Freeze();
                result = bi;
            }
            catch (Exception ex) { Log.W("img " + key + ": " + ex.Message); }
            cache[k] = result;
            return result;
        }
        public static BitmapSource Res(string name)
        {
            if (cache.TryGetValue("res:" + name, out var hit)) return hit;
            BitmapSource r = null;
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = s; bi.EndInit(); bi.Freeze();
                    r = bi;
                }
            }
            catch (Exception ex) { Log.W("res: " + ex.Message); }
            cache["res:" + name] = r;
            return r;
        }
        public static Color Dominant(Track t)
        {
            var fallback = Color.FromRgb(59, 130, 246);
            if (t == null || string.IsNullOrEmpty(t.Cover)) return fallback;
            if (colors.TryGetValue(t.Id, out var c0)) return c0;
            var res = fallback;
            try
            {
                var src = FromData(t.Id, t.Cover, 64);
                if (src != null)
                {
                    var scaled = new TransformedBitmap(src, new ScaleTransform(40.0 / src.PixelWidth, 40.0 / src.PixelHeight));
                    var bmp = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                    int w = bmp.PixelWidth, h = bmp.PixelHeight;
                    var px = new byte[w * h * 4];
                    bmp.CopyPixels(px, w * 4, 0);
                    var list = new List<Tuple<int, int, int, double>>();
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        int b = px[i], g = px[i + 1], r = px[i + 2];
                        double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
                        if (lum < .15 || lum > .9) continue;
                        int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                        list.Add(Tuple.Create(r, g, b, mx == 0 ? 0 : (mx - mn) / (double)mx));
                    }
                    if (list.Count > 0)
                    {
                        var top = list.OrderByDescending(x => x.Item4).Take(Math.Max(1, list.Count * 3 / 10)).ToList();
                        res = Color.FromRgb((byte)top.Average(x => x.Item1), (byte)top.Average(x => x.Item2), (byte)top.Average(x => x.Item3));
                    }
                }
            }
            catch (Exception ex) { Log.W("color: " + ex.Message); }
            colors[t.Id] = res;
            return res;
        }
        public static Brush PlayerGradient(Track t)
        {
            // единая синяя палитра вместо цвета обложки
            return Th.G(Th.C("#1E3A8A"), Th.C("#0B1022"), false);
        }
    }

    public static class U
    {
        static readonly Dictionary<string, Geometry> geos = new Dictionary<string, Geometry>();
        public static Geometry Geo(string d)
        {
            if (geos.TryGetValue(d, out var g)) return g;
            try { g = Geometry.Parse(d); g.Freeze(); }
            catch (Exception ex) { Log.W("geo: " + ex.Message + " " + d); g = Geometry.Empty; }
            geos[d] = g;
            return g;
        }

        public static FrameworkElement Ico(string name, double size, Brush brush, bool? fill = null, double stroke = 2)
        {
            Ico ic;
            if (!Icons.All.TryGetValue(name, out ic)) ic = Icons.All["music"];
            var p = new System.Windows.Shapes.Path { Data = Geo(ic.D), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            if (ic.Fill) p.Fill = brush;
            else
            {
                p.Stroke = brush;
                p.StrokeThickness = stroke;
                if (fill == true) p.Fill = brush;
            }
            var g = new Grid { Width = 24, Height = 24 };
            g.Children.Add(p);
            return new Viewbox { Width = size, Height = size, Child = g, IsHitTestVisible = false, Stretch = Stretch.Uniform };
        }
        public static FrameworkElement GoogleIco(double size)
        {
            var g = new Grid { Width = 24, Height = 24 };
            foreach (var part in Icons.Google)
                g.Children.Add(new System.Windows.Shapes.Path { Data = Geo(part[1]), Fill = Th.B(part[0]) });
            return new Viewbox { Width = size, Height = size, Child = g, IsHitTestVisible = false };
        }

        public static TextBlock Tx(string s, double size, Brush fg = null, double opacity = 1, bool trim = true)
        {
            var t = new TextBlock { Text = s ?? "", FontSize = size, Foreground = fg ?? Th.Txt, Opacity = opacity, FontFamily = Th.Font };
            bool dim = fg == Th.Dim || opacity < 1;
            // фирменный шрифт Widock — для заголовков, названий и навигации; мелкий служебный текст — Segoe UI
            // Widock — капсовый акцидентный шрифт: только для крупных заголовков; остальной текст — Segoe UI
            if (dim || size < 17) { t.FontFamily = Th.Body; t.FontWeight = dim ? (size >= 13 ? FontWeights.Medium : FontWeights.Normal) : size >= 12.5 ? FontWeights.SemiBold : FontWeights.Normal; }
            else t.FontWeight = FontWeights.Bold;
            if (trim) { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }
            return t;
        }
        public static TextBlock Wrap(string s, double size, Brush fg = null, int maxLines = 0, double lineHeight = 0)
        {
            var t = Tx(s, size, fg, 1, false);
            t.TextWrapping = TextWrapping.Wrap;
            var lh = lineHeight > 0 ? lineHeight : Math.Round(size * 1.35, 1);
            t.LineHeight = lh;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            if (maxLines > 0) { t.MaxHeight = lh * maxLines + 1; t.TextTrimming = TextTrimming.CharacterEllipsis; }
            return t;
        }
        public static T M<T>(T el, double l, double t, double r, double b) where T : FrameworkElement { el.Margin = new Thickness(l, t, r, b); return el; }

        public static DropShadowEffect Shadow(double blur, double depth, double opacity, Color? color = null)
        {
            if (color != null || blur < 36) return null; // минимализм: без цветных свечений и мелких теней
            var e = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Direction = 270, Color = color ?? Colors.Black, RenderingBias = RenderingBias.Performance };
            e.Freeze();
            return e;
        }

        static void Scale(FrameworkElement el, double v)
        {
            if (!(el.RenderTransform is ScaleTransform st) || st.IsFrozen) { st = new ScaleTransform(1, 1); el.RenderTransform = st; el.RenderTransformOrigin = new Point(.5, .5); }
            var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(110)) { EasingFunction = new QuadraticEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        public static T Click<T>(T el, Action action, double pressScale = .96) where T : FrameworkElement
        {
            el.Cursor = Cursors.Hand;
            if (el is Border bd && bd.Background == null) bd.Background = Th.Clear;
            if (el is Panel pn && pn.Background == null) pn.Background = Th.Clear;
            bool down = false;
            el.MouseLeftButtonDown += (s, e) => { down = true; if (pressScale < 1) Scale(el, pressScale); e.Handled = true; };
            el.MouseLeftButtonUp += (s, e) =>
            {
                if (pressScale < 1) Scale(el, 1);
                if (!down) return;
                down = false;
                e.Handled = true;
                try { action(); } catch (Exception ex) { Log.W("click: " + ex); Shell.Toast("Ошибка: " + ex.Message); }
            };
            el.MouseLeave += (s, e) => { if (down && pressScale < 1) Scale(el, 1); down = false; };
            return el;
        }
        public static T Hover<T>(T el, Brush normal, Brush hover) where T : Border
        {
            el.Background = normal ?? Th.Clear;
            el.MouseEnter += (s, e) => el.Background = hover;
            el.MouseLeave += (s, e) => el.Background = normal ?? Th.Clear;
            return el;
        }
        public static void Fade(UIElement el, double from, double to, int ms = 220)
        {
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        public static void ClipRound(FrameworkElement el, double r)
        {
            el.SizeChanged += (s, e) => el.Clip = new RectangleGeometry(new Rect(0, 0, el.ActualWidth, el.ActualHeight), r, r);
        }
        public static Rect Fixed(FrameworkElement el, double w, double h, double r)
        {
            var rect = new Rect(0, 0, w, h);
            el.Clip = new RectangleGeometry(rect, r, r);
            return rect;
        }

        public static Brush CoverBrush(Track t, int px)
        {
            var img = t == null ? null : Img.FromData(t.Id, t.Cover, px);
            if (img == null) return null;
            var b = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            if (b.CanFreeze) b.Freeze();
            return b;
        }
        public static Border Cover(Track t, double size, double radius, double iconSize, Brush placeholder = null, string icon = "music")
        {
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius) };
            var br = CoverBrush(t, (int)Math.Min(600, size * 2));
            if (br != null) b.Background = br;
            else
            {
                b.Background = placeholder ?? Th.Hot;
                b.Child = new Grid { Children = { Center(Ico(icon, iconSize, Th.White)) } };
            }
            return b;
        }
        public static T Center<T>(T el) where T : FrameworkElement { el.HorizontalAlignment = HorizontalAlignment.Center; el.VerticalAlignment = VerticalAlignment.Center; return el; }

        public static Border Circle(double size, Brush bg, UIElement child = null, Brush border = null, double bt = 0)
        {
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = bg ?? Th.Clear, BorderBrush = border, BorderThickness = new Thickness(bt), Child = child };
        }
        public static Border IconBtn(string icon, double size, double iconSize, Brush fg, Action a, Brush bg = null, bool? fill = null)
        {
            var b = Circle(size, bg, Center(Ico(icon, iconSize, fg, fill)));
            return Click(b, a, .88);
        }

        public static StackPanel Row(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }
        public static StackPanel Col(params UIElement[] items)
        {
            var sp = new StackPanel();
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }
        public static Grid Cols(string spec, params UIElement[] items)
        {
            var g = new Grid();
            var parts = spec.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                GridLength len = p == "*" ? new GridLength(1, GridUnitType.Star) : p == "a" ? GridLength.Auto : p.EndsWith("*") ? new GridLength(double.Parse(p.TrimEnd('*'), CultureInfo.InvariantCulture), GridUnitType.Star) : new GridLength(double.Parse(p, CultureInfo.InvariantCulture));
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = len });
            }
            for (int i = 0; i < items.Length && i < parts.Length; i++)
            {
                if (items[i] == null) continue;
                Grid.SetColumn(items[i], i);
                g.Children.Add(items[i]);
            }
            return g;
        }

        public static string Plural(int n, string one, string few, string many)
        {
            int a = Math.Abs(n) % 100, b = a % 10;
            if (a > 10 && a < 20) return many;
            if (b > 1 && b < 5) return few;
            if (b == 1) return one;
            return many;
        }
        public static string Tn(int n) { return n + " " + Plural(n, "трек", "трека", "треков"); }
        public static string Time(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            var t = TimeSpan.FromSeconds(sec);
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }
    }

    public sealed class Input : Border
    {
        readonly TextBox tb;
        readonly PasswordBox pb;
        readonly TextBlock ph;
        public event Action Changed, Enter;

        public Input(string placeholder, bool password = false, string icon = null)
        {
            CornerRadius = new CornerRadius(12);
            Background = Th.Card;
            BorderBrush = Th.Border;
            BorderThickness = new Thickness(1);
            Padding = new Thickness(14, 11, 14, 11);
            Margin = new Thickness(0, 0, 0, 10);
            var g = new Grid();
            ph = U.Tx(placeholder, 14, Th.Dim, .75);
            ph.IsHitTestVisible = false;
            ph.VerticalAlignment = VerticalAlignment.Center;
            Control box;
            if (password)
            {
                pb = new PasswordBox();
                pb.PasswordChanged += (s, e) => { Sync(); Changed?.Invoke(); };
                box = pb;
            }
            else
            {
                tb = new TextBox { TextWrapping = TextWrapping.NoWrap, AcceptsReturn = false };
                tb.TextChanged += (s, e) => { Sync(); Changed?.Invoke(); };
                box = tb;
            }
            box.Background = Th.Clear;
            box.BorderThickness = new Thickness(0);
            box.Foreground = Th.Txt;
            box.FontFamily = Th.Body;
            box.FontSize = 14;
            box.Padding = new Thickness(0);
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.FocusVisualStyle = null;
            if (tb != null) { tb.CaretBrush = Th.Txt; tb.SelectionBrush = Th.Accent; }
            else { pb.CaretBrush = Th.Txt; pb.SelectionBrush = Th.Accent; }
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) Enter?.Invoke(); };
            box.GotKeyboardFocus += (s, e) => { BorderBrush = Th.Accent; Background = Th.Card2; };
            box.LostKeyboardFocus += (s, e) => { BorderBrush = Th.Border; Background = Th.Card; };
            g.Children.Add(ph);
            g.Children.Add(box);
            if (icon != null)
            {
                var ic = U.Ico(icon, 18, Th.Dim);
                ic.VerticalAlignment = VerticalAlignment.Center;
                g = U.Cols("a *", ic, g);
                ((FrameworkElement)g.Children[1]).Margin = new Thickness(10, 0, 0, 0);
            }
            Child = g;
            MouseLeftButtonDown += (s, e) => Focus2();
        }
        void Sync() { ph.Visibility = string.IsNullOrEmpty(Text) ? Visibility.Visible : Visibility.Collapsed; }
        public string Text
        {
            get { return tb != null ? tb.Text : pb.Password; }
            set { if (tb != null) tb.Text = value ?? ""; else pb.Password = value ?? ""; Sync(); }
        }
        public void Focus2()
        {
            if (tb != null) { tb.Focus(); tb.CaretIndex = tb.Text.Length; }
            else pb.Focus();
        }
    }
}
