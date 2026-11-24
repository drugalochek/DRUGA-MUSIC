using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sonora
{
    public sealed partial class Shell
    {
        TextBlock hourSub;
        string exploreFilter = "all";
        int exploreSeed = Environment.TickCount;
        Input searchInput;
        StackPanel searchResults;

        FrameworkElement BuildPage(string p, object arg)
        {
            hourSub = null;
            switch (p)
            {
                case "explore": return Explore();
                case "fav": return Fav();
                case "search": return SearchPage();
                case "artist": return ArtistPage(arg as string);
                case "playlist": return PlaylistPage(arg as string);
                case "artists": return LikedArtistsPage();
                case "profile": return ProfilePage();
                case "services": return ServicesPage();
                case "settings": return SettingsPage();
                default: return Home();
            }
        }

        // ---------- building blocks ----------
        static TextBlock PTitle(string s)
        {
            var t = U.Tx(s, 34);
            t.Margin = new Thickness(0, 6, 0, 18);
            return t;
        }
        static FrameworkElement SHead(string title, string sub, out TextBlock subBlock)
        {
            var t = U.Tx(title, 24);
            subBlock = U.Tx(sub ?? "", 12, Th.Dim);
            subBlock.VerticalAlignment = VerticalAlignment.Bottom;
            subBlock.Margin = new Thickness(8, 0, 0, 1);
            var g = U.Cols("* a", t, subBlock);
            g.Margin = new Thickness(0, 34, 0, 14);
            return g;
        }
        static FrameworkElement SHead(string title, string sub = null) { TextBlock _; return SHead(title, sub, out _); }
        static FrameworkElement Note(string s)
        {
            var t = U.Wrap(s, 12.5, Th.Dim);
            t.Margin = new Thickness(0, 14, 0, 14);
            return t;
        }
        static FrameworkElement Empty(string icon, string title, string text)
        {
            var ic = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(20), Background = Th.Card, BorderBrush = Th.Border, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14), Child = U.Center(U.Ico(icon, 26, Th.Dim)) };
            var t = U.Tx(title, 15);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 0, 0, 5);
            var d = U.Wrap(text, 13, Th.Dim, 0, 20);
            d.TextAlignment = TextAlignment.Center;
            var sp = U.Col(ic, t, d);
            sp.Margin = new Thickness(20, 40, 20, 40);
            return sp;
        }

        FrameworkElement HRow(IEnumerable<FrameworkElement> items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 6, 20, 10) };
            bool first = true;
            foreach (var i in items)
            {
                var w = new Border { Child = i, Margin = new Thickness(first ? 0 : 14, 0, 0, 0) };
                sp.Children.Add(w);
                first = false;
            }
            var sv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = sp, Focusable = false, PanningMode = PanningMode.HorizontalOnly };
            sv.PreviewMouseWheel += (s, e) =>
            {
                e.Handled = true;
                if (Keyboard.Modifiers == ModifierKeys.Shift) { sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta); return; }
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.6);
            };
            Point start = new Point(); double startOff = 0; bool pressed = false, dragging = false;
            sv.PreviewMouseLeftButtonDown += (s, e) => { pressed = true; dragging = false; start = e.GetPosition(sv); startOff = sv.HorizontalOffset; };
            sv.PreviewMouseMove += (s, e) =>
            {
                if (!pressed) return;
                if (e.LeftButton != MouseButtonState.Pressed) { pressed = false; dragging = false; return; }
                var dx = e.GetPosition(sv).X - start.X;
                if (!dragging && Math.Abs(dx) > 6) { dragging = true; sv.CaptureMouse(); }
                if (dragging) sv.ScrollToHorizontalOffset(startOff - dx);
            };
            sv.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (dragging) { e.Handled = true; sv.ReleaseMouseCapture(); }
                pressed = false; dragging = false;
            };

            var left = Arrow("chevL", () => sv.ScrollToHorizontalOffset(sv.HorizontalOffset - 420));
            var right = Arrow("chevR", () => sv.ScrollToHorizontalOffset(sv.HorizontalOffset + 420));
            left.HorizontalAlignment = HorizontalAlignment.Left;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            var g = new Grid { Margin = new Thickness(-16, 0, -16, 0) };
            g.Children.Add(sv);
            g.Children.Add(left);
            g.Children.Add(right);
            Action upd = () =>
            {
                bool hover = g.IsMouseOver;
                left.Visibility = hover && sv.HorizontalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
                right.Visibility = hover && sv.HorizontalOffset < sv.ScrollableWidth - 1 ? Visibility.Visible : Visibility.Collapsed;
            };
            g.MouseEnter += (s, e) => upd();
            g.MouseLeave += (s, e) => upd();
            sv.ScrollChanged += (s, e) => upd();
            return g;
        }
        static Border Arrow(string icon, Action a)
        {
            var b = U.IconBtn(icon, 36, 18, Th.Txt, a, Th.B("#1E1E2A", .92));
            b.BorderBrush = Th.BorderHi;
            b.BorderThickness = new Thickness(1);
            b.Margin = new Thickness(8, 0, 8, 30);
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Effect = U.Shadow(16, 4, .6);
            b.Visibility = Visibility.Collapsed;
            return b;
        }

        static FrameworkElement Pills(string[] ids, string[] names, string active, Action<string> pick)
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                bool on = id == active;
                var b = new Border
                {
                    Padding = new Thickness(14, 8, 14, 8),
                    CornerRadius = new CornerRadius(20),
                    Background = on ? Th.Soft : Th.Card,
                    BorderBrush = on ? Th.B("#8B5CF6", .4) : Th.Border,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 8),
                    Child = U.Tx(names[i], 12.5, on ? Th.Accent3 : Th.Dim)
                };
                wp.Children.Add(U.Click(b, () => pick(id), .95));
            }
            return wp;
        }

        public FrameworkElement TrackRow(Track t, List<Track> list, int idx, string playlistId = null)
        {
            bool playing = Player.I.Current != null && Player.I.Current.Id == t.Id;
            bool fav = Library.IsFav(t.Id);
            var cover = U.Cover(t, 46, 10, 18);
            if (playing)
            {
                var g0 = new Grid();
                cover.Child = null;
                g0.Children.Add(cover);
                g0.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Th.B("#000000", .35), Child = U.Center(U.Ico(Player.I.IsPlaying ? "pause" : "play", 16, Th.White)) });
                cover.Child = null;
                var holder = new Border { Width = 46, Height = 46, Child = g0 };
                return TrackRowCore(t, list, idx, holder, playing, fav, playlistId);
            }
            return TrackRowCore(t, list, idx, cover, playing, fav, playlistId);
        }
        FrameworkElement TrackRowCore(Track t, List<Track> list, int idx, FrameworkElement cover, bool playing, bool fav, string playlistId)
        {
            var title = U.Tx(t.Title, 13.5, playing ? Th.Accent3 : Th.Txt);
            title.Margin = new Thickness(0, 0, 0, 2);
            var meta = new StackPanel { Orientation = Orientation.Horizontal };
            var artistLine = U.Tx(t.Artist + (string.IsNullOrEmpty(t.Album) ? "" : " · " + t.Album), 11.5, Th.Dim);
            meta.Children.Add(artistLine);
            if (t.HasLyrics)
                meta.Children.Add(new Border { Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(7, 2, 7, 2), CornerRadius = new CornerRadius(9), Background = Th.B("#06B6D4", .2), VerticalAlignment = VerticalAlignment.Center, Child = U.Tx("ТЕКСТ", 9, Th.Accent3) });
            if (t.IsLocal)
                meta.Children.Add(new Border { Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(7, 2, 7, 2), CornerRadius = new CornerRadius(9), Background = Th.B("#10B981", .18), VerticalAlignment = VerticalAlignment.Center, Child = U.Tx("ПК", 9, Th.Ok) });
            var info = U.Col(title, meta);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(12, 0, 8, 0);

            var dur = U.Tx(t.Duration > 0 ? U.Time(t.Duration) : "", 11.5, Th.Dim);
            dur.VerticalAlignment = VerticalAlignment.Center;
            dur.Margin = new Thickness(0, 0, 6, 0);
            var favBtn = U.IconBtn("heart", 32, 18, fav ? Th.Heart : Th.Dim, () => Library.ToggleFav(t.Id), null, fav);
            var menuBtn = U.IconBtn("dots", 32, 16, Th.Dim, () => TrackMenu(t, playlistId));
            var acts = U.Row(dur, favBtn, menuBtn);
            acts.VerticalAlignment = VerticalAlignment.Center;

            var g = U.Cols("46 * a", cover, info, acts);
            var row = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6, 7, 6, 7),
                BorderThickness = new Thickness(1),
                BorderBrush = playing ? Th.B("#8B5CF6", .3) : Th.Clear,
                Child = g
            };
            if (playing) row.Background = Th.Soft;
            else U.Hover(row, Th.Clear, Th.Card);
            return U.Click(row, () =>
            {
                if (playing) { OpenFull(); return; }
                Player.I.PlayList(list, idx);
                OpenFull();
            }, .99);
        }
        StackPanel TrackList(List<Track> list, int max = int.MaxValue, string playlistId = null)
        {
            var sp = new StackPanel();
            for (int i = 0; i < list.Count && i < max; i++) sp.Children.Add(TrackRow(list[i], list, i, playlistId));
            return sp;
        }

        FrameworkElement ArtistCard(ArtistInfo a)
        {
            var img = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(42), Effect = U.Shadow(20, 8, .55) };
            var br = U.CoverBrush(a.CoverTrack, 200);
            if (br != null) img.Background = br;
            else { img.Background = Th.Hot; img.Child = U.Center(U.Ico("user", 30, Th.B("#FFFFFF", .85))); }
            var top = new Grid { Width = 90, Height = 84 };
            img.HorizontalAlignment = HorizontalAlignment.Center;
            top.Children.Add(img);
            if (Library.IsFavArtist(a.Name))
                top.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Th.Pink, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 6, 0), Effect = U.Shadow(8, 2, .5), Child = U.Center(U.Ico("heartFill", 11, Th.White)) });
            var name = U.Wrap(a.Name, 12, Th.Txt, 2, 15);
            name.TextAlignment = TextAlignment.Center;
            name.Width = 90;
            name.Margin = new Thickness(0, 8, 0, 0);
            var sp = U.Col(top, name);
            sp.Width = 90;
            return U.Click(new Border { Child = sp }, () => Go("artist", a.Name), .95);
        }

        static FrameworkElement Mosaic(List<Track> tracks, double size, double radius, Brush fallback, string icon, double iconSize)
        {
            var g = new Grid { Width = size, Height = size, Background = fallback };
            var withCovers = tracks.Where(t => !string.IsNullOrEmpty(t.Cover)).Take(4).ToList();
            if (withCovers.Count >= 4 || (withCovers.Count > 0 && tracks.Count >= 4))
            {
                g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
                var picks = tracks.Take(4).ToList();
                for (int i = 0; i < 4; i++)
                {
                    var cell = new Border { Background = Th.B("#222222") };
                    if (i < picks.Count)
                    {
                        var br = U.CoverBrush(picks[i], (int)size);
                        if (br != null) cell.Background = br;
                        else cell.Child = U.Center(U.Ico("music", 26, Th.B("#FFFFFF", .3)));
                    }
                    else cell.Background = Th.G(Th.C("#8b5cf6", .4), Th.C("#06b6d4", .4));
                    Grid.SetRow(cell, i / 2);
                    Grid.SetColumn(cell, i % 2);
                    g.Children.Add(cell);
                }
            }
            else if (withCovers.Count > 0)
            {
                g.Background = U.CoverBrush(withCovers[0], (int)(size * 1.5));
            }
            else g.Children.Add(U.Center(U.Ico(icon, iconSize, Th.B("#FFFFFF", .8))));
            U.Fixed(g, size, size, radius);
            return g;
        }

        FrameworkElement MixCard(Mix m, double size = 200)
        {
            var cov = new Grid { Width = size, Height = size };
            var shadowHolder = new Border { CornerRadius = new CornerRadius(16), Background = Th.Hot, Effect = U.Shadow(32, 14, .7) };
            cov.Children.Add(shadowHolder);
            cov.Children.Add(Mosaic(m.Tracks, size, 16, Th.Hot, m.Icon, 70));
            var stripeHost = new Grid { Width = size, Height = size, IsHitTestVisible = false };
            stripeHost.Children.Add(TopShade(size * .5));
            stripeHost.Children.Add(CardKicker(m.Tag, size >= 180 ? 22 : 17));
            stripeHost.Children.Add(CardStripe(m.Name));
            U.Fixed(stripeHost, size, size, 16);
            cov.Children.Add(stripeHost);
            var play = U.Circle(46, Th.White, U.Center(U.Ico("play", 18, Th.B("#000000"))));
            play.HorizontalAlignment = HorizontalAlignment.Right;
            play.VerticalAlignment = VerticalAlignment.Bottom;
            play.Margin = new Thickness(0, 0, 10, 40);
            U.Click(play, () => { if (m.Tracks.Count > 0) { Player.I.PlayList(m.Tracks, 0); OpenFull(); } }, .9);
            cov.Children.Add(play);
            var t = U.Wrap(m.Name, 14, Th.Txt, 2, 17.5);
            t.Margin = new Thickness(0, 10, 0, 3);
            var d = U.Wrap(m.Sub ?? "", 11.5, Th.Dim, 2, 15.5);
            var sp = U.Col(cov, t, d);
            sp.Width = size;
            return U.Click(new Border { Child = sp }, () => Go("playlist", m.Id), .97);
        }

        FrameworkElement PctCard(string name, List<Track> tracks)
        {
            const double size = 200;
            var cov = new Grid { Width = size, Height = size };
            cov.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = Th.Hot, Effect = U.Shadow(32, 14, .7) });
            var inner = new Grid { Width = size, Height = size };
            var coverTrack = tracks.FirstOrDefault(x => !string.IsNullOrEmpty(x.Cover));
            var br = U.CoverBrush(coverTrack, 400);
            if (br != null) inner.Background = br;
            else { inner.Background = Th.Brand; inner.Children.Add(U.Center(U.Ico("user", 60, Th.White))); }
            inner.Children.Add(new Border { Height = size * .5, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = Th.G(Th.C("#000000", .62), Th.C("#000000", 0), false) });
            var pct = U.Tx("100%", 26, Th.White);
            pct.FontFamily = Th.Font;
            pct.FontWeight = FontWeights.Black;
            pct.Margin = new Thickness(12, 10, 0, 0);
            pct.HorizontalAlignment = HorizontalAlignment.Left;
            pct.VerticalAlignment = VerticalAlignment.Top;
            pct.Effect = U.Shadow(10, 2, .85);
            inner.Children.Add(pct);
            inner.Children.Add(CardStripe(name));
            U.Fixed(inner, size, size, 16);
            cov.Children.Add(inner);
            var t = U.Tx("100% " + name, 13.5);
            t.Margin = new Thickness(0, 10, 0, 3);
            var d = U.Tx(U.Tn(tracks.Count), 11.5, Th.Dim);
            var sp = U.Col(cov, t, d);
            sp.Width = size;
            return U.Click(new Border { Child = sp }, () => Go("playlist", "__pct_" + name), .97);
        }

        FrameworkElement PlaylistCard(Playlist p)
        {
            var tracks = p.TrackIds.Select(Catalog.Get).Where(x => x != null).ToList();
            var cov = new Grid { Width = 140, Height = 140 };
            cov.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Th.Card, Effect = U.Shadow(24, 10, .6) });
            var bg = Th.G(p.Color ?? "#8b5cf6", Th.Shade(p.Color ?? "#8b5cf6", -30));
            var pcb = PlCoverBrush(p, 280);
            if (pcb != null) { var pc = new Border { Width = 140, Height = 140, Background = pcb }; U.Fixed(pc, 140, 140, 14); cov.Children.Add(pc); }
            else cov.Children.Add(Mosaic(tracks, 140, 14, bg, "music", 40));
            var ps = new Grid { Width = 140, Height = 140, IsHitTestVisible = false };
            ps.Children.Add(TopShade(64, .55));
            ps.Children.Add(CardStripe(null));
            U.Fixed(ps, 140, 140, 14);
            cov.Children.Add(ps);
            var del = U.IconBtn("close", 28, 14, Th.White, () => ConfirmDeletePlaylist(p), Th.B("#000000", .6));
            del.HorizontalAlignment = HorizontalAlignment.Right;
            del.VerticalAlignment = VerticalAlignment.Top;
            del.Margin = new Thickness(0, 6, 6, 0);
            del.Opacity = .75;
            cov.Children.Add(del);
            var t = U.Wrap(p.Name, 13.5, Th.Txt, 2, 17);
            t.Margin = new Thickness(0, 10, 0, 4);
            var m = U.Tx(U.Tn(tracks.Count), 11, Th.Dim);
            var sp = U.Col(cov, t, m);
            sp.Width = 140;
            return U.Click(new Border { Child = sp }, () => Go("playlist", p.Id), .97);
        }
        FrameworkElement NewPlaylistCard()
        {
            var cov = new Border { Width = 140, Height = 140, CornerRadius = new CornerRadius(14), Background = Th.Card, BorderBrush = Th.BorderHi, BorderThickness = new Thickness(1.5), Child = U.Center(U.Ico("plus", 40, Th.Accent)) };
            var dash = new System.Windows.Shapes.Rectangle { RadiusX = 14, RadiusY = 14, Stroke = Th.BorderHi, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Width = 140, Height = 140 };
            cov.BorderThickness = new Thickness(0);
            var g = new Grid();
            g.Children.Add(cov);
            g.Children.Add(dash);
            var t = U.Tx("Новый плейлист", 13.5);
            t.Margin = new Thickness(0, 10, 0, 4);
            var sp = U.Col(g, t);
            sp.Width = 140;
            return U.Click(new Border { Child = sp }, () => NewPlaylistSheet(null), .97);
        }

        static FrameworkElement LRow(FrameworkElement cover, string title, string meta, Action click)
        {
            var t = U.Tx(title, 14);
            t.Margin = new Thickness(0, 0, 0, 3);
            var info = U.Col(t, U.Tx(meta ?? "", 11.5, Th.Dim));
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(12, 0, 12, 0);
            var ch = U.Ico("chevR", 15, Th.Dim);
            ch.VerticalAlignment = VerticalAlignment.Center;
            var g = U.Cols("50 * a", cover, info, ch);
            var b = new Border { Padding = new Thickness(4, 9, 4, 9), CornerRadius = new CornerRadius(12), Child = g };
            U.Hover(b, Th.Clear, Th.Card);
            return U.Click(b, click, .99);
        }
        static Border LCover(Brush bg, string icon, bool round = false, Brush img = null)
        {
            var b = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(round ? 25 : 10), Background = img ?? bg };
            if (img == null) b.Child = U.Center(U.Ico(icon, 20, Th.White));
            return b;
        }

        FrameworkElement BackBtn() { return new Border { Height = 0 }; }

        // полоска внизу обложки — фирменный синий градиент; текст (если есть) фирменным шрифтом
        // затемнение сверху, как на карточках «100%»
        static Border TopShade(double h, double a = .62)
        {
            return new Border { Height = h, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = Th.G(Th.C("#000000", a), Th.C("#000000", 0), false) };
        }
        static TextBlock CardKicker(string text, double size)
        {
            var k = new TextBlock { Text = L.T(text ?? ""), FontFamily = Th.Font, FontSize = size, FontWeight = FontWeights.Black, Foreground = Th.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 10, 12, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Effect = U.Shadow(10, 2, .85) };
            return k;
        }
        public static Brush PlCoverBrush(Playlist p, int px)
        {
            if (p == null || string.IsNullOrEmpty(p.Cover)) return null;
            var img = Img.FromData("pl:" + p.Id + ":" + p.Cover.Length + ":" + p.Cover.GetHashCode(), p.Cover, px);
            return Img.MakeBrush(img);
        }

        static Border CardStripe(string text, double h = 30)
        {
            var b = new Border { Height = text == null ? 6 : h, VerticalAlignment = VerticalAlignment.Bottom, Background = Th.GH("#60A5FA", "#1D4ED8"), Padding = new Thickness(12, 0, 12, 0), IsHitTestVisible = false };
            if (text != null)
            {
                var t = new TextBlock { Text = L.T(text).ToUpperInvariant(), FontFamily = Th.Font, FontSize = 12.5, Foreground = Th.White, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
                b.Child = t;
            }
            return b;
        }

        // ---------- Home ----------
        FrameworkElement Home()
        {
            var sp = new StackPanel();

            if (Auth.Current == null)
            {
                var chk = U.Circle(22, Th.Accent, U.Center(U.Ico("user", 12, Th.White)));
                var txt = U.Wrap("Вы в гостевом режиме — данные сохраняются только на этом устройстве.", 13, Th.Txt);
                txt.VerticalAlignment = VerticalAlignment.Center;
                txt.Margin = new Thickness(12, 0, 8, 0);
                var login = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 7, 12, 7), Background = Th.White, VerticalAlignment = VerticalAlignment.Center, Child = U.Tx("Войти", 13, Th.B("#000000")) };
                U.Click(login, OpenWelcome, .95);
                var g = U.Cols("a * a", chk, txt, login);
                sp.Children.Add(new Border { Padding = new Thickness(14, 12, 12, 12), CornerRadius = new CornerRadius(12), Background = Th.Glass(.045), BorderBrush = Th.GlassEdge, BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 12), Child = g });
            }

            if (!Catalog.Loaded)
            {
                sp.Children.Add(Empty("music", "Загружаем каталог…", "Подключаемся к серверу."));
                return sp;
            }

            var favArtists = Library.D.FavArtists.Select(Mixes.Artist).ToList();
            if (favArtists.Count > 0)
            {
                sp.Children.Add(SHead("Любимые исполнители", favArtists.Count.ToString()));
                sp.Children.Add(HRow(favArtists.Select(ArtistCard)));
            }

            var hour = Mixes.GenreOfHour();
            if (hour != null)
            {
                sp.Children.Add(SHead("Жанр получаса", "Каждые 30 минут новый жанр · " + Mixes.HourCountdown(), out hourSub));
                sp.Children.Add(HRow(new[] { MixCard(hour) }));
            }

            var mixes = Mixes.ForYou();
            sp.Children.Add(SHead("Миксы для тебя", mixes.Count > 0 ? "Из любимых" : ""));
            if (mixes.Count == 0) sp.Children.Add(Note("Лайкай треки и исполнителей ♥ — они появятся здесь."));
            else sp.Children.Add(HRow(mixes.Select(m => MixCard(m))));

            var pct = Mixes.Pct();
            sp.Children.Add(SHead("100% для вас"));
            if (pct.Count == 0) sp.Children.Add(Note("Лайкай треки и исполнителей ♥ — они появятся здесь."));
            else sp.Children.Add(HRow(pct.Select(p => PctCard(p.Key, p.Value))));

            var fresh = Mixes.Fresh();
            sp.Children.Add(SHead("Новинки для вас", fresh.Count > 0 ? U.Tn(fresh.Count) : ""));
            if (fresh.Count == 0)
            {
                sp.Children.Add(Note("Свежих треков пока нет"));
                var popular = Catalog.All.Where(t => !t.IsLocal).OrderByDescending(t => t.Plays).Take(8).ToList();
                if (popular.Count > 0)
                {
                    sp.Children.Add(SHead("Популярное", U.Tn(popular.Count)));
                    sp.Children.Add(TrackList(popular));
                }
            }
            else sp.Children.Add(TrackList(fresh));
            return sp;
        }

        // ---------- Explore ----------
        FrameworkElement Explore()
        {
            var sp = new StackPanel();
            sp.Children.Add(PTitle("Исследовать"));
            var artists = Mixes.Artists(Catalog.All);
            sp.Children.Add(SHead("Все исполнители", artists.Count > 0 ? artists.Count.ToString() : ""));
            if (artists.Count == 0) sp.Children.Add(Note("—"));
            else sp.Children.Add(HRow(artists.Select(ArtistCard)));
            sp.Children.Add(new Border { Height = 12 });
            sp.Children.Add(Pills(new[] { "all", "recent", "long", "short", "random" }, new[] { "Все треки", "Недавние", "Долгие", "Короткие", "Случайно" }, exploreFilter, id =>
            {
                exploreFilter = id;
                if (id == "random") exploreSeed = Environment.TickCount;
                Render();
            }));
            var list = Catalog.All.ToList();
            var title = "Все треки";
            switch (exploreFilter)
            {
                case "recent": list = list.OrderByDescending(t => t.AddedAt).ToList(); title = "Недавние"; break;
                case "long": list = list.OrderByDescending(t => t.Duration).ToList(); title = "Долгие"; break;
                case "short": list = list.OrderBy(t => t.Duration).ToList(); title = "Короткие"; break;
                case "random": list = Mixes.Shuffle(list, "r" + exploreSeed); title = "Случайно"; break;
            }
            var head = SHead(title, U.Tn(list.Count));
            head.Margin = new Thickness(0, 4, 0, 10);
            sp.Children.Add(head);
            if (list.Count == 0) sp.Children.Add(Empty("music", "Пока пусто", "Каталог ещё загружается или недоступен."));
            else sp.Children.Add(TrackList(list));
            return sp;
        }

        // ---------- Favorites ----------
        FrameworkElement Fav()
        {
            var sp = new StackPanel();
            sp.Children.Add(PTitle("Избранное"));
            var favs = Mixes.FavTracks();
            sp.Children.Add(LRow(LCover(Th.Pink, "heartFill"), "Любимые треки", U.Tn(favs.Count), () => Go("playlist", Mixes.Liked)));
            sp.Children.Add(LRow(LCover(Th.Brand, "user", true), "Любимые исполнители", Library.D.FavArtists.Count + " " + U.Plural(Library.D.FavArtists.Count, "исполнитель", "исполнителя", "исполнителей"), () => Go("artists")));
            var recent = Library.D.Recent.Count(id => Catalog.Get(id) != null);
            sp.Children.Add(LRow(LCover(Th.G("#8b5cf6", "#06b6d4"), "clock"), "Недавно прослушанные", U.Tn(recent), () => Go("playlist", Mixes.Recent)));
            sp.Children.Add(LRow(LCover(Th.G("#2DD4BF", "#2563EB"), "download"), "Скачанные", Library.D.Offline.Count > 0 ? U.Tn(Library.D.Offline.Count) + " · офлайн" : "Слушай без интернета", () => Go("playlist", Mixes.Downloaded)));

            sp.Children.Add(SHead("Любимые треки", favs.Count > 0 ? U.Tn(favs.Count) : ""));
            if (favs.Count == 0) sp.Children.Add(Note("Лайкай треки и исполнителей ♥ — они появятся здесь."));
            else sp.Children.Add(TrackList(favs, 5));

            sp.Children.Add(SHead("Плейлисты", Library.D.Playlists.Count.ToString()));
            sp.Children.Add(HRow(Library.D.Playlists.Select(PlaylistCard).Concat(new[] { NewPlaylistCard() })));

            var local = Library.LocalTracks();
            sp.Children.Add(SHead("Загруженная музыка", local.Count > 0 ? U.Tn(local.Count) : ""));
            var addBtn = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 14, 16, 14), Background = Th.Card, BorderBrush = Th.Border, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
            var addTxt = U.Col(U.Tx("Добавить музыку с компьютера", 14), U.M(U.Tx("Нажми или перетащи файлы в окно · MP3, M4A, WAV, FLAC, WMA", 11.5, Th.Dim), 0, 3, 0, 0));
            addTxt.Margin = new Thickness(12, 0, 0, 0);
            addTxt.VerticalAlignment = VerticalAlignment.Center;
            addBtn.Child = U.Cols("a *", U.Circle(40, Th.Soft, U.Center(U.Ico("upload", 20, Th.Accent3))), addTxt);
            U.Hover(addBtn, Th.Card, Th.Card2);
            sp.Children.Add(U.Click(addBtn, PickFiles, .99));
            if (local.Count > 0)
            {
                sp.Children.Add(TrackList(local, 5, Mixes.Uploaded));
                if (local.Count > 5) sp.Children.Add(LRow(LCover(Th.G("#06b6d4", "#10b981"), "upload"), "Вся загруженная музыка", U.Tn(local.Count), () => Go("playlist", Mixes.Uploaded)));
            }
            return sp;
        }
        public void PickFiles()
        {
            if (!Guard.Need("Загрузка своей музыки")) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = L.T("Добавить музыку"), Filter = L.T("Аудио|*.mp3;*.m4a;*.aac;*.wav;*.wma;*.flac;*.mp4|Все файлы|*.*") };
            if (dlg.ShowDialog() == true) AddFilesUi(dlg.FileNames);
        }

        // ---------- Search ----------
        FrameworkElement SearchPage()
        {
            var sp = new StackPanel();
            sp.Children.Add(PTitle("Поиск"));
            if (searchInput == null)
            {
                searchInput = new Input("Треки, исполнители, плейлисты...", false, "search");
                searchInput.CornerRadius = new CornerRadius(14);
                searchInput.Padding = new Thickness(15, 12, 15, 12);
                searchInput.Margin = new Thickness(0, 0, 0, 20);
                searchInput.Changed += () => FillSearch();
                searchResults = new StackPanel();
            }
            if (searchInput.Parent is Panel p1) p1.Children.Remove(searchInput);
            if (searchResults.Parent is Panel p2) p2.Children.Remove(searchResults);
            sp.Children.Add(searchInput);
            sp.Children.Add(searchResults);
            FillSearch();
            if (!ShotMode) Dispatcher.BeginInvoke(new Action(() => searchInput.Focus2()), System.Windows.Threading.DispatcherPriority.Input);
            return sp;
        }
        public void SetSearch(string q) { if (searchInput == null) SearchPage(); searchInput.Text = q; }
        void FillSearch()
        {
            var box = searchResults;
            box.Children.Clear();
            var q = (searchInput.Text ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) { box.Children.Add(Empty("search", "Начни вводить", "Поиск по трекам, исполнителям и плейлистам. Можно вставить ссылку на трек.")); return; }
            var sharedId = Share.Parse(searchInput.Text);
            if (sharedId != null)
            {
                var st = Catalog.Get(sharedId);
                if (st == null) { box.Children.Add(Empty("link", "Трек по ссылке не найден", Catalog.Loaded ? "Возможно, его удалили из каталога." : "Каталог ещё загружается — попробуй через пару секунд.")); return; }
                box.Children.Add(SHead("Трек по ссылке"));
                box.Children.Add(TrackList(new List<Track> { st }));
                return;
            }
            Func<string, bool> has = s => !string.IsNullOrEmpty(s) && s.ToLowerInvariant().Contains(q);
            var tr = Catalog.All.Where(t => has(t.Title) || has(t.Artist) || has(t.Album) || Mixes.Genres(t.Genre).Any(g => g.Contains(q))).ToList();
            var ar = Mixes.Artists(Catalog.All.Where(t => has(t.Artist))).ToList();
            var pl = Library.D.Playlists.Where(p => has(p.Name)).ToList();
            bool none = tr.Count == 0 && ar.Count == 0 && pl.Count == 0;
            var extBox = new StackPanel();
            if (none) { box.Children.Add(extBox); if (q.Length >= 2) ExtSearch(q, extBox, true); else extBox.Children.Add(Empty("search", "Ничего не нашлось", "Попробуй изменить запрос.")); return; }
            if (ar.Count > 0)
            {
                box.Children.Add(SHead("Исполнители", ar.Count.ToString()));
                foreach (var a in ar.Take(6))
                    box.Children.Add(LRow(LCover(Th.Hot, "user", true, U.CoverBrush(a.CoverTrack, 100)), a.Name, U.Tn(a.Count), () => Go("artist", a.Name)));
            }
            if (tr.Count > 0)
            {
                box.Children.Add(SHead("Треки", U.Tn(tr.Count)));
                box.Children.Add(TrackList(tr, 50));
            }
            if (pl.Count > 0)
            {
                box.Children.Add(SHead("Плейлисты", pl.Count.ToString()));
                foreach (var p in pl)
                    box.Children.Add(LRow(LCover(Th.G(p.Color ?? "#8b5cf6", Th.Shade(p.Color ?? "#8b5cf6", -30)), "music"), p.Name, U.Tn(p.TrackIds.Count), () => Go("playlist", p.Id)));
            }
            box.Children.Add(extBox);
            if (q.Length >= 2 && !ShotMode) ExtSearch(q, extBox, false);
        }

        // ---------- Artist ----------
        FrameworkElement ArtistPage(string name)
        {
            var sp = new StackPanel();
            sp.Children.Add(BackBtn());
            if (string.IsNullOrEmpty(name)) return sp;
            var tracks = Catalog.All.Where(t => t.Artist == name).ToList();
            var a = Mixes.Artist(name);
            var img = new Border { Width = 140, Height = 140, CornerRadius = new CornerRadius(70), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 16), Effect = U.Shadow(44, 16, .7) };
            var br = U.CoverBrush(a.CoverTrack, 300);
            if (br != null) img.Background = br; else { img.Background = Th.Hot; img.Child = U.Center(U.Ico("user", 50, Th.B("#FFFFFF", .85))); }
            sp.Children.Add(img);
            var nameRow = U.Row(U.Tx(name, 24));
            nameRow.HorizontalAlignment = HorizontalAlignment.Center;
            if (a.Verified)
                nameRow.Children.Add(new Border { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), CornerRadius = new CornerRadius(12), Background = Th.Hot, VerticalAlignment = VerticalAlignment.Center, Child = U.Row(U.Ico("check", 10, Th.White, null, 3), U.M(U.Tx("АРТИСТ", 10, Th.White), 4, 0, 0, 0)) });
            sp.Children.Add(U.M(nameRow, 0, 0, 0, 6));
            var albums = tracks.Select(t => t.Album).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count();
            var meta = U.Tx(U.Tn(tracks.Count) + (albums > 0 ? " · " + albums + " " + U.Plural(albums, "альбом", "альбома", "альбомов") : "") + " · " + tracks.Sum(t => t.Plays) + " прослушиваний", 12.5, Th.Dim);
            meta.HorizontalAlignment = HorizontalAlignment.Center;
            meta.Margin = new Thickness(0, 0, 0, 16);
            sp.Children.Add(meta);

            var play = new Border { Padding = new Thickness(28, 12, 28, 12), CornerRadius = new CornerRadius(24), Background = Th.Hot, Effect = U.Shadow(24, 8, .8, Th.C("#8b5cf6")), Child = U.Row(U.Ico("play", 16, Th.White), U.M(U.Tx("Слушать всё", 14, Th.White), 8, 0, 0, 0)), Margin = new Thickness(0, 0, 10, 0) };
            U.Click(play, () => { if (tracks.Count > 0) { Player.I.PlayList(tracks, 0); OpenFull(); } }, .96);
            bool liked = Library.IsFavArtist(name);
            var like = new Border { Padding = new Thickness(22, 12, 22, 12), CornerRadius = new CornerRadius(24), Background = liked ? (Brush)Th.Pink : Th.Card, BorderBrush = liked ? Th.Clear : Th.BorderHi, BorderThickness = new Thickness(1), Child = U.Row(U.Ico("heart", 18, liked ? Th.White : Th.Txt, liked), U.M(U.Tx(liked ? "В любимых" : "В любимые", 14, liked ? Th.White : Th.Txt), 8, 0, 0, 0)) };
            U.Click(like, () => Library.ToggleFavArtist(name), .96);
            var acts = U.Row(play, like);
            acts.HorizontalAlignment = HorizontalAlignment.Center;
            acts.Margin = new Thickness(0, 0, 0, 20);
            sp.Children.Add(acts);
            sp.Children.Add(SHead("Треки", U.Tn(tracks.Count)));
            sp.Children.Add(TrackList(tracks));
            var albumList = tracks.Where(t => !string.IsNullOrEmpty(t.Album)).GroupBy(t => t.Album).ToList();
            if (albumList.Count > 0)
            {
                sp.Children.Add(SHead("Альбомы", albumList.Count.ToString()));
                foreach (var g in albumList)
                {
                    var list = g.ToList();
                    var first = list.First();
                    sp.Children.Add(LRow(LCover(Th.Hot, "music", false, U.CoverBrush(list.FirstOrDefault(x => !string.IsNullOrEmpty(x.Cover)), 100)), g.Key, (first.Year > 0 ? first.Year + " · " : "") + U.Tn(list.Count), () => { Player.I.PlayList(list, 0); OpenFull(); }));
                }
            }
            return sp;
        }

        FrameworkElement LikedArtistsPage()
        {
            var sp = new StackPanel();
            sp.Children.Add(BackBtn());
            var cover = new Border { Width = 220, Height = 220, CornerRadius = new CornerRadius(20), Background = Th.Brand, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 20), Effect = U.Shadow(50, 20, .8), Child = U.Center(U.Ico("user", 100, Th.White)) };
            sp.Children.Add(cover);
            var n = U.Tx("Любимые исполнители", 24); n.HorizontalAlignment = HorizontalAlignment.Center; n.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(n);
            var list = Library.D.FavArtists.Select(Mixes.Artist).ToList();
            var m = U.Tx(list.Count + " " + U.Plural(list.Count, "исполнитель", "исполнителя", "исполнителей"), 13, Th.Dim); m.HorizontalAlignment = HorizontalAlignment.Center; m.Margin = new Thickness(0, 0, 0, 22);
            sp.Children.Add(m);
            if (list.Count == 0) sp.Children.Add(Empty("user", "Пока никого", "Открой исполнителя и нажми «В любимые»."));
            foreach (var a in list)
                sp.Children.Add(LRow(LCover(Th.Hot, "user", true, U.CoverBrush(a.CoverTrack, 100)), a.Name, U.Tn(a.Count), () => Go("artist", a.Name)));
            return sp;
        }

        // ---------- Playlist / mix ----------
        FrameworkElement PlaylistPage(string id)
        {
            bool ext = id != null && id.StartsWith("ext:");
            Mix mix;
            if (ext) { if (!Ext.Lists.TryGetValue(id, out mix)) return ExtListLoading(id); }
            else mix = Mixes.Get(id);
            var sp = new StackPanel();
            sp.Children.Add(BackBtn());
            if (mix == null) { sp.Children.Add(Empty("music", "Плейлист не найден", "Возможно, он был удалён.")); return sp; }
            var pl = Library.D.Playlists.FirstOrDefault(p => p.Id == id);
            var grad = Th.G(mix.From ?? "#8b5cf6", mix.To ?? "#06b6d4");
            var head = new Grid { Width = 220, Height = 220, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 20) };
            head.Children.Add(new Border { CornerRadius = new CornerRadius(20), Background = grad, Effect = U.Shadow(50, 20, .8) });
            bool iconCover = (ext && id.EndsWith(":likes")) || id == Mixes.Liked || id.StartsWith("__mix_") || id == "__hour_genre__" || id == Mixes.Recent || id == Mixes.Uploaded || id == Mixes.Downloaded;
            var plb = PlCoverBrush(pl, 440);
            if (plb != null) { var pc = new Border { Width = 220, Height = 220, Background = plb }; U.Fixed(pc, 220, 220, 20); head.Children.Add(pc); }
            else if (iconCover)
            {
                var c = new Border { CornerRadius = new CornerRadius(20), Background = grad, Child = U.Center(U.Ico(id == Mixes.Liked || ext && id.EndsWith(":likes") ? "heartFill" : mix.Icon, 100, Th.White)) };
                head.Children.Add(c);
            }
            else head.Children.Add(Mosaic(mix.Tracks, 220, 20, grad, mix.Icon, 70));
            sp.Children.Add(head);
            var tag = new Border { HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(8), Background = Th.B("#3B82F6", .16), Margin = new Thickness(0, 0, 0, 6), Child = U.Tx((mix.Tag ?? "").ToUpperInvariant(), 10, Th.Accent3) };
            sp.Children.Add(tag);
            var name = U.Wrap(mix.Name, 24, Th.Txt);
            name.TextAlignment = TextAlignment.Center;
            name.Margin = new Thickness(12, 0, 12, 6);
            sp.Children.Add(name);
            var total = mix.Tracks.Sum(t => t.Duration);
            var meta = U.Tx(U.Tn(mix.Tracks.Count) + (total > 0 ? " · " + U.Time(total) : "") + (string.IsNullOrEmpty(mix.Sub) || mix.Sub == U.Tn(mix.Tracks.Count) ? "" : " · " + mix.Sub), 13, Th.Dim);
            meta.HorizontalAlignment = HorizontalAlignment.Center;
            meta.Margin = new Thickness(0, 0, 0, 22);
            sp.Children.Add(meta);

            var play = new Border { Padding = new Thickness(24, 13, 24, 13), CornerRadius = new CornerRadius(26), Background = Th.Hot, Effect = U.Shadow(26, 10, .6, Th.C("#2563EB")), Child = U.Row(U.Ico("play", 17, Th.White), U.M(U.Tx("Слушать всё", 14, Th.White), 8, 0, 0, 0)) };
            U.Click(play, () => { if (mix.Tracks.Count > 0) { Player.I.PlayList(mix.Tracks, 0); OpenFull(); } }, .96);
            var shuffle = RoundBtn("shuffle", () => { if (mix.Tracks.Count > 0) { Player.I.PlayList(Mixes.Shuffle(mix.Tracks, "s" + Environment.TickCount), 0); OpenFull(); } });
            var items = new List<UIElement> { play, U.M(shuffle, 10, 0, 0, 0) };
            if (id != Mixes.Liked && mix.Tracks.Count > 0)
            {
                bool allFav = mix.Tracks.All(t => Library.IsFav(t.Id));
                var likeAll = U.IconBtn("heart", 50, 20, allFav ? Th.Heart : Th.Txt, () =>
                {
                    int n = Library.SetFavAll(mix.Tracks.Select(t => t.Id), !allFav);
                    if (n < 0) return;
                    Toast(allFav ? "Убрано из «Мне нравится»: " + U.Tn(n) : n == 0 ? "Все треки уже в «Мне нравится»" : "Добавлено в «Мне нравится»: " + U.Tn(n));
                    Render();
                }, Th.Card, allFav);
                likeAll.BorderBrush = Th.BorderHi; likeAll.BorderThickness = new Thickness(1);
                likeAll.ToolTip = allFav ? "Убрать все из «Мне нравится»" : "Лайкнуть всё";
                items.Add(U.M(likeAll, 10, 0, 0, 0));
            }
            if (pl != null) items.Add(U.M(RoundBtn("edit", () => NewPlaylistSheet(pl)), 10, 0, 0, 0));
            if (id == Mixes.Uploaded) items.Add(U.M(RoundBtn("plus", PickFiles), 10, 0, 0, 0));
            if (id != Mixes.Downloaded && mix.Tracks.Any(Offline.CanDownload))
            {
                bool allDone = mix.Tracks.Where(Offline.CanDownload).All(t => Offline.Has(t.Id));
                items.Add(U.M(RoundBtn(allDone ? "check" : "download", () => { if (!Guard.Need("Скачивание треков")) return; Offline.DownloadAll(mix.Tracks); }), 10, 0, 0, 0));
            }
            if (ext)
            {
                items.Add(U.M(RoundBtn("plus", () => SaveExtList(mix)), 10, 0, 0, 0));
                items.Add(U.M(RoundBtn("refresh", () => RefreshExtList(id)), 10, 0, 0, 0));
            }
            var left = U.Row(items.ToArray());
            FrameworkElement del = null;
            if (pl != null)
            {
                var d = U.IconBtn("trash", 50, 20, Th.Dim, () => ConfirmDeletePlaylist(pl));
                d.BorderBrush = Th.BorderHi; d.BorderThickness = new Thickness(1);
                del = d;
            }
            var actions = U.Cols("a *", left, del);
            if (del != null) del.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 0, 0, 22);
            sp.Children.Add(actions);
            if (mix.Tracks.Count == 0)
            {
                sp.Children.Add(id == Mixes.Uploaded
                    ? Empty("upload", "Здесь пока пусто", "Перетащи аудиофайлы в окно или нажми «+».")
                    : Empty("music", "Пока пусто", ext ? "В этом списке сервиса нет треков." : pl != null ? "Добавляй треки через меню «⋮» у трека." : "Лайкай треки и исполнителей ♥ — они появятся здесь."));
            }
            else sp.Children.Add(TrackList(mix.Tracks, int.MaxValue, pl != null ? pl.Id : (id == Mixes.Uploaded ? id : null)));
            return sp;
        }
        static Border RoundBtn(string icon, Action a)
        {
            var b = U.IconBtn(icon, 50, 20, Th.Txt, a, Th.Card);
            b.BorderBrush = Th.BorderHi;
            b.BorderThickness = new Thickness(1);
            return b;
        }

        internal static FrameworkElement SetTitle(string s)
        {
            var t = U.Tx(s.ToUpperInvariant(), 11, Th.Dim);
            t.Margin = new Thickness(0, 22, 0, 10);
            return t;
        }
        internal static FrameworkElement SetBtn(string icon, string text, string right, Action a, Brush color = null)
        {
            var ic = U.Ico(icon, 20, color ?? Th.Dim);
            ic.VerticalAlignment = VerticalAlignment.Center;
            var t = U.Tx(text, 14, color ?? Th.Txt);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(12, 0, 12, 0);
            var r = U.Tx(right ?? "", 12, Th.Dim);
            r.VerticalAlignment = VerticalAlignment.Center;
            r.Margin = new Thickness(0, 0, 8, 0);
            var arr = U.Ico("chevR", 16, Th.Dim);
            arr.VerticalAlignment = VerticalAlignment.Center;
            var b = new Border { Padding = new Thickness(12, 14, 12, 14), CornerRadius = new CornerRadius(12), BorderBrush = Th.Border, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), Child = U.Cols("a * a a", ic, t, r, arr) };
            U.Hover(b, Th.Card, Th.Card2);
            return U.Click(b, a, .99);
        }
    }
}
