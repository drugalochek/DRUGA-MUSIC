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
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace Sonora
{
    public static class Paths
    {
        public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SONORA");
        public static string Sub(string name)
        {
            var p = Path.Combine(Data, name);
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public static class Log
    {
        static readonly object Gate = new object();
        public static void W(string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Paths.Data);
                    File.AppendAllText(Path.Combine(Paths.Data, "sonora.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    public static class J
    {
        static JavaScriptSerializer S() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 }; }
        public static object Parse(string s) { return string.IsNullOrWhiteSpace(s) ? null : S().DeserializeObject(s); }
        public static string Ser(object o) { return S().Serialize(o); }
        public static T To<T>(string s) { return S().Deserialize<T>(s); }
        public static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
        public static string Str(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.TryGetValue(k, out var v) || v == null) return null;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        public static double Num(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.TryGetValue(k, out var v) || v == null) return 0;
            if (v is string s) { double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r); return r; }
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }
        public static bool Bool(Dictionary<string, object> d, string k)
        {
            if (d == null || !d.TryGetValue(k, out var v) || v == null) return false;
            if (v is bool b) return b;
            return string.Equals(Convert.ToString(v), "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class Net
    {
        public static readonly HttpClient Http = CreateClient();
        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SONORA/2.0 (Windows)");
            return c;
        }
        public sealed class Resp { public int Code; public string Body; public bool Ok { get { return Code >= 200 && Code < 300; } } }
        public static async Task<Resp> Send(string method, string url, string json = null, string form = null)
        {
            using (var req = new HttpRequestMessage(new HttpMethod(method), url))
            {
                if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                else if (form != null) req.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");
                using (var r = await Http.SendAsync(req).ConfigureAwait(false))
                    return new Resp { Code = (int)r.StatusCode, Body = await r.Content.ReadAsStringAsync().ConfigureAwait(false) };
            }
        }
        public static async Task<string> Get(string url)
        {
            var r = await Send("GET", url).ConfigureAwait(false);
            if (!r.Ok) throw new Exception("HTTP " + r.Code);
            return r.Body;
        }
        public static string Form(params string[] kv)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv[i])).Append('=').Append(Uri.EscapeDataString(kv[i + 1] ?? ""));
            }
            return sb.ToString();
        }
    }

    public sealed class Track
    {
        public string Id, Title, Artist, Album, Genre, Lyrics, Cover, Owner, LocalPath;
        public int Year;
        public double Duration;
        public long Plays, AddedAt;
        public bool Verified, Explicit;
        public string Source, Ref, Isrc, Match;   // внешние сервисы: ya / sp / dz
        public string Db;                         // null — каталог Sonic Wave, иначе база SONORA (треки пользователей)
        public string OwnerId;
        public bool IsLocal { get { return LocalPath != null; } }
        public bool HasLyrics { get { return !string.IsNullOrWhiteSpace(Lyrics); } }
    }

    public static class Catalog
    {
        public const string Db = "https://sonic-563fa-default-rtdb.firebaseio.com";
        public static List<Track> Remote = new List<Track>();
        public static bool Loaded;
        public static event Action Changed;
        static List<Track> all;

        public static List<Track> All
        {
            get
            {
                if (all == null)
                {
                    var ids = new HashSet<string>(Remote.Select(t => t.Id));
                    all = Remote.Concat(Library.LocalTracks()).Concat(global::Sonora.Offline.Tracks().Where(t => !ids.Contains(t.Id))).ToList();
                }
                return all;
            }
        }
        public static void Invalidate() { all = null; }
        public static Track Get(string id) { return All.FirstOrDefault(t => t.Id == id) ?? global::Sonora.Ext.Get(id); }

        // Треки, которые пользователи SONORA публикуют сами (база SONORA, раздел catalog)
        public static string UserDb { get { return Auth.Db + "/catalog"; } }

        static async Task<string> GetCached(string url, string cacheName)
        {
            var cacheFile = Path.Combine(Paths.Sub("cache"), cacheName);
            try
            {
                var body = await Net.Get(url);
                try { File.WriteAllText(cacheFile, body); } catch { }
                return body;
            }
            catch (Exception ex)
            {
                Log.W("catalog " + cacheName + ": " + ex.Message);
                return File.Exists(cacheFile) ? File.ReadAllText(cacheFile) : null;
            }
        }
        static void Parse(string body, string db, List<Track> list)
        {
            try
            {
                if (J.Parse(body) is Dictionary<string, object> d)
                {
                    foreach (var kv in d)
                    {
                        var t = J.D(kv.Value);
                        if (t == null) continue;
                        list.Add(new Track
                        {
                            Id = kv.Key,
                            Title = J.Str(t, "title") ?? "—",
                            Artist = J.Str(t, "artist") ?? "—",
                            Album = J.Str(t, "album") ?? "",
                            Genre = J.Str(t, "genre") ?? "",
                            Lyrics = J.Str(t, "lyrics") ?? "",
                            Cover = J.Str(t, "cover"),
                            Owner = J.Str(t, "ownerName") ?? "",
                            OwnerId = J.Str(t, "ownerId"),
                            Year = (int)J.Num(t, "year"),
                            Duration = J.Num(t, "duration"),
                            Plays = (long)J.Num(t, "plays"),
                            AddedAt = (long)J.Num(t, "addedAt"),
                            Verified = J.Bool(t, "verified"),
                            Explicit = J.Bool(t, "explicit"),
                            Db = db,
                        });
                    }
                }
            }
            catch (Exception ex) { Log.W("catalog parse: " + ex.Message); }
        }

        public static async Task LoadAsync()
        {
            var sonic = GetCached(Db + "/tracks.json", "tracks.json");
            var users = GetCached(UserDb + "/tracks.json", "tracks-sonora.json");
            var list = new List<Track>();
            Parse(await sonic, null, list);
            var own = new List<Track>();
            Parse(await users, UserDb, own);
            var ids = new HashSet<string>(list.Select(x => x.Id));
            list.AddRange(own.Where(x => !ids.Contains(x.Id)));
            Remote = list.OrderByDescending(x => x.AddedAt).ToList();
            Loaded = true;
            Invalidate();
            Changed?.Invoke();
        }

        static readonly Dictionary<string, Task<string>> Pending = new Dictionary<string, Task<string>>();
        public static Task<string> AudioFile(Track t)
        {
            if (t.IsLocal) return Task.FromResult(t.LocalPath);
            var off = global::Sonora.Offline.FileOf(t.Id);
            if (off != null) return Task.FromResult(off);
            if (t.Source != null) return global::Sonora.Ext.StreamUrl(t);
            lock (Pending)
            {
                if (Pending.TryGetValue(t.Id, out var p) && !p.IsFaulted && !p.IsCanceled) return p;
                var task = Task.Run(() => Fetch(t));
                Pending[t.Id] = task;
                return task;
            }
        }
        public static string Safe(string id)
        {
            var sb = new StringBuilder();
            foreach (var c in id) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
        static string Ext(string mime)
        {
            mime = (mime ?? "").ToLowerInvariant();
            if (mime.Contains("wav")) return ".wav";
            if (mime.Contains("flac")) return ".flac";
            if (mime.Contains("ogg")) return ".ogg";
            if (mime.Contains("webm")) return ".webm";
            if (mime.Contains("aac")) return ".aac";
            if (mime.Contains("mp4") || mime.Contains("m4a")) return ".m4a";
            return ".mp3";
        }
        static async Task<string> Fetch(Track t)
        {
            var dir = Paths.Sub("audio");
            var existing = Directory.GetFiles(dir, Safe(t.Id) + ".*").FirstOrDefault(f => !f.EndsWith(".tmp"));
            if (existing != null) return existing;
            var body = await Net.Get((t.Db ?? Db) + "/audio/" + Uri.EscapeDataString(t.Id) + ".json").ConfigureAwait(false);
            var d = J.D(J.Parse(body));
            var data = J.Str(d, "data");
            int parts = (int)J.Num(d, "parts");
            if (string.IsNullOrEmpty(data) && parts > 0)
            {
                // большие треки хранятся частями (в базе строка не может быть больше 10 МБ)
                var sb = new StringBuilder();
                for (int i = 0; i < parts; i++)
                {
                    var piece = J.Str(d, "c" + i);
                    if (string.IsNullOrEmpty(piece)) throw new Exception("Трек загружен не полностью");
                    sb.Append(piece);
                }
                data = sb.ToString();
            }
            if (string.IsNullOrEmpty(data)) throw new Exception("У трека нет аудио");
            int comma = data.IndexOf(',');
            if (data.StartsWith("data:") && comma > 0) data = data.Substring(comma + 1);
            var bytes = Convert.FromBase64String(data);
            var file = Path.Combine(dir, Safe(t.Id) + Ext(J.Str(d, "mime")));
            File.WriteAllBytes(file + ".tmp", bytes);
            if (File.Exists(file)) File.Delete(file);
            File.Move(file + ".tmp", file);
            return file;
        }
        public static long CacheSize()
        {
            try { return Directory.GetFiles(Paths.Sub("audio")).Sum(f => new FileInfo(f).Length); } catch { return 0; }
        }
        public static void ClearCache()
        {
            foreach (var f in Directory.GetFiles(Paths.Sub("audio"))) { try { File.Delete(f); } catch { } }
            lock (Pending) Pending.Clear();
        }
    }

    public sealed class Playlist
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public List<string> TrackIds { get; set; } = new List<string>();
        public string Cover { get; set; }
    }

    public sealed class LocalFile
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public double Duration { get; set; }
        public long AddedAt { get; set; }
        public string Sig { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public int Year { get; set; }
        public string Cover { get; set; }
        public string Lyrics { get; set; }
        public bool Explicit { get; set; }
        public bool Public { get; set; }
        public string PubId { get; set; }
        public long PublishedAt { get; set; }
    }

    public sealed class LibData
    {
        public List<string> Fav { get; set; } = new List<string>();
        public List<string> FavArtists { get; set; } = new List<string>();
        public List<string> Recent { get; set; } = new List<string>();
        public List<Playlist> Playlists { get; set; } = new List<Playlist>();
        public List<LocalFile> Local { get; set; } = new List<LocalFile>();
        public bool GuestChosen { get; set; }
        public List<OfflineItem> Offline { get; set; } = new List<OfflineItem>();
        public string Lang { get; set; } = "ru";
        public bool LiquidBg { get; set; } = true;
        public bool BeatGlow { get; set; } = true;
        public double Volume { get; set; } = 0.8;
        public long UpdatedAt { get; set; }
        public string SeenNotif { get; set; }
        public string LastVersion { get; set; }
    }

    public static class Library
    {
        public static LibData D = new LibData();
        public static event Action Changed;
        public static bool Persist = true;
        static string FileP { get { return Path.Combine(Paths.Data, "library.json"); } }
        public static long Now { get { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; } }

        public static void Load()
        {
            try { if (File.Exists(FileP)) D = J.To<LibData>(File.ReadAllText(FileP)) ?? new LibData(); }
            catch (Exception ex) { Log.W("library: " + ex.Message); D = new LibData(); }
            Fix();
        }
        static void Fix()
        {
            if (D.Fav == null) D.Fav = new List<string>();
            if (D.FavArtists == null) D.FavArtists = new List<string>();
            if (D.Recent == null) D.Recent = new List<string>();
            if (D.Playlists == null) D.Playlists = new List<Playlist>();
            if (D.Local == null) D.Local = new List<LocalFile>();
            if (D.Offline == null) D.Offline = new List<OfflineItem>();
            D.Offline.RemoveAll(o => o == null || string.IsNullOrEmpty(o.Id));
            if (D.Lang != "en") D.Lang = "ru";
            foreach (var p in D.Playlists) if (p.TrackIds == null) p.TrackIds = new List<string>();
            D.Local.RemoveAll(l => l == null || string.IsNullOrEmpty(l.Path));
            if (D.Volume <= 0 || D.Volume > 1) D.Volume = 0.8;
        }
        public static void Save(bool touch = true)
        {
            if (touch) D.UpdatedAt = Now;
            if (Persist)
            {
                try { Directory.CreateDirectory(Paths.Data); File.WriteAllText(FileP, J.Ser(D)); }
                catch (Exception ex) { Log.W("save: " + ex.Message); }
            }
            Catalog.Invalidate();
            Changed?.Invoke();
            if (touch) Cloud.Schedule();
        }

        public static List<Track> LocalTracks()
        {
            return D.Local.Select(l => new Track { Id = l.Id, Title = l.Title, Artist = l.Artist, Album = l.Album ?? "", Genre = l.Genre ?? "", Lyrics = l.Lyrics ?? "", Year = l.Year, Cover = l.Cover, Explicit = l.Explicit, Duration = l.Duration, LocalPath = l.Path, AddedAt = l.AddedAt }).ToList();
        }
        public static void Touch() { Changed?.Invoke(); }
        public static bool IsFav(string id) { return D.Fav.Contains(id); }
        public static void ToggleFav(string id) { if (!Guard.Need("Лайки")) return; if (!D.Fav.Remove(id)) D.Fav.Insert(0, id); Save(); }
        public static bool IsFavArtist(string a) { return D.FavArtists.Contains(a); }
        public static void ToggleFavArtist(string a) { if (!Guard.Need("Любимые исполнители")) return; if (!D.FavArtists.Remove(a)) D.FavArtists.Insert(0, a); Save(); }
        public static void AddRecent(string id)
        {
            D.Recent.Remove(id);
            D.Recent.Insert(0, id);
            if (D.Recent.Count > 60) D.Recent.RemoveRange(60, D.Recent.Count - 60);
            Save();
        }
        public static void SetLocalDuration(string id, double sec)
        {
            var l = D.Local.FirstOrDefault(x => x.Id == id);
            if (l != null && Math.Abs(l.Duration - sec) > 0.5) { l.Duration = sec; Save(false); }
        }

        public static readonly string[] AudioExt = { ".mp3", ".m4a", ".aac", ".wav", ".wma", ".flac", ".mp4" };
        public sealed class AddResult { public int Added, Dupes; public List<Junk.Result> Rejected = new List<Junk.Result>(); }
        static IEnumerable<string> Expand(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    string[] files;
                    try { files = Directory.GetFiles(p, "*.*", SearchOption.AllDirectories); } catch { continue; }
                    foreach (var f in files) yield return f;
                }
                else yield return p;
            }
        }
        // Добавление файлов с проверкой: формат, длительность, тишина, дубликаты, «голосовые»
        public static async Task<AddResult> AddFilesAsync(IEnumerable<string> paths)
        {
            var res = new AddResult();
            if (Guard.Guest) return res;
            var files = Expand(paths).Where(p => File.Exists(p) && AudioExt.Contains(Path.GetExtension(p).ToLowerInvariant())).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var fresh = files.Where(f => !D.Local.Any(l => string.Equals(l.Path, f, StringComparison.OrdinalIgnoreCase))).ToList();
            res.Dupes = files.Count - fresh.Count;
            var known = D.Local.ToList();
            var checks = await Task.Run(() =>
            {
                foreach (var l in known) if (string.IsNullOrEmpty(l.Sig) && File.Exists(l.Path)) { try { l.Sig = Junk.Signature(l.Path, new FileInfo(l.Path).Length); } catch { } }
                return fresh.AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Max(1, Math.Min(4, Environment.ProcessorCount))).Select(Junk.Check).ToList();
            });
            var sigs = new HashSet<string>(D.Local.Where(l => !string.IsNullOrEmpty(l.Sig)).Select(l => l.Sig));
            foreach (var c in checks)
            {
                if (!c.Ok) { res.Rejected.Add(c); continue; }
                if (c.Sig != null && !sigs.Add(c.Sig)) { c.Reason = "такой же файл уже есть в библиотеке"; res.Rejected.Add(c); continue; }
                var full = c.Path;
                var name = Path.GetFileNameWithoutExtension(full);
                string artist = "Неизвестный исполнитель", title = name.Trim();
                int dash = name.IndexOf(" - ", StringComparison.Ordinal);
                if (dash > 0) { artist = name.Substring(0, dash).Trim(); title = name.Substring(dash + 3).Trim(); }
                title = Regex.Replace(title, @"^\d{1,3}[\s._-]+(?=\D)", "").Trim();
                if (title.Length == 0) title = name.Trim();
                D.Local.Insert(0, new LocalFile { Id = "local_" + Hash(full.ToLowerInvariant()), Path = full, Title = title, Artist = artist, AddedAt = Now, Duration = c.Duration, Sig = c.Sig });
                res.Added++;
            }
            if (res.Added > 0 || known.Any(l => l.Sig != null)) Save(false);
            return res;
        }
        public static void RemoveLocal(string id) { D.Local.RemoveAll(l => l.Id == id); D.Fav.Remove(id); Save(false); }
        public static string Hash(string s)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        public static readonly string[] PlColors = { "#8b5cf6", "#ec4899", "#06b6d4", "#10b981", "#f59e0b", "#ef4444", "#6366f1", "#14b8a6" };
        public static Playlist NewPlaylist(string name, string color)
        {
            var p = new Playlist { Id = "pl_" + Now, Name = string.IsNullOrWhiteSpace(name) ? "Новый плейлист" : name.Trim(), Color = color ?? PlColors[0] };
            D.Playlists.Add(p);
            Save();
            return p;
        }
        public static void DeletePlaylist(string id) { D.Playlists.RemoveAll(p => p.Id == id); Save(); }
        public static bool AddToPlaylist(string plId, string trackId)
        {
            var p = D.Playlists.FirstOrDefault(x => x.Id == plId);
            if (p == null || p.TrackIds.Contains(trackId)) return false;
            p.TrackIds.Add(trackId);
            Save();
            return true;
        }
        public static void RemoveFromPlaylist(string plId, string trackId)
        {
            var p = D.Playlists.FirstOrDefault(x => x.Id == plId);
            if (p != null && p.TrackIds.Remove(trackId)) Save();
        }

        public static Dictionary<string, object> Portable()
        {
            return new Dictionary<string, object>
            {
                ["fav"] = D.Fav,
                ["favArtists"] = D.FavArtists,
                ["recent"] = D.Recent,
                ["playlists"] = D.Playlists.Select(p => new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["color"] = p.Color, ["trackIds"] = p.TrackIds, ["cover"] = p.Cover ?? "" }).ToList(),
            };
        }
        static List<string> Strs(object o) { return (o as object[] ?? (o as System.Collections.ArrayList)?.ToArray() ?? new object[0]).Select(x => Convert.ToString(x)).Where(x => !string.IsNullOrEmpty(x)).ToList(); }
        public static void ApplyPortable(Dictionary<string, object> d, long updatedAt)
        {
            if (d == null) return;
            if (d.ContainsKey("fav")) D.Fav = Strs(d["fav"]);
            if (d.ContainsKey("favArtists")) D.FavArtists = Strs(d["favArtists"]);
            if (d.ContainsKey("recent")) D.Recent = Strs(d["recent"]);
            if (d.TryGetValue("playlists", out var pl))
            {
                var arr = pl as object[] ?? new object[0];
                D.Playlists = arr.Select(J.D).Where(x => x != null).Select(x => new Playlist
                {
                    Id = J.Str(x, "id") ?? ("pl_" + Now),
                    Name = J.Str(x, "name") ?? "Плейлист",
                    Color = J.Str(x, "color") ?? PlColors[0],
                    TrackIds = x.ContainsKey("trackIds") ? Strs(x["trackIds"]) : new List<string>(),
                    Cover = string.IsNullOrEmpty(J.Str(x, "cover")) ? null : J.Str(x, "cover")
                }).ToList();
            }
            D.UpdatedAt = updatedAt;
            Fix();
            Save(false);
        }
    }

    public sealed class Session
    {
        public string Uid { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string Photo { get; set; }
        public string Bio { get; set; }
        public string Color { get; set; }
        public string Provider { get; set; }
        public string IdToken { get; set; }
        public string RefreshToken { get; set; }
        public long ExpiresAt { get; set; }
    }

    public sealed class AuthException : Exception { public AuthException(string m) : base(m) { } }

    public static class Auth
    {
        const string ApiKey = "AIzaSyCgBtQNo-cebuAoJ7u9vkOGHHpHI0DxSWQ";
        const string GoogleClientId = "951132244373-odclgbamf6ra65l8vljkkvhcqidqlthm.apps.googleusercontent.com";
        public const string Db = "https://sonora-ab012-default-rtdb.firebaseio.com";
        public static Session Current;
        public static event Action Changed;
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SONORA.session.v2");
        static string FileP { get { return Path.Combine(Paths.Data, "session.bin"); } }
        static string IT(string m) { return "https://identitytoolkit.googleapis.com/v1/accounts:" + m + "?key=" + ApiKey; }

        public static void LoadSaved()
        {
            try
            {
                if (!File.Exists(FileP)) return;
                var raw = ProtectedData.Unprotect(File.ReadAllBytes(FileP), Entropy, DataProtectionScope.CurrentUser);
                Current = J.To<Session>(Encoding.UTF8.GetString(raw));
                if (Current != null && string.IsNullOrEmpty(Current.Uid)) Current = null;
            }
            catch (Exception ex) { Log.W("session load: " + ex.Message); Current = null; }
        }
        static void Persist()
        {
            try
            {
                if (Current == null) { if (File.Exists(FileP)) File.Delete(FileP); return; }
                var raw = ProtectedData.Protect(Encoding.UTF8.GetBytes(J.Ser(Current)), Entropy, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllBytes(FileP, raw);
            }
            catch (Exception ex) { Log.W("session save: " + ex.Message); }
        }
        public static void SignOut()
        {
            Current = null;
            Persist();
            Changed?.Invoke();
        }

        static string Human(string code)
        {
            code = (code ?? "").Trim();
            var key = code.Split(' ', ':')[0];
            switch (key)
            {
                case "EMAIL_NOT_FOUND":
                case "INVALID_PASSWORD":
                case "INVALID_LOGIN_CREDENTIALS": return "Неверная почта или пароль.";
                case "USER_DISABLED": return "Аккаунт отключён.";
                case "EMAIL_EXISTS": return "Эта почта уже зарегистрирована. Попробуй войти.";
                case "INVALID_EMAIL": return "Некорректная почта.";
                case "WEAK_PASSWORD": return "Слишком простой пароль — минимум 6 символов.";
                case "MISSING_PASSWORD": return "Введи пароль.";
                case "MISSING_EMAIL": return "Введи почту.";
                case "TOO_MANY_ATTEMPTS_TRY_LATER": return "Слишком много попыток. Попробуй позже.";
                case "OPERATION_NOT_ALLOWED": return "Этот способ входа отключён в проекте Firebase.";
                case "TOKEN_EXPIRED":
                case "INVALID_REFRESH_TOKEN":
                case "USER_NOT_FOUND": return "Сессия устарела — войди снова.";
                default: return string.IsNullOrEmpty(code) ? "Не удалось выполнить запрос." : code;
            }
        }
        static string ErrorOf(string body)
        {
            try
            {
                var e = J.D(J.D(J.Parse(body))?["error"]);
                if (e != null) return J.Str(e, "message");
                return J.Str(J.D(J.Parse(body)), "error_description") ?? J.Str(J.D(J.Parse(body)), "error");
            }
            catch { return null; }
        }
        static async Task<Dictionary<string, object>> Call(string method, object payload)
        {
            Net.Resp r;
            try { r = await Net.Send("POST", IT(method), J.Ser(payload)); }
            catch (Exception ex) { throw new AuthException("Нет соединения с сервером: " + ex.Message); }
            if (!r.Ok) throw new AuthException(Human(ErrorOf(r.Body)));
            return J.D(J.Parse(r.Body));
        }
        static void FromResp(Dictionary<string, object> d, string provider)
        {
            var exp = J.Num(d, "expiresIn");
            Current = new Session
            {
                Uid = J.Str(d, "localId"),
                Email = J.Str(d, "email"),
                Name = J.Str(d, "displayName") ?? J.Str(d, "fullName"),
                Photo = J.Str(d, "photoUrl"),
                Provider = provider,
                IdToken = J.Str(d, "idToken"),
                RefreshToken = J.Str(d, "refreshToken"),
                ExpiresAt = Library.Now + (long)((exp > 0 ? exp : 3600) * 1000)
            };
        }

        public static async Task SignIn(string email, string pass)
        {
            var d = await Call("signInWithPassword", new { email = email.Trim(), password = pass, returnSecureToken = true });
            FromResp(d, "password");
            await LoadProfile();
            Done();
        }
        public static async Task SignUp(string name, string email, string pass)
        {
            var d = await Call("signUp", new { email = email.Trim(), password = pass, returnSecureToken = true });
            FromResp(d, "password");
            Current.Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim();
            try { await Call("update", new { idToken = Current.IdToken, displayName = Current.Name, returnSecureToken = false }); } catch (Exception ex) { Log.W("displayName: " + ex.Message); }
            await SaveProfile();
            Done();
        }
        public static Task Reset(string email)
        {
            return Call("sendOobCode", new { requestType = "PASSWORD_RESET", email = email.Trim() });
        }
        static void Done()
        {
            Persist();
            Changed?.Invoke();
        }
        public static async Task UpdateName(string name)
        {
            if (Current == null) return;
            Current.Name = name.Trim();
            try { await Call("update", new { idToken = await Token(), displayName = Current.Name, returnSecureToken = false }); } catch (Exception ex) { Log.W("rename: " + ex.Message); }
            await SaveProfile();
            Done();
        }

        public static async Task<string> Token()
        {
            var s = Current;
            if (s == null) return null;
            if (Library.Now < s.ExpiresAt - 120000) return s.IdToken;
            var r = await Net.Send("POST", "https://securetoken.googleapis.com/v1/token?key=" + ApiKey, form: Net.Form("grant_type", "refresh_token", "refresh_token", s.RefreshToken));
            if (!r.Ok) { Log.W("refresh: " + r.Body); throw new AuthException(Human(ErrorOf(r.Body))); }
            var d = J.D(J.Parse(r.Body));
            s.IdToken = J.Str(d, "id_token");
            s.RefreshToken = J.Str(d, "refresh_token") ?? s.RefreshToken;
            s.ExpiresAt = Library.Now + (long)(Math.Max(300, J.Num(d, "expires_in")) * 1000);
            Persist();
            return s.IdToken;
        }

        static string UserUrl(string path, string token) { return Db + "/users/" + Uri.EscapeDataString(Current.Uid) + "/" + path + ".json?auth=" + Uri.EscapeDataString(token); }
        public static async Task LoadProfile()
        {
            try
            {
                var r = await Net.Send("GET", UserUrl("profile", Current.IdToken));
                var d = r.Ok ? J.D(J.Parse(r.Body)) : null;
                if (d != null)
                {
                    var n = J.Str(d, "name");
                    if (!string.IsNullOrWhiteSpace(n)) Current.Name = n;
                    var ph = J.Str(d, "photo"); if (!string.IsNullOrWhiteSpace(ph)) Current.Photo = ph;
                    Current.Bio = J.Str(d, "bio") ?? Current.Bio;
                    Current.Color = J.Str(d, "color") ?? Current.Color;
                    Persist();
                    Changed?.Invoke();
                }
                else await SaveProfile();
            }
            catch (Exception ex) { Log.W("profile: " + ex.Message); }
        }
        public static async Task SaveProfile()
        {
            try
            {
                var tok = await Token();
                var body = J.Ser(new Dictionary<string, object> { ["name"] = Current.Name ?? "", ["email"] = Current.Email ?? "", ["photo"] = Current.Photo ?? "", ["bio"] = Current.Bio ?? "", ["color"] = Current.Color ?? "", ["updatedAt"] = Library.Now, ["client"] = "SONORA Windows" });
                var r = await Net.Send("PATCH", UserUrl("profile", tok), body);
                if (!r.Ok) Log.W("profile save: " + r.Code + " " + r.Body);
            }
            catch (Exception ex) { Log.W("profile save: " + ex.Message); }
        }

        static string B64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        static byte[] Rnd(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }

        // Вход через Google: Firebase сам ведёт OAuth (его веб-клиент разрешает домен localhost).
        // Приложение поднимает локальную страницу http://localhost:PORT, браузер открывает её,
        // пользователь жмёт кнопку, Firebase показывает окно Google и возвращает токен Google,
        // страница отдаёт его обратно приложению, а приложение обменивает его через signInWithIdp.
        static System.Threading.CancellationTokenSource googleCts;
        public static void CancelGoogle() { try { googleCts?.Cancel(); } catch { } }

        public static async Task Google()
        {
            CancelGoogle();
            var cts = new System.Threading.CancellationTokenSource();
            googleCts = cts;
            var state = B64Url(Rnd(16));
            var v4 = new TcpListener(IPAddress.Loopback, 0);
            v4.Start();
            var port = ((IPEndPoint)v4.LocalEndpoint).Port;
            TcpListener v6 = null;
            try { v6 = new TcpListener(IPAddress.IPv6Loopback, port); v6.Start(); } catch { v6 = null; }
            Dictionary<string, object> got;
            try
            {
                var url = "http://localhost:" + port + "/?s=" + state;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { throw new AuthException("Не удалось открыть браузер: " + ex.Message); }
                got = await WaitForGoogle(v4, v6, port, state, cts.Token);
            }
            finally
            {
                try { v4.Stop(); } catch { }
                try { v6?.Stop(); } catch { }
                if (googleCts == cts) googleCts = null;
            }

            var idTok = J.Str(got, "idToken");
            var accTok = J.Str(got, "accessToken");
            string post = !string.IsNullOrEmpty(idTok) ? "id_token=" + Uri.EscapeDataString(idTok)
                        : !string.IsNullOrEmpty(accTok) ? "access_token=" + Uri.EscapeDataString(accTok) : null;
            if (post == null) throw new AuthException("Google не вернул данные для входа. Попробуй ещё раз.");
            var d = await Call("signInWithIdp", new
            {
                postBody = post + "&providerId=google.com",
                requestUri = "http://localhost",
                returnIdpCredential = true,
                returnSecureToken = true
            });
            FromResp(d, "google");
            await LoadProfile();
            Done();
        }

        static string GooglePage(string state)
        {
            return @"<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>SONORA — вход через Google</title>
<style>html,body{margin:0;height:100%;background:#050608;color:#F5F7FA;font-family:'Segoe UI',system-ui,sans-serif}
.w{min-height:100%;display:flex;align-items:center;justify-content:center}.c{width:380px;max-width:90vw;text-align:center;padding:36px 32px;border-radius:24px;background:#101216;border:1px solid rgba(255,255,255,.08)}
.l{width:64px;height:64px;border-radius:18px;margin:0 auto 18px;background:linear-gradient(135deg,#60a5fa,#1d4ed8)}h2{margin:0 0 8px;font-size:22px}p{color:#8C93A1;font-size:14px;line-height:1.45;margin:0 0 22px}
button{width:100%;border:0;border-radius:14px;padding:14px;font-size:15px;font-weight:600;cursor:pointer;background:#fff;color:#111;display:flex;align-items:center;justify-content:center;gap:10px}
button:disabled{opacity:.6;cursor:default}.e{color:#F87171}.ok{color:#60A5FA}</style></head><body><div class='w'><div class='c'><div class='l'></div>
<h2 id='t'>Вход в SONORA</h2><p id='m'>Нажми кнопку и выбери аккаунт Google. Потом вернись в приложение.</p>
<button id='b'><svg width='18' height='18' viewBox='0 0 48 48'><path fill='#FFC107' d='M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.2 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.3-.1-2.4-.4-3.5z'/><path fill='#FF3D00' d='M6.3 14.7l6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z'/><path fill='#4CAF50' d='M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-8l-6.5 5C9.5 39.6 16.2 44 24 44z'/><path fill='#1976D2' d='M43.6 20.5H42V20H24v8h11.3c-.8 2.2-2.2 4.2-4.1 5.6l6.2 5.2C37 39.2 44 34 44 24c0-1.3-.1-2.4-.4-3.5z'/></svg><span>Войти через Google</span></button></div></div>
<script src='https://www.gstatic.com/firebasejs/10.12.2/firebase-app-compat.js'></script>
<script src='https://www.gstatic.com/firebasejs/10.12.2/firebase-auth-compat.js'></script>
<script>
var S='" + state + @"',b=document.getElementById('b'),m=document.getElementById('m'),t=document.getElementById('t');
function msg(x,c){m.textContent=x;m.className=c||''}
function send(path,obj){return fetch(path+'?s='+S,{method:'POST',headers:{'Content-Type':'text/plain'},body:JSON.stringify(obj||{})})}
function done(r){var c=r&&r.credential;if(!c){msg('Google не вернул данные. Попробуй ещё раз.','e');b.disabled=false;return}
 msg('Секунду…');send('/done',{idToken:c.idToken||'',accessToken:c.accessToken||''}).then(function(){t.textContent='Готово!';msg('Можно закрыть вкладку и вернуться в SONORA.','ok');b.style.display='none'})
 .catch(function(){msg('Приложение SONORA закрыто или вход уже отменён. Запусти вход заново.','e')})}
function fail(e){var c=e&&e.code||'';b.disabled=false;
 if(c==='auth/popup-closed-by-user'||c==='auth/cancelled-popup-request'){msg('Окно Google закрыто. Нажми кнопку ещё раз.');return}
 if(c==='auth/network-request-failed'){msg('Нет связи с Google. Проверь интернет и попробуй ещё раз.','e');return}
 msg('Ошибка: '+(e&&e.message||e),'e');send('/log',{code:c,message:String(e&&e.message||e)})}
if(!window.firebase){msg('Не удалось загрузить модуль входа Google. Проверь интернет или отключи блокировщик и обнови страницу.','e');b.disabled=true}
else{
 firebase.initializeApp({apiKey:'" + ApiKey + @"',authDomain:'sonora-ab012.firebaseapp.com',projectId:'sonora-ab012'});
 var A=firebase.auth(),P=new firebase.auth.GoogleAuthProvider();P.addScope('email');P.addScope('profile');P.setCustomParameters({prompt:'select_account'});
 A.getRedirectResult().then(function(r){if(r&&r.credential)done(r)}).catch(fail);
 b.onclick=function(){b.disabled=true;msg('Выбери аккаунт в окне Google…');
  A.signInWithPopup(P).then(done).catch(function(e){if(e&&e.code==='auth/popup-blocked'){msg('Переходим на страницу Google…');A.signInWithRedirect(P).catch(fail)}else fail(e)})};
}
</script></body></html>";
        }

        static async Task<Dictionary<string, object>> WaitForGoogle(TcpListener v4, TcpListener v6, int port, string state, System.Threading.CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMinutes(5);
            Task<TcpClient> a4 = null, a6 = null;
            while (DateTime.UtcNow < deadline)
            {
                if (a4 == null) a4 = v4.AcceptTcpClientAsync();
                if (a6 == null && v6 != null) a6 = v6.AcceptTcpClientAsync();
                var wait = new List<Task> { a4, Task.Delay(deadline - DateTime.UtcNow, ct) };
                if (a6 != null) wait.Add(a6);
                var fin = await Task.WhenAny(wait);
                if (ct.IsCancellationRequested) throw new AuthException("Вход через Google отменён.");
                TcpClient client;
                if (fin == a4) { client = a4.Result; a4 = null; }
                else if (a6 != null && fin == a6) { client = a6.Result; a6 = null; }
                else break;
                Dictionary<string, object> res = null;
                try { res = await Task.Run(() => HandleGoogleReq(client, port, state)); }
                catch (Exception ex) { Log.W("google http: " + ex.Message); }
                finally { try { client.Close(); } catch { } }
                if (res != null) return res;
            }
            throw new AuthException("Время ожидания входа через Google истекло. Нажми «Войти через Google» ещё раз.");
        }

        // Мини-HTTP: GET / — страница входа, POST /done — токен Google, POST /log — ошибка из браузера.
        static Dictionary<string, object> HandleGoogleReq(TcpClient client, int port, string state)
        {
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 8000;
                var ms = new MemoryStream();
                var buf = new byte[8192];
                int headEnd = -1, contentLen = 0;
                while (ms.Length < 256 * 1024)
                {
                    int n;
                    try { n = stream.Read(buf, 0, buf.Length); } catch { break; }
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);
                    var all = ms.ToArray();
                    if (headEnd < 0)
                    {
                        var s = Encoding.ASCII.GetString(all);
                        headEnd = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (headEnd >= 0)
                        {
                            var mm = System.Text.RegularExpressions.Regex.Match(s.Substring(0, headEnd), @"(?im)^content-length:\s*(\d+)");
                            if (mm.Success) contentLen = int.Parse(mm.Groups[1].Value);
                        }
                    }
                    if (headEnd >= 0 && all.Length >= headEnd + 4 + contentLen) break;
                }
                var raw = ms.ToArray();
                if (headEnd < 0) return null;
                var head = Encoding.ASCII.GetString(raw, 0, headEnd);
                var body = Encoding.UTF8.GetString(raw, headEnd + 4, Math.Max(0, Math.Min(contentLen, raw.Length - headEnd - 4)));
                var first = head.Split('\n')[0].Trim().Split(' ');
                var method = first.Length > 0 ? first[0] : "GET";
                var target = first.Length > 1 ? first[1] : "/";
                var path = target.Split('?')[0];
                var q = ParseQuery(target);
                var hostM = System.Text.RegularExpressions.Regex.Match(head, @"(?im)^host:\s*([^\r\n:]+)");
                var host = hostM.Success ? hostM.Groups[1].Value.Trim().ToLowerInvariant() : "localhost";
                string sv;
                bool stateOk = q.TryGetValue("s", out sv) && sv == state;

                if (method == "GET" && path == "/" && host != "localhost")
                {
                    // Firebase разрешает только домен localhost — перенаправляем 127.0.0.1 → localhost
                    Reply(stream, "302 Found", "text/plain", "", "Location: http://localhost:" + port + target + "\r\n");
                    return null;
                }
                if (method == "GET" && path == "/")
                {
                    if (!stateOk) { Reply(stream, "403 Forbidden", "text/plain; charset=utf-8", "Ссылка устарела. Нажми «Войти через Google» в SONORA ещё раз."); return null; }
                    Reply(stream, "200 OK", "text/html; charset=utf-8", GooglePage(state), "Cache-Control: no-store\r\n");
                    return null;
                }
                if (method == "POST" && stateOk && path == "/done")
                {
                    Reply(stream, "200 OK", "text/plain", "ok");
                    return J.D(J.Parse(body)) ?? new Dictionary<string, object>();
                }
                if (method == "POST" && stateOk && path == "/log")
                {
                    Log.W("google page: " + body);
                    Reply(stream, "200 OK", "text/plain", "ok");
                    return null;
                }
                Reply(stream, "404 Not Found", "text/plain", "");
                return null;
            }
        }
        static void Reply(Stream stream, string status, string type, string text, string extra = "")
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + bytes.Length + "\r\n" + extra + "Connection: close\r\n\r\n");
            try { stream.Write(head, 0, head.Length); stream.Write(bytes, 0, bytes.Length); stream.Flush(); } catch { }
        }

        static Dictionary<string, string> ParseQuery(string target)
        {
            var res = new Dictionary<string, string>();
            int i = target.IndexOf('?');
            if (i < 0) return res;
            foreach (var pair in target.Substring(i + 1).Split('&'))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                if (kv[0].Length == 0) continue;
                res[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
            }
            return res;
        }
    }

    public static class Cloud
    {
        static DispatcherTimer timer;
        public static string Status = "";
        public static event Action StatusChanged;
        static void Set(string s) { Status = s; StatusChanged?.Invoke(); }

        public static void Schedule()
        {
            if (Auth.Current == null || Program.ShotsDir != null) return;
            if (timer == null)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += async (s, e) => { timer.Stop(); await Push(); };
            }
            timer.Stop();
            timer.Start();
        }
        static string Url(string tok) { return Auth.Db + "/users/" + Uri.EscapeDataString(Auth.Current.Uid) + "/sonora_v2.json?auth=" + Uri.EscapeDataString(tok); }
        public static async Task Push()
        {
            if (Auth.Current == null) return;
            try
            {
                Set("Синхронизация…");
                var tok = await Auth.Token();
                var body = J.Ser(new Dictionary<string, object> { ["data"] = J.Ser(Library.Portable()), ["updatedAt"] = Library.D.UpdatedAt, ["device"] = "windows" });
                var r = await Net.Send("PUT", Url(tok), body);
                if (!r.Ok) { Log.W("push: " + r.Code + " " + r.Body); Set("Ошибка синхронизации"); return; }
                Set("Синхронизировано");
            }
            catch (Exception ex) { Log.W("push: " + ex.Message); Set("Нет связи с облаком"); }
        }
        public static async Task SyncNow()
        {
            if (Auth.Current == null) return;
            try
            {
                Set("Синхронизация…");
                var tok = await Auth.Token();
                var r = await Net.Send("GET", Url(tok));
                if (r.Ok)
                {
                    var d = J.D(J.Parse(r.Body));
                    var remoteAt = (long)J.Num(d, "updatedAt");
                    var data = J.Str(d, "data");
                    if (d != null && !string.IsNullOrEmpty(data) && remoteAt > Library.D.UpdatedAt)
                    {
                        Library.ApplyPortable(J.D(J.Parse(data)), remoteAt);
                        Set("Синхронизировано");
                        return;
                    }
                }
                else Log.W("pull: " + r.Code + " " + r.Body);
                await Push();
            }
            catch (Exception ex) { Log.W("sync: " + ex.Message); Set("Нет связи с облаком"); }
        }
    }
}
