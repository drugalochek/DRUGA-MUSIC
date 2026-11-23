using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Sonora
{
    // Каркас окна в минималистичном стиле: боковая панель, контент, нижняя панель плеера
    public sealed partial class Shell : Grid
    {
        public static Shell I;
        public readonly bool ShotMode;
        public const double ContentMax = 1180;
        const double CompactWidth = 900;

        readonly Grid layout = new Grid();
        readonly Grid center = new Grid();
        readonly ScrollViewer scroll;
        readonly Border host;
        readonly Grid modal = new Grid();
        readonly Border sheet;
        readonly ScaleTransform sheetScale = new ScaleTransform(1, 1);
        public readonly FullPlayer Full;
        Border toastBox; TextBlock toastText; DispatcherTimer toastTimer;
        Border profileBtn, notifBadge, backBtn;
        Border side;
        StackPanel sideNav, sideLib;
        Border sideProfile;
        bool compact;
        static readonly string[] Tabs = { "home", "search", "explore", "fav" };
        static readonly string[] TabNames = { "Главная", "Поиск", "Исследовать", "Моя музыка" };
        static readonly string[] TabIcons = { "home", "search", "explore", "heart" };
        string lastTab = "home";
        bool renderQueued;
        Action sheetClosed;

        public string Page = "home";
        public object Arg;
        readonly List<KeyValuePair<string, object>> history = new List<KeyValuePair<string, object>>();

        public Shell(bool shots)
        {
            I = this;
            ShotMode = shots;
            Background = Th.Bg;
            ClipToBounds = true;
            TextElement.SetFontFamily(this, Th.Font);
            TextElement.SetForeground(this, Th.Txt);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            UseLayoutRounding = true;

            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Children.Add(layout);

            side = BuildSide();
            layout.Children.Add(side);

            // панель контента
            host = new Border { MaxWidth = ContentMax, Padding = new Thickness(28, 0, 28, 36) };
            scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = host, PanningMode = PanningMode.VerticalOnly };
            var topGlow = new Rectangle { Height = 280, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Fill = Th.G(Th.C("#3B82F6", .16), Th.C("#3B82F6", 0), false) };
            var contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition());
            var top = BuildTopbar();
            Grid.SetRow(scroll, 1);
            contentGrid.Children.Add(top);
            contentGrid.Children.Add(scroll);
            center.Children.Add(topGlow);
            center.Children.Add(contentGrid);
            center.Children.Add(BuildToast());
            var panel = new Border { CornerRadius = new CornerRadius(10), Background = Th.Panel, Margin = new Thickness(0, 8, 8, 0), Child = center };
            U.ClipRound(center, 10);
            Grid.SetColumn(panel, 1);
            layout.Children.Add(panel);

            var bar = BuildBar();
            Grid.SetRow(bar, 1);
            Grid.SetColumnSpan(bar, 2);
            layout.Children.Add(bar);

            Full = new FullPlayer();
            Children.Add(Full);

            modal.Background = Th.B("#000000", .6);
            modal.Visibility = Visibility.Collapsed;
            modal.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == modal) CloseSheet(); };
            sheet = new Border
            {
                MaxWidth = 460,
                Margin = new Thickness(16),
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(12),
                Background = Th.Bg2,
                BorderBrush = Th.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24, 22, 24, 22),
                RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = sheetScale,
                Effect = U.Shadow(48, 12, .55)
            };
            sheet.MouseLeftButtonDown += (s, e) => e.Handled = true;
            modal.Children.Add(sheet);
            Children.Add(modal);

            Library.Changed += QueueRender;
            Catalog.Changed += QueueRender;
            Auth.Changed += () => { UpdateProfileBtn(); QueueRender(); };
            Player.I.TrackChanged += () => { UpdateMini(); QueueRender(); };
            Player.I.StateChanged += UpdateMini;
            Player.I.Tick += UpdateMiniProgress;

            SizeChanged += (s, e) =>
            {
                bool c = ActualWidth < CompactWidth;
                if (c != compact) { compact = c; UpdateSide(); }
            };

            AllowDrop = true;
            DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += (s, e) =>
            {
                if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
                int n = Library.AddFiles(files);
                Toast(n > 0 ? "Добавлено: " + U.Tn(n) : "Подходящих аудиофайлов не найдено");
                if (n > 0) Go("playlist", Mixes.Uploaded);
            };

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            long slot = Mixes.Slot;
            clock.Tick += (s, e) =>
            {
                if (hourSub != null && Page == "home") hourSub.Text = "Каждые 30 минут новый жанр · " + Mixes.HourCountdown();
                if (Mixes.Slot != slot) { slot = Mixes.Slot; if (Page == "home") QueueRender(); }
            };
            clock.Start();

            UpdateProfileBtn();
            UpdateMini();
            Render();
        }

        // ---------- Боковая панель ----------
        Border BuildSide()
        {
            sideNav = new StackPanel();
            sideLib = new StackPanel();
            var libScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sideLib, Focusable = false };
            sideProfile = new Border();

            var navBox = new Border { CornerRadius = new CornerRadius(10), Background = Th.Panel, Padding = new Thickness(10, 12, 10, 12), Margin = new Thickness(0, 0, 0, 8), Child = sideNav };
            var libGrid = new Grid();
            libGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            libGrid.RowDefinitions.Add(new RowDefinition());
            libGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            libHead = new Border { Margin = new Thickness(6, 0, 0, 10) };
            libGrid.Children.Add(libHead);
            Grid.SetRow(libScroll, 1); libGrid.Children.Add(libScroll);
            Grid.SetRow(sideProfile, 2); libGrid.Children.Add(sideProfile);
            var libBox = new Border { CornerRadius = new CornerRadius(10), Background = Th.Panel, Padding = new Thickness(10, 14, 10, 10), Child = libGrid };

            var dp = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(navBox, Dock.Top);
            dp.Children.Add(navBox);
            dp.Children.Add(libBox);
            return new Border { Width = 260, Margin = new Thickness(8, 8, 8, 0), Child = dp };
        }
        Border libHead;

        void UpdateSide()
        {
            side.Width = compact ? 76 : 260;
            // логотип + навигация
            sideNav.Children.Clear();
            var logo = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Th.Accent, Child = U.Center(U.Ico("wave", 17, Th.White)) };
            var logoImg = Img.Res("logo.png");
            if (logoImg != null) { logo.Background = new ImageBrush(logoImg) { Stretch = Stretch.UniformToFill }; logo.Child = null; }
            var brand = U.Tx("SONORA", 18, Th.Txt);
            brand.FontFamily = Th.LogoFont;
            brand.VerticalAlignment = VerticalAlignment.Center;
            brand.Margin = new Thickness(10, 0, 0, 0);
            var brandRow = compact ? (FrameworkElement)U.Center(logo) : U.Row(logo, brand);
            var brandBox = U.Click(new Border { Padding = new Thickness(compact ? 0 : 8, 2, 0, 14), Child = brandRow }, () => Go("home"), 1);
            sideNav.Children.Add(brandBox);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                bool on = Tabs[i] == lastTab && (Page == Tabs[i] || !(Page == "playlist"));
                var ic = U.Ico(TabIcons[i], 21, on ? Th.Txt : Th.Dim, on && TabIcons[i] == "heart" ? true : (bool?)null, on ? 2.4 : 2);
                FrameworkElement content = ic;
                if (!compact)
                {
                    var t = U.Tx(TabNames[i], 14, on ? Th.Txt : Th.Dim);
                    t.FontWeight = FontWeights.SemiBold;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.Margin = new Thickness(16, 0, 0, 0);
                    content = U.Row(ic, t);
                }
                var b = new Border { Padding = new Thickness(compact ? 0 : 10, 9, 10, 9), CornerRadius = new CornerRadius(8), Child = compact ? U.Center(content) : content, Margin = new Thickness(0, 1, 0, 1) };
                if (on) b.Background = Th.Card; else U.Hover(b, Th.Clear, Th.Card);
                sideNav.Children.Add(U.Click(b, () => Go(Tabs[idx]), .98));
            }

            // медиатека
            var libIco = U.Ico("queue", 20, Th.Dim);
            FrameworkElement head;
            var plus = U.IconBtn("plus", 30, 16, Th.Dim, () => NewPlaylistSheet(null));
            if (compact) head = U.Center(plus);
            else
            {
                var lt = U.Tx("Медиатека", 14, Th.Dim);
                lt.FontWeight = FontWeights.SemiBold;
                lt.VerticalAlignment = VerticalAlignment.Center;
                lt.Margin = new Thickness(12, 0, 0, 0);
                libIco.VerticalAlignment = VerticalAlignment.Center;
                head = U.Cols("a * a", libIco, lt, plus);
            }
            libHead.Child = head;

            sideLib.Children.Clear();
            Action<string, string, string, Brush, Brush> item = (id, name, sub, bg, img) =>
            {
                bool on = Page == "playlist" && (Arg as string) == id;
                var cov = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(6), Background = img ?? bg };
                if (img == null) cov.Child = U.Center(U.Ico(id == Mixes.Liked ? "heartFill" : id == Mixes.Uploaded ? "upload" : id == Mixes.Recent ? "clock" : "music", 18, Th.White));
                FrameworkElement content = cov;
                if (!compact)
                {
                    var t = U.Tx(name, 13.5, on ? Th.Accent2 : Th.Txt);
                    t.Margin = new Thickness(0, 0, 0, 2);
                    var info = U.Col(t, U.Tx(sub, 11.5, Th.Dim));
                    info.VerticalAlignment = VerticalAlignment.Center;
                    info.Margin = new Thickness(12, 0, 4, 0);
                    content = U.Cols("a *", cov, info);
                }
                var b = new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(8), Child = compact ? U.Center(content) : content };
                if (on) b.Background = Th.Card; else U.Hover(b, Th.Clear, Th.Card);
                sideLib.Children.Add(U.Click(b, () => Go("playlist", id), .99));
            };
            item(Mixes.Liked, "Любимые треки", "Плейлист · " + U.Tn(Library.D.Fav.Count), Th.G("#2563EB", "#60A5FA"), null);
            if (Library.D.Recent.Count > 0) item(Mixes.Recent, "Недавно прослушанные", "История · " + U.Tn(Library.D.Recent.Count), Th.B("#1E3A5F"), null);
            if (Library.D.Local.Count > 0) item(Mixes.Uploaded, "Загруженная музыка", "С компьютера · " + U.Tn(Library.D.Local.Count), Th.B("#0E4C5A"), null);
            foreach (var p in Library.D.Playlists)
            {
                var first = p.TrackIds.Select(Catalog.Get).FirstOrDefault(t => t != null && !string.IsNullOrEmpty(t.Cover));
                item(p.Id, p.Name, "Плейлист · " + U.Tn(p.TrackIds.Count), Th.B(p.Color ?? "#3B82F6"), U.CoverBrush(first, 96));
            }
            if (!compact && Library.D.Playlists.Count == 0)
            {
                var hint = new Border { CornerRadius = new CornerRadius(8), Background = Th.Card, Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 10, 0, 0) };
                var ht = U.Tx("Создай свой первый плейлист", 13.5);
                var hd = U.Wrap("Это проще, чем кажется.", 12, Th.Dim);
                hd.Margin = new Thickness(0, 3, 0, 10);
                var hb = new Border { CornerRadius = new CornerRadius(16), Background = Th.White, Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left, Child = U.Tx("Создать плейлист", 12.5, Th.B("#000000")) };
                U.Click(hb, () => NewPlaylistSheet(null), .96);
                hint.Child = U.Col(ht, hd, hb);
                sideLib.Children.Add(hint);
            }

            // профиль внизу
            var s = Auth.Current;
            var av = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Th.Accent };
            string nm = s != null ? (s.Name ?? s.Email ?? "Профиль") : "Гость";
            if (s != null && !string.IsNullOrWhiteSpace(nm)) av.Child = U.Center(U.Tx(nm.Trim().Substring(0, 1).ToUpperInvariant(), 13, Th.White));
            else av.Child = U.Center(U.Ico("user", 16, Th.White));
            FrameworkElement pc = av;
            if (!compact)
            {
                var n = U.Tx(nm, 13, Th.Txt);
                var sub = U.Tx(s != null ? "Аккаунт SONORA" : "Войти в аккаунт", 11, Th.Dim);
                var info = U.Col(n, sub);
                info.VerticalAlignment = VerticalAlignment.Center;
                info.Margin = new Thickness(10, 0, 4, 0);
                var gear = U.Ico("gear", 16, Th.Dim);
                gear.VerticalAlignment = VerticalAlignment.Center;
                pc = U.Cols("a * a", av, info, gear);
            }
            var pb = new Border { Padding = new Thickness(6, 8, 8, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 8, 0, 0), BorderBrush = Th.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = compact ? U.Center(pc) : pc };
            U.Hover(pb, Th.Clear, Th.Card);
            sideProfile.Child = U.Click(pb, () => { if (Auth.Current == null) OpenWelcome(); else Go("profile"); }, .99);
        }

        // ---------- Верхняя строка контента ----------
        FrameworkElement BuildTopbar()
        {
            backBtn = U.IconBtn("chevL", 34, 18, Th.Txt, Back, Th.B("#000000", .45));
            var bell = IBtn("bell", OpenNotifications);
            notifBadge = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = Th.Accent2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 7, 0), IsHitTestVisible = false };
            var bellWrap = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            bellWrap.Children.Add(bell);
            bellWrap.Children.Add(notifBadge);
            profileBtn = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Th.Accent, BorderBrush = Th.B("#000000", .5), BorderThickness = new Thickness(3) };
            U.Click(profileBtn, () => Go("profile"), .92);
            var g = U.Cols("a * a a", backBtn, null, bellWrap, profileBtn);
            g.Margin = new Thickness(20, 14, 20, 8);
            return g;
        }
        public static Border IBtn(string icon, Action a)
        {
            var b = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Th.B("#000000", .45), Child = U.Center(U.Ico(icon, 17, Th.Txt)) };
            return U.Click(b, a, .92);
        }
        void UpdateProfileBtn()
        {
            var s = Auth.Current;
            if (s != null && !string.IsNullOrWhiteSpace(s.Name ?? s.Email))
            {
                var letter = (s.Name ?? s.Email).Trim().Substring(0, 1).ToUpperInvariant();
                profileBtn.Child = U.Center(U.Tx(letter, 13, Th.White));
            }
            else profileBtn.Child = U.Center(U.Ico("user", 15, Th.White));
            notifBadge.Visibility = Library.D.UpdatedAt == 0 || ShotMode ? Visibility.Visible : Visibility.Collapsed;
        }
        void UpdateNav()
        {
            if (Tabs.Contains(Page)) lastTab = Page;
            backBtn.Opacity = history.Count > 0 ? 1 : .4;
            UpdateSide();
        }

        // ---------- Нижняя панель плеера ----------
        Border bar, barCover, barPlay, barFav, barShuffle, barRepeat, barLyrics, barQueue, barTimer, barVolIco;
        Grid barSeek, volBar;
        Border barFill, barThumb, volFill;
        TextBlock barTitle, barArtist, barCur, barDur;
        bool barDrag, volDrag;
        double lastVol = .8;

        FrameworkElement BuildBar()
        {
            barCover = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(6), Background = Th.Card };
            U.Click(barCover, OpenFull, .96);
            barTitle = U.Tx("", 13.5, Th.Txt);
            barTitle.Cursor = Cursors.Hand;
            barTitle.MouseLeftButtonUp += (s, e) => OpenFull();
            barTitle.Margin = new Thickness(0, 0, 0, 3);
            barArtist = U.Tx("", 11.5, Th.Dim);
            barArtist.Cursor = Cursors.Hand;
            barArtist.MouseLeftButtonUp += (s, e) => { var t = Player.I.Current; if (t != null && !t.IsLocal) Go("artist", t.Artist); };
            var info = U.Col(barTitle, barArtist);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(14, 0, 10, 0);
            barFav = U.IconBtn("heart", 32, 17, Th.Dim, () => { var t = Player.I.Current; if (t != null) Library.ToggleFav(t.Id); UpdateMini(); });
            var left = U.Cols("a * a", barCover, info, barFav);
            left.VerticalAlignment = VerticalAlignment.Center;

            barShuffle = SmallBtn("shuffle", () => Player.I.ToggleShuffle());
            var prev = U.IconBtn("prev", 34, 19, Th.Txt, () => Player.I.Prev());
            barPlay = U.Click(new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Th.White, Margin = new Thickness(12, 0, 12, 0) }, () => Player.I.Toggle(), .92);
            var next = U.IconBtn("next", 34, 19, Th.Txt, () => Player.I.Next());
            barRepeat = SmallBtn("repeat", () => Player.I.CycleRepeat());
            var ctrls = U.Row(U.M(barShuffle, 0, 0, 8, 0), prev, barPlay, next, U.M(barRepeat, 8, 0, 0, 0));
            ctrls.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (FrameworkElement c in ctrls.Children) c.VerticalAlignment = VerticalAlignment.Center;

            barCur = U.Tx("0:00", 11, Th.Dim); barCur.TextAlignment = TextAlignment.Right; barCur.Width = 40; barCur.VerticalAlignment = VerticalAlignment.Center;
            barDur = U.Tx("0:00", 11, Th.Dim); barDur.Width = 40; barDur.VerticalAlignment = VerticalAlignment.Center;
            barSeek = Bar(out barFill, out barThumb);
            barSeek.Margin = new Thickness(10, 0, 10, 0);
            barSeek.MouseLeftButtonDown += (s, e) => { barDrag = true; barSeek.CaptureMouse(); SeekBar(e.GetPosition(barSeek).X, false); e.Handled = true; };
            barSeek.MouseMove += (s, e) => { if (barDrag) SeekBar(e.GetPosition(barSeek).X, false); };
            barSeek.MouseLeftButtonUp += (s, e) => { if (!barDrag) return; barDrag = false; barSeek.ReleaseMouseCapture(); SeekBar(e.GetPosition(barSeek).X, true); };
            var seekRow = U.Cols("a * a", barCur, barSeek, barDur);
            seekRow.Margin = new Thickness(0, 6, 0, 0);
            var mid = U.Col(ctrls, seekRow);
            mid.VerticalAlignment = VerticalAlignment.Center;
            mid.MaxWidth = 620;

            barLyrics = SmallBtn("lyrics", () => Full.OpenLyrics());
            barQueue = SmallBtn("queue", QueueSheet);
            barTimer = SmallBtn("timer", TimerSheet);
            barVolIco = SmallBtn("volume", () =>
            {
                if (Player.I.Volume > 0) { lastVol = Player.I.Volume; Player.I.Volume = 0; }
                else Player.I.Volume = lastVol > 0 ? lastVol : .8;
                UpdateVolume();
            });
            Border volThumb;
            volBar = Bar(out volFill, out volThumb);
            volBar.Width = 96;
            volBar.Margin = new Thickness(2, 0, 10, 0);
            volBar.MouseLeftButtonDown += (s, e) => { volDrag = true; volBar.CaptureMouse(); SetVol(e.GetPosition(volBar).X); e.Handled = true; };
            volBar.MouseMove += (s, e) => { if (volDrag) SetVol(e.GetPosition(volBar).X); };
            volBar.MouseLeftButtonUp += (s, e) => { volDrag = false; volBar.ReleaseMouseCapture(); Library.Save(false); };
            volBar.MouseWheel += (s, e) => { Player.I.Volume += e.Delta > 0 ? .05 : -.05; UpdateVolume(); e.Handled = true; };
            var expand = SmallBtn("expand", OpenFull);
            var right = U.Row(barLyrics, barQueue, barTimer, U.M(barVolIco, 6, 0, 0, 0), volBar, expand);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            foreach (FrameworkElement c in right.Children) c.VerticalAlignment = VerticalAlignment.Center;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 180 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            Grid.SetColumn(mid, 1); Grid.SetColumn(right, 2);
            g.Children.Add(left); g.Children.Add(mid); g.Children.Add(right);
            right.ClipToBounds = true;
            bar = new Border { Height = 88, Padding = new Thickness(16, 8, 16, 8), Background = Th.Bg, Child = g, Visibility = Visibility.Collapsed };
            return bar;
        }
        static Border SmallBtn(string icon, Action a)
        {
            var b = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Th.Clear, Child = U.Center(U.Ico(icon, 17, Th.Dim)) };
            return U.Click(b, a, .9);
        }
        static void SetSmall(Border b, string icon, bool on, bool enabled = true, string badge = null)
        {
            var g = new Grid();
            g.Children.Add(U.Center(U.Ico(icon, 17, on ? Th.Accent2 : enabled ? Th.Dim : Th.B("#FFFFFF", .25))));
            if (badge != null) { var t = U.Tx(badge, 8, Th.Accent2); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 1); g.Children.Add(t); }
            if (on) g.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Th.Accent2, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) });
            b.Child = g;
        }
        static Grid Bar(out Border fill, out Border thumb)
        {
            var g = new Grid { Height = 16, Background = Th.Clear, Cursor = Cursors.Hand };
            var line = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.B("#FFFFFF", .18), VerticalAlignment = VerticalAlignment.Center };
            var f = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.Txt, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
            var th = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Th.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden, IsHitTestVisible = false };
            g.Children.Add(line); g.Children.Add(f); g.Children.Add(th);
            g.MouseEnter += (s, e) => { f.Background = Th.Accent2; th.Visibility = Visibility.Visible; };
            g.MouseLeave += (s, e) => { if (Mouse.Captured == g) return; f.Background = Th.Txt; th.Visibility = Visibility.Hidden; };
            g.LostMouseCapture += (s, e) => { if (!g.IsMouseOver) { f.Background = Th.Txt; th.Visibility = Visibility.Hidden; } };
            fill = f; thumb = th;
            return g;
        }
        static void SetBar(Border fill, Border thumb, double r, double w)
        {
            r = Math.Max(0, Math.Min(1, r));
            fill.Width = r * w;
            thumb.Margin = new Thickness(Math.Max(0, r * w - 6), 0, 0, 0);
        }
        void SeekBar(double x, bool commit)
        {
            var len = Player.I.Length; var w = barSeek.ActualWidth;
            if (len <= 0 || w <= 0) return;
            var r = Math.Max(0, Math.Min(1, x / w));
            SetBar(barFill, barThumb, r, w);
            barCur.Text = U.Time(r * len);
            if (commit) Player.I.Seek(r * len);
        }
        void SetVol(double x)
        {
            var w = volBar.ActualWidth;
            if (w <= 0) return;
            Player.I.Volume = Math.Max(0, Math.Min(1, x / w));
            UpdateVolume();
        }
        void UpdateVolume()
        {
            var v = Player.I.Volume;
            volFill.Width = v * (volBar.ActualWidth > 0 ? volBar.ActualWidth : 96);
            barVolIco.Child = U.Center(U.Ico(v <= 0.001 ? "mute" : "volume", 17, Th.Dim));
        }

        public bool MiniVisible { get { return Player.I.Current != null; } }
        void UpdateMini()
        {
            var t = Player.I.Current;
            bar.Visibility = t == null ? Visibility.Collapsed : Visibility.Visible;
            if (t == null) { Full.Close(); return; }
            var br = U.CoverBrush(t, 112);
            barCover.Background = br ?? (Brush)Th.Card;
            barCover.Child = br == null ? U.Center(U.Ico("music", 20, Th.Dim)) : null;
            barTitle.Text = t.Title;
            barArtist.Text = t.Artist;
            var black = Th.B("#000000");
            barPlay.Child = U.Center(U.Ico(Player.I.IsPlaying ? "pause" : "play", 16, black));
            bool fav = Library.IsFav(t.Id);
            barFav.Child = U.Center(U.Ico("heart", 17, fav ? Th.Accent2 : Th.Dim, fav));
            SetSmall(barShuffle, "shuffle", Player.I.Shuffle);
            SetSmall(barRepeat, "repeat", Player.I.Repeat > 0, true, Player.I.Repeat == 2 ? "1" : null);
            SetSmall(barLyrics, "lyrics", false, t.HasLyrics);
            SetSmall(barQueue, "queue", false);
            SetSmall(barTimer, "timer", Player.I.SleepAt.HasValue);
            UpdateVolume();
            Full.Update();
        }
        void UpdateMiniProgress()
        {
            if (!barDrag && bar.Visibility == Visibility.Visible)
            {
                var len = Player.I.Length;
                var pos = Player.I.Position;
                barCur.Text = U.Time(pos);
                barDur.Text = U.Time(len);
                SetBar(barFill, barThumb, len > 0 ? pos / len : 0, barSeek.ActualWidth);
            }
            Full.Tick();
        }
        public void OpenFull() { if (Player.I.Current != null) Full.Open(); }

        // ---------- Toast ----------
        FrameworkElement BuildToast()
        {
            toastText = U.Wrap("", 13, Th.B("#000000"));
            toastText.TextAlignment = TextAlignment.Center;
            toastBox = new Border
            {
                Padding = new Thickness(16, 9, 16, 9),
                Background = Th.Txt,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(16, 0, 16, 24),
                MaxWidth = 420,
                Child = toastText,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Effect = U.Shadow(36, 6, .5)
            };
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); toastBox.Visibility = Visibility.Collapsed; };
            return toastBox;
        }
        public static void Toast(string msg)
        {
            var s = I;
            if (s == null) return;
            s.Dispatcher.BeginInvoke(new Action(() =>
            {
                s.toastText.Text = msg;
                s.toastBox.Visibility = Visibility.Visible;
                U.Fade(s.toastBox, 0, 1);
                s.toastTimer.Stop();
                s.toastTimer.Start();
            }));
        }

        // ---------- Диалоги ----------
        public bool SheetOpen { get { return modal.Visibility == Visibility.Visible; } }
        public void OpenSheet(string title, string desc, UIElement body, Action onClosed = null)
        {
            var sp = new StackPanel();
            var close = U.IconBtn("close", 30, 15, Th.Dim, CloseSheet);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            var head = new Grid();
            if (title != null)
            {
                var tt = U.Wrap(title, 20, Th.Txt);
                tt.FontWeight = FontWeights.Bold;
                tt.Margin = new Thickness(0, 0, 36, 6);
                head.Children.Add(tt);
            }
            else head.Height = 26;
            head.Children.Add(close);
            sp.Children.Add(head);
            if (desc != null) sp.Children.Add(U.M(U.Wrap(desc, 13, Th.Dim, 0, 19), 0, 0, 0, 16));
            if (body != null) sp.Children.Add(body);
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp, Focusable = false };
            sv.MaxHeight = Math.Max(320, (ActualHeight > 0 ? ActualHeight : 800) - 80);
            sheet.Child = sv;
            sheetClosed = onClosed;
            if (modal.Visibility != Visibility.Visible)
            {
                modal.Visibility = Visibility.Visible;
                if (!ShotMode)
                {
                    U.Fade(modal, 0, 1, 160);
                    var a = new DoubleAnimation(.96, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    sheetScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    sheetScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
            }
        }
        public void CloseSheet()
        {
            if (modal.Visibility != Visibility.Visible) return;
            modal.Visibility = Visibility.Collapsed;
            sheet.Child = null;
            var a = sheetClosed;
            sheetClosed = null;
            a?.Invoke();
        }

        public void HandleKey(KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox)
            {
                if (e.Key == Key.Escape && SheetOpen) { CloseSheet(); e.Handled = true; }
                return;
            }
            switch (e.Key)
            {
                case Key.Escape:
                    if (SheetOpen) CloseSheet();
                    else if (Full.IsOpen) Full.Close();
                    else if (history.Count > 0) Back();
                    e.Handled = true;
                    break;
                case Key.Space:
                    Player.I.Toggle(); e.Handled = true; break;
                case Key.MediaPlayPause:
                    Player.I.Toggle(); e.Handled = true; break;
                case Key.MediaNextTrack:
                    Player.I.Next(); e.Handled = true; break;
                case Key.MediaPreviousTrack:
                    Player.I.Prev(); e.Handled = true; break;
                case Key.Right:
                    if (Keyboard.Modifiers == ModifierKeys.Control) { Player.I.Next(); e.Handled = true; }
                    else if (Player.I.Current != null) { Player.I.Seek(Player.I.Position + 5); e.Handled = true; }
                    break;
                case Key.Left:
                    if (Keyboard.Modifiers == ModifierKeys.Control) { Player.I.Prev(); e.Handled = true; }
                    else if (Player.I.Current != null) { Player.I.Seek(Player.I.Position - 5); e.Handled = true; }
                    break;
                case Key.F:
                    if (Keyboard.Modifiers == ModifierKeys.Control) { Go("search"); e.Handled = true; }
                    break;
            }
        }

        // ---------- Навигация ----------
        public void QueueRender()
        {
            if (renderQueued) return;
            renderQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (renderQueued) Render(); }));
        }
        public void Render(bool keepScroll = true)
        {
            renderQueued = false;
            double off = scroll.VerticalOffset;
            FrameworkElement el;
            try { el = BuildPage(Page, Arg); }
            catch (Exception ex)
            {
                Log.W("render " + Page + ": " + ex);
                el = U.M(U.Wrap("Не удалось показать раздел: " + ex.Message, 13, Th.Danger), 0, 20, 0, 0);
            }
            host.Child = el;
            UpdateNav();
            if (keepScroll && off > 0)
            {
                scroll.UpdateLayout();
                scroll.ScrollToVerticalOffset(off);
            }
        }
        public void Go(string page, object arg = null)
        {
            if (Full.IsOpen) Full.Close();
            if (SheetOpen) CloseSheet();
            if (Tabs.Contains(page)) history.Clear();
            else if (page != Page || !Equals(arg, Arg)) history.Add(new KeyValuePair<string, object>(Page, Arg));
            Page = page;
            Arg = arg;
            Render(false);
            scroll.ScrollToVerticalOffset(0);
        }
        public void Back()
        {
            if (history.Count == 0) { if (Page != lastTab) Go(lastTab); return; }
            var kv = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            Page = kv.Key;
            Arg = kv.Value;
            Render(false);
            scroll.ScrollToVerticalOffset(0);
        }
        public void ScrollTo(double y) { scroll.UpdateLayout(); scroll.ScrollToVerticalOffset(y); }
    }
}
