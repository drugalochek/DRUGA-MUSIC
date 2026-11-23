using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Sonora
{
    // Полноэкранный плеер — повторяет .fs из Sonic Wave
    public sealed class FullPlayer : Grid
    {
        readonly Border bg = new Border();
        readonly TranslateTransform move = new TranslateTransform();
        readonly TextBlock headSub, headTitle, title, artist, cur, dur, timerText;
        readonly Border cover, sidePrev, sideNext, favBtn, playBtn, repeatBtn, shuffleBtn, timerBtn, lyricsBtn, queueBtn, timerInfo;
        readonly Grid seek;
        readonly Border seekFill, seekThumb;
        readonly TextBlock repeatBadge;
        bool dragging;
        readonly Liquid liquid;
        Border dlBtn;
        string colorId;
        public bool IsOpen { get; private set; }
        public void SyncLiquid() { liquid.Sync(); }

        List<KeyValuePair<double, string>> lyr;
        List<Border> lyrLines;
        ScrollViewer lyrScroll;
        int lyrActive = -1;

        public FullPlayer()
        {
            Visibility = Visibility.Collapsed;
            RenderTransform = move;
            ClipToBounds = true;
            Children.Add(bg);
            liquid = new Liquid(true);
            Children.Add(liquid);
            Children.Add(new Rectangle { Fill = Th.G(Th.C("#000000", .22), Th.C("#000000", .55), false), IsHitTestVisible = false });
            TextElement.SetForeground(this, Th.White);

            var col = new Grid { MaxWidth = 520, Margin = new Thickness(20, 10, 20, 16) };
            for (int i = 0; i < 9; i++) col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            col.RowDefinitions[7].Height = new GridLength(.15, GridUnitType.Star);
            Children.Add(col);

            var back = U.IconBtn("chevD", 40, 22, Th.White, Close);
            headSub = U.Tx("СЕЙЧАС ИГРАЕТ", 10, Th.White, .75);
            headSub.HorizontalAlignment = HorizontalAlignment.Center;
            headTitle = U.Tx("", 13, Th.White);
            headTitle.HorizontalAlignment = HorizontalAlignment.Center;
            headTitle.Margin = new Thickness(0, 2, 0, 0);
            var ht = U.Col(headSub, headTitle);
            ht.VerticalAlignment = VerticalAlignment.Center;
            var head = U.Cols("40 * 40", back, ht, new Border());
            head.Margin = new Thickness(0, 0, 0, 12);
            Add(col, head, 0);

            sidePrev = new Border { Width = 40, CornerRadius = new CornerRadius(10), Background = Th.B("#000000", .35), Opacity = .5, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            sideNext = new Border { Width = 40, CornerRadius = new CornerRadius(10), Background = Th.B("#000000", .35), Opacity = .5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cover = new Border { Width = 300, Height = 300, CornerRadius = new CornerRadius(14), Background = Th.B("#000000", .3), Effect = U.Shadow(60, 24, .7) };
            U.Click(sidePrev, () => Player.I.Prev(), .96);
            U.Click(sideNext, () => Player.I.Next(), .96);
            var covRow = new Grid { Margin = new Thickness(0, 0, 0, 18), MinHeight = 160 };
            covRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            covRow.ColumnDefinitions.Add(new ColumnDefinition());
            covRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var covInner = new Grid { Width = 330, Height = 330 };
            covInner.Children.Add(cover);
            var covBox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxWidth = 360, MaxHeight = 360, Child = covInner };
            Grid.SetColumn(sidePrev, 0); Grid.SetColumn(covBox, 1); Grid.SetColumn(sideNext, 2);
            covRow.Children.Add(sidePrev); covRow.Children.Add(covBox); covRow.Children.Add(sideNext);
            covBox.SizeChanged += (s, e) => { var h = covBox.ActualHeight * 300 / 330 * .8; sidePrev.Height = sideNext.Height = Math.Max(60, h); };
            Add(col, covRow, 1);

            var share = Act("share", () => { var t = Player.I.Current; if (t != null) Shell.CopyTrack(t); }, false);
            var menu = Act("dots", () => { var t = Player.I.Current; if (t != null) Shell.I.TrackMenu(t); }, true);
            favBtn = Act("heart", () => { var t = Player.I.Current; if (t == null) return; Library.ToggleFav(t.Id); Update(); }, false);
            dlBtn = Act("download", () =>
            {
                var t = Player.I.Current;
                if (t == null) return;
                if (!Guard.Need("Скачивание треков")) return;
                if (!Offline.CanDownload(t)) { Shell.Toast("Скачать можно только треки каталога SONORA"); return; }
                if (Offline.Has(t.Id)) { Shell.Toast("Трек уже скачан — играет без интернета"); return; }
                var _ = Offline.Download(t);
            }, false);
            Offline.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateDl));
            Library.Changed += UpdateDl;
            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            acts.Children.Add(dlBtn); acts.Children.Add(U.M(share, 14, 0, 0, 0)); acts.Children.Add(U.M(menu, 30, 0, 30, 0)); acts.Children.Add(favBtn);
            Add(col, acts, 2);

            cur = U.Tx("0:00", 11, Th.White, .85); cur.MinWidth = 34; cur.TextAlignment = TextAlignment.Center; cur.VerticalAlignment = VerticalAlignment.Center;
            dur = U.Tx("0:00", 11, Th.White, .85); dur.MinWidth = 34; dur.TextAlignment = TextAlignment.Center; dur.VerticalAlignment = VerticalAlignment.Center;
            seek = new Grid { Height = 24, Background = Th.Clear, Cursor = Cursors.Hand, Margin = new Thickness(10, 0, 10, 0) };
            var trackLine = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = Th.B("#FFFFFF", .3), VerticalAlignment = VerticalAlignment.Center };
            seekFill = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = Th.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
            seekThumb = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Th.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Effect = U.Shadow(8, 2, .4), Opacity = 0, IsHitTestVisible = false };
            seek.Children.Add(trackLine); seek.Children.Add(seekFill); seek.Children.Add(seekThumb);
            seek.MouseEnter += (s, e) => seekThumb.Opacity = 1;
            seek.MouseLeave += (s, e) => { if (!dragging) seekThumb.Opacity = 0; };
            seek.MouseLeftButtonDown += (s, e) => { dragging = true; seek.CaptureMouse(); SeekAt(e.GetPosition(seek).X, false); e.Handled = true; };
            seek.MouseMove += (s, e) => { if (dragging) SeekAt(e.GetPosition(seek).X, false); };
            seek.MouseLeftButtonUp += (s, e) => { if (!dragging) return; dragging = false; seek.ReleaseMouseCapture(); SeekAt(e.GetPosition(seek).X, true); if (!seek.IsMouseOver) seekThumb.Opacity = 0; };
            var seekRow = U.Cols("a * a", cur, seek, dur);
            seekRow.Margin = new Thickness(0, 0, 0, 14);
            Add(col, seekRow, 3);

            title = U.Tx("", 21, Th.White); title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 0, 0, 5);
            artist = U.Tx("", 13.5, Th.White, .85); artist.HorizontalAlignment = HorizontalAlignment.Center; artist.Cursor = Cursors.Hand;
            artist.MouseLeftButtonUp += (s, e) => { var t = Player.I.Current; if (t != null && !t.IsLocal) Shell.I.Go("artist", t.Artist); };
            var info = U.Col(title, artist);
            info.Margin = new Thickness(8, 0, 8, 18);
            Add(col, info, 4);

            repeatBadge = U.Tx("1", 9, Th.White); repeatBadge.HorizontalAlignment = HorizontalAlignment.Center; repeatBadge.VerticalAlignment = VerticalAlignment.Center; repeatBadge.IsHitTestVisible = false;
            repeatBtn = Ctrl("repeat", 44, 22, () => Player.I.CycleRepeat());
            var prev = Ctrl("prev", 52, 32, () => Player.I.Prev());
            playBtn = U.Click(new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Background = Th.White, Effect = U.Shadow(30, 14, .5) }, () => Player.I.Toggle(), .93);
            var next = Ctrl("next", 52, 32, () => Player.I.Next());
            shuffleBtn = Ctrl("shuffle", 44, 22, () => Player.I.ToggleShuffle());
            var ctrl = new Grid { Margin = new Thickness(8, 0, 8, 16) };
            ctrl.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) ctrl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ctrl.ColumnDefinitions.Add(new ColumnDefinition());
            var items = new FrameworkElement[] { repeatBtn, U.M(prev, 14, 0, 14, 0), playBtn, U.M(next, 14, 0, 14, 0), shuffleBtn };
            for (int i = 0; i < items.Length; i++) { items[i].HorizontalAlignment = HorizontalAlignment.Center; items[i].VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(items[i], i); ctrl.Children.Add(items[i]); }
            Add(col, ctrl, 5);

            timerBtn = Ctrl("timer", 48, 22, () => Shell.I.TimerSheet());
            lyricsBtn = Ctrl("lyrics", 48, 22, OpenLyrics);
            queueBtn = Ctrl("queue", 48, 22, () => Shell.I.QueueSheet());
            var bottom = new EvenRow(timerBtn, lyricsBtn, queueBtn) { Margin = new Thickness(40, 4, 40, 0), Opacity = .9 };
            Add(col, bottom, 8);

            timerText = U.Tx("", 11.5, Th.White);
            timerText.Margin = new Thickness(6, 0, 0, 0);
            timerInfo = new Border
            {
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(14),
                Background = Th.B("#000000", .5),
                BorderBrush = Th.B("#FFFFFF", .15),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 60, 20, 0),
                Child = U.Row(U.Ico("timer", 13, Th.White), timerText),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            Children.Add(timerInfo);

            PreviewMouseWheel += (s, e) =>
            {
                if (Shell.I.SheetOpen) return;
                Player.I.Volume += e.Delta > 0 ? .05 : -.05;
                Shell.Toast("Громкость: " + Math.Round(Player.I.Volume * 100) + "%");
            };
        }

        static void Add(Grid g, UIElement el, int row) { Grid.SetRow(el, row); g.Children.Add(el); }
        void UpdateDl()
        {
            var t = Player.I.Current;
            if (dlBtn == null || t == null) return;
            bool has = Offline.Has(t.Id), busy = Offline.Busy(t.Id);
            dlBtn.Child = U.Center(busy ? (FrameworkElement)U.Tx("···", 16, Th.White) : U.Ico(has ? "check" : "download", 21, has ? Th.Accent3 : Th.White));
            dlBtn.Opacity = Offline.CanDownload(t) ? 1 : .35;
            dlBtn.ToolTip = L.T(has ? "Скачано — играет без интернета" : "Скачать для офлайна");
        }

        static Border Act(string icon, Action a, bool big)
        {
            var b = new Border { Width = big ? 52 : 50, Height = big ? 52 : 50, CornerRadius = new CornerRadius(26), Background = Th.Clear };
            if (big) { b.BorderBrush = Th.B("#FFFFFF", .4); b.BorderThickness = new Thickness(1.5); }
            b.Child = U.Center(U.Ico(icon, 21, Th.White));
            U.Hover(b, Th.Clear, Th.B("#FFFFFF", .08));
            return U.Click(b, a, .9);
        }
        static Border Ctrl(string icon, double size, double iconSize, Action a)
        {
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Th.Clear, Child = U.Center(U.Ico(icon, iconSize, Th.White)) };
            U.Hover(b, Th.Clear, Th.B("#FFFFFF", .08));
            return U.Click(b, a, .9);
        }
        static void SetActive(Border b, string icon, double iconSize, bool on, Brush color = null, UIElement extra = null)
        {
            var g = new Grid();
            g.Children.Add(U.Center(U.Ico(icon, iconSize, color ?? Th.White)));
            if (extra != null) g.Children.Add(extra);
            if (on) g.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = color ?? Th.White, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) });
            b.Child = g;
        }

        void SeekAt(double x, bool commit)
        {
            var len = Player.I.Length;
            var w = seek.ActualWidth;
            if (len <= 0 || w <= 0) return;
            var r = Math.Max(0, Math.Min(1, x / w));
            SetProgress(r, w);
            cur.Text = U.Time(r * len);
            if (commit) Player.I.Seek(r * len);
        }
        void SetProgress(double r, double w)
        {
            seekFill.Width = Math.Max(0, r * w);
            seekThumb.Margin = new Thickness(Math.Max(0, r * w - 6), 0, 0, 0);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Update();
            Visibility = Visibility.Visible;
            if (Shell.I != null && Shell.I.ShotMode) { move.BeginAnimation(TranslateTransform.YProperty, null); move.Y = 0; return; }
            var h = Shell.I != null && Shell.I.ActualHeight > 0 ? Shell.I.ActualHeight : 800;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(h, 0, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (Shell.I != null && Shell.I.ShotMode) { Visibility = Visibility.Collapsed; return; }
            var h = Shell.I != null && Shell.I.ActualHeight > 0 ? Shell.I.ActualHeight : 800;
            var an = new DoubleAnimation(0, h, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            an.Completed += (s, e) => { if (!IsOpen) Visibility = Visibility.Collapsed; };
            move.BeginAnimation(TranslateTransform.YProperty, an);
        }

        public void Update()
        {
            var t = Player.I.Current;
            if (t == null) return;
            if (colorId != t.Id)
            {
                colorId = t.Id;
                bg.Background = Img.PlayerGradient(t);
                liquid.SetColors(Palette.Of(t), IsOpen && !(Shell.I != null && Shell.I.ShotMode));
            }
            UpdateDl();
            headTitle.Text = string.IsNullOrEmpty(t.Album) ? (t.Artist ?? "—") : t.Album;
            title.Text = t.Title;
            artist.Text = string.IsNullOrEmpty(t.Album) ? t.Artist : t.Artist + " — " + t.Album;
            var br = U.CoverBrush(t, 600);
            cover.Background = br ?? (Brush)Th.B("#000000", .3);
            cover.Child = br == null ? U.Center(U.Ico("music", 72, Th.B("#FFFFFF", .5), null, 1.6)) : null;

            var q = Player.I.Queue;
            Track p = null, n = null;
            if (q.Count > 1)
            {
                p = q[(Player.I.Index - 1 + q.Count) % q.Count];
                n = q[(Player.I.Index + 1) % q.Count];
            }
            sidePrev.Background = U.CoverBrush(p, 120) ?? (Brush)Th.B("#000000", .35);
            sideNext.Background = U.CoverBrush(n, 120) ?? (Brush)Th.B("#000000", .35);
            sidePrev.Visibility = sideNext.Visibility = q.Count > 1 ? Visibility.Visible : Visibility.Hidden;

            bool fav = Library.IsFav(t.Id);
            favBtn.Child = U.Center(U.Ico("heart", 21, fav ? Th.Heart : Th.White, fav));
            var black = Th.B("#000000");
            playBtn.Child = U.Center(Player.I.Loading ? (FrameworkElement)U.Tx("···", 18, black) : U.Ico(Player.I.IsPlaying ? "pause" : "play", 32, black));

            repeatBadge.Visibility = Player.I.Repeat == 2 ? Visibility.Visible : Visibility.Collapsed;
            SetActive(repeatBtn, "repeat", 22, Player.I.Repeat > 0, null, Player.I.Repeat == 2 ? repeatBadge : null);
            SetActive(shuffleBtn, "shuffle", 22, Player.I.Shuffle);
            SetActive(timerBtn, "timer", 22, Player.I.SleepAt.HasValue, Player.I.SleepAt.HasValue ? Th.Accent3 : null);
            SetActive(lyricsBtn, "lyrics", 22, false, t.HasLyrics ? null : Th.B("#FFFFFF", .35));
            SetActive(queueBtn, "queue", 22, false);
            dur.Text = U.Time(Player.I.Length);
            if (lyrLines != null && lyrTrack != t.Id) FillLyrics();
            Tick();
        }

        public void Tick()
        {
            if (Player.I.SleepAt.HasValue)
            {
                var left = Player.I.SleepAt.Value - DateTime.Now;
                if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                timerText.Text = ((int)left.TotalMinutes) + ":" + left.Seconds.ToString("00");
                timerInfo.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
            }
            else timerInfo.Visibility = Visibility.Collapsed;

            if (lyrLines != null) Karaoke(Player.I.Position);
            if (!IsOpen || dragging) return;
            var len = Player.I.Length;
            var pos = Player.I.Position;
            cur.Text = U.Time(pos);
            dur.Text = U.Time(len);
            SetProgress(len > 0 ? Math.Min(1, pos / len) : 0, seek.ActualWidth);
        }

        // ---------- Текст песни (караоке) ----------
        string lyrTrack;
        StackPanel lyrBody;
        TextBlock lyrTitle, lyrArtist;
        public void OpenLyrics()
        {
            var t = Player.I.Current;
            if (t == null) return;
            if (!t.HasLyrics) { Shell.Toast("У этого трека нет текста"); return; }
            lyrTitle = U.Tx("", 18); lyrTitle.HorizontalAlignment = HorizontalAlignment.Center; lyrTitle.Margin = new Thickness(0, 0, 0, 4);
            lyrArtist = U.Tx("", 12.5, Th.Dim); lyrArtist.HorizontalAlignment = HorizontalAlignment.Center;
            var head = new Border { Padding = new Thickness(10, 4, 10, 14), BorderBrush = Th.Border, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 14), Child = U.Col(lyrTitle, lyrArtist) };
            lyrBody = new StackPanel { Margin = new Thickness(4, 12, 4, 40) };
            lyrScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lyrBody, Focusable = false };
            lyrScroll.MaxHeight = Math.Max(260, (Shell.I.ActualHeight > 0 ? Shell.I.ActualHeight : 800) * .6);
            lyrLines = new List<Border>();
            FillLyrics();
            Shell.I.OpenSheet(null, null, U.Col(head, lyrScroll), () => { lyrLines = null; lyrScroll = null; lyrBody = null; lyrTrack = null; });
        }
        void FillLyrics()
        {
            var t = Player.I.Current;
            if (t == null || lyrBody == null) return;
            lyrTrack = t.Id;
            lyrTitle.Text = t.Title;
            lyrArtist.Text = string.IsNullOrEmpty(t.Album) ? t.Artist : t.Artist + " — " + t.Album;
            lyrBody.Children.Clear();
            lyrLines.Clear();
            lyrActive = -1;
            lyr = Shell.ParseLrc(t.Lyrics);
            bool synced = lyr.Count > 0;
            IEnumerable<string> lines = synced ? lyr.Select(x => x.Value) : (t.Lyrics ?? "").Replace("\r", "").Split('\n');
            if (!t.HasLyrics) lines = new[] { "—" };
            foreach (var ln in lines)
            {
                var tb = U.Wrap(string.IsNullOrWhiteSpace(ln) ? " " : ln, synced ? 15 : 14.5, synced ? Th.Dim : Th.Txt, 0, synced ? 27 : 24.6);
                tb.TextAlignment = synced ? TextAlignment.Center : TextAlignment.Left;
                var b = new Border { Padding = synced ? new Thickness(8, 5, 8, 5) : new Thickness(0, 2, 0, 2), CornerRadius = new CornerRadius(8), Child = tb, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
                if (synced)
                {
                    int idx = lyrLines.Count;
                    U.Click(b, () => { if (lyr != null && idx < lyr.Count) Player.I.Seek(lyr[idx].Key); }, 1);
                }
                lyrLines.Add(b);
                lyrBody.Children.Add(b);
            }
            if (!synced) lyr = null;
            else Karaoke(Player.I.Position);
        }
        void Karaoke(double pos)
        {
            if (lyr == null || lyrLines == null || lyrLines.Count != lyr.Count) return;
            int idx = -1;
            for (int i = 0; i < lyr.Count; i++) { if (lyr[i].Key <= pos + .25) idx = i; else break; }
            if (idx == lyrActive) return;
            if (lyrActive >= 0 && lyrActive < lyrLines.Count) Mark(lyrLines[lyrActive], false);
            lyrActive = idx;
            if (idx < 0) return;
            var el = lyrLines[idx];
            Mark(el, true);
            if (lyrScroll != null && el.IsLoaded)
            {
                try
                {
                    var p = el.TransformToAncestor(lyrBody).Transform(new Point(0, 0));
                    lyrScroll.ScrollToVerticalOffset(Math.Max(0, p.Y - lyrScroll.ViewportHeight / 2 + el.ActualHeight / 2));
                }
                catch { }
            }
        }
        static void Mark(Border b, bool on)
        {
            b.Background = on ? Th.B("#8B5CF6", .08) : null;
            ((TextBlock)b.Child).Foreground = on ? Th.Txt : Th.Dim;
            var sc = (ScaleTransform)b.RenderTransform;
            sc.ScaleX = sc.ScaleY = on ? 1.04 : 1;
        }
    }

    // Ряд из равных ячеек (аналог justify-content:space-around)
    public sealed class EvenRow : Grid
    {
        public EvenRow(params FrameworkElement[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                ColumnDefinitions.Add(new ColumnDefinition());
                items[i].HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(items[i], i);
                Children.Add(items[i]);
            }
        }
    }
}
