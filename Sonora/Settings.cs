using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sonora
{
    public sealed partial class Shell
    {
        // ---------- Настройки ----------
        FrameworkElement SettingsPage()
        {
            var sp = new StackPanel();
            sp.Children.Add(PTitle("Настройки"));

            sp.Children.Add(SetTitle("Язык"));
            var langs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
            langs.Children.Add(LangCard("ru", "Русский", "Интерфейс на русском"));
            langs.Children.Add(LangCard("en", "English", "English interface"));
            sp.Children.Add(langs);

            sp.Children.Add(SetTitle("Оформление"));
            sp.Children.Add(Toggle("drop", "Живой фон плеера", "Цвета обложки плавно перетекают", Library.D.LiquidBg, v => { Library.D.LiquidBg = v; Library.Save(false); barLiquid.Sync(); Full.SyncLiquid(); }));
            sp.Children.Add(Toggle("wave", "Свечение в такт музыке", Beat.Available ? "Фон пульсирует под ритм трека" : "Недоступно на этом компьютере", Library.D.BeatGlow, v => { Library.D.BeatGlow = v; Library.Save(false); var t = Player.I.Current; if (v && t != null && t.IsLocal) Beat.Analyze(t, t.LocalPath); }));

            sp.Children.Add(SetTitle("Windows"));
            sp.Children.Add(SetBtn("windows", "Сделать SONORA плеером по умолчанию", null, () =>
            {
                WinReg.Register();
                try { System.Diagnostics.Process.Start("ms-settings:defaultapps"); Toast("Найди SONORA в списке и выбери её для MP3, FLAC и других форматов"); }
                catch { Toast("Открой «Параметры → Приложения → Приложения по умолчанию» и выбери SONORA"); }
            }));
            sp.Children.Add(SetBtn("music", "Медиа-клавиши и панель Windows", Smtc.On ? "работают" : "недоступны", () => Toast(Smtc.On ? "Клавиши ⏯ ⏭ ⏮ и всплывающая панель Windows управляют SONORA" : "Нужна Windows 10 или 11")));

            sp.Children.Add(SetTitle("Данные"));
            var off = Offline.Size() / 1024.0 / 1024.0;
            sp.Children.Add(SetBtn("download", "Скачанные в SONORA", U.Tn(Library.D.Offline.Count) + " · " + off.ToString("0.0") + " МБ", () => Go("playlist", Mixes.Downloaded)));
            if (Library.D.Offline.Count > 0)
                sp.Children.Add(SetBtn("trash", "Удалить все скачанные", null, () => Confirm("Удалить все скачанные треки?", "Они перестанут играть без интернета. Лайки и плейлисты останутся.", "Удалить", () => { Offline.Clear(); Toast("Скачанные треки удалены"); Render(); }, "danger"), Th.Danger));
            var mb = Catalog.CacheSize() / 1024.0 / 1024.0;
            sp.Children.Add(SetBtn("trash", "Очистить кэш аудио", mb.ToString("0.0") + " МБ", () => { Catalog.ClearCache(); Toast("Кэш очищен"); Render(); }));
            sp.Children.Add(SetBtn("refresh", "Обновить каталог", U.Tn(Catalog.Remote.Count), async () => { await Catalog.LoadAsync(); Toast("Каталог обновлён"); }));

            sp.Children.Add(SetTitle("Экран"));
            sp.Children.Add(SetBtn("expand", "Полноэкранный режим", "F11", () => (Window.GetWindow(this) as MainWindow)?.ToggleFullscreen()));

            sp.Children.Add(SetTitle("Подписка"));
            sp.Children.Add(SetBtn("star", "SONORA Plus", "скоро", () => Toast("Подписка появится в одном из следующих обновлений")));

            sp.Children.Add(SetTitle("О приложении"));
            sp.Children.Add(SetBtn("star", "SONORA для Windows", "версия " + Program.Version, () => Toast("Пробел — пауза, ←/→ — перемотка, Ctrl+←/→ — треки")));
            return sp;
        }

        FrameworkElement LangCard(string code, string name, string sub)
        {
            bool on = (Library.D.Lang ?? "ru") == code;
            var t = new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Th.Txt, FontFamily = Th.Body };
            var st = new TextBlock { Text = sub, FontSize = 12, Foreground = Th.Dim, FontFamily = Th.Body, Margin = new Thickness(0, 3, 0, 0) };
            var check = U.Circle(22, on ? Th.Accent : Th.Clear, on ? U.Center(U.Ico("check", 13, Th.White, null, 3)) : null, on ? Th.Accent : Th.BorderHi, 1.5);
            check.VerticalAlignment = VerticalAlignment.Center;
            var info = U.Col(t, st);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(0, 0, 10, 0);
            var b = new Border { Padding = new Thickness(16, 14, 16, 14), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(on ? 1.5 : 1), BorderBrush = on ? Th.Accent : Th.Border, Margin = new Thickness(0, 0, 8, 8), Child = U.Cols("* a", info, check) };
            U.Hover(b, on ? Th.Soft : Th.Card, on ? Th.Soft : Th.Card2);
            return U.Click(b, () =>
            {
                if (on) return;
                Library.D.Lang = code;
                Library.Save(false);
                Restart();
            }, .98);
        }
        static void Restart()
        {
            try
            {
                Library.Save(false);
                Single.Release();
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                System.Diagnostics.Process.Start(exe);
            }
            catch (Exception ex) { Log.W("restart: " + ex.Message); }
            Application.Current.Shutdown();
        }

        static FrameworkElement Toggle(string icon, string text, string sub, bool value, Action<bool> changed)
        {
            var ic = U.Ico(icon, 20, Th.Dim);
            ic.VerticalAlignment = VerticalAlignment.Center;
            var tt = U.Tx(text, 14, Th.Txt);
            var info = U.Col(tt, U.M(U.Tx(sub, 11.5, Th.Dim), 0, 2, 0, 0));
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(12, 0, 12, 0);
            bool v = value;
            var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Th.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border { Width = 42, Height = 24, CornerRadius = new CornerRadius(12), Padding = new Thickness(3), Child = knob, VerticalAlignment = VerticalAlignment.Center };
            Action paint = () =>
            {
                pill.Background = v ? (Brush)Th.Accent : Th.B("#FFFFFF", .16);
                knob.Margin = new Thickness(v ? 18 : 0, 0, 0, 0);
            };
            paint();
            var b = new Border { Padding = new Thickness(12, 12, 12, 12), CornerRadius = new CornerRadius(12), BorderBrush = Th.Border, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), Child = U.Cols("a * a", ic, info, pill) };
            U.Hover(b, Th.Card, Th.Card2);
            return U.Click(b, () => { v = !v; paint(); changed(v); }, .99);
        }

        // ---------- Гостевой режим ----------
        public void NeedAccount(string what)
        {
            var body = new StackPanel();
            var perks = new StackPanel();
            foreach (var s in new[] { "Треки целиком, без ограничения 30 секунд", "Лайки, плейлисты и своя музыка с компьютера", "Скачивание для офлайна и ссылки на треки", "Яндекс Музыка, Spotify и Deezer" })
            {
                var ic = U.Ico("check", 16, Th.Accent3, null, 2.5);
                var t = U.Wrap(s, 12.5, Th.Txt);
                t.Margin = new Thickness(8, 0, 0, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                perks.Children.Add(U.M(U.Cols("a *", ic, t), 0, 3, 0, 3));
            }
            body.Children.Add(new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(12), Background = Th.Soft, Margin = new Thickness(0, 0, 0, 14), Child = perks });
            body.Children.Add(Provider(U.Ico("user", 20, Th.White), "Создать аккаунт", () => OpenAuth(true, "register"), true));
            body.Children.Add(Provider(U.Ico("login", 20, Th.Txt), "У меня уже есть аккаунт", () => OpenAuth(true, "login"), false, true));
            body.Children.Add(Provider(null, "Не сейчас", CloseSheet, false, true));
            OpenSheet("Нужен аккаунт", L.T(what ?? "Это действие") + L.T(" — только для зарегистрированных. Без аккаунта можно слушать треки по 30 секунд."), body);
        }
        DateTime lastLimit = DateTime.MinValue;
        public void GuestLimit()
        {
            if (SheetOpen || (DateTime.Now - lastLimit).TotalSeconds < 2) return;
            lastLimit = DateTime.Now;
            NeedAccount("Полное прослушивание");
        }

        // ---------- Добавление своих файлов с проверкой ----------
        public async void AddFilesUi(IEnumerable<string> files, bool go = false)
        {
            if (!Guard.Need("Загрузка своей музыки")) return;
            Toast("Проверяем файлы…");
            Library.AddResult r;
            try { r = await Library.AddFilesAsync(files); }
            catch (Exception ex) { Log.W("add: " + ex); Toast("Не удалось добавить файлы"); return; }
            if (r.Rejected.Count == 0)
            {
                Toast(r.Added > 0 ? "Добавлено: " + U.Tn(r.Added) : r.Dupes > 0 ? "Эти файлы уже есть в библиотеке" : "Подходящих аудиофайлов не найдено");
                if (r.Added > 0 && go) Go("playlist", Mixes.Uploaded);
                return;
            }
            var body = new StackPanel();
            foreach (var x in r.Rejected.Take(30))
            {
                var name = U.Tx(Path.GetFileName(x.Path), 13.5, Th.Txt);
                var why = U.Wrap(x.Reason, 12, Th.Dim);
                why.Margin = new Thickness(0, 2, 0, 0);
                var ic = U.Ico("shield", 18, Th.Danger);
                ic.VerticalAlignment = VerticalAlignment.Top;
                var info = U.Col(name, why);
                info.Margin = new Thickness(10, 0, 0, 0);
                body.Children.Add(new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), Background = Th.Card, Margin = new Thickness(0, 0, 0, 6), Child = U.Cols("a *", ic, info) });
            }
            if (r.Rejected.Count > 30) body.Children.Add(U.M(U.Tx(L.T("и ещё ") + (r.Rejected.Count - 30), 12, Th.Dim), 4, 4, 0, 8));
            body.Children.Add(U.M(SheetBtn("Понятно", () => { CloseSheet(); if (r.Added > 0 && go) Go("playlist", Mixes.Uploaded); }), 0, 8, 0, 0));
            OpenSheet(L.T("Фильтр не пропустил: ") + r.Rejected.Count + " " + U.Plural(r.Rejected.Count, "файл", "файла", "файлов"),
                (r.Added > 0 ? L.T("Добавлено: ") + U.Tn(r.Added) + ". " : "") + L.T("SONORA проверяет файлы перед добавлением: формат, длительность, тишину, дубликаты и голосовые записи."), body);
        }

        // ---------- Файлы и ссылки, переданные при запуске («Открыть с помощью», sonora://) ----------
        public async void HandleArgs(string[] args)
        {
            if (args == null) return;
            var win = Window.GetWindow(this);
            if (win != null)
            {
                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                win.Show();
                win.Activate();
                win.Topmost = true; win.Topmost = false;
            }
            var files = new List<Track>();
            foreach (var a in args)
            {
                if (string.IsNullOrWhiteSpace(a) || a.StartsWith("--")) continue;
                var id = Share.Parse(a);
                if (id != null) { await OpenShared(id); return; }
                try
                {
                    if (File.Exists(a) && Library.AudioExt.Contains(Path.GetExtension(a).ToLowerInvariant()))
                    {
                        var full = Path.GetFullPath(a);
                        var known = Library.D.Local.FirstOrDefault(l => string.Equals(l.Path, full, StringComparison.OrdinalIgnoreCase));
                        var name = Path.GetFileNameWithoutExtension(full);
                        string artist = L.T("Неизвестный исполнитель"), title = name.Trim();
                        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
                        if (dash > 0) { artist = name.Substring(0, dash).Trim(); title = name.Substring(dash + 3).Trim(); }
                        files.Add(known != null
                            ? new Track { Id = known.Id, Title = known.Title, Artist = known.Artist, Album = "", Genre = "", Lyrics = "", Duration = known.Duration, LocalPath = known.Path }
                            : new Track { Id = "file_" + Library.Hash(full.ToLowerInvariant()), Title = title, Artist = artist, Album = "", Genre = "", Lyrics = "", LocalPath = full });
                    }
                }
                catch (Exception ex) { Log.W("arg: " + ex.Message); }
            }
            if (files.Count > 0) { Player.I.PlayList(files, 0); OpenFull(); }
        }
        public async Task OpenShared(string id)
        {
            var t = Catalog.Get(id);
            if (t == null && !Catalog.Loaded)
            {
                Toast("Открываем трек по ссылке…");
                for (int i = 0; i < 40 && !Catalog.Loaded; i++) await Task.Delay(250);
                t = Catalog.Get(id);
            }
            if (t == null) { Toast("Трек по ссылке не найден"); return; }
            Player.I.PlayList(new List<Track> { t }, 0);
            OpenFull();
        }
    }
}
