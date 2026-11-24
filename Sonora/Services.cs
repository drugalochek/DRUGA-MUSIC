using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sonora
{
    // ---------- Подключённые музыкальные сервисы: Яндекс Музыка, Spotify, Deezer ----------
    public sealed class ExtTrackRec
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Cover { get; set; }
        public string Src { get; set; }
        public string Ref { get; set; }
        public string Isrc { get; set; }
        public string Match { get; set; }
        public double Duration { get; set; }
    }

    public sealed class ExtState
    {
        public string YaToken { get; set; }
        public string YaUid { get; set; }
        public string YaName { get; set; }
        public bool YaPlus { get; set; }
        public string SpClientId { get; set; }
        public string SpAccess { get; set; }
        public string SpRefresh { get; set; }
        public long SpExpires { get; set; }
        public string SpName { get; set; }
        public string SpProduct { get; set; }
        public string DzUser { get; set; }
        public string DzName { get; set; }
        public List<ExtTrackRec> Tracks { get; set; } = new List<ExtTrackRec>();
    }

    public sealed class ExtList
    {
        public string Id, Title, Sub, Cover;
        public int Count;
        public bool Liked;
    }

    public sealed class ExtException : Exception { public ExtException(string m) : base(m) { } }

    public static class Ext
    {
        public const int SpPort = 43117;
        public static readonly string SpRedirect = "http://127.0.0.1:" + SpPort + "/callback";
        public const string YaClientId = "23cabbbdc6cd418abb4b39c32c41195d";
        const string YaSecret = "53bc75238f0c4d08a118e51fe9203300";
        public static readonly string YaAuthUrl = "https://oauth.yandex.ru/authorize?response_type=token&client_id=" + YaClientId;
        const string YaApi = "https://api.music.yandex.net";
        const string DzApi = "https://api.deezer.com";
        const string SpApi = "https://api.spotify.com/v1";

        public static ExtState S = new ExtState();
        static readonly Dictionary<string, Track> known = new Dictionary<string, Track>();
        public static readonly Dictionary<string, Mix> Lists = new Dictionary<string, Mix>();
        public static List<ExtList> YaLists, SpLists, DzLists;
        public static event Action Changed;

        public static bool YaOn { get { return !string.IsNullOrEmpty(S.YaToken) && !string.IsNullOrEmpty(S.YaUid); } }
        public static bool SpOn { get { return !string.IsNullOrEmpty(S.SpRefresh); } }
        public static bool DzOn { get { return !string.IsNullOrEmpty(S.DzUser); } }

        static string FileP { get { return Path.Combine(Paths.Data, "services.json"); } }

        // ---------- хранение ----------
        public static void Load()
        {
            try
            {
                if (File.Exists(FileP)) S = J.To<ExtState>(File.ReadAllText(FileP)) ?? new ExtState();
            }
            catch (Exception ex) { Log.W("services: " + ex.Message); S = new ExtState(); }
            if (S.Tracks == null) S.Tracks = new List<ExtTrackRec>();
            S.YaToken = Unprotect(S.YaToken);
            S.SpAccess = Unprotect(S.SpAccess);
            S.SpRefresh = Unprotect(S.SpRefresh);
            foreach (var r in S.Tracks)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                known[r.Id] = new Track { Id = r.Id, Title = r.Title, Artist = r.Artist, Album = r.Album ?? "", Cover = r.Cover, Source = r.Src, Ref = r.Ref, Isrc = r.Isrc, Match = r.Match, Duration = r.Duration, Genre = "", Lyrics = "" };
            }
            Library.Changed += () => Save();
        }
        public static void Save()
        {
            if (!Library.Persist) return;
            try
            {
                var used = new HashSet<string>(Library.D.Fav.Concat(Library.D.Recent).Concat(Library.D.Playlists.SelectMany(p => p.TrackIds)));
                var copy = new ExtState
                {
                    YaToken = Protect(S.YaToken), YaUid = S.YaUid, YaName = S.YaName, YaPlus = S.YaPlus,
                    SpClientId = S.SpClientId, SpAccess = Protect(S.SpAccess), SpRefresh = Protect(S.SpRefresh), SpExpires = S.SpExpires, SpName = S.SpName, SpProduct = S.SpProduct,
                    DzUser = S.DzUser, DzName = S.DzName,
                    Tracks = KnownList().Where(t => used.Contains(t.Id)).Select(t => new ExtTrackRec { Id = t.Id, Title = t.Title, Artist = t.Artist, Album = t.Album, Cover = t.Cover, Src = t.Source, Ref = t.Ref, Isrc = t.Isrc, Match = t.Match, Duration = t.Duration }).ToList()
                };
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(FileP, J.Ser(copy));
            }
            catch (Exception ex) { Log.W("services save: " + ex.Message); }
        }
        static List<Track> KnownList() { lock (known) return known.Values.ToList(); }
        static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try { return "dp:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
            catch { return s; }
        }
        static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith("dp:")) return s;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.Substring(3)), null, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
        static void Fire() { Save(); var h = Changed; if (h != null) Shell.UI(() => h()); }

        public static Track Get(string id)
        {
            if (id == null) return null;
            lock (known) { known.TryGetValue(id, out var t); return t; }
        }
        static Track Remember(Track t)
        {
            if (t == null) return null;
            lock (known) return RememberCore(t);
        }
        static Track RememberCore(Track t)
        {
            if (known.TryGetValue(t.Id, out var old))
            {
                old.Title = t.Title; old.Artist = t.Artist; old.Album = t.Album; old.Cover = t.Cover ?? old.Cover;
                if (t.Duration > 0) old.Duration = t.Duration;
                if (!string.IsNullOrEmpty(t.Isrc)) old.Isrc = t.Isrc;
                return old;
            }
            known[t.Id] = t;
            return t;
        }
        public static string SourceName(string src)
        {
            switch (src) { case "ya": return "Яндекс Музыка"; case "sp": return "Spotify"; case "dz": return "Deezer"; default: return "SONORA"; }
        }

        // ---------- сеть ----------
        static async Task<Net.Resp> Req(string method, string url, Dictionary<string, string> headers, string form = null, string json = null)
        {
            using (var req = new HttpRequestMessage(new HttpMethod(method), url))
            {
                if (headers != null) foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                else if (form != null) req.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");
                try
                {
                    using (var r = await Net.Http.SendAsync(req).ConfigureAwait(false))
                        return new Net.Resp { Code = (int)r.StatusCode, Body = await r.Content.ReadAsStringAsync().ConfigureAwait(false) };
                }
                catch (HttpRequestException) { throw new ExtException("Нет соединения с сервером — проверь интернет"); }
                catch (TaskCanceledException) { throw new ExtException("Сервер слишком долго не отвечает"); }
            }
        }
        static IEnumerable<Dictionary<string, object>> Arr(object o)
        {
            if (o is System.Collections.IEnumerable e && !(o is string) && !(o is Dictionary<string, object>))
                foreach (var x in e) { var d = J.D(x); if (d != null) yield return d; }
        }
        static object P(object o, params string[] path)
        {
            foreach (var k in path)
            {
                var d = J.D(o);
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }
        static string Md5(string s)
        {
            using (var m = MD5.Create()) return string.Concat(m.ComputeHash(Encoding.UTF8.GetBytes(s)).Select(b => b.ToString("x2")));
        }
        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s.ToLowerInvariant(), @"\(.*?\)|\[.*?\]|feat\..*$|ft\..*$", " ");
            s = s.Replace('ё', 'е');
            return Regex.Replace(s, @"[^\p{L}\p{N}]+", " ").Trim();
        }
        static Track Best(List<Track> cands, Track want)
        {
            if (cands == null || cands.Count == 0) return null;
            string wt = Norm(want.Title), wa = Norm((want.Artist ?? "").Split(',')[0]);
            Track best = null; int bestScore = -1;
            foreach (var c in cands.Take(10))
            {
                string ct = Norm(c.Title), ca = Norm(c.Artist);
                int sc = 0;
                if (ct == wt) sc += 4; else if (ct.Contains(wt) || wt.Contains(ct)) sc += 2;
                if (wa.Length > 0 && (ca.Contains(wa) || wa.Contains(ca))) sc += 3;
                if (want.Duration > 0 && c.Duration > 0 && Math.Abs(want.Duration - c.Duration) < 6) sc += 1;
                if (sc > bestScore) { bestScore = sc; best = c; }
            }
            return bestScore >= 4 ? best : null;
        }

        // ================= Яндекс Музыка =================
        static Dictionary<string, string> YaHeaders()
        {
            var h = new Dictionary<string, string> { ["X-Yandex-Music-Client"] = "YandexMusicAndroid/24023621", ["Accept-Language"] = "ru" };
            if (!string.IsNullOrEmpty(S.YaToken)) h["Authorization"] = "OAuth " + S.YaToken;
            return h;
        }
        static async Task<object> Ya(string method, string path, string form = null)
        {
            var r = await Req(method, path.StartsWith("http") ? path : YaApi + path, YaHeaders(), form).ConfigureAwait(false);
            if (r.Code == 401) throw new ExtException("Вход в Яндекс Музыку устарел — подключи её заново");
            if (r.Code == 451) throw new ExtException("Яндекс Музыка недоступна в этом регионе");
            if (!r.Ok) throw new ExtException("Яндекс Музыка ответила ошибкой " + r.Code);
            return P(J.Parse(r.Body), "result");
        }
        public static string ParseYaToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = Regex.Match(s, @"access_token=([^&\s#]+)");
            if (m.Success) return Uri.UnescapeDataString(m.Groups[1].Value);
            s = s.Trim();
            return Regex.IsMatch(s, @"^[A-Za-z0-9_\-\.]{20,}$") ? s : null;
        }
        public static async Task YaConnect(string input)
        {
            var token = ParseYaToken(input);
            if (token == null) throw new ExtException("Не нашли токен. Скопируй адрес страницы целиком — в нём есть access_token=…");
            var oldTok = S.YaToken; S.YaToken = token;
            try
            {
                var res = await Ya("GET", "/account/status").ConfigureAwait(false);
                var acc = J.D(P(res, "account"));
                var uid = J.Str(acc, "uid");
                if (string.IsNullOrEmpty(uid) || uid == "0") throw new ExtException("Яндекс не принял токен — попробуй войти ещё раз");
                S.YaUid = uid;
                S.YaName = J.Str(acc, "displayName") ?? J.Str(acc, "login") ?? J.Str(acc, "fullName") ?? "Яндекс";
                S.YaPlus = J.Bool(J.D(P(res, "plus")), "hasPlus");
            }
            catch { S.YaToken = oldTok; throw; }
            YaLists = null;
            Fire();
        }
        public sealed class YaCode { public string Device, User, Url; public int Interval, Expires; }
        static Dictionary<string, object> Js(string body) { try { return J.D(J.Parse(body)); } catch { return null; } }
        public static async Task<YaCode> YaDeviceStart()
        {
            var r = await Req("POST", "https://oauth.yandex.ru/device/code", null,
                Net.Form("client_id", YaClientId, "device_name", "SONORA для Windows")).ConfigureAwait(false);
            var d = Js(r.Body);
            var dev = J.Str(d, "device_code");
            if (!r.Ok || string.IsNullOrEmpty(dev))
                throw new ExtException("Яндекс не выдал код входа: " + (J.Str(d, "error_description") ?? J.Str(d, "error") ?? ("HTTP " + r.Code)));
            return new YaCode
            {
                Device = dev, User = J.Str(d, "user_code") ?? "",
                Url = J.Str(d, "verification_url") ?? "https://ya.ru/device",
                Interval = Math.Max(2, (int)J.Num(d, "interval")),
                Expires = Math.Max(60, (int)J.Num(d, "expires_in"))
            };
        }
        public static async Task YaDeviceWait(YaCode c, System.Threading.CancellationToken ct)
        {
            var until = DateTime.UtcNow.AddSeconds(c.Expires);
            int iv = c.Interval;
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(iv * 1000, ct).ConfigureAwait(false);
                Net.Resp r;
                try
                {
                    r = await Req("POST", "https://oauth.yandex.ru/token", null, Net.Form(
                        "grant_type", "device_code", "code", c.Device, "client_id", YaClientId, "client_secret", YaSecret)).ConfigureAwait(false);
                }
                catch (ExtException) { continue; }
                ct.ThrowIfCancellationRequested();
                var d = Js(r.Body);
                var tok = J.Str(d, "access_token");
                if (r.Ok && !string.IsNullOrEmpty(tok)) { await YaConnect(tok).ConfigureAwait(false); return; }
                var err = J.Str(d, "error");
                if (err == "authorization_pending") continue;
                if (err == "slow_down") { iv += 2; continue; }
                if (err == "expired_token") break;
                if (err == "access_denied") throw new ExtException("Вход в Яндекс отменён. Нажми «Получить новый код».");
                throw new ExtException("Яндекс отклонил вход: " + (J.Str(d, "error_description") ?? err ?? ("HTTP " + r.Code)));
            }
            throw new ExtException("Код устарел. Нажми «Получить новый код».");
        }
        public static void YaDisconnect()
        {
            S.YaToken = null; S.YaUid = null; S.YaName = null; S.YaPlus = false; YaLists = null;
            foreach (var k in Lists.Keys.Where(k => k.StartsWith("ext:ya:")).ToList()) Lists.Remove(k);
            Fire();
        }
        static Track YaTrack(Dictionary<string, object> t)
        {
            if (t == null) return null;
            var id = J.Str(t, "id");
            if (string.IsNullOrEmpty(id)) return null;
            if (t.ContainsKey("available") && !J.Bool(t, "available")) return null;
            var title = J.Str(t, "title") ?? "—";
            var ver = J.Str(t, "version");
            if (!string.IsNullOrEmpty(ver)) title += " (" + ver + ")";
            var artists = string.Join(", ", Arr(P(t, "artists")).Select(a => J.Str(a, "name")).Where(n => !string.IsNullOrEmpty(n)));
            var album = Arr(P(t, "albums")).FirstOrDefault();
            var cover = J.Str(t, "coverUri") ?? J.Str(album, "coverUri");
            return Remember(new Track
            {
                Id = "ya:" + id, Ref = id, Source = "ya",
                Title = title, Artist = string.IsNullOrEmpty(artists) ? "—" : artists,
                Album = J.Str(album, "title") ?? "", Genre = J.Str(album, "genre") ?? "", Lyrics = "",
                Year = (int)J.Num(album, "year"),
                Cover = string.IsNullOrEmpty(cover) ? null : "https://" + cover.Replace("%%", "400x400"),
                Duration = J.Num(t, "durationMs") / 1000.0,
            });
        }
        public static async Task<List<Track>> YaSearch(string q)
        {
            var res = await Ya("GET", "/search?" + Net.Form("text", q, "type", "track", "page", "0", "nocorrect", "false")).ConfigureAwait(false);
            return Arr(P(res, "tracks", "results")).Select(YaTrack).Where(t => t != null).ToList();
        }
        static async Task<List<Track>> YaByIds(List<string> ids)
        {
            var res = new List<Track>();
            for (int i = 0; i < ids.Count; i += 200)
            {
                var chunk = ids.Skip(i).Take(200).ToList();
                var r = await Ya("POST", "/tracks", Net.Form("track-ids", string.Join(",", chunk), "with-positions", "false")).ConfigureAwait(false);
                var map = new Dictionary<string, Track>();
                foreach (var t in Arr(r).Select(YaTrack)) if (t != null) map[t.Ref] = t;
                foreach (var id in chunk) { var key = id.Split(':')[0]; if (map.TryGetValue(key, out var t)) res.Add(t); }
            }
            return res;
        }
        public static async Task<List<ExtList>> YaPlaylists()
        {
            var r = await Ya("GET", "/users/" + S.YaUid + "/playlists/list").ConfigureAwait(false);
            var list = new List<ExtList>();
            foreach (var p in Arr(r))
            {
                var kind = J.Str(p, "kind");
                if (kind == null) continue;
                var cover = J.Str(J.D(P(p, "cover")), "uri");
                var items = P(p, "cover", "itemsUri") as System.Collections.IEnumerable;
                if (cover == null && items != null) foreach (var x in items) { cover = x as string; break; }
                if (cover == null) cover = J.Str(p, "ogImage");
                list.Add(new ExtList { Id = "ext:ya:pl:" + kind, Title = J.Str(p, "title") ?? "Плейлист", Count = (int)J.Num(p, "trackCount"), Cover = cover == null ? null : "https://" + cover.Replace("%%", "200x200") });
            }
            return YaLists = list;
        }
        static async Task<List<Track>> YaLikes()
        {
            var r = await Ya("GET", "/users/" + S.YaUid + "/likes/tracks").ConfigureAwait(false);
            var ids = Arr(P(r, "library", "tracks")).Select(t => J.Str(t, "id")).Where(x => !string.IsNullOrEmpty(x)).Take(2000).ToList();
            return await YaByIds(ids).ConfigureAwait(false);
        }
        static async Task<List<Track>> YaPlaylist(string kind)
        {
            var r = await Ya("GET", "/users/" + S.YaUid + "/playlists/" + kind).ConfigureAwait(false);
            var res = new List<Track>();
            var missing = new List<string>();
            foreach (var x in Arr(P(r, "tracks")))
            {
                var t = YaTrack(J.D(P(x, "track")));
                if (t != null) res.Add(t);
                else { var id = J.Str(x, "id"); if (!string.IsNullOrEmpty(id)) missing.Add(id); }
            }
            if (res.Count == 0 && missing.Count > 0) res = await YaByIds(missing).ConfigureAwait(false);
            return res;
        }
        static async Task<string> YaStream(string id)
        {
            var r = await Ya("GET", "/tracks/" + id + "/download-info").ConfigureAwait(false);
            var opts = Arr(r).ToList();
            if (opts.Count == 0) throw new ExtException("Яндекс не отдал этот трек");
            var pick = opts.Where(o => (J.Str(o, "codec") ?? "") == "mp3")
                           .OrderBy(o => J.Bool(o, "preview") ? 1 : 0).ThenByDescending(o => J.Num(o, "bitrateInKbps")).FirstOrDefault() ?? opts[0];
            var infoUrl = J.Str(pick, "downloadInfoUrl");
            var x = await Req("GET", infoUrl, YaHeaders()).ConfigureAwait(false);
            if (!x.Ok) throw new ExtException("Яндекс не отдал ссылку на трек (" + x.Code + ")");
            Func<string, string> tag = n => { var m = Regex.Match(x.Body, "<" + n + ">(.*?)</" + n + ">", RegexOptions.Singleline); return m.Success ? m.Groups[1].Value : ""; };
            string host = tag("host"), path = tag("path"), ts = tag("ts"), s = tag("s");
            if (host.Length == 0 || path.Length == 0) throw new ExtException("Неожиданный ответ Яндекса");
            var sign = Md5("XGRlBW9FXlekgbPrRHuSiA" + path.Substring(1) + s);
            return "https://" + host + "/get-mp3/" + sign + "/" + ts + path;
        }

        // ================= Deezer =================
        static async Task<object> Dz(string pathOrUrl)
        {
            var r = await Req("GET", pathOrUrl.StartsWith("http") ? pathOrUrl : DzApi + pathOrUrl, null).ConfigureAwait(false);
            if (!r.Ok) throw new ExtException("Deezer ответил ошибкой " + r.Code);
            var d = J.Parse(r.Body);
            var err = J.D(P(d, "error"));
            if (err != null) throw new ExtException(J.Num(err, "code") == 800 ? "Deezer: не найдено" : "Deezer: " + (J.Str(err, "message") ?? "ошибка"));
            return d;
        }
        static Track DzTrack(Dictionary<string, object> t, Dictionary<string, object> albumOverride = null)
        {
            if (t == null) return null;
            var id = J.Str(t, "id");
            if (string.IsNullOrEmpty(id) || (t.ContainsKey("readable") && !J.Bool(t, "readable"))) return null;
            var al = J.D(P(t, "album")) ?? albumOverride;
            if (albumOverride != null && al != null && J.Str(al, "cover_big") == null) al = albumOverride;
            return Remember(new Track
            {
                Id = "dz:" + id, Ref = id, Source = "dz",
                Title = J.Str(t, "title") ?? "—", Artist = J.Str(J.D(P(t, "artist")), "name") ?? "—",
                Album = J.Str(al, "title") ?? "", Genre = "", Lyrics = "",
                Cover = J.Str(al, "cover_big") ?? J.Str(al, "cover_medium"),
                Duration = J.Num(t, "duration"), Isrc = J.Str(t, "isrc"),
            });
        }
        public static async Task<List<Track>> DzSearch(string q)
        {
            var d = await Dz("/search?" + Net.Form("q", q, "limit", "25")).ConfigureAwait(false);
            return Arr(P(d, "data")).Select(x => DzTrack(x)).Where(t => t != null).ToList();
        }
        static async Task<List<Track>> DzPaged(string first, int max = 2000)
        {
            var res = new List<Track>();
            string next = first;
            while (next != null && res.Count < max)
            {
                var d = await Dz(next).ConfigureAwait(false);
                res.AddRange(Arr(P(d, "data")).Select(x => DzTrack(x)).Where(t => t != null));
                next = J.Str(J.D(d), "next");
            }
            return res;
        }
        // ссылка на профиль/плейлист/альбом Deezer → (тип, id)
        public static async Task<Tuple<string, string>> DzParseLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;
            link = link.Trim();
            var m = Regex.Match(link, @"(playlist|profile|user|album|artist)/(\d+)");
            if (!m.Success && Regex.IsMatch(link, @"^\d+$")) return Tuple.Create("profile", link);
            if (!m.Success && (link.Contains("link.deezer.com") || link.Contains("deezer.page.link")))
            {
                try
                {
                    using (var r = await Net.Http.GetAsync(link).ConfigureAwait(false))
                    {
                        var final = r.RequestMessage.RequestUri.ToString();
                        var body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                        m = Regex.Match(final, @"(playlist|profile|user|album|artist)/(\d+)");
                        if (!m.Success) m = Regex.Match(body, @"deezer\.com/(?:\w{2}/)?(playlist|profile|album|artist)/(\d+)");
                    }
                }
                catch { }
            }
            if (!m.Success) return null;
            var kind = m.Groups[1].Value == "user" ? "profile" : m.Groups[1].Value;
            return Tuple.Create(kind, m.Groups[2].Value);
        }
        public static async Task DzConnect(string userId)
        {
            var d = J.D(await Dz("/user/" + userId).ConfigureAwait(false));
            S.DzUser = userId;
            S.DzName = J.Str(d, "name") ?? ("Deezer " + userId);
            DzLists = null;
            Fire();
        }
        public static void DzDisconnect()
        {
            S.DzUser = null; S.DzName = null; DzLists = null;
            foreach (var k in Lists.Keys.Where(k => k.StartsWith("ext:dz:")).ToList()) Lists.Remove(k);
            Fire();
        }
        public static async Task<List<ExtList>> DzPlaylists()
        {
            var d = await Dz("/user/" + S.DzUser + "/playlists?limit=100").ConfigureAwait(false);
            return DzLists = Arr(P(d, "data")).Where(p => !J.Bool(p, "is_loved_track"))
                .Select(p => new ExtList { Id = "ext:dz:pl:" + J.Str(p, "id"), Title = J.Str(p, "title") ?? "Плейлист", Count = (int)J.Num(p, "nb_tracks"), Cover = J.Str(p, "picture_medium") }).ToList();
        }
        static async Task<string> DzStream(Track t)
        {
            var d = J.D(await Dz("/track/" + t.Ref).ConfigureAwait(false));
            var url = J.Str(d, "preview");
            if (string.IsNullOrEmpty(url)) throw new ExtException("У этого трека в Deezer нет отрывка");
            return url;
        }

        // ================= Spotify =================
        static string B64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        static byte[] Rnd(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }

        static System.Threading.CancellationTokenSource spCts;
        public static void SpCancel() { try { spCts?.Cancel(); } catch { } }
        public static async Task SpConnect(string clientId)
        {
            SpCancel();
            clientId = (clientId ?? "").Trim();
            if (!Regex.IsMatch(clientId, "^[0-9a-fA-F]{32}$")) throw new ExtException("Client ID — это 32 символа (буквы a–f и цифры) из кабинета Spotify");
            var verifier = B64Url(Rnd(48));
            string challenge;
            using (var sha = SHA256.Create()) challenge = B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var state = B64Url(Rnd(16));
            var cts = new System.Threading.CancellationTokenSource();
            spCts = cts;
            TcpListener listener = null;
            for (int i = 0; i < 10 && listener == null; i++)
            {
                try { listener = new TcpListener(IPAddress.Loopback, SpPort); listener.Start(); }
                catch { listener = null; await Task.Delay(200).ConfigureAwait(false); }
            }
            if (listener == null)
                throw new ExtException("Порт " + SpPort + " занят другой программой — закрой её и попробуй снова");
            string code;
            try
            {
                var url = "https://accounts.spotify.com/authorize?" + Net.Form(
                    "client_id", clientId, "response_type", "code", "redirect_uri", SpRedirect,
                    "code_challenge_method", "S256", "code_challenge", challenge, "state", state,
                    "scope", "user-library-read playlist-read-private playlist-read-collaborative user-read-private user-read-email");
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { throw new ExtException("Не удалось открыть браузер: " + ex.Message); }
                using (cts.Token.Register(() => { try { listener.Stop(); } catch { } }))
                {
                    try { code = await WaitForCode(listener, state, "Spotify").ConfigureAwait(false); }
                    catch (Exception) when (cts.IsCancellationRequested) { throw new ExtException("Подключение Spotify отменено"); }
                }
            }
            finally { try { listener.Stop(); } catch { } if (spCts == cts) spCts = null; }

            var tr = await Req("POST", "https://accounts.spotify.com/api/token", null, Net.Form(
                "grant_type", "authorization_code", "code", code, "redirect_uri", SpRedirect, "client_id", clientId, "code_verifier", verifier)).ConfigureAwait(false);
            var td = J.D(J.Parse(tr.Body));
            if (!tr.Ok || J.Str(td, "access_token") == null)
                throw new ExtException("Spotify не выдал доступ: " + (J.Str(td, "error_description") ?? J.Str(td, "error") ?? ("HTTP " + tr.Code)));
            S.SpClientId = clientId;
            S.SpAccess = J.Str(td, "access_token");
            S.SpRefresh = J.Str(td, "refresh_token");
            S.SpExpires = Library.Now + (long)(J.Num(td, "expires_in") * 1000);
            try
            {
                var me = J.D(await Sp(SpApi + "/me").ConfigureAwait(false));
                S.SpName = J.Str(me, "display_name") ?? J.Str(me, "id") ?? "Spotify";
                S.SpProduct = J.Str(me, "product");
            }
            catch (Exception ex) { Log.W("spotify me: " + ex.Message); S.SpName = "Spotify"; }
            SpLists = null;
            Fire();
        }
        public static void SpDisconnect()
        {
            S.SpAccess = null; S.SpRefresh = null; S.SpExpires = 0; S.SpName = null; S.SpProduct = null; SpLists = null;
            foreach (var k in Lists.Keys.Where(k => k.StartsWith("ext:sp:")).ToList()) Lists.Remove(k);
            Fire();
        }
        static async Task<string> SpToken(bool force = false)
        {
            if (!force && !string.IsNullOrEmpty(S.SpAccess) && S.SpExpires > Library.Now + 60000) return S.SpAccess;
            if (string.IsNullOrEmpty(S.SpRefresh)) throw new ExtException("Spotify не подключён");
            var r = await Req("POST", "https://accounts.spotify.com/api/token", null, Net.Form("grant_type", "refresh_token", "refresh_token", S.SpRefresh, "client_id", S.SpClientId)).ConfigureAwait(false);
            var d = J.D(J.Parse(r.Body));
            if (!r.Ok || J.Str(d, "access_token") == null)
            {
                if (r.Code == 400) { SpDisconnect(); throw new ExtException("Вход в Spotify устарел — подключи его заново"); }
                throw new ExtException("Spotify: не удалось обновить вход (" + r.Code + ")");
            }
            S.SpAccess = J.Str(d, "access_token");
            if (J.Str(d, "refresh_token") != null) S.SpRefresh = J.Str(d, "refresh_token");
            S.SpExpires = Library.Now + (long)(J.Num(d, "expires_in") * 1000);
            Save();
            return S.SpAccess;
        }
        static async Task<object> Sp(string url)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var tok = await SpToken(attempt > 0).ConfigureAwait(false);
                var r = await Req("GET", url, new Dictionary<string, string> { ["Authorization"] = "Bearer " + tok }).ConfigureAwait(false);
                if (r.Code == 401) continue;
                if (r.Code == 429) { await Task.Delay(1500).ConfigureAwait(false); continue; }
                if (r.Code == 403) throw new ExtException("Spotify запретил доступ. Проверь, что твой аккаунт добавлен в User Management приложения в кабинете Spotify");
                if (r.Code == 404) return null;
                if (!r.Ok) throw new ExtException("Spotify ответил ошибкой " + r.Code);
                return J.Parse(r.Body);
            }
            throw new ExtException("Spotify не отвечает — попробуй позже");
        }
        static Track SpTrack(Dictionary<string, object> t)
        {
            if (t == null || (J.Str(t, "type") ?? "track") != "track") return null;
            var id = J.Str(t, "id");
            if (string.IsNullOrEmpty(id)) return null;
            var al = J.D(P(t, "album"));
            var img = Arr(P(al, "images")).FirstOrDefault();
            return Remember(new Track
            {
                Id = "sp:" + id, Ref = id, Source = "sp",
                Title = J.Str(t, "name") ?? "—",
                Artist = string.Join(", ", Arr(P(t, "artists")).Select(a => J.Str(a, "name")).Where(n => !string.IsNullOrEmpty(n))),
                Album = J.Str(al, "name") ?? "", Genre = "", Lyrics = "",
                Cover = J.Str(img, "url"), Duration = J.Num(t, "duration_ms") / 1000.0,
                Isrc = J.Str(J.D(P(t, "external_ids")), "isrc"),
            });
        }
        static async Task<List<Track>> SpPaged(string url, int max = 2000)
        {
            var res = new List<Track>();
            while (url != null && res.Count < max)
            {
                var d = await Sp(url).ConfigureAwait(false);
                if (d == null) return null;
                foreach (var it in Arr(P(d, "items")))
                {
                    var t = SpTrack(J.D(P(it, "track")) ?? J.D(P(it, "item")));
                    if (t != null) res.Add(t);
                }
                url = J.Str(J.D(d), "next");
            }
            return res;
        }
        public static async Task<List<ExtList>> SpPlaylists()
        {
            var list = new List<ExtList>();
            string url = SpApi + "/me/playlists?limit=50";
            while (url != null && list.Count < 500)
            {
                var d = await Sp(url).ConfigureAwait(false);
                if (d == null) break;
                foreach (var p in Arr(P(d, "items")))
                {
                    var img = Arr(P(p, "images")).FirstOrDefault();
                    var total = J.Num(J.D(P(p, "tracks")), "total");
                    if (total <= 0) total = J.Num(J.D(P(p, "items")), "total");
                    list.Add(new ExtList { Id = "ext:sp:pl:" + J.Str(p, "id"), Title = J.Str(p, "name") ?? "Плейлист", Count = (int)total, Cover = J.Str(img, "url") });
                }
                url = J.Str(J.D(d), "next");
            }
            return SpLists = list;
        }

        // ---------- общий приём кода авторизации на 127.0.0.1 ----------
        static async Task<string> WaitForCode(TcpListener listener, string state, string who)
        {
            var deadline = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < deadline)
            {
                var acceptTask = listener.AcceptTcpClientAsync();
                var done = await Task.WhenAny(acceptTask, Task.Delay(deadline - DateTime.UtcNow)).ConfigureAwait(false);
                if (done != acceptTask) break;
                using (var client = acceptTask.Result)
                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = 5000;
                    var buf = new byte[8192];
                    int n = 0;
                    try { n = stream.Read(buf, 0, buf.Length); } catch { }
                    var req = Encoding.ASCII.GetString(buf, 0, n);
                    var parts = req.Split('\n')[0].Split(' ');
                    var target = parts.Length > 1 ? parts[1] : "/";
                    var q = new Dictionary<string, string>();
                    int qi = target.IndexOf('?');
                    if (qi >= 0)
                        foreach (var pair in target.Substring(qi + 1).Split('&'))
                        {
                            var kv = pair.Split(new[] { '=' }, 2);
                            if (kv[0].Length > 0) q[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
                        }
                    bool ok = q.ContainsKey("code") || q.ContainsKey("error");
                    var html = ok
                        ? "<!doctype html><html><head><meta charset='utf-8'><title>SONORA</title></head><body style='margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#050608;color:#F5F7FA;font-family:Segoe UI,sans-serif'><div style='text-align:center'><div style='width:64px;height:64px;border-radius:18px;margin:0 auto 18px;background:linear-gradient(135deg,#60a5fa,#1d4ed8)'></div><h2 style='margin:0 0 8px'>" + (q.ContainsKey("code") ? who + " подключён!" : "Подключение отменено") + "</h2><p style='color:#8C93A1'>Можно закрыть вкладку и вернуться в SONORA.</p></div></body></html>"
                        : "<!doctype html><html><body></body></html>";
                    var bytes = Encoding.UTF8.GetBytes(html);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + (ok ? "200 OK" : "404 Not Found") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                    try { stream.Write(head, 0, head.Length); stream.Write(bytes, 0, bytes.Length); stream.Flush(); } catch { }
                    if (!ok) continue;
                    if (q.ContainsKey("error")) throw new ExtException(q["error"] == "access_denied" ? "Подключение " + who + " отменено" : who + ": " + q["error"]);
                    if (!q.TryGetValue("state", out var st) || st != state) throw new ExtException("Ответ " + who + " не прошёл проверку — попробуй ещё раз");
                    return q["code"];
                }
            }
            throw new ExtException("Время ожидания входа в " + who + " истекло");
        }

        // ================= списки и воспроизведение =================
        public static async Task<Mix> LoadList(string id, string title = null)
        {
            if (Lists.TryGetValue(id, out var cached)) return cached;
            var parts = id.Split(':');               // ext:ya:pl:KIND
            string src = parts.Length > 1 ? parts[1] : "", kind = parts.Length > 2 ? parts[2] : "", key = parts.Length > 3 ? string.Join(":", parts.Skip(3)) : "";
            List<Track> tracks = null;
            string name = title;
            if (src == "ya")
            {
                if (!YaOn) throw new ExtException("Яндекс Музыка не подключена");
                tracks = kind == "likes" ? await YaLikes().ConfigureAwait(false) : await YaPlaylist(key).ConfigureAwait(false);
                if (name == null) name = kind == "likes" ? "Мне нравится" : YaLists?.FirstOrDefault(l => l.Id == id)?.Title ?? "Плейлист";
            }
            else if (src == "sp")
            {
                if (!SpOn) throw new ExtException("Spotify не подключён");
                if (kind == "likes") tracks = await SpPaged(SpApi + "/me/tracks?limit=50").ConfigureAwait(false);
                else
                {
                    tracks = await SpPaged(SpApi + "/playlists/" + key + "/items?limit=50").ConfigureAwait(false)
                          ?? await SpPaged(SpApi + "/playlists/" + key + "/tracks?limit=50").ConfigureAwait(false);
                }
                if (name == null) name = kind == "likes" ? "Любимые треки" : SpLists?.FirstOrDefault(l => l.Id == id)?.Title ?? "Плейлист";
            }
            else if (src == "dz")
            {
                if (kind == "likes") tracks = await DzPaged("/user/" + S.DzUser + "/tracks?limit=100").ConfigureAwait(false);
                else if (kind == "al")
                {
                    var al = J.D(await Dz("/album/" + key).ConfigureAwait(false));
                    tracks = Arr(P(al, "tracks", "data")).Select(x => DzTrack(x, al)).Where(t => t != null).ToList();
                    if (name == null) name = J.Str(al, "title");
                }
                else
                {
                    if (name == null) name = J.Str(J.D(await Dz("/playlist/" + key).ConfigureAwait(false)), "title");
                    tracks = await DzPaged("/playlist/" + key + "/tracks?limit=100").ConfigureAwait(false);
                }
                if (name == null) name = kind == "likes" ? "Любимые треки" : DzLists?.FirstOrDefault(l => l.Id == id)?.Title ?? "Плейлист";
            }
            if (tracks == null) throw new ExtException("Список не найден");
            var mix = new Mix { Id = id, Name = name, Tag = SourceName(src), Tracks = tracks, Icon = kind == "likes" ? "heartFill" : "music", From = "#60a5fa", To = "#1d4ed8" };
            Lists[id] = mix;
            return mix;
        }

        static readonly Dictionary<string, Tuple<List<Track>, List<Track>>> searchCache = new Dictionary<string, Tuple<List<Track>, List<Track>>>();
        public static Tuple<List<Track>, List<Track>> CachedSearch(string q)
        {
            searchCache.TryGetValue((YaOn ? "y|" : "n|") + q, out var r);
            return r;
        }
        public static async Task<Tuple<List<Track>, List<Track>>> SearchAll(string q)
        {
            var key = (YaOn ? "y|" : "n|") + q;
            if (searchCache.TryGetValue(key, out var hit)) return hit;
            Task<List<Track>> ya = YaOn ? Safe(YaSearch(q)) : Task.FromResult(new List<Track>());
            Task<List<Track>> dz = Safe(DzSearch(q));
            await Task.WhenAll(ya, dz).ConfigureAwait(false);
            var res = Tuple.Create(ya.Result, dz.Result);
            if (searchCache.Count > 60) searchCache.Clear();
            searchCache[key] = res;
            return res;
        }
        static async Task<List<Track>> Safe(Task<List<Track>> t)
        {
            try { return await t.ConfigureAwait(false); }
            catch (Exception ex) { Log.W("ext search: " + ex.Message); return new List<Track>(); }
        }

        // Откуда реально играет трек: Яндекс — целиком; Deezer — отрывок 30 с;
        // Spotify (и Deezer при подключённом Яндексе) — ищем тот же трек в Яндекс Музыке.
        public static string PlaybackNote(Track t)
        {
            if (t == null || t.Source == null) return null;
            if (t.Source == "ya") return S.YaPlus ? null : "без Плюса — отрывок";
            if (YaOn) return "через Яндекс Музыку";
            return "отрывок 30 с";
        }
        static string Q(Track t) { return ((t.Artist ?? "").Split(',')[0] + " " + t.Title).Trim(); }
        static bool NetErr(string m) { return m != null && (m.Contains("соединени") || m.Contains("долго")); }

        // Откуда играть: 1) Яндекс целиком; 2) отрывок Deezer; 3) отрывок iTunes.
        // Отрывки скачиваем сами (тем же клиентом, что грузит списки) и играем из файла —
        // так звук не зависит от сетевых настроек встроенного плеера Windows.
        public static async Task<string> StreamUrl(Track t)
        {
            var errs = new List<string>();
            var full = Cached(t, "ya_");
            if (full != null) return full;
            if (t.Source == "ya" || YaOn)
            {
                try
                {
                    string yid = t.Source == "ya" ? t.Ref : t.Match != null && t.Match.StartsWith("ya:") ? t.Match.Substring(3) : null;
                    if (yid == null)
                    {
                        var m = Best(await YaSearch(Q(t)).ConfigureAwait(false), t);
                        if (m != null) { yid = m.Ref; t.Match = "ya:" + yid; }
                    }
                    if (yid != null) return await YaStream(yid).ConfigureAwait(false);
                    errs.Add("Яндекс: не найден");
                }
                catch (Exception ex) { Log.W("play ya " + t.Id + ": " + ex.Message); errs.Add("Яндекс: " + ex.Message); }
            }
            var pv = Cached(t, "pv_");
            if (pv != null) return pv;
            var steps = new List<Tuple<string, Func<Task<string>>>>
            {
                Tuple.Create("Deezer", (Func<Task<string>>)(() => DzPreview(t))),
                Tuple.Create("iTunes", (Func<Task<string>>)(() => ItPreview(t))),
            };
            bool allNet = true;
            foreach (var st in steps)
            {
                string url = null;
                try
                {
                    url = await st.Item2().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(url)) { errs.Add(st.Item1 + ": не найден"); allNet = false; continue; }
                    return await Download(t, url, "pv_").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (!NetErr(ex.Message)) allNet = false;
                    Log.W("play " + st.Item1 + " " + t.Id + ": " + ex.Message + (url != null ? " · " + url.Split('?')[0] : ""));
                    errs.Add(st.Item1 + ": " + ex.Message);
                }
            }
            Log.W("play failed " + t.Id + " (" + Q(t) + "): " + string.Join(" | ", errs));
            if (allNet) throw new ExtException("Музыкальные сервисы недоступны — проверь интернет или VPN");
            throw new ExtException(YaOn ? "Этот трек не нашёлся ни в одном сервисе" : "Не нашли, откуда включить этот трек — подключи Яндекс Музыку");
        }

        static async Task<string> DzPreview(Track t)
        {
            string did = t.Match != null && t.Match.StartsWith("dz:") ? t.Match.Substring(3) : t.Source == "dz" ? t.Ref : null;
            if (did != null)
            {
                try { var u = J.Str(J.D(await Dz("/track/" + did).ConfigureAwait(false)), "preview"); if (!string.IsNullOrEmpty(u)) return u; }
                catch (Exception ex) { Log.W("dz track " + did + ": " + ex.Message); if (NetErr(ex.Message)) throw; }
            }
            if (!string.IsNullOrEmpty(t.Isrc))
            {
                try
                {
                    var d = J.D(await Dz("/track/isrc:" + t.Isrc).ConfigureAwait(false));
                    var u = J.Str(d, "preview");
                    if (!string.IsNullOrEmpty(u)) { t.Match = "dz:" + J.Str(d, "id"); return u; }
                }
                catch (Exception ex) { if (NetErr(ex.Message)) throw; }
            }
            // поиск: в выдаче сразу есть ссылка на отрывок
            var r = await Dz("/search?" + Net.Form("q", Q(t), "limit", "15")).ConfigureAwait(false);
            var prev = new Dictionary<string, string>();
            var list = new List<Track>();
            foreach (var x in Arr(P(r, "data")))
            {
                var u = J.Str(x, "preview"); var id = J.Str(x, "id");
                if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(id)) continue;
                prev["dz:" + id] = u;
                list.Add(new Track { Id = "dz:" + id, Ref = id, Title = J.Str(x, "title") ?? "", Artist = J.Str(J.D(P(x, "artist")), "name") ?? "", Duration = J.Num(x, "duration") });
            }
            var m = Best(list, t);
            if (m == null) return null;
            t.Match = "dz:" + m.Ref;
            return prev[m.Id];
        }

        static async Task<string> ItPreview(Track t)
        {
            var r = await Req("GET", "https://itunes.apple.com/search?" + Net.Form("term", Q(t), "entity", "song", "limit", "15"), null).ConfigureAwait(false);
            if (!r.Ok) throw new ExtException("ответ " + r.Code);
            var prev = new Dictionary<string, string>();
            var list = new List<Track>();
            foreach (var x in Arr(P(J.Parse(r.Body), "results")))
            {
                var u = J.Str(x, "previewUrl"); var id = J.Str(x, "trackId");
                if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(id)) continue;
                prev[id] = u;
                list.Add(new Track { Id = id, Ref = id, Title = J.Str(x, "trackName") ?? "", Artist = J.Str(x, "artistName") ?? "", Duration = J.Num(x, "trackTimeMillis") / 1000 });
            }
            var m = Best(list, t);
            return m == null ? null : prev[m.Id];
        }

        static string Cached(Track t, string prefix)
        {
            try { return Directory.GetFiles(Paths.Sub("audio"), prefix + Catalog.Safe(t.Id) + ".*").FirstOrDefault(f => !f.EndsWith(".tmp") && new FileInfo(f).Length > 16000); }
            catch { return null; }
        }
        // скачать звук по ссылке в кэш и вернуть путь к файлу
        public static async Task<string> Download(Track t, string url, string prefix)
        {
            byte[] bytes;
            try
            {
                using (var r = await Net.Http.GetAsync(url).ConfigureAwait(false))
                {
                    if (!r.IsSuccessStatusCode) throw new ExtException("сервер не отдал звук (" + (int)r.StatusCode + ")");
                    bytes = await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex) { throw new ExtException("нет соединения с сервером звука (" + (ex.InnerException?.Message ?? ex.Message) + ")"); }
            catch (TaskCanceledException) { throw new ExtException("сервер звука слишком долго не отвечает"); }
            if (bytes.Length < 16000) throw new ExtException("сервер отдал пустой файл");
            var path = url.Split('?')[0].ToLowerInvariant();
            var ext = path.EndsWith(".m4a") || path.EndsWith(".aac") || path.EndsWith(".mp4") ? ".m4a" : ".mp3";
            var file = Path.Combine(Paths.Sub("audio"), prefix + Catalog.Safe(t.Id) + ext);
            File.WriteAllBytes(file + ".tmp", bytes);
            if (File.Exists(file)) File.Delete(file);
            File.Move(file + ".tmp", file);
            return file;
        }
    }
}
