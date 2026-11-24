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
        Border notifBadge, backBtn;
        Border side, contentPanel, topGlow, sidePromo;
        Grid ambient; Ellipse trackGlow;
        static Ellipse Glow(string hex, double a, double w, double h, HorizontalAlignment ha, VerticalAlignment va, Thickness m)
        {
            return new Ellipse { Width = w, Height = h, HorizontalAlignment = ha, VerticalAlignment = va, Margin = m, Fill = GlowBrush(Th.C(hex), a), IsHitTestVisible = false };
        }
        static Brush GlowBrush(Color c, double a)
        {
            var br = new RadialGradientBrush();
            br.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B), 0));
            br.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 120), c.R, c.G, c.B), .5));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            br.Freeze();
            return br;
        }
        StackPanel sideNav, sideLib;
        Border sideProfile;
        bool compact;
        static readonly string[] Tabs = { "home", "search", "explore", "fav", "services" };
        static readonly string[] TabNames = { "Главная", "Поиск", "Исследовать", "Моя музыка", "Сервисы" };
        static readonly string[] TabIcons = { "home", "search", "explore", "heart", "globe" };
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
            TextElement.SetFontFamily(this, Th.Body);
            TextElement.SetForeground(this, Th.Txt);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            UseLayoutRounding = true;

            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ambient = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            ambient.Children.Add(Glow("#2563EB", .30, 1000, 760, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-380, -300, 0, 0)));
            ambient.Children.Add(Glow("#1D4ED8", .16, 900, 700, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -320, -300)));
            trackGlow = Glow("#38BDF8", .12, 1100, 640, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, -380));
            ambient.Children.Add(trackGlow);
            Children.Add(ambient);
            Children.Add(layout);

            side = BuildSide();
            Grid.SetRowSpan(side, 2);
            layout.Children.Add(side);

            // панель контента — скруглённая «карточка» на чёрном фоне
            host = new Border { MaxWidth = ContentMax, Padding = new Thickness(36, 0, 36, 40) };
            scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = host, PanningMode = PanningMode.VerticalOnly };
            topGlow = new Border { Height = 300, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            var contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition());
            var top = BuildTopbar();
            Grid.SetRow(scroll, 1);
            contentGrid.Children.Add(top);
            contentGrid.Children.Add(scroll);
            center.Children.Add(new Border { Height = 160, VerticalAlignment = VerticalAlignment.Top, Background = Th.GlassShine, IsHitTestVisible = false });
            center.Children.Add(topGlow);
            center.Children.Add(contentGrid);
            center.Children.Add(BuildToast());
            contentPanel = new Border { CornerRadius = new CornerRadius(26), Background = Th.B("#0E1015", .74), BorderBrush = Th.GlassEdge, BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 14, 14), Child = center };
            U.ClipRound(center, 26);
            Grid.SetColumn(contentPanel, 1);
            layout.Children.Add(contentPanel);

            var bar = BuildBar();
            Grid.SetRow(bar, 1);
            Grid.SetColumn(bar, 1);
            layout.Children.Add(bar);

            Full = new FullPlayer();
            Children.Add(Full);

            modal.Background = Th.B("#000000", .5);
            modal.Visibility = Visibility.Collapsed;
            modal.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == modal) CloseSheet(); };
            sheet = new Border
            {
                MaxWidth = 460,
                Margin = new Thickness(16),
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(12),
                Background = Th.B("#15181F", .93),
                BorderBrush = Th.GlassEdge,
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
            Ext.Changed += QueueRender;
            Catalog.Changed += QueueRender;
            Auth.Changed += () => { UpdateProfileBtn(); QueueRender(); _ = Plus.Refresh(); };
            Plus.Changed += QueueRender;
            Player.I.TrackChanged += () => { UpdateMini(); QueueRender(); };
            Player.I.StateChanged += UpdateMini;
            Palette.Ready += id => { if (Player.I.Current != null && Player.I.Current.Id == id) { barColorId = null; UpdateMini(); } };
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
                if (!Guard.Need("Загрузка своей музыки")) return;
                AddFilesUi(files, true);
            };

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            long slot = Mixes.Slot;
            clock.Tick += (s, e) =>
            {
                if (hourSub != null && Page == "home") hourSub.Text = L.T("Каждые 30 минут новый жанр · ") + Mixes.HourCountdown();
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
            sidePromo = new Border();
            libHead = new Border { Margin = new Thickness(14, 22, 2, 8) };
            var dp = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(sideNav, Dock.Top); dp.Children.Add(sideNav);
            DockPanel.SetDock(libHead, Dock.Top); dp.Children.Add(libHead);
            DockPanel.SetDock(sideProfile, Dock.Bottom); dp.Children.Add(sideProfile);
            DockPanel.SetDock(sidePromo, Dock.Bottom); dp.Children.Add(sidePromo);
            dp.Children.Add(libScroll);
            return new Border { Width = 292, Padding = new Thickness(18, 0, 18, 14), Child = dp };
        }
        Border libHead;

        void UpdateSide()
        {
            side.Width = compact ? 92 : 292;
            side.Padding = compact ? new Thickness(10, 0, 10, 14) : new Thickness(18, 0, 18, 14);
            // логотип (иконка из репозитория) + фирменный шрифт
            sideNav.Children.Clear();
            FrameworkElement logo;
            var logoImg = Img.Res("logo.png");
            if (logoImg != null)
            {
                var im = new Image { Source = logoImg, Width = 54, Height = 54, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                logo = im;
            }
            else logo = U.Circle(54, Th.Accent, U.Center(U.Ico("wave", 26, Th.White)));
            FrameworkElement brandRow = logo;
            if (!compact)
            {
                var big = U.Tx("SONORA", 31, Th.Accent);
                big.FontFamily = Th.LogoFont;
                big.Margin = new Thickness(0, 2, 0, 0);
                var col = U.Col(big);
                col.VerticalAlignment = VerticalAlignment.Center;
                col.Margin = new Thickness(12, 0, 0, 0);
                brandRow = U.Row(logo, col);
            }
            var brandBox = U.Click(new Border { Background = Th.Clear, Padding = new Thickness(compact ? 0 : 4, 22, 0, 30), Child = compact ? U.Center(brandRow) : brandRow }, () => Go("home"), 1);
            sideNav.Children.Add(brandBox);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                bool on = Tabs[i] == lastTab && (Page == Tabs[i] || !(Page == "playlist"));
                var ic = U.Ico(TabIcons[i], 26, on ? Th.White : Th.Dim, on && TabIcons[i] == "heart" ? true : (bool?)null, on ? 2.5 : 2);
                FrameworkElement content = ic;
                if (!compact)
                {
                    var t = U.Tx(TabNames[i], 17, on ? Th.White : Th.Dim);
                    t.FontFamily = Th.Body;
                    t.FontWeight = FontWeights.SemiBold;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.Margin = new Thickness(18, 0, 0, 0);
                    content = U.Row(ic, t);
                }
                var b = new Border { Padding = new Thickness(compact ? 0 : 14, 12, 12, 12), CornerRadius = new CornerRadius(14), Child = compact ? U.Center(content) : content, Margin = new Thickness(0, 1, 0, 1) };
                U.Hover(b, Th.Clear, Th.B("#FFFFFF", .05));
                sideNav.Children.Add(U.Click(b, () => Go(Tabs[idx]), .98));
            }

            // медиатека
            FrameworkElement head;
            var plus = U.IconBtn("plus", 30, 16, Th.Dim, () => NewPlaylistSheet(null));
            if (compact) head = U.Center(plus);
            else
            {
                var lt = U.Tx("МЕДИАТЕКА", 11.5, Th.Dim);
                lt.FontWeight = FontWeights.Bold;
                lt.VerticalAlignment = VerticalAlignment.Center;
                head = U.Cols("* a", lt, plus);
            }
            libHead.Child = head;
            libHead.Margin = compact ? new Thickness(0, 16, 0, 8) : new Thickness(14, 22, 2, 8);

            sideLib.Children.Clear();
            Action<string, string, string, Brush, Brush> item = (id, name, sub, bg, img) =>
            {
                bool on = Page == "playlist" && (Arg as string) == id;
                var cov = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(id == Mixes.Liked || id.EndsWith(":likes") ? 22 : 10), Background = img ?? bg };
                if (img == null) cov.Child = U.Center(U.Ico(id == Mixes.Liked || id.EndsWith(":likes") ? "heartFill" : id == Mixes.Uploaded ? "upload" : id == Mixes.Recent ? "clock" : id == Mixes.Downloaded ? "download" : "music", 19, Th.White));
                FrameworkElement content = cov;
                if (!compact)
                {
                    var t = U.Tx(name, 14, on ? Th.Accent2 : Th.Txt);
                    t.Margin = new Thickness(0, 0, 0, 2);
                    var info = U.Col(t, U.Tx(sub, 12, Th.Dim));
                    info.VerticalAlignment = VerticalAlignment.Center;
                    info.Margin = new Thickness(12, 0, 4, 0);
                    content = U.Cols("a *", cov, info);
                }
                var b = new Border { Padding = new Thickness(compact ? 0 : 8, 6, 6, 6), CornerRadius = new CornerRadius(12), Child = compact ? U.Center(content) : content };
                if (on) b.Background = Th.B("#FFFFFF", .06); else U.Hover(b, Th.Clear, Th.B("#FFFFFF", .05));
                sideLib.Children.Add(U.Click(b, () => Go("playlist", id), .99));
            };
            item(Mixes.Liked, "Мне нравится", U.Tn(Library.D.Fav.Count), Th.Pink, null);
            if (Library.D.Recent.Count > 0) item(Mixes.Recent, "Недавно прослушанные", "История · " + U.Tn(Library.D.Recent.Count), Th.B("#1E3A5F"), null);
            if (Library.D.Offline.Count > 0) item(Mixes.Downloaded, "Скачанные", "В SONORA · " + U.Tn(Library.D.Offline.Count), Th.G("#60A5FA", "#1D4ED8"), null);
            if (Library.D.Local.Count > 0) item(Mixes.Uploaded, "Загруженная музыка", "С компьютера · " + U.Tn(Library.D.Local.Count), Th.B("#0E4C5A"), null);
            if (Ext.YaOn) item("ext:ya:likes", "Мне нравится", "Яндекс Музыка", Th.Pink, null);
            if (Ext.SpOn) item("ext:sp:likes", "Любимые треки", "Spotify", Th.Pink, null);
            if (Ext.DzOn) item("ext:dz:likes", "Любимые треки", "Deezer", Th.Pink, null);
            foreach (var p in Library.D.Playlists)
            {
                var first = p.TrackIds.Select(Catalog.Get).FirstOrDefault(t => t != null && !string.IsNullOrEmpty(t.Cover));
                item(p.Id, p.Name, "Плейлист · " + U.Tn(p.TrackIds.Count), Th.B(p.Color ?? "#3B82F6"), U.CoverBrush(first, 96));
            }

            // карточка «своя музыка» — как блок «Музыка на MacOS»
            if (compact) sidePromo.Child = null;
            else
            {
                var pt = new TextBlock { TextAlignment = TextAlignment.Center, FontSize = 15, Foreground = Th.Dim, FontFamily = Th.Body, FontWeight = FontWeights.SemiBold };
                pt.Inlines.Add(new Run("Музыка с "));
                pt.Inlines.Add(new Run("компьютера") { Foreground = Th.Txt });
                var bt = U.Tx("Загрузить", 15, Th.Txt);
                bt.VerticalAlignment = VerticalAlignment.Center;
                bt.Margin = new Thickness(10, 0, 0, 0);
                var btn = new Border { CornerRadius = new CornerRadius(24), Background = Th.Glass(.08), BorderBrush = Th.GlassEdgeSoft, BorderThickness = new Thickness(1), Padding = new Thickness(18, 11, 20, 11), Margin = new Thickness(0, 14, 0, 0), Child = U.Center(U.Row(U.Ico("upload", 18, Th.Txt), bt)) };
                U.Hover(btn, Th.Glass(.08), Th.Glass(.14));
                U.Click(btn, PickFiles, .97);
                sidePromo.Child = new Border { CornerRadius = new CornerRadius(20), BorderBrush = Th.GlassEdge, BorderThickness = new Thickness(1), Background = Th.Glass(.04), Padding = new Thickness(18, 18, 18, 18), Margin = new Thickness(0, 12, 0, 16), Child = U.Col(pt, btn) };
            }

            // профиль внизу: аватар в градиентном кольце + бейдж
            var s = Auth.Current;
            string nm = s != null ? (s.Name ?? s.Email ?? "Профиль") : "Гость";
            var av = Avatar(44);
            var ring = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = ProfileRing(), Padding = new Thickness(2.5) };
            ring.Child = new Border { CornerRadius = new CornerRadius(24), Background = Th.Bg, Padding = new Thickness(1.5), Child = av };
            FrameworkElement pc = ring;
            if (!compact)
            {
                var n = U.Tx(nm, 15, Th.Txt);
                n.Margin = new Thickness(0, 0, 0, 4);
                var badgeTxt = U.Tx(s != null ? "Профиль" : "Войти", 12.5, Th.White);
                badgeTxt.FontWeight = FontWeights.Bold;
                var badge = new Border { CornerRadius = new CornerRadius(11), Background = Th.GH("#60A5FA", "#1D4ED8"), Padding = new Thickness(10, 2, 11, 3), HorizontalAlignment = HorizontalAlignment.Left, Child = badgeTxt };
                var info = U.Col(n, badge);
                info.VerticalAlignment = VerticalAlignment.Center;
                info.Margin = new Thickness(12, 0, 4, 0);
                pc = U.Cols("a *", ring, info);
            }
            var pb = new Border { Padding = new Thickness(compact ? 0 : 4, 6, 6, 6), CornerRadius = new CornerRadius(16), Child = compact ? U.Center(pc) : pc };
            U.Hover(pb, Th.Clear, Th.B("#FFFFFF", .04));
            U.Click(pb, () => { if (Auth.Current == null) OpenWelcome(); else Go("profile"); }, .99);
            // уведомления — рядом с профилем внизу
            var bell = IBtn("bell", OpenNotifications);
            bell.ToolTip = L.T("Уведомления");
            if (notifBadge.Parent is Panel op) op.Children.Remove(notifBadge);
            notifBadge.Visibility = HasUnread ? Visibility.Visible : Visibility.Collapsed;
            var bellWrap = new Grid { VerticalAlignment = VerticalAlignment.Center, Width = 34, Height = 34 };
            bellWrap.Children.Add(bell);
            bellWrap.Children.Add(notifBadge);
            notifBadge.Margin = new Thickness(0, 5, 5, 0);
            if (compact) sideProfile.Child = U.Col(U.M(U.Center(bellWrap), 0, 0, 0, 10), pb);
            else sideProfile.Child = U.Cols("* a", pb, U.M(bellWrap, 6, 0, 0, 0));
        }

        // ---------- Верхняя строка контента ----------
        FrameworkElement BuildTopbar()
        {
            backBtn = U.IconBtn("chevL", 34, 18, Th.Txt, Back, Th.B("#000000", .45));
            notifBadge = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = Th.Heart, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 7, 0), IsHitTestVisible = false };
            var gear = IBtn("gear", () => Go("settings"));
            gear.ToolTip = L.T("Настройки");
            var g = U.Cols("a * a", backBtn, null, gear);
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
            UpdateSide();
        }
        bool HasUnread { get { return ShotMode || Library.D.SeenNotif != Program.Version; } }
        void UpdateNav()
        {
            if (Tabs.Contains(Page)) lastTab = Page;
            // стрелка «назад» только там, где она реально нужна
            backBtn.Visibility = Tabs.Contains(Page) ? Visibility.Hidden : Visibility.Visible;
            UpdateSide();
        }

        // ---------- Нижняя панель плеера ----------
        Border bar, barCover, barPlay, barFav, barShuffle, barRepeat, barLyrics, barQueue, barTimer, barVolIco;
        Grid barSeek, volBar;
        Border barFill, barThumb, volFill, volThumb;
        Liquid barLiquid;
        string barColorId;
        static Brush barAccent = Th.Accent2;
        TextBlock barTitle, barArtist, barCur, barDur;
        bool barDrag, volDrag;
        double lastVol = .8;

        FrameworkElement BuildBar()
        {
            barCover = new Border { Width = 66, Height = 66, CornerRadius = new CornerRadius(10), Background = Th.Card };
            U.Click(barCover, OpenFull, .96);
            barTitle = U.Tx("", 16, Th.Txt);
            barTitle.Cursor = Cursors.Hand;
            barTitle.MouseLeftButtonUp += (s, e) => OpenFull();
            barTitle.Margin = new Thickness(0, 0, 0, 3);
            barArtist = U.Tx("", 13, Th.B("#FFFFFF", .62));
            barArtist.Cursor = Cursors.Hand;
            barArtist.MouseLeftButtonUp += (s, e) => { var t = Player.I.Current; if (t != null && !t.IsLocal) Go("artist", t.Artist); };
            var info = U.Col(barTitle, barArtist);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(14, 0, 10, 0);
            barFav = U.IconBtn("heart", 40, 24, Th.Txt, () => { var t = Player.I.Current; if (t != null) Library.ToggleFav(t.Id); UpdateMini(); });
            var more = U.IconBtn("dots", 40, 22, Th.Txt, () => { var t = Player.I.Current; if (t != null) TrackMenu(t); });
            var left = U.Cols("a * a a", barCover, info, barFav, more);
            left.VerticalAlignment = VerticalAlignment.Center;

            barShuffle = SmallBtn("shuffle", () => Player.I.ToggleShuffle());
            var prev = U.IconBtn("prev", 44, 26, Th.Txt, () => Player.I.Prev(), null, true);
            barPlay = U.Click(new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = Th.White, Margin = new Thickness(16, 0, 16, 0) }, () => Player.I.Toggle(), .92);
            var next = U.IconBtn("next", 44, 26, Th.Txt, () => Player.I.Next(), null, true);
            barRepeat = SmallBtn("repeat", () => Player.I.CycleRepeat());
            var ctrls = U.Row(U.M(barShuffle, 0, 0, 18, 0), prev, barPlay, next, U.M(barRepeat, 18, 0, 0, 0));
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
            seekRow.Margin = new Thickness(0, 2, 0, 0);
            seekRow.MaxWidth = 520;
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
            volBar = Bar(out volFill, out volThumb);
            volBar.SizeChanged += (s, e) => UpdateVolume();
            volBar.Width = 110;
            volBar.Margin = new Thickness(2, 0, 10, 0);
            volBar.MouseLeftButtonDown += (s, e) => { volDrag = true; volBar.CaptureMouse(); SetVol(e.GetPosition(volBar).X); e.Handled = true; };
            volBar.MouseMove += (s, e) => { if (volDrag) SetVol(e.GetPosition(volBar).X); };
            volBar.MouseLeftButtonUp += (s, e) => { volDrag = false; volBar.ReleaseMouseCapture(); Library.Save(false); };
            volBar.MouseWheel += (s, e) => { Player.I.Volume += e.Delta > 0 ? .05 : -.05; UpdateVolume(); e.Handled = true; };
            var expand = SmallBtn("expand", OpenFull);
            expand.ToolTip = L.T("Развернуть плеер");
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
            g.Margin = new Thickness(16, 8, 22, 8);
            barLiquid = new Liquid(false);
            var shade = new Border { Background = Th.G(Th.C("#000000", .04), Th.C("#000000", .2), true), IsHitTestVisible = false };
            var stack = new Grid();
            stack.Children.Add(barLiquid);
            stack.Children.Add(shade);
            stack.Children.Add(g);
            U.ClipRound(stack, 23);
            bar = new Border { Height = 100, CornerRadius = new CornerRadius(24), Margin = new Thickness(0, 0, 14, 14), Background = Th.B("#1A1D24", .9), BorderBrush = Th.GlassEdge, BorderThickness = new Thickness(1), Child = stack, Visibility = Visibility.Collapsed };
            return bar;
        }
        static Border SmallBtn(string icon, Action a)
        {
            var b = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = Th.Clear, Child = U.Center(U.Ico(icon, 22, Th.Txt)) };
            return U.Click(b, a, .9);
        }
        static void SetSmall(Border b, string icon, bool on, bool enabled = true, string badge = null)
        {
            var g = new Grid();
            g.Children.Add(U.Center(U.Ico(icon, 22, on ? barAccent : enabled ? Th.B("#FFFFFF", .88) : Th.B("#FFFFFF", .25))));
            if (badge != null) { var t = U.Tx(badge, 8, barAccent); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 1); g.Children.Add(t); }
            if (on) g.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = barAccent, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) });
            b.Child = g;
        }
        static Grid Bar(out Border fill, out Border thumb)
        {
            var g = new Grid { Height = 16, Background = Th.Clear, Cursor = Cursors.Hand };
            var line = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.B("#FFFFFF", .22), VerticalAlignment = VerticalAlignment.Center };
            var f = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.Txt, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
            var th = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Th.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden, IsHitTestVisible = false };
            g.Children.Add(line); g.Children.Add(f); g.Children.Add(th);
            g.MouseEnter += (s, e) => { f.Background = barAccent ?? Th.Accent2; th.Visibility = Visibility.Visible; };
            g.MouseLeave += (s, e) => { if (Mouse.Captured == g) return; f.Background = Th.Txt; th.Visibility = Visibility.Hidden; };
            g.LostMouseCapture += (s, e) => { if (!g.IsMouseOver) { f.Background = Th.Txt; th.Visibility = Visibility.Hidden; } };
            fill = f; thumb = th;
            return g;
        }
        static void SetBar(Border fill, Border thumb, double r, double w)
        {
            r = Math.Max(0, Math.Min(1, r));
            fill.Width = r * w;
            thumb.Margin = new Thickness(Math.Max(0, Math.Min(w - 12, r * w - 6)), 0, 0, 0);
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
            SetBar(volFill, volThumb, v, volBar.ActualWidth > 0 ? volBar.ActualWidth : 110);
            barVolIco.Child = U.Center(U.Ico(v <= 0.001 ? "mute" : v < .5 ? "volLow" : "volume", 22, Th.B("#FFFFFF", .88)));
            barVolIco.ToolTip = L.T("Громкость: ") + (int)Math.Round(v * 100) + "%";
        }

        public bool MiniVisible { get { return Player.I.Current != null; } }
        void UpdateMini()
        {
            var t = Player.I.Current;
            bar.Visibility = t == null ? Visibility.Collapsed : Visibility.Visible;
            contentPanel.Margin = new Thickness(0, 14, 14, t == null ? 14 : 12);
            if (t == null) { Full.Close(); topGlow.Background = null; return; }
            // панель плеера и подсветка — в цветах обложки, «живая вода»
            if (barColorId != t.Id)
            {
                var pal = Palette.Of(t);
                barColorId = Palette.Has(t) ? t.Id : null;
                barAccent = Th.B(Palette.Light(pal[0]));
                var dc = pal[0];
                barLiquid.SetColors(pal, IsLoaded && !ShotMode);
                trackGlow.Fill = GlowBrush(dc, .22);
                topGlow.Background = Th.G(Color.FromArgb(70, dc.R, dc.G, dc.B), Color.FromArgb(0, dc.R, dc.G, dc.B), false);
            }
            var br = U.CoverBrush(t, 112);
            barCover.Background = br ?? (Brush)Th.Card;
            barCover.Child = br == null ? U.Center(U.Ico("music", 20, Th.Dim)) : null;
            barTitle.Text = t.Title;
            barArtist.Text = t.Artist;
            var black = Th.B("#000000");
            barPlay.Child = U.Center(U.Ico(Player.I.IsPlaying ? "pause" : "play", 22, black, true));
            bool fav = Library.IsFav(t.Id);
            barFav.Child = U.Center(U.Ico("heart", 24, fav ? Th.Heart : Th.Txt, fav));
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
                s.toastText.Text = L.T(msg);
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
            if (e.Key == Key.F11) { (Window.GetWindow(this) as MainWindow)?.ToggleFullscreen(); e.Handled = true; return; }
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox)
            {
                if (e.Key == Key.Escape && SheetOpen) { CloseSheet(); e.Handled = true; }
                return;
            }
            switch (e.Key)
            {
                case Key.F11:
                    (Window.GetWindow(this) as MainWindow)?.ToggleFullscreen(); e.Handled = true; break;
                case Key.Escape:
                    if (SheetOpen) CloseSheet();
                    else if (Window.GetWindow(this) is MainWindow mw && mw.IsFullscreen && !Full.IsOpen) mw.ToggleFullscreen();
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
