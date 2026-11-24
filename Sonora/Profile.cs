using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sonora
{
    public sealed partial class Shell
    {
        public static readonly string[] ProfileColors = { "#3B82F6", "#60A5FA", "#1E40AF", "#7DD3FC", "#FF4F9A", "#94A3B8" };
        public static readonly string[] ProfileColorNames = { "Синий", "Голубой", "Глубокий синий", "Небесный", "Розовый", "Графит" };
        static string PColor { get { var c = Auth.Current?.Color; return string.IsNullOrEmpty(c) ? "#3B82F6" : c; } }

        public static Brush ProfileRing()
        {
            var c = PColor;
            return Th.G(Th.Shade(c, 40), Th.Shade(c, -45));
        }

        // аватар: фото профиля, иначе первая буква на градиенте
        public static Border Avatar(double size, string photo = null, string name = null, string color = null)
        {
            var s = Auth.Current;
            photo = photo ?? s?.Photo;
            name = name ?? (s != null ? (s.Name ?? s.Email) : null);
            color = color ?? PColor;
            var av = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2) };
            BitmapSource img = string.IsNullOrWhiteSpace(photo) ? null : Img.FromData("avatar:" + photo.Length + ":" + photo.GetHashCode(), photo, (int)(size * 2));
            if (img != null) av.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            else
            {
                av.Background = Th.G(Th.Shade(color, 25), Th.Shade(color, -40));
                if (s != null && !string.IsNullOrWhiteSpace(name)) av.Child = U.Center(U.Tx(name.Trim().Substring(0, 1).ToUpperInvariant(), size * .42, Th.White));
                else av.Child = U.Center(U.Ico("user", size * .45, Th.White));
            }
            return av;
        }

        // картинка с диска → JPEG data URI (для аватара и обложек)
        public static string ImageData(string path, int max)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            BitmapSource src = bi;
            // квадрат по центру
            int side = Math.Min(src.PixelWidth, src.PixelHeight);
            src = new CroppedBitmap(src, new Int32Rect((src.PixelWidth - side) / 2, (src.PixelHeight - side) / 2, side, side));
            if (side > max) src = new TransformedBitmap(src, new ScaleTransform((double)max / side, (double)max / side));
            var enc = new JpegBitmapEncoder { QualityLevel = 85 };
            enc.Frames.Add(BitmapFrame.Create(src));
            using (var ms = new MemoryStream()) { enc.Save(ms); return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray()); }
        }
        static string PickImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Изображения") + "|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif", Title = L.T("Выбери картинку") };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ---------- Профиль ----------
        FrameworkElement ProfilePage()
        {
            var sp = new StackPanel();
            var s = Auth.Current;
            var col = PColor;

            // шапка в цвете профиля
            var ring = new Border { Width = 132, Height = 132, CornerRadius = new CornerRadius(66), Background = ProfileRing(), Padding = new Thickness(3), Effect = U.Shadow(40, 12, .55, Th.C(col)) };
            ring.Child = new Border { CornerRadius = new CornerRadius(63), Background = Th.Bg, Padding = new Thickness(4), Child = Avatar(118) };
            var kicker = U.Tx(s != null ? (Plus.Active ? "ПРОФИЛЬ · PLUS" : "ПРОФИЛЬ") : "ГОСТЬ", 11.5, Th.B("#FFFFFF", .75));
            kicker.FontWeight = FontWeights.Bold;
            var name = new TextBlock { Text = s != null ? (s.Name ?? s.Email ?? "Профиль") : L.T("Гость"), FontFamily = Th.Font, FontSize = 34, Foreground = Th.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 6) };
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
            info.Children.Add(kicker);
            info.Children.Add(name);
            if (s != null && !string.IsNullOrWhiteSpace(s.Bio)) info.Children.Add(U.M(U.Wrap(s.Bio, 14, Th.B("#FFFFFF", .9), 3, 20), 0, 0, 0, 6));
            var meta = s != null
                ? (s.Email ?? "") + (s.Provider == "google" ? " · Google" : "") + " · " + U.Tn(Library.D.Fav.Count) + L.T(" в любимых")
                : L.T("Войди, чтобы сохранять лайки, плейлисты и свою музыку в облаке");
            info.Children.Add(U.Wrap(meta, 12.5, Th.B("#FFFFFF", .7), 2));
            var editTxt = U.Tx(s != null ? "Редактировать профиль" : "Войти или создать аккаунт", 13.5, Th.B("#000000"));
            var edit = new Border { CornerRadius = new CornerRadius(20), Background = Th.White, Padding = new Thickness(18, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Child = U.Row(U.Ico(s != null ? "edit" : "login", 15, Th.B("#000000")), U.M(editTxt, 8, 0, 0, 0)) };
            U.Click(edit, () => { if (Auth.Current == null) OpenWelcome(); else EditProfileSheet(); }, .96);
            info.Children.Add(edit);
            var headGrid = U.Cols("a *", ring, info);
            var banner = new Border
            {
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(28, 26, 28, 26),
                Margin = new Thickness(0, 6, 0, 20),
                Background = Th.G(Th.C(Th.Shade(col, -10), .85), Th.C("#0B0E14", .9)),
                BorderBrush = Th.GlassEdge,
                BorderThickness = new Thickness(1),
                Child = headGrid
            };
            sp.Children.Add(banner);

            // статистика
            var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            Action<string, string> stat = (n, l) =>
            {
                var a = U.Tx(n, 24); a.HorizontalAlignment = HorizontalAlignment.Center; a.Margin = new Thickness(0, 0, 0, 2);
                var b = U.Tx(l, 11.5, Th.Dim); b.HorizontalAlignment = HorizontalAlignment.Center;
                stats.Children.Add(new Border { Padding = new Thickness(10, 14, 10, 14), CornerRadius = new CornerRadius(16), Background = Th.Glass(.04), BorderBrush = Th.GlassEdgeSoft, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), Child = U.Col(a, b) });
            };
            stat(Library.D.Fav.Count.ToString(), "в любимых");
            stat(Library.D.Playlists.Count.ToString(), "плейлистов");
            stat(Library.D.Recent.Count.ToString(), "прослушано");
            stat(Library.D.Local.Count(l => l.Public) + " / " + Publish.FreeLimit, "публичных треков");
            sp.Children.Add(stats);

            // свои треки
            var mine = Library.LocalTracks();
            if (mine.Count > 0)
            {
                sp.Children.Add(SHead("Мои треки", L.T("публичных: ") + Library.D.Local.Count(l => l.Public) + " · " + U.Tn(mine.Count)));
                sp.Children.Add(Note("Нажми «⋮» у трека → «Настройки трека»: название, обложка, текст, 18+ и доступ — только ты или все."));
                sp.Children.Add(TrackList(mine.Take(8).ToList(), int.MaxValue, Mixes.Uploaded));
            }

            sp.Children.Add(SetTitle("Аккаунт"));
            if (s == null) sp.Children.Add(SetBtn("login", "Войти или создать аккаунт", null, OpenWelcome));
            else
            {
                sp.Children.Add(SetBtn("edit", "Редактировать профиль", null, EditProfileSheet));
                sp.Children.Add(SetBtn("refresh", "Синхронизировать сейчас", Cloud.Status, async () => { await Cloud.SyncNow(); Toast(Cloud.Status); Render(); }));
                sp.Children.Add(SetBtn("logout", "Выйти из аккаунта", null, ConfirmLogout, Th.Danger));
            }
            sp.Children.Add(SetBtn("gear", "Настройки", L.En ? "English" : "Русский", () => Go("settings")));
            sp.Children.Add(SetTitle("Музыка"));
            sp.Children.Add(SetBtn("upload", "Добавить музыку с компьютера", U.Tn(Library.D.Local.Count), PickFiles));
            sp.Children.Add(SetBtn("download", "Скачанные в SONORA", U.Tn(Library.D.Offline.Count), () => Go("playlist", Mixes.Downloaded)));
            sp.Children.Add(SetTitle("О приложении"));
            sp.Children.Add(SetBtn("star", "SONORA для Windows", "версия " + Program.Version, () => Toast("Пробел — пауза, ←/→ — перемотка, Ctrl+←/→ — треки, F11 — полный экран")));
            return sp;
        }

        // ---------- Редактирование профиля ----------
        void EditProfileSheet()
        {
            if (!Guard.Need("Редактирование профиля")) return;
            var s = Auth.Current;
            string photo = s.Photo, color = PColor;
            var avHost = new Border { HorizontalAlignment = HorizontalAlignment.Left };
            var nameInp = new Input("Имя") { Text = s.Name ?? "" };
            var bioInp = new Input("О себе (до 140 символов)") { Text = s.Bio ?? "" };
            Action paintAv = () => avHost.Child = new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(46), Background = Th.G(Th.Shade(color, 40), Th.Shade(color, -45)), Padding = new Thickness(3), Child = new Border { CornerRadius = new CornerRadius(43), Background = Th.Bg, Padding = new Thickness(3), Child = Avatar(80, photo ?? "", nameInp.Text, color) } };
            paintAv();
            var upload = SheetBtn("Загрузить фото", () =>
            {
                var f = PickImage();
                if (f == null) return;
                try { photo = ImageData(f, 256); paintAv(); }
                catch (Exception ex) { Log.W("avatar: " + ex.Message); Toast("Не удалось открыть картинку"); }
            });
            var remove = SheetBtn("Убрать фото", () => { photo = ""; paintAv(); }, "ghost");
            var btns = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            btns.Children.Add(U.M(upload, 0, 0, 0, 8));
            btns.Children.Add(remove);
            var top = U.Cols("a *", avHost, btns);
            top.Margin = new Thickness(0, 0, 0, 16);

            var sw = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            Action paintSw = null;
            paintSw = () =>
            {
                sw.Children.Clear();
                for (int i = 0; i < ProfileColors.Length; i++)
                {
                    var c = ProfileColors[i];
                    bool on = string.Equals(c, color, StringComparison.OrdinalIgnoreCase);
                    var dot = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Th.G(Th.Shade(c, 30), Th.Shade(c, -35)), BorderBrush = on ? Th.White : Th.Clear, BorderThickness = new Thickness(2.5), Margin = new Thickness(0, 0, 10, 0), ToolTip = L.T(ProfileColorNames[i]) };
                    if (on) dot.Child = U.Center(U.Ico("check", 15, Th.White, null, 3));
                    sw.Children.Add(U.Click(dot, () => { color = c; paintSw(); paintAv(); }, .9));
                }
            };
            paintSw();
            nameInp.Changed += () => { if (string.IsNullOrEmpty(photo)) paintAv(); };

            Func<Task> save = async () =>
            {
                var n = (nameInp.Text ?? "").Trim();
                if (n.Length == 0) { Toast("Введи имя"); return; }
                var bio = (bioInp.Text ?? "").Trim();
                if (bio.Length > 140) bio = bio.Substring(0, 140);
                s.Bio = bio; s.Color = color; s.Photo = photo ?? "";
                CloseSheet();
                await Auth.UpdateName(n);
                Toast("Профиль сохранён");
                UpdateSide();
                Render();
            };
            var body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(SetTitle("Имя"));
            body.Children.Add(nameInp);
            body.Children.Add(SetTitle("О себе"));
            body.Children.Add(bioInp);
            body.Children.Add(SetTitle("Цвет профиля"));
            body.Children.Add(sw);
            body.Children.Add(Acts(SheetBtn("Отмена", CloseSheet, "ghost"), SheetBtn("Сохранить", async () => await save())));
            OpenSheet("Редактировать профиль", "Фото, имя, описание и цвет видны в профиле и синхронизируются между устройствами.", body);
        }

        // ---------- Настройки своего трека ----------
        public void TrackSettingsSheet(Track t)
        {
            var lf = Library.D.Local.FirstOrDefault(l => l.Id == t.Id);
            if (lf == null) { Toast("Настройки доступны только для своих треков"); return; }
            if (!Guard.Need("Настройки своих треков")) return;
            string cover = lf.Cover;
            bool isPublic = lf.Public, explicitOn = lf.Explicit;
            var title = new Input("Название") { Text = lf.Title ?? "" };
            var artist = new Input("Исполнитель") { Text = lf.Artist ?? "" };
            var album = new Input("Альбом") { Text = lf.Album ?? "" };
            var genre = new Input("Жанр") { Text = lf.Genre ?? "" };
            var year = new Input("Год") { Text = lf.Year > 0 ? lf.Year.ToString() : "" };
            var lyrics = new TextBox { Text = lf.Lyrics ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Th.Clear, Foreground = Th.Txt, CaretBrush = Th.Txt, BorderThickness = new Thickness(0), FontFamily = Th.Body, FontSize = 13.5 };
            var lyrBox = new Border { CornerRadius = new CornerRadius(12), Background = Th.Card, BorderBrush = Th.Border, BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 10), Child = lyrics };

            var covHost = new Border();
            Action paintCover = () =>
            {
                var preview = new Track { Id = t.Id + ":edit:" + (cover ?? "").Length, Cover = cover };
                var c = U.Cover(preview, 96, 14, 34);
                covHost.Child = c;
            };
            paintCover();
            var pick = SheetBtn("Выбрать обложку", () =>
            {
                var f = PickImage();
                if (f == null) return;
                try { cover = ImageData(f, 500); paintCover(); }
                catch (Exception ex) { Log.W("cover: " + ex.Message); Toast("Не удалось открыть картинку"); }
            });
            var unpick = SheetBtn("Убрать", () => { cover = null; paintCover(); }, "ghost");
            var cb = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            cb.Children.Add(U.M(pick, 0, 0, 0, 8)); cb.Children.Add(unpick);
            var covRow = U.Cols("a *", covHost, cb);
            covRow.Margin = new Thickness(0, 0, 0, 14);

            var visHost = new UniformGrid2();
            Action paintVis = null;
            paintVis = () =>
            {
                visHost.Children.Clear();
                visHost.Children.Add(Choice("lock", "Только я", "Трек видишь только ты", !isPublic, () => { isPublic = false; paintVis(); }));
                visHost.Children.Add(Choice("globe", "Публичный", (Plus.Active ? L.T("Виден всем в каталоге SONORA · с Plus до ") : L.T("Виден всем в каталоге SONORA · без подписки до ")) + Publish.FreeLimit + L.T(" треков"), isPublic, () => { isPublic = true; paintVis(); }));
            };
            paintVis();
            var expl = Toggle("warn", "18+ / Explicit", "В треке есть мат — пометим и скроем у тех, кто не хочет такое слышать", explicitOn, v => explicitOn = v);

            Func<Task> save = async () =>
            {
                var tt = (title.Text ?? "").Trim();
                if (tt.Length == 0) { Toast("Введи название"); return; }
                int y; int.TryParse((year.Text ?? "").Trim(), out y);
                if (isPublic && !lf.Public)
                {
                    var why = Publish.CanPublish(lf);
                    if (why != null) { Toast(why); return; }
                }
                lf.Title = tt; lf.Artist = string.IsNullOrWhiteSpace(artist.Text) ? L.T("Неизвестный исполнитель") : artist.Text.Trim();
                lf.Album = (album.Text ?? "").Trim(); lf.Genre = (genre.Text ?? "").Trim(); lf.Year = y > 1800 && y < 2200 ? y : 0;
                lf.Lyrics = lyrics.Text ?? ""; lf.Cover = cover; lf.Explicit = explicitOn;
                CloseSheet();
                bool was = lf.Public;
                Library.Save(false);
                Library.Touch();
                if (isPublic && !was) await Publish.Up(lf);
                else if (!isPublic && was) await Publish.Down(lf);
                else if (isPublic) await Publish.UpdateMeta(lf);
                else Toast("Сохранено");
                Render();
            };
            var body = new StackPanel();
            body.Children.Add(covRow);
            body.Children.Add(title); body.Children.Add(artist);
            var two = U.Cols("* 10 *", album, null, genre);
            body.Children.Add(two);
            body.Children.Add(year);
            body.Children.Add(SetTitle("Текст песни"));
            body.Children.Add(lyrBox);
            body.Children.Add(SetTitle("Доступ"));
            body.Children.Add(visHost);
            body.Children.Add(U.M(expl, 0, 8, 0, 0));
            body.Children.Add(Note((Plus.Active ? "SONORA Plus: до " : "Без подписки: до ") + Publish.FreeLimit + " публичных треков, файл до " + Publish.MaxMb + " МБ и до " + (int)(Publish.MaxSec / 60) + " минут. Перед публикацией трек проходит проверку SONORA."));
            body.Children.Add(Acts(SheetBtn("Отмена", CloseSheet, "ghost"), SheetBtn("Сохранить", async () => await save())));
            OpenSheet("Настройки трека", null, body);
        }

        FrameworkElement Choice(string icon, string text, string sub, bool on, Action pick)
        {
            var t = U.Tx(text, 14.5, Th.Txt);
            var st = U.Wrap(sub, 11.5, Th.Dim, 3);
            st.Margin = new Thickness(0, 3, 0, 0);
            var info = U.Col(U.Row(U.Ico(icon, 16, on ? Th.Accent2 : Th.Dim), U.M(t, 8, 0, 0, 0)), st);
            var b = new Border { Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(on ? 1.5 : 1), BorderBrush = on ? Th.Accent : Th.Border, Margin = new Thickness(0, 0, 8, 8), Child = info };
            U.Hover(b, on ? Th.Soft : Th.Card, on ? Th.Soft : Th.Card2);
            return U.Click(b, pick, .98);
        }
    }

    public sealed class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid2() { Columns = 2; } }

    // ---------- Публикация своих треков в общий каталог ----------
    public static class Publish
    {
        // без подписки: 2 трека, 12 МБ, 10 минут; с SONORA Plus: 100 треков, 40 МБ, 30 минут
        public static int FreeLimit { get { return Plus.Active ? 100 : 2; } }
        public static int MaxMb { get { return Plus.Active ? 40 : 12; } }
        public static double MaxSec { get { return Plus.Active ? 1800 : 600; } }
        static string NoSub { get { return Plus.Active ? L.T(" — это предел даже с Plus") : L.T(" — без подписки такой не опубликовать, оформи SONORA Plus"); } }
        static string Mime(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".flac": return "audio/flac";
                case ".wav": return "audio/wav";
                case ".ogg": return "audio/ogg";
                case ".m4a": case ".aac": return "audio/mp4";
                default: return "audio/mpeg";
            }
        }
        public static string CanPublish(LocalFile l)
        {
            if (Auth.Current == null) return L.T("Публиковать треки можно только с аккаунтом");
            if (Library.D.Local.Count(x => x.Public) >= FreeLimit) return (Plus.Active ? L.T("С Plus можно опубликовать до ") : L.T("Без подписки можно опубликовать до ")) + FreeLimit + L.T(" треков — сделай один из них приватным") + (Plus.Active ? "" : L.T(" или оформи SONORA Plus"));
            if (!File.Exists(l.Path)) return L.T("Файл трека не найден на компьютере");
            var mb = new FileInfo(l.Path).Length / 1024.0 / 1024.0;
            if (mb > MaxMb) return L.T("Файл больше ") + MaxMb + L.T(" МБ") + NoSub;
            if (l.Duration > MaxSec) return L.T("Трек длиннее ") + (int)(MaxSec / 60) + L.T(" минут") + NoSub;
            return null;
        }
        // Каталог пользователей SONORA: база SONORA, раздел catalog (читать могут все, писать — только автор)
        static string Url(string path, string tok) { return Catalog.UserDb + "/" + path + ".json?auth=" + Uri.EscapeDataString(tok); }
        const int Chunk = 4 * 1024 * 1024;   // база не принимает строки больше 10 МБ — аудио кладём частями по 4 МБ
        public static string LastError;
        static Dictionary<string, object> Meta(LocalFile l)
        {
            var s = Auth.Current;
            return new Dictionary<string, object>
            {
                ["title"] = l.Title ?? "", ["artist"] = l.Artist ?? "", ["album"] = l.Album ?? "", ["genre"] = l.Genre ?? "",
                ["lyrics"] = l.Lyrics ?? "", ["cover"] = l.Cover ?? "", ["year"] = l.Year, ["duration"] = Math.Round(l.Duration),
                ["explicit"] = l.Explicit, ["ownerId"] = s.Uid, ["ownerName"] = s.Name ?? "",
                ["addedAt"] = l.PublishedAt > 0 ? l.PublishedAt : Library.Now, ["client"] = "SONORA Windows " + Program.Version
            };
        }
        static string Why(Net.Resp r, string step)
        {
            var body = (r.Body ?? "").Replace("\n", " ").Trim();
            if (body.Length > 160) body = body.Substring(0, 160);
            Log.W("publish " + step + ": HTTP " + r.Code + " " + body);
            if (r.Code == 401 || r.Code == 403)
                return body.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0 || body.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 && body.IndexOf("Permission", StringComparison.OrdinalIgnoreCase) < 0
                    ? "Сессия устарела — выйди и войди в аккаунт заново"
                    : "Сервер SONORA пока не принимает треки: у базы не включены правила для публикации";
            if (r.Code == 400 || r.Code == 413) return "Сервер не принял файл (" + r.Code + ") — попробуй MP3 поменьше";
            return "Сервер ответил ошибкой " + r.Code + " — попробуй позже";
        }
        public static async Task Up(LocalFile l)
        {
            Shell.Toast("Публикуем трек…");
            string id = null, tok = null;
            bool metaWritten = false;
            try
            {
                tok = await Auth.Token();
                if (string.IsNullOrEmpty(tok)) throw new Exception("Сессия устарела — выйди и войди в аккаунт заново");
                id = string.IsNullOrEmpty(l.PubId) ? "sn_" + Library.Hash(Auth.Current.Uid + l.Id + Library.Now) : l.PubId;
                var bytes = await Task.Run(() => File.ReadAllBytes(l.Path));
                var b64 = Convert.ToBase64String(bytes);
                l.PublishedAt = Library.Now;
                // 1) карточка трека (по ней сервер проверяет, что аудио пишет автор)
                var r = await Net.Send("PUT", Url("tracks/" + id, tok), J.Ser(Meta(l)));
                if (!r.Ok) throw new Exception(Why(r, "track"));
                metaWritten = true;
                // 2) аудио частями
                int parts = (b64.Length + Chunk - 1) / Chunk;
                var head = new Dictionary<string, object> { ["mime"] = Mime(Path.GetExtension(l.Path)), ["parts"] = parts, ["size"] = bytes.Length };
                r = await Net.Send("PUT", Url("audio/" + id, tok), J.Ser(head));
                if (!r.Ok) throw new Exception(Why(r, "audio"));
                for (int i = 0; i < parts; i++)
                {
                    var piece = b64.Substring(i * Chunk, Math.Min(Chunk, b64.Length - i * Chunk));
                    if (parts > 1) Shell.Toast("Публикуем трек… " + (i * 100 / parts) + "%");
                    r = await Net.Send("PUT", Url("audio/" + id + "/c" + i, tok), J.Ser(piece));
                    if (!r.Ok) throw new Exception(Why(r, "audio part " + i));
                }
                l.PubId = id; l.Public = true;
                Library.Save(false);
                LastError = null;
                Shell.Toast("Трек опубликован — его видят все в SONORA");
                try { await Catalog.LoadAsync(); } catch { }
            }
            catch (Exception ex)
            {
                Log.W("publish: " + ex);
                LastError = ex.Message;
                if (metaWritten && tok != null && id != null)
                {
                    try { await Net.Send("DELETE", Url("audio/" + id, tok)); } catch { }
                    try { await Net.Send("DELETE", Url("tracks/" + id, tok)); } catch { }
                }
                l.Public = false; Library.Save(false);
                var msg = ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException
                    ? "Нет связи с сервером SONORA — проверь интернет или VPN"
                    : ex.Message;
                Shell.Toast("Не удалось опубликовать: " + msg);
            }
        }
        public static async Task Down(LocalFile l)
        {
            try
            {
                if (!string.IsNullOrEmpty(l.PubId) && Auth.Current != null)
                {
                    var tok = await Auth.Token();
                    await Net.Send("DELETE", Url("audio/" + l.PubId, tok));   // сначала аудио: правило сверяет автора по карточке
                    await Net.Send("DELETE", Url("tracks/" + l.PubId, tok));
                }
                l.Public = false; l.PubId = null;
                Library.Save(false);
                Shell.Toast("Трек снова виден только тебе");
                try { await Catalog.LoadAsync(); } catch { }
            }
            catch (Exception ex) { Log.W("unpublish: " + ex.Message); Shell.Toast("Не удалось скрыть трек — проверь интернет"); }
        }
        public static async Task UpdateMeta(LocalFile l)
        {
            try
            {
                if (string.IsNullOrEmpty(l.PubId) || Auth.Current == null) return;
                var tok = await Auth.Token();
                var r = await Net.Send("PATCH", Url("tracks/" + l.PubId, tok), J.Ser(Meta(l)));
                if (!r.Ok) Why(r, "meta");
                Shell.Toast(r.Ok ? "Сохранено и обновлено в каталоге" : "Сохранено, но каталог не обновился");
                if (r.Ok) { try { await Catalog.LoadAsync(); } catch { } }
            }
            catch (Exception ex) { Log.W("publish meta: " + ex.Message); Shell.Toast("Сохранено, но каталог не обновился"); }
        }
    }
}
