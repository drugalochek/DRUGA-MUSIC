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
    public sealed partial class Shell : Grid
    {
        public static Shell I;
        public readonly bool ShotMode;
        public const double ContentMax = 1040;

        readonly DockPanel dock = new DockPanel();
        readonly Grid center = new Grid();
        readonly ScrollViewer scroll;
        readonly Border host;
        readonly Grid modal = new Grid();
        readonly Border sheet;
        readonly TranslateTransform sheetMove = new TranslateTransform();
        public readonly FullPlayer Full;
        Border toastBox; TextBlock toastText; DispatcherTimer toastTimer;
        Border profileBtn, notifBadge;
        readonly Border[] navNi = new Border[4];
        readonly TextBlock[] navLabel = new TextBlock[4];
        static readonly string[] Tabs = { "home", "explore", "fav", "search" };
        static readonly string[] TabNames = { "Главная", "Исследовать", "Избранное", "Поиск" };
        static readonly string[] TabIcons = { "home", "explore", "heart", "search" };
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

            Children.Add(Glow(new Point(.1, 0), .6, .4, Th.C("#8b5cf6", .14)));
            Children.Add(Glow(new Point(1, .3), .5, .4, Th.C("#06b6d4", .09)));
            Children.Add(dock);

            var top = BuildTopbar();
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            var nav = BuildNav();
            DockPanel.SetDock(nav, Dock.Bottom);
            dock.Children.Add(nav);
            dock.Children.Add(center);

            host = new Border { MaxWidth = ContentMax, Padding = new Thickness(16, 0, 16, 30) };
            scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = host, PanningMode = PanningMode.VerticalOnly };
            center.Children.Add(scroll);
            center.Children.Add(BuildMini());
            center.Children.Add(BuildToast());

            Full = new FullPlayer();
            Children.Add(Full);

            modal.Background = Th.B("#000000", .55);
            modal.Visibility = Visibility.Collapsed;
            modal.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == modal) CloseSheet(); };
            sheet = new Border
            {
                MaxWidth = 540,
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(24, 24, 0, 0),
                Background = Th.Bg2,
                BorderBrush = Th.BorderHi,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(20, 8, 20, 22),
                RenderTransform = sheetMove,
                Effect = U.Shadow(40, 0, .6)
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

        static Rectangle Glow(Point c, double rx, double ry, Color col)
        {
            var b = new RadialGradientBrush { Center = c, GradientOrigin = c, RadiusX = rx, RadiusY = ry };
            b.GradientStops.Add(new GradientStop(col, 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), .55 / 1.0));
            b.Freeze();
            return new Rectangle { Fill = b, IsHitTestVisible = false };
        }

        // ---------- Top bar ----------
        FrameworkElement BuildTopbar()
        {
            var logo = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Th.Brand, Effect = U.Shadow(16, 6, .6, Th.C("#8b5cf6")), Child = U.Center(U.Ico("wave", 17, Th.White)) };
            var brandText = U.Tx("SONORA", 19);
            brandText.VerticalAlignment = VerticalAlignment.Center;
            brandText.Margin = new Thickness(9, 0, 0, 0);
            var brand = U.Click(new Border { Child = U.Row(logo, brandText) }, () => Go("home"), 1);

            var bell = IBtn("bell", OpenNotifications);
            notifBadge = new Border { MinWidth = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Th.Danger, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -2, 0), Padding = new Thickness(4, 0, 4, 0), Child = U.Center(U.Tx("1", 9, Th.White)), IsHitTestVisible = false };
            var bellWrap = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            bellWrap.Children.Add(bell);
            bellWrap.Children.Add(notifBadge);

            profileBtn = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Th.Hot, BorderBrush = Th.Accent, BorderThickness = new Thickness(2) };
            U.Click(profileBtn, () => Go("profile"), .9);

            var g = U.Cols("a * a a", brand, null, bellWrap, profileBtn);
            g.MaxWidth = ContentMax;
            g.Margin = new Thickness(16, 12, 16, 10);
            return new Border { Child = g, Background = Th.G(Th.C("#0A0A12", .7), Th.C("#0A0A12", 0), false) };
        }
        public static Border IBtn(string icon, Action a)
        {
            var b = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Th.Card, BorderBrush = Th.Border, BorderThickness = new Thickness(1), Child = U.Center(U.Ico(icon, 18, Th.Dim)) };
            return U.Click(b, a, .9);
        }
        void UpdateProfileBtn()
        {
            var s = Auth.Current;
            if (s != null && !string.IsNullOrWhiteSpace(s.Name ?? s.Email))
            {
                var letter = (s.Name ?? s.Email).Trim().Substring(0, 1).ToUpperInvariant();
                profileBtn.Child = U.Center(U.Tx(letter, 15, Th.White));
            }
            else profileBtn.Child = U.Center(U.Ico("user", 18, Th.White));
            notifBadge.Visibility = Library.D.UpdatedAt == 0 || ShotMode ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- Bottom navigation ----------
        FrameworkElement BuildNav()
        {
            var ug = new UniformGrid { Columns = 4, Rows = 1, MaxWidth = 720 };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                navNi[i] = new Border { Width = 42, Height = 30, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Center };
                navLabel[i] = U.Tx(TabNames[i], 10.5, Th.Dim);
                navLabel[i].HorizontalAlignment = HorizontalAlignment.Center;
                navLabel[i].Margin = new Thickness(0, 3, 0, 0);
                var sp = U.Col(navNi[i], navLabel[i]);
                sp.VerticalAlignment = VerticalAlignment.Center;
                var cell = new Border { MinHeight = 54, Padding = new Thickness(4, 5, 4, 5), Child = sp };
                U.Click(cell, () => Go(Tabs[idx]), .95);
                ug.Children.Add(cell);
            }
            return new Border { Background = Th.B("#0A0A12", .92), BorderBrush = Th.BorderHi, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 6), Child = ug };
        }
        void UpdateNav()
        {
            if (Tabs.Contains(Page)) lastTab = Page;
            for (int i = 0; i < 4; i++)
            {
                bool on = Tabs[i] == lastTab;
                navNi[i].Background = on ? Th.Hot : null;
                navNi[i].Effect = on ? U.Shadow(18, 8, .8, Th.C("#8b5cf6")) : null;
                navNi[i].Child = U.Center(U.Ico(TabIcons[i], on ? 22 : 21, on ? Th.White : Th.Dim));
                navLabel[i].Foreground = on ? Th.Txt : Th.Dim;
            }
        }

        // ---------- Mini player ----------
        Border mini, miniCover, miniPlay, miniFav, miniProg;
        Grid miniTrack;
        TextBlock miniTitle, miniArtist;
        FrameworkElement BuildMini()
        {
            miniCover = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(9), Background = Th.B("#000000", .3) };
            miniTitle = U.Tx("", 13, Th.White);
            miniTitle.Margin = new Thickness(0, 0, 0, 2);
            miniArtist = U.Tx("", 11, Th.White, .85);
            var info = U.Col(miniTitle, miniArtist);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(10, 0, 10, 0);
            miniPlay = U.Click(U.Circle(38, Th.B("#FFFFFF", .22)), () => Player.I.Toggle(), .9);
            miniFav = U.Click(U.Circle(34, null), () => { var t = Player.I.Current; if (t != null) Library.ToggleFav(t.Id); UpdateMini(); }, .85);
            miniFav.Margin = new Thickness(6, 0, 0, 0);
            var close = U.IconBtn("close", 30, 15, Th.B("#FFFFFF", .85), () => Player.I.Stop());
            close.Margin = new Thickness(2, 0, 0, 0);
            var g = U.Cols("a * a a a", miniCover, info, miniPlay, miniFav, close);
            g.Margin = new Thickness(7, 7, 10, 7);
            miniProg = new Border { Background = Th.White, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            miniTrack = new Grid { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = Th.B("#FFFFFF", .18) };
            miniTrack.Children.Add(miniProg);
            var root = new Grid();
            root.Children.Add(g);
            root.Children.Add(miniTrack);
            U.ClipRound(root, 14);
            mini = new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(10, 0, 10, 6), VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = ContentMax - 20, Child = root, Effect = U.Shadow(36, 14, .85), Visibility = Visibility.Collapsed };
            U.Click(mini, OpenFull, 1);
            return mini;
        }
        public bool MiniVisible { get { return Player.I.Current != null; } }
        void UpdateMini()
        {
            var t = Player.I.Current;
            mini.Visibility = t == null ? Visibility.Collapsed : Visibility.Visible;
            host.Padding = new Thickness(16, 0, 16, t == null ? 30 : 96);
            if (t == null) { Full.Close(); return; }
            var br = U.CoverBrush(t, 96);
            miniCover.Background = br ?? (Brush)Th.B("#000000", .3);
            miniCover.Child = br == null ? U.Center(U.Ico("music", 18, Th.White)) : null;
            miniTitle.Text = t.Title;
            miniArtist.Text = t.Artist;
            miniPlay.Child = U.Center(U.Ico(Player.I.IsPlaying ? "pause" : "play", 16, Th.White));
            bool fav = Library.IsFav(t.Id);
            miniFav.Child = U.Center(U.Ico("heart", 17, Th.B("#FFFFFF", .9), fav));
            mini.Background = Img.PlayerGradient(t);
            Full.Update();
        }
        void UpdateMiniProgress()
        {
            var len = Player.I.Length;
            var w = miniTrack.ActualWidth;
            miniProg.Width = len > 0 && w > 0 ? Math.Max(0, Math.Min(w, w * Player.I.Position / len)) : 0;
            Full.Tick();
        }
        public void OpenFull() { if (Player.I.Current != null) Full.Open(); }

        // ---------- Toast ----------
        FrameworkElement BuildToast()
        {
            toastText = U.Wrap("", 12.5, Th.White);
            toastText.TextAlignment = TextAlignment.Center;
            toastBox = new Border
            {
                Padding = new Thickness(16, 10, 16, 10),
                Background = Th.B("#14141E", .94),
                BorderBrush = Th.BorderHi,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(16, 0, 16, 84),
                MaxWidth = 360,
                Child = toastText,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Effect = U.Shadow(24, 8, .6)
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

        // ---------- Sheets ----------
        public bool SheetOpen { get { return modal.Visibility == Visibility.Visible; } }
        public void OpenSheet(string title, string desc, UIElement body, Action onClosed = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(new Border { Width = 36, Height = 4, CornerRadius = new CornerRadius(2), Background = Th.BorderHi, Margin = new Thickness(0, 6, 0, 14), HorizontalAlignment = HorizontalAlignment.Center });
            if (title != null) sp.Children.Add(U.M(U.Tx(title, 17), 0, 0, 0, 5));
            if (desc != null) sp.Children.Add(U.M(U.Wrap(desc, 12.5, Th.Dim, 0, 18.75), 0, 0, 0, 14));
            if (body != null) sp.Children.Add(body);
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp, Focusable = false };
            sv.MaxHeight = Math.Max(320, (ActualHeight > 0 ? ActualHeight : 800) - 90);
            sheet.Child = sv;
            var prev = sheetClosed;
            sheetClosed = onClosed;
            if (modal.Visibility != Visibility.Visible)
            {
                modal.Visibility = Visibility.Visible;
                if (!ShotMode)
                {
                    U.Fade(modal, 0, 1, 200);
                    sheetMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(420, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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

        // ---------- Navigation & rendering ----------
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
            if (history.Count == 0) { Go(lastTab); return; }
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
