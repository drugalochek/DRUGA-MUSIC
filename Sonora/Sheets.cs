using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sonora
{
    public sealed partial class Shell
    {
        public static Border Provider(FrameworkElement icon, string text, Action a, bool primary = false, bool centered = false)
        {
            var t = U.Tx(text, 14, primary ? Th.White : Th.Txt);
            t.VerticalAlignment = VerticalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = centered || primary ? HorizontalAlignment.Center : HorizontalAlignment.Left };
            if (icon != null) { icon.VerticalAlignment = VerticalAlignment.Center; icon.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(icon); }
            row.Children.Add(t);
            var b = new Border { Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(primary ? 0 : 1), BorderBrush = Th.Border, Child = row };
            if (primary) { b.Background = Th.Hot; b.Effect = U.Shadow(20, 8, .7, Th.C("#8b5cf6")); }
            else U.Hover(b, Th.Card, Th.Card2);
            return U.Click(b, a, .98);
        }
        public static Border SheetBtn(string text, Action a, string kind = "solid")
        {
            var t = U.Tx(text, 13.5, kind == "ghost" ? Th.Dim : Th.White);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border { Padding = new Thickness(13), CornerRadius = new CornerRadius(13), Child = t };
            if (kind == "ghost") { b.Background = Th.Card; b.BorderBrush = Th.BorderHi; b.BorderThickness = new Thickness(1); }
            else if (kind == "danger") b.Background = Th.Danger;
            else { b.Background = Th.Hot; b.Effect = U.Shadow(20, 8, .7, Th.C("#8b5cf6")); }
            return U.Click(b, a, .97);
        }
        static FrameworkElement Acts(params Border[] btns)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < btns.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition());
                btns[i].Margin = new Thickness(i == 0 ? 0 : 4.5, 0, i == btns.Length - 1 ? 0 : 4.5, 0);
                Grid.SetColumn(btns[i], i);
                g.Children.Add(btns[i]);
            }
            return g;
        }
        void Confirm(string title, string text, string yes, Action onYes, string kind = "danger")
        {
            OpenSheet(title, text, Acts(SheetBtn("Отмена", CloseSheet, "ghost"), SheetBtn(yes, () => { CloseSheet(); onYes(); }, kind)));
        }

        // ---------- Welcome & auth ----------
        public void OpenWelcome()
        {
            var benefits = new StackPanel();
            foreach (var s in new[] { "Синхронизация треков между устройствами", "Избранное и плейлисты в облаке", "Единый профиль на всех устройствах" })
            {
                var ic = U.Ico("check", 16, Th.Accent3, null, 2.5);
                var t = U.Wrap(s, 12.5, Th.Txt);
                t.Margin = new Thickness(8, 0, 0, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                benefits.Children.Add(U.M(U.Cols("a *", ic, t), 0, 3, 0, 3));
            }
            var body = new StackPanel();
            body.Children.Add(new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(12), Background = Th.Soft, Margin = new Thickness(0, 0, 0, 14), Child = benefits });
            body.Children.Add(Provider(U.Ico("login", 20, Th.White), "Войти или создать аккаунт", OpenAuth, true));
            body.Children.Add(Provider(U.Ico("user", 20, Th.Txt), "Продолжить как гость (30 секунд)", () =>
            {
                Library.D.GuestChosen = true;
                Library.Save(false);
                CloseSheet();
            }, false, true));
            OpenSheet("Добро пожаловать в SONORA", "Войди или создай аккаунт: лайки, плейлисты, своя музыка, скачивание и сервисы. Без аккаунта можно только слушать — по 30 секунд от трека.", body, () =>
            {
                if (!Library.D.GuestChosen && Auth.Current == null) { Library.D.GuestChosen = true; Library.Save(false); }
            });
        }

        public void OpenAuth() { OpenAuth(false, null); }
        public void OpenAuth(bool emailOpen, string mode)
        {
            mode = mode ?? "login";
            var body = new StackPanel();
            var err = U.Wrap("", 12.5, Th.Danger);
            err.Margin = new Thickness(2, 0, 0, 10);
            err.Visibility = Visibility.Collapsed;
            bool busy = false;
            Action<string> fail = m => { err.Text = m; err.Foreground = Th.Danger; err.Visibility = Visibility.Visible; busy = false; };

            body.Children.Add(Provider(U.GoogleIco(20), "Войти через Google", async () =>
            {
                if (busy) return;
                busy = true;
                err.Text = "Открыли браузер: нажми там «Войти через Google», выбери аккаунт и вернись сюда.";
                err.Foreground = Th.Dim;
                err.Visibility = Visibility.Visible;
                try { await Auth.Google(); AfterLogin(); }
                catch (Exception ex) { Log.W("google: " + ex); fail(ex is AuthException ? ex.Message : "Не удалось войти через Google: " + ex.Message); }
            }));

            var form = new StackPanel { Visibility = emailOpen ? Visibility.Visible : Visibility.Collapsed };
            var mailBtn = Provider(U.Ico("mail", 20, Th.Txt), "Почта и пароль", () => { form.Visibility = form.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; });
            body.Children.Add(mailBtn);
            body.Children.Add(form);

            var nameIn = new Input("Имя");
            var mailIn = new Input("Почта");
            var passIn = new Input("Пароль", true);
            var tabsHost = new Border();
            var submit = new Border();
            var forgot = U.Tx("Забыли пароль?", 12.5, Th.Accent3);
            forgot.HorizontalAlignment = HorizontalAlignment.Center;
            forgot.Margin = new Thickness(0, 12, 0, 0);
            string cur = mode;
            Action apply = null;
            Func<System.Threading.Tasks.Task> go = async () =>
            {
                if (busy) return;
                var email = mailIn.Text.Trim();
                var pass = passIn.Text;
                if (email.Length == 0 || !email.Contains("@")) { fail("Введи корректную почту."); return; }
                if (pass.Length < 6) { fail("Пароль — минимум 6 символов."); return; }
                busy = true;
                err.Text = cur == "login" ? "Входим…" : "Создаём аккаунт…";
                err.Foreground = Th.Dim;
                err.Visibility = Visibility.Visible;
                try
                {
                    if (cur == "login") await Auth.SignIn(email, pass);
                    else await Auth.SignUp(nameIn.Text, email, pass);
                    AfterLogin();
                }
                catch (Exception ex) { Log.W("auth: " + ex.Message); fail(ex is AuthException ? ex.Message : "Ошибка: " + ex.Message); }
            };
            apply = () =>
            {
                tabsHost.Child = Pills(new[] { "login", "register" }, new[] { "Войти", "Регистрация" }, cur, id => { cur = id; apply(); });
                nameIn.Visibility = cur == "register" ? Visibility.Visible : Visibility.Collapsed;
                var st = SheetBtn(cur == "login" ? "Войти" : "Создать аккаунт", async () => await go());
                submit.Child = st;
                forgot.Visibility = cur == "login" ? Visibility.Visible : Visibility.Collapsed;
            };
            apply();
            passIn.Enter += async () => await go();
            mailIn.Enter += () => passIn.Focus2();
            U.Click(forgot, async () =>
            {
                var email = mailIn.Text.Trim();
                if (!email.Contains("@")) { fail("Сначала введи почту — пришлём письмо для сброса пароля."); return; }
                try { await Auth.Reset(email); err.Text = "Письмо для сброса пароля отправлено на " + email; err.Foreground = Th.Ok; err.Visibility = Visibility.Visible; }
                catch (Exception ex) { fail(ex.Message); }
            }, 1);
            form.Children.Add(tabsHost);
            form.Children.Add(nameIn);
            form.Children.Add(mailIn);
            form.Children.Add(passIn);
            form.Children.Add(err);
            form.Children.Add(submit);
            form.Children.Add(forgot);
            if (!emailOpen) { form.Children.Remove(err); body.Children.Add(err); }
            OpenSheet("Вход в SONORA", "Тот же аккаунт, что и на сайте: избранное и плейлисты синхронизируются.", body, Auth.CancelGoogle);
            if (emailOpen && !ShotMode) Dispatcher.BeginInvoke(new Action(() => mailIn.Focus2()), DispatcherPriority.Input);
        }
        void AfterLogin()
        {
            CloseSheet();
            Library.D.GuestChosen = true;
            Library.Save(false);
            Toast("Добро пожаловать, " + (Auth.Current?.Name ?? Auth.Current?.Email ?? "") + "!");
            var _ = Cloud.SyncNow();
            Render();
        }
        void ConfirmLogout()
        {
            Confirm("Выйти из аккаунта?", "Вы сможете снова войти в любой момент.", "Выйти", () => { Auth.SignOut(); Toast("Вы вышли из аккаунта"); Go("home"); });
        }
        void RenameSheet()
        {
            var inp = new Input("Имя");
            inp.Text = Auth.Current?.Name ?? "";
            Func<System.Threading.Tasks.Task> save = async () =>
            {
                if (string.IsNullOrWhiteSpace(inp.Text)) return;
                CloseSheet();
                await Auth.UpdateName(inp.Text);
                Toast("Имя сохранено");
                Render();
            };
            inp.Enter += async () => await save();
            OpenSheet("Изменить имя", "Имя видно в профиле и синхронизируется с сайтом.", U.Col(inp, Acts(SheetBtn("Отмена", CloseSheet, "ghost"), SheetBtn("Сохранить", async () => await save()))));
            if (!ShotMode) Dispatcher.BeginInvoke(new Action(() => inp.Focus2()), DispatcherPriority.Input);
        }

        // ---------- Notifications ----------
        void OpenNotifications()
        {
            var list = new StackPanel();
            Action<string, string, string, string, string> item = (from, to, icon, title, text) =>
            {
                var ic = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Th.G(from, to), Child = U.Center(U.Ico(icon, 20, Th.White)) };
                var t = U.Wrap(title, 13.5, Th.Txt);
                t.Margin = new Thickness(0, 0, 0, 3);
                var d = U.Wrap(text, 11.5, Th.Dim);
                var info = U.Col(t, d);
                info.Margin = new Thickness(12, 0, 0, 0);
                list.Children.Add(new Border { Padding = new Thickness(8, 12, 8, 12), BorderBrush = Th.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = U.Cols("a *", ic, info) });
            };
            item("#60A5FA", "#1D4ED8", "star", L.T("Обновление ") + Program.Version, L.T("Экран загрузки, F11 — полный экран, живой фон в цветах обложки, офлайн-треки внутри SONORA, подробный профиль, настройки своих треков и публикация, ссылки, которые открывают приложение."));
            item("#60A5FA", "#1D4ED8", "music", "Добро пожаловать", "SONORA — нативный музыкальный плеер для Windows. Лайкай треки, собирай плейлисты, слушай миксы.");
            foreach (var t in Catalog.Remote.OrderByDescending(x => x.AddedAt).Take(5))
                item("#93C5FD", "#2563EB", "music", "Новый трек", t.Artist + " — " + t.Title + (t.AddedAt > 0 ? " · " + new DateTime(1970, 1, 1).AddMilliseconds(t.AddedAt).ToLocalTime().ToString("d MMM yyyy") : ""));
            OpenSheet("Уведомления", null, list);
            if (!ShotMode) { Library.D.SeenNotif = Program.Version; Library.Save(false); notifBadge.Visibility = Visibility.Collapsed; }
        }

        // ---------- Track menu / playlists ----------
        public void TrackMenu(Track t, string playlistId = null)
        {
            var body = new StackPanel();
            var head = U.Cols("a *", U.Cover(t, 56, 12, 22), U.M(U.Col(U.M(U.Tx(t.Title, 15), 0, 0, 0, 3), U.Tx(t.Artist + (string.IsNullOrEmpty(t.Album) ? "" : " · " + t.Album), 12, Th.Dim)), 12, 0, 0, 0));
            ((FrameworkElement)head.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            head.Margin = new Thickness(0, 0, 0, 16);
            body.Children.Add(head);
            bool fav = Library.IsFav(t.Id);
            body.Children.Add(Provider(U.Ico("heart", 20, fav ? Th.Heart : Th.Txt, fav), fav ? "Убрать из любимых" : "Добавить в любимые", () => { Library.ToggleFav(t.Id); CloseSheet(); }));
            body.Children.Add(Provider(U.Ico("plus", 20, Th.Txt), "Добавить в плейлист", () => AddToPlaylistSheet(t)));
            body.Children.Add(Provider(U.Ico("next", 20, Th.Txt), "Играть следующим", () => { Player.I.PlayNext(t); CloseSheet(); }));
            if (t.Source != null) body.Children.Add(Provider(U.Ico("search", 20, Th.Txt), "Найти исполнителя", () => { CloseSheet(); Go("search"); SetSearch((t.Artist ?? "").Split(',')[0].Trim()); }));
            else if (!t.IsLocal) body.Children.Add(Provider(U.Ico("user", 20, Th.Txt), "Перейти к исполнителю", () => { CloseSheet(); Go("artist", t.Artist); }));
            if (Offline.CanDownload(t))
            {
                if (Offline.Has(t.Id)) body.Children.Add(Provider(U.Ico("check", 20, Th.Accent2), "Удалить из скачанных", () => { Offline.Remove(t.Id); CloseSheet(); }));
                else body.Children.Add(Provider(U.Ico("download", 20, Th.Txt), Offline.Busy(t.Id) ? "Скачивается…" : "Скачать в SONORA", () => { CloseSheet(); if (Guard.Need("Скачивание треков")) { var _ = Offline.Download(t); } }));
            }
            body.Children.Add(Provider(U.Ico(Share.Link(t) != null ? "link" : "share", 20, Th.Txt), Share.Link(t) != null ? "Поделиться ссылкой" : "Скопировать название", () => { CloseSheet(); CopyTrack(t); }));
            if (playlistId != null && playlistId != Mixes.Uploaded)
                body.Children.Add(Provider(U.Ico("trash", 20, Th.Danger), "Убрать из плейлиста", () => { Library.RemoveFromPlaylist(playlistId, t.Id); CloseSheet(); }));
            if (t.IsLocal && Library.D.Local.Any(l => l.Id == t.Id))
                body.Children.Add(Provider(U.Ico("gear", 20, Th.Txt), "Настройки трека", () => { CloseSheet(); TrackSettingsSheet(t); }));
            if (t.IsLocal)
                body.Children.Add(Provider(U.Ico("trash", 20, Th.Danger), "Убрать из загруженных", () => { if (Player.I.Current?.Id == t.Id) Player.I.Stop(); Library.RemoveLocal(t.Id); CloseSheet(); }));
            OpenSheet(null, null, body);
        }
        public static void CopyTrack(Track t)
        {
            if (!Guard.Need("Отправка треков по ссылке")) return;
            var link = Share.Link(t);
            try
            {
                if (link != null) { Clipboard.SetText(t.Artist + " — " + t.Title + "\n" + link); Toast("Ссылка на трек скопирована — отправь её другу"); }
                else { Clipboard.SetText(t.Artist + " — " + t.Title); Toast(t.IsLocal ? "Свой файл нельзя отправить ссылкой — скопировано название" : "Скопировано: " + t.Artist + " — " + t.Title); }
            }
            catch { Toast("Не удалось скопировать"); }
        }
        void AddToPlaylistSheet(Track t)
        {
            if (!Guard.Need("Плейлисты")) return;
            var body = new StackPanel();
            foreach (var p in Library.D.Playlists)
            {
                var pp = p;
                bool has = p.TrackIds.Contains(t.Id);
                var pcb = PlCoverBrush(p, 72);
                var cov = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = pcb ?? Th.G(p.Color ?? "#8b5cf6", Th.Shade(p.Color ?? "#8b5cf6", -30)), Child = pcb != null && !has ? null : U.Center(U.Ico(has ? "check" : "music", 16, Th.White)) };
                if (pcb != null && has) cov.Child = new Border { CornerRadius = new CornerRadius(9), Background = Th.B("#000000", .45), Child = U.Center(U.Ico("check", 16, Th.White)) };
                body.Children.Add(Provider(cov, p.Name + (has ? " · уже есть" : ""), () =>
                {
                    if (Library.AddToPlaylist(pp.Id, t.Id)) Toast("Добавлено в «" + pp.Name + "»");
                    else Toast("Трек уже в этом плейлисте");
                    CloseSheet();
                }));
            }
            body.Children.Add(Provider(U.Ico("plus", 20, Th.Accent), "Новый плейлист", () => NewPlaylistSheet(null, t)));
            OpenSheet("Добавить в плейлист", t.Artist + " — " + t.Title, body);
        }
        void NewPlaylistSheet(Playlist edit, Track addAfter = null)
        {
            if (!Guard.Need("Плейлисты")) return;
            var inp = new Input("Название плейлиста");
            if (edit != null) inp.Text = edit.Name;
            string color = edit?.Color ?? Library.PlColors[Library.D.Playlists.Count % Library.PlColors.Length];
            var dots = new WrapPanel { Margin = new Thickness(0, 4, 0, 16) };
            Action draw = null;
            draw = () =>
            {
                dots.Children.Clear();
                foreach (var c in Library.PlColors)
                {
                    var cc = c;
                    var d = U.Circle(34, Th.G(c, Th.Shade(c, -30)), null, c == color ? Th.White : Th.Clear, 2.5);
                    d.Margin = new Thickness(0, 0, 10, 0);
                    dots.Children.Add(U.Click(d, () => { color = cc; draw(); }, .9));
                }
            };
            draw();
            // фото обложки
            string cover = edit?.Cover;
            var prev = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 14, 0), Effect = U.Shadow(18, 6, .5) };
            var coverActs = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Action paint = null;
            paint = () =>
            {
                Brush b = null;
                if (!string.IsNullOrEmpty(cover)) { var img = Img.FromData("plnew:" + cover.Length + ":" + cover.GetHashCode(), cover, 168); if (img != null) b = new ImageBrush(img) { Stretch = Stretch.UniformToFill }; }
                prev.Background = b ?? Th.G(color, Th.Shade(color, -30));
                prev.Child = b != null ? null : U.Center(U.Ico("music", 30, Th.White));
                coverActs.Children.Clear();
                coverActs.Children.Add(SheetBtn(string.IsNullOrEmpty(cover) ? "Выбрать фото" : "Заменить фото", () =>
                {
                    var f = PickImage();
                    if (f == null) return;
                    try { cover = ImageData(f, 400); paint(); }
                    catch (Exception ex) { Log.W("playlist cover: " + ex.Message); Toast("Не удалось открыть картинку"); }
                }, "ghost"));
                if (!string.IsNullOrEmpty(cover)) coverActs.Children.Add(U.M(SheetBtn("Убрать фото", () => { cover = null; paint(); }, "ghost"), 0, 8, 0, 0));
            };
            paint();
            var oldDraw = draw;
            draw = () => { oldDraw(); paint(); };
            var coverRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16), LastChildFill = true };
            DockPanel.SetDock(prev, Dock.Left);
            coverRow.Children.Add(prev);
            coverRow.Children.Add(coverActs);
            var lblCov = U.Tx("ФОТО", 11, Th.Dim);
            lblCov.Margin = new Thickness(2, 0, 0, 8);
            Action save = () =>
            {
                if (edit != null)
                {
                    edit.Name = string.IsNullOrWhiteSpace(inp.Text) ? edit.Name : inp.Text.Trim();
                    edit.Color = color;
                    edit.Cover = cover;
                    Library.Save();
                    CloseSheet();
                    return;
                }
                var p = Library.NewPlaylist(inp.Text, color);
                if (!string.IsNullOrEmpty(cover)) { p.Cover = cover; Library.Save(); }
                if (addAfter != null) Library.AddToPlaylist(p.Id, addAfter.Id);
                CloseSheet();
                Toast("Плейлист «" + p.Name + "» создан");
            };
            inp.Enter += save;
            var lbl = U.Tx("ЦВЕТ", 11, Th.Dim);
            lbl.Margin = new Thickness(2, 4, 0, 8);
            OpenSheet(edit == null ? "Новый плейлист" : "Настройки плейлиста", "Имя, цвет и фото обложки.", U.Col(inp, lbl, dots, lblCov, coverRow, Acts(SheetBtn("Отмена", CloseSheet, "ghost"), SheetBtn(edit == null ? "Создать" : "Сохранить", save))));
            if (!ShotMode) Dispatcher.BeginInvoke(new Action(() => inp.Focus2()), DispatcherPriority.Input);
        }
        void ConfirmDeletePlaylist(Playlist p)
        {
            Confirm("Удалить плейлист?", "Вы уверены, что хотите удалить плейлист «" + p.Name + "»? Действие нельзя отменить.", "Да, удалить", () =>
            {
                Library.DeletePlaylist(p.Id);
                if (Page == "playlist" && (Arg as string) == p.Id) Back();
            });
        }

        // ---------- Player sheets ----------
        public void TimerSheet()
        {
            var opts = new[] { "5", "15", "30", "45", "60", "90", "off" };
            var names = new[] { "5 минут", "15 минут", "30 минут", "45 минут", "60 минут", "90 минут", "Выкл" };
            string active = "off";
            if (Player.I.SleepAt.HasValue)
            {
                var left = (Player.I.SleepAt.Value - DateTime.Now).TotalMinutes;
                active = opts.Where(o => o != "off").OrderBy(o => Math.Abs(int.Parse(o) - left)).First();
            }
            var info = U.Tx(Player.I.SleepAt.HasValue ? "Музыка остановится в " + Player.I.SleepAt.Value.ToString("HH:mm") : "Таймер выключен", 12.5, Th.Dim);
            info.Margin = new Thickness(2, 0, 0, 12);
            var body = U.Col(info, Pills(opts, names, active, id =>
            {
                Player.I.SleepAt = id == "off" ? (DateTime?)null : DateTime.Now.AddMinutes(int.Parse(id));
                CloseSheet();
                Toast(id == "off" ? "Таймер сна выключен" : "Таймер сна: " + id + " мин");
                Full.Update();
            }));
            OpenSheet("Таймер сна", "Музыка плавно остановится через выбранное время.", body);
        }
        public void QueueSheet()
        {
            var body = new StackPanel();
            var q = Player.I.Queue;
            if (q.Count == 0) body.Children.Add(Note("Очередь пуста"));
            for (int i = 0; i < q.Count && i < 200; i++)
            {
                int idx = i;
                var t = q[i];
                bool cur = i == Player.I.Index;
                var title = U.Tx(t.Title, 13.5, cur ? Th.Accent3 : Th.Txt);
                var info = U.Col(title, U.M(U.Tx(t.Artist, 11.5, Th.Dim), 0, 2, 0, 0));
                info.Margin = new Thickness(12, 0, 8, 0);
                info.VerticalAlignment = VerticalAlignment.Center;
                var num = U.Tx(cur ? "▶" : (i + 1).ToString(), 11.5, cur ? Th.Accent3 : Th.Dim);
                num.VerticalAlignment = VerticalAlignment.Center;
                var row = new Border { Padding = new Thickness(6, 6, 6, 6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = cur ? Th.B("#8B5CF6", .3) : Th.Clear, Child = U.Cols("a * a", U.Cover(t, 40, 9, 16), info, num) };
                if (cur) row.Background = Th.Soft; else U.Hover(row, Th.Clear, Th.Card);
                body.Children.Add(U.Click(row, () => { Player.I.JumpTo(idx); CloseSheet(); }, .99));
            }
            OpenSheet("Очередь", U.Tn(q.Count) + (Player.I.Shuffle ? " · перемешивание" : ""), body);
        }

        public static List<KeyValuePair<double, string>> ParseLrc(string text)
        {
            var res = new List<KeyValuePair<double, string>>();
            if (string.IsNullOrEmpty(text)) return res;
            var rx = new Regex(@"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var ms = rx.Matches(raw);
                if (ms.Count == 0) continue;
                var line = rx.Replace(raw, "").Trim();
                foreach (Match m in ms)
                {
                    double sec = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
                    if (m.Groups[3].Success) { var f = m.Groups[3].Value; sec += int.Parse(f) / Math.Pow(10, f.Length); }
                    res.Add(new KeyValuePair<double, string>(sec, line));
                }
            }
            return res.OrderBy(x => x.Key).ToList();
        }
    }
}
