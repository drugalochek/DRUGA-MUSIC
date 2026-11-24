using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sonora
{
    public sealed partial class Shell
    {
        public static void UI(Action a)
        {
            var s = I;
            if (s == null || a == null) return;
            if (s.Dispatcher.CheckAccess()) a(); else s.Dispatcher.BeginInvoke(a);
        }
        static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Toast("Не удалось открыть браузер: " + ex.Message); }
        }
        static Brush UrlBrush(string url, int px = 100)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var img = Img.FromData("url:" + url, url, px);
            return Img.MakeBrush(img);
        }
        static Border Pill(string text, Action a, bool primary)
        {
            var t = U.Tx(text, 13, Th.White);
            t.FontWeight = FontWeights.SemiBold;
            var b = new Border { Padding = new Thickness(16, 9, 16, 9), CornerRadius = new CornerRadius(18), Child = t, VerticalAlignment = VerticalAlignment.Center };
            if (primary) { b.Background = Th.Hot; b.Effect = U.Shadow(18, 6, .6, Th.C("#2563EB")); }
            else { b.BorderBrush = Th.BorderHi; b.BorderThickness = new Thickness(1); U.Hover(b, Th.Glass(.06), Th.Glass(.12)); }
            return U.Click(b, a, .96);
        }
        static string Ago(int n) { return n > 0 ? U.Tn(n) : "плейлист"; }

        // ---------- страница «Музыкальные сервисы» ----------
        readonly Dictionary<string, string> extErr = new Dictionary<string, string>();
        readonly HashSet<string> extLoading = new HashSet<string>();

        FrameworkElement ServicesPage()
        {
            var sp = new StackPanel();
            sp.Children.Add(PTitle("Музыкальные сервисы"));
            sp.Children.Add(U.M(U.Wrap("Подключи свои аккаунты — лайки и плейлисты появятся здесь, а поиск SONORA будет искать сразу и в Яндекс Музыке, и в Deezer. Всё играет в одном плеере и одной очереди.", 13.5, Th.Dim), 0, -8, 0, 20));

            // Яндекс Музыка
            {
                var body = new StackPanel();
                if (Ext.YaOn)
                {
                    body.Children.Add(LRow(LCover(Th.Pink, "heartFill"), "Мне нравится", "Яндекс Музыка", () => Go("playlist", "ext:ya:likes")));
                    ListsBlock(body, "ya", Ext.YaLists, Ext.YaPlaylists);
                }
                else body.Children.Add(U.Wrap("Лайки, плейлисты и полные версии треков (с Яндекс Плюсом). Без Плюса — отрывки по 30 секунд.", 13, Th.Dim));
                sp.Children.Add(ServiceCard("Я", "Яндекс Музыка",
                    Ext.YaOn ? Ext.S.YaName + (Ext.S.YaPlus ? " · Плюс" : " · без Плюса") : "Не подключена", Ext.YaOn,
                    YandexSheet, () => Confirm("Отключить Яндекс Музыку?", "Лайки и плейлисты Яндекса пропадут из SONORA. Подключить снова можно в любой момент.", "Отключить", () => { Ext.YaDisconnect(); Toast("Яндекс Музыка отключена"); }), body));
            }
            // Spotify
            {
                var body = new StackPanel();
                if (Ext.SpOn)
                {
                    body.Children.Add(LRow(LCover(Th.Pink, "heartFill"), "Любимые треки", "Spotify", () => Go("playlist", "ext:sp:likes")));
                    ListsBlock(body, "sp", Ext.SpLists, Ext.SpPlaylists);
                    body.Children.Add(U.M(U.Wrap(Ext.YaOn ? "Треки Spotify играют целиком через Яндекс Музыку." : "Spotify не отдаёт звук сторонним плеерам, поэтому треки играют через Deezer отрывками. Подключи Яндекс Музыку — будут играть целиком.", 12, Th.Dim), 4, 8, 4, 0));
                }
                else body.Children.Add(U.Wrap("Лайки и плейлисты из Spotify. Звук берётся из Яндекс Музыки (целиком) или Deezer (отрывки) — Spotify не отдаёт его сторонним плеерам.", 13, Th.Dim));
                sp.Children.Add(ServiceCard("S", "Spotify",
                    Ext.SpOn ? Ext.S.SpName + (Ext.S.SpProduct == "premium" ? " · Premium" : "") : "Не подключён", Ext.SpOn,
                    SpotifySheet, () => Confirm("Отключить Spotify?", "Лайки и плейлисты Spotify пропадут из SONORA.", "Отключить", () => { Ext.SpDisconnect(); Toast("Spotify отключён"); }), body));
            }
            // Deezer
            {
                var body = new StackPanel();
                if (Ext.DzOn)
                {
                    body.Children.Add(LRow(LCover(Th.Pink, "heartFill"), "Любимые треки", "Deezer", () => Go("playlist", "ext:dz:likes")));
                    ListsBlock(body, "dz", Ext.DzLists, Ext.DzPlaylists);
                }
                else body.Children.Add(U.Wrap("Поиск по Deezer работает без входа (отрывки по 30 секунд). Укажи свой профиль — появятся любимые треки и плейлисты.", 13, Th.Dim));
                body.Children.Add(U.M(LRow(LCover(Th.Hot, "share"), "Открыть плейлист или альбом по ссылке", "deezer.com/playlist/…", () => DeezerSheet(true)), 0, 6, 0, 0));
                sp.Children.Add(ServiceCard("D", "Deezer",
                    Ext.DzOn ? Ext.S.DzName : "Профиль не указан", Ext.DzOn,
                    () => DeezerSheet(false), () => { Ext.DzDisconnect(); Toast("Профиль Deezer отключён"); }, body));
            }
            return sp;
        }

        FrameworkElement ServiceCard(string letter, string name, string status, bool on, Action connect, Action disconnect, FrameworkElement body)
        {
            var logo = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(15), Background = Th.Hot, Effect = U.Shadow(18, 6, .5, Th.C("#2563EB")), Child = U.Center(U.Tx(letter, 22, Th.White)) };
            var title = U.Tx(name, 18);
            title.Margin = new Thickness(0, 0, 0, 3);
            var st = U.Tx(status, 12.5, on ? Th.Accent2 : Th.Dim);
            var info = U.Col(title, st);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(14, 0, 12, 0);
            var btn = Pill(on ? "Отключить" : "Подключить", on ? disconnect : connect, !on);
            var head = U.Cols("a * a", logo, info, btn);
            body.Margin = new Thickness(0, 14, 0, 0);
            return new Border
            {
                CornerRadius = new CornerRadius(20), Background = Th.Glass(.045), BorderBrush = Th.GlassEdge, BorderThickness = new Thickness(1),
                Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14), Child = U.Col(head, body)
            };
        }

        void ListsBlock(StackPanel body, string src, List<ExtList> lists, Func<Task<List<ExtList>>> load)
        {
            if (lists == null)
            {
                if (extErr.TryGetValue("lists:" + src, out var err))
                {
                    body.Children.Add(U.M(U.Wrap(err, 12.5, Th.Danger), 4, 6, 4, 6));
                    body.Children.Add(LRow(LCover(Th.Hot, "refresh"), "Повторить", null, () => { extErr.Remove("lists:" + src); Render(); }));
                    return;
                }
                body.Children.Add(U.M(U.Tx("Загружаем плейлисты…", 12.5, Th.Dim), 4, 8, 4, 4));
                if (extLoading.Add("lists:" + src))
                {
                    Func<Task> run = async () =>
                    {
                        try { await load(); }
                        catch (Exception ex) { extErr["lists:" + src] = ex.Message; }
                        extLoading.Remove("lists:" + src);
                        if (Page == "services") Render();
                    };
                    run();
                }
                return;
            }
            foreach (var l in lists.Take(40))
            {
                var id = l.Id;
                body.Children.Add(LRow(LCover(Th.Hot, "music", false, UrlBrush(l.Cover)), l.Title, Ago(l.Count), () => Go("playlist", id)));
            }
            if (lists.Count > 40) body.Children.Add(U.M(U.Tx("и ещё " + (lists.Count - 40), 12, Th.Dim), 4, 4, 0, 0));
        }

        // открывает внешний список: показывает «загрузку», грузит и перерисовывает
        FrameworkElement ExtListLoading(string id)
        {
            var sp = new StackPanel();
            sp.Children.Add(BackBtn());
            var src = id.Split(':').Skip(1).FirstOrDefault();
            if (extErr.TryGetValue(id, out var err))
            {
                sp.Children.Add(Empty("warn", "Не удалось загрузить", err));
                sp.Children.Add(U.M(Pill("Попробовать снова", () => { extErr.Remove(id); Render(); }, true), 0, 4, 0, 0));
                ((FrameworkElement)sp.Children[sp.Children.Count - 1]).HorizontalAlignment = HorizontalAlignment.Center;
                return sp;
            }
            sp.Children.Add(Empty("music", "Загружаем…", Ext.SourceName(src)));
            if (extLoading.Add(id))
            {
                Func<Task> run = async () =>
                {
                    try { await Ext.LoadList(id); }
                    catch (Exception ex) { extErr[id] = ex.Message; }
                    extLoading.Remove(id);
                    if (Page == "playlist" && (Arg as string) == id) Render();
                };
                run();
            }
            return sp;
        }
        void SaveExtList(Mix mix)
        {
            if (!Guard.Need("Музыкальные сервисы")) return;
            var p = Library.NewPlaylist(mix.Name + " · " + mix.Tag, "#3b82f6");
            foreach (var t in mix.Tracks) if (!p.TrackIds.Contains(t.Id)) p.TrackIds.Add(t.Id);
            Library.Save();
            Toast("Сохранено в медиатеку: «" + p.Name + "»");
        }
        void RefreshExtList(string id)
        {
            Ext.Lists.Remove(id);
            extErr.Remove(id);
            Render();
        }

        // ---------- поиск по сервисам ----------
        int extSearchGen;
        async void ExtSearch(string q, StackPanel box, bool localEmpty)
        {
            int my = ++extSearchGen;
            var res = Ext.CachedSearch(q);
            if (res == null)
            {
                box.Children.Add(U.M(U.Tx(Ext.YaOn ? "Ищем в Яндекс Музыке и Deezer…" : "Ищем в Deezer…", 12.5, Th.Dim), 4, 14, 0, 0));
                await Task.Delay(ShotMode ? 0 : 420);
                if (my != extSearchGen) return;
                try { res = await Ext.SearchAll(q); } catch { res = null; }
                if (my != extSearchGen) return;
            }
            box.Children.Clear();
            var ya = res?.Item1 ?? new List<Track>();
            var dz = res?.Item2 ?? new List<Track>();
            if (ya.Count > 0)
            {
                box.Children.Add(SHead("Яндекс Музыка", U.Tn(ya.Count)));
                box.Children.Add(TrackList(ya, 10));
            }
            if (dz.Count > 0)
            {
                box.Children.Add(SHead(Ext.YaOn ? "Deezer" : "Deezer · отрывки 30 с", U.Tn(dz.Count)));
                box.Children.Add(TrackList(dz, 10));
            }
            if (ya.Count == 0 && dz.Count == 0 && localEmpty) box.Children.Add(Empty("search", "Ничего не нашлось", "Попробуй изменить запрос."));
            if (!Ext.YaOn && dz.Count > 0)
                box.Children.Add(U.M(LRow(LCover(Th.Hot, "globe"), "Подключи Яндекс Музыку", "Тогда найденные треки будут играть целиком", () => Go("services")), 0, 10, 0, 0));
        }

        // ---------- окна подключения ----------
        // Яндекс: вход по коду (как на телевизорах). Ничего копировать из браузера не нужно.
        public void YandexSheet()
        {
            if (!Guard.Need("Музыкальные сервисы")) return;
            var status = U.Wrap("", 12.5, Th.Dim);
            status.Margin = new Thickness(2, 0, 2, 10);
            status.Visibility = Visibility.Collapsed;
            var code = U.Tx("········", 34, Th.White);
            code.FontWeight = FontWeights.Bold;
            code.FontFamily = new FontFamily("Consolas");
            code.HorizontalAlignment = HorizontalAlignment.Center;
            var codeHint = U.Tx("Код для входа", 12, Th.Dim);
            codeHint.HorizontalAlignment = HorizontalAlignment.Center;
            var codeBox = new Border
            {
                Padding = new Thickness(16, 14, 16, 16), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(16),
                Background = Th.Soft, BorderBrush = Th.BorderHi, BorderThickness = new Thickness(1),
                Child = U.Col(codeHint, U.M(code, 0, 4, 0, 0))
            };
            string curCode = null, curUrl = "https://ya.ru/device";
            System.Threading.CancellationTokenSource cts = null;
            Action<string, Brush> say = (m, b) => { status.Text = m; status.Foreground = b; status.Visibility = string.IsNullOrEmpty(m) ? Visibility.Collapsed : Visibility.Visible; };
            Func<Task> start = null;
            start = async () =>
            {
                try { cts?.Cancel(); } catch { }
                var my = new System.Threading.CancellationTokenSource();
                cts = my;
                code.Text = "········"; curCode = null;
                say("Получаем код у Яндекса…", Th.Dim);
                try
                {
                    var c = await Ext.YaDeviceStart();
                    if (my.IsCancellationRequested) return;
                    curCode = c.User; curUrl = c.Url;
                    code.Text = c.User.ToUpperInvariant();
                    try { Clipboard.SetText(c.User); } catch { }
                    OpenUrl(c.Url);
                    say("Открыли " + c.Url.Replace("https://", "") + " в браузере. Войди в Яндекс и введи этот код (он уже скопирован — просто вставь). Ждём подтверждения…", Th.Dim);
                    await Ext.YaDeviceWait(c, my.Token);
                    if (my.IsCancellationRequested) return;
                    CloseSheet();
                    Toast("Яндекс Музыка подключена: " + Ext.S.YaName + (Ext.S.YaPlus ? " · Плюс" : ""));
                    Go("services");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (my.IsCancellationRequested) return;
                    Log.W("yandex: " + ex.Message);
                    say(ex.Message, Th.Danger);
                }
            };

            // запасной способ — вставить токен вручную
            var manualErr = U.Wrap("", 12.5, Th.Danger);
            manualErr.Visibility = Visibility.Collapsed;
            manualErr.Margin = new Thickness(2, 0, 2, 10);
            var inp = new Input("Токен или адрес страницы с access_token=…");
            bool busy = false;
            Func<Task> manual = async () =>
            {
                if (busy) return;
                busy = true; manualErr.Visibility = Visibility.Collapsed;
                try
                {
                    await Ext.YaConnect(inp.Text);
                    CloseSheet();
                    Toast("Яндекс Музыка подключена: " + Ext.S.YaName);
                    Go("services");
                }
                catch (Exception ex) { manualErr.Text = ex.Message; manualErr.Visibility = Visibility.Visible; }
                busy = false;
            };
            inp.Enter += async () => await manual();
            var manualBox = U.Col(
                U.M(U.Wrap("Открой ссылку, войди в Яндекс и скопируй адрес страницы, на которую попадёшь, целиком — в нём есть access_token=…", 12.5, Th.Dim, 0, 19), 0, 0, 0, 10),
                Provider(U.Ico("globe", 20, Th.Txt), "Открыть страницу входа", () => OpenUrl(Ext.YaAuthUrl)),
                inp, manualErr,
                SheetBtn("Подключить по токену", async () => await manual(), "ghost"));
            manualBox.Visibility = Visibility.Collapsed;
            var manualLink = U.Tx("Не получается? Подключить вручную", 12.5, Th.Accent3);
            manualLink.HorizontalAlignment = HorizontalAlignment.Center;
            manualLink.Margin = new Thickness(0, 12, 0, 8);
            U.Click(manualLink, () => { manualBox.Visibility = manualBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }, 1);

            var steps = U.Wrap("1. SONORA покажет код и откроет страницу Яндекса.\n2. Войди там в свой аккаунт и вставь код (он уже скопирован).\n3. Всё — Яндекс Музыка подключится сама, здесь ничего нажимать не нужно.", 13, Th.Dim, 0, 21);
            steps.Margin = new Thickness(0, 0, 0, 14);
            var body = U.Col(steps, codeBox, status,
                Acts(SheetBtn("Скопировать код", () => { if (curCode == null) return; try { Clipboard.SetText(curCode); Toast("Код скопирован: " + curCode.ToUpperInvariant()); } catch { } }, "ghost"),
                     SheetBtn("Открыть страницу", () => OpenUrl(curUrl), "ghost")),
                U.M(SheetBtn("Получить новый код", async () => await start()), 0, 10, 0, 0),
                manualLink, manualBox);
            OpenSheet("Яндекс Музыка", "Вход проходит на сайте Яндекса — пароль SONORA не видит. Ключ доступа хранится только на этом компьютере, в зашифрованном виде.", body,
                () => { try { cts?.Cancel(); } catch { } });
            if (!ShotMode) { var _ = start(); }
        }

        public void SpotifySheet()
        {
            if (!Guard.Need("Музыкальные сервисы")) return;
            var err = U.Wrap("", 12.5, Th.Danger);
            err.Visibility = Visibility.Collapsed;
            err.Margin = new Thickness(2, 0, 2, 10);
            var inp = new Input("Client ID (32 символа)");
            inp.Text = Ext.S.SpClientId ?? "";
            int gen = 0;
            Func<Task> go = async () =>
            {
                int my = ++gen;
                err.Visibility = Visibility.Collapsed;
                try
                {
                    err.Text = "Открыли браузер — нажми там «Agree / Принять». Если в браузере ошибка «Invalid redirect URI», проверь шаг 3 и нажми кнопку ещё раз.";
                    err.Foreground = Th.Dim; err.Visibility = Visibility.Visible;
                    await Ext.SpConnect(inp.Text);
                    CloseSheet();
                    Toast("Spotify подключён: " + Ext.S.SpName);
                    Go("services");
                }
                catch (Exception ex) { if (my == gen) { err.Text = ex.Message; err.Foreground = Th.Danger; err.Visibility = Visibility.Visible; } }
            };
            inp.Enter += async () => await go();
            var steps = U.Wrap(
                "Spotify не даёт сторонним плеерам общий ключ, поэтому один раз нужен свой Client ID (бесплатно, 2 минуты):\n" +
                "1. Нажми «Открыть кабинет Spotify» и войди своим аккаунтом Spotify.\n" +
                "2. Нажми «Create app». App name и Description — любые, например SONORA.\n" +
                "3. В поле «Redirect URIs» вставь " + Ext.SpRedirect + " (кнопка ниже копирует его) и нажми «Add».\n" +
                "4. В «Which API/SDKs are you planning to use?» отметь «Web API», поставь галочку согласия и нажми «Save».\n" +
                "5. Открой «Settings» приложения, скопируй Client ID, вставь его ниже и нажми «Войти через Spotify».", 13, Th.Dim, 0, 21);
            steps.Margin = new Thickness(0, 0, 0, 14);
            var body = U.Col(steps,
                Provider(U.Ico("globe", 20, Th.Txt), "Открыть кабинет Spotify", () => OpenUrl("https://developer.spotify.com/dashboard/create")),
                Provider(U.Ico("share", 20, Th.Txt), "Скопировать Redirect URI", () => { try { Clipboard.SetText(Ext.SpRedirect); Toast("Скопировано: " + Ext.SpRedirect); } catch { } }),
                inp, err,
                Acts(SheetBtn("Отмена", CloseSheet, "ghost"), SheetBtn("Войти через Spotify", async () => await go())));
            OpenSheet("Spotify", "Вход проходит на сайте Spotify — пароль SONORA не видит. Если Spotify в твоей стране недоступен, сайт может не открыться без VPN.", body, Ext.SpCancel);
        }

        public void DeezerSheet(bool linkOnly)
        {
            if (!Guard.Need("Музыкальные сервисы")) return;
            var err = U.Wrap("", 12.5, Th.Danger);
            err.Visibility = Visibility.Collapsed;
            err.Margin = new Thickness(2, 0, 2, 10);
            var inp = new Input(linkOnly ? "https://www.deezer.com/playlist/…" : "Ссылка на профиль или его номер");
            bool busy = false;
            Func<Task> go = async () =>
            {
                if (busy) return;
                busy = true; err.Visibility = Visibility.Collapsed;
                try
                {
                    var r = await Ext.DzParseLink(inp.Text);
                    if (r == null) throw new ExtException("Не похоже на ссылку Deezer. Пример: https://www.deezer.com/profile/123456");
                    if (r.Item1 == "profile")
                    {
                        await Ext.DzConnect(r.Item2);
                        CloseSheet();
                        Toast("Профиль Deezer подключён: " + Ext.S.DzName);
                        Go("services");
                    }
                    else if (r.Item1 == "playlist" || r.Item1 == "album")
                    {
                        var id = "ext:dz:" + (r.Item1 == "album" ? "al" : "pl") + ":" + r.Item2;
                        await Ext.LoadList(id);
                        CloseSheet();
                        Go("playlist", id);
                    }
                    else throw new ExtException("Это ссылка на исполнителя — вставь ссылку на плейлист, альбом или профиль");
                }
                catch (Exception ex) { err.Text = ex.Message; err.Visibility = Visibility.Visible; }
                busy = false;
            };
            inp.Enter += async () => await go();
            var steps = U.Wrap(linkOnly
                ? "Вставь ссылку на любой публичный плейлист или альбом Deezer — он откроется в SONORA, и его можно сохранить в медиатеку."
                : "Открой свой профиль на deezer.com и скопируй адрес из строки браузера (deezer.com/profile/…). Профиль должен быть публичным.", 13, Th.Dim, 0, 21);
            steps.Margin = new Thickness(0, 0, 0, 14);
            var body = U.Col(steps, inp, err, Acts(SheetBtn("Отмена", CloseSheet, "ghost"), SheetBtn(linkOnly ? "Открыть" : "Подключить", async () => await go())));
            OpenSheet(linkOnly ? "Плейлист Deezer" : "Профиль Deezer", null, body);
        }
    }
}
