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
        public bool Verified;
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
                if (all == null) all = Remote.Concat(Library.LocalTracks()).ToList();
                return all;
            }
        }
        public static void Invalidate() { all = null; }
        public static Track Get(string id) { return All.FirstOrDefault(t => t.Id == id); }

        public static async Task LoadAsync()
        {
            string body = null;
            var cacheFile = Path.Combine(Paths.Sub("cache"), "tracks.json");
            try
            {
                body = await Net.Get(Db + "/tracks.json");
                try { File.WriteAllText(cacheFile, body); } catch { }
            }
            catch (Exception ex)
            {
                Log.W("catalog: " + ex.Message);
                if (File.Exists(cacheFile)) body = File.ReadAllText(cacheFile);
            }
            var list = new List<Track>();
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
                            Year = (int)J.Num(t, "year"),
                            Duration = J.Num(t, "duration"),
                            Plays = (long)J.Num(t, "plays"),
                            AddedAt = (long)J.Num(t, "addedAt"),
                            Verified = J.Bool(t, "verified"),
                        });
                    }
                }
            }
            catch (Exception ex) { Log.W("catalog parse: " + ex.Message); }
            Remote = list.OrderByDescending(x => x.AddedAt).ToList();
            Loaded = true;
            Invalidate();
            Changed?.Invoke();
        }

        static readonly Dictionary<string, Task<string>> Pending = new Dictionary<string, Task<string>>();
        public static Task<string> AudioFile(Track t)
        {
            if (t.IsLocal) return Task.FromResult(t.LocalPath);
            lock (Pending)
            {
                if (Pending.TryGetValue(t.Id, out var p) && !p.IsFaulted && !p.IsCanceled) return p;
                var task = Task.Run(() => Fetch(t));
                Pending[t.Id] = task;
                return task;
            }
        }
        static string Safe(string id)
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
            var body = await Net.Get(Db + "/audio/" + Uri.EscapeDataString(t.Id) + ".json").ConfigureAwait(false);
            var d = J.D(J.Parse(body));
            var data = J.Str(d, "data");
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
    }

    public sealed class LocalFile
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public double Duration { get; set; }
        public long AddedAt { get; set; }
    }

    public sealed class LibData
    {
        public List<string> Fav { get; set; } = new List<string>();
        public List<string> FavArtists { get; set; } = new List<string>();
        public List<string> Recent { get; set; } = new List<string>();
        public List<Playlist> Playlists { get; set; } = new List<Playlist>();
        public List<LocalFile> Local { get; set; } = new List<LocalFile>();
        public bool GuestChosen { get; set; }
        public double Volume { get; set; } = 0.8;
        public long UpdatedAt { get; set; }
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
            return D.Local.Select(l => new Track { Id = l.Id, Title = l.Title, Artist = l.Artist, Album = "", Genre = "", Lyrics = "", Duration = l.Duration, LocalPath = l.Path, AddedAt = l.AddedAt }).ToList();
        }
        public static bool IsFav(string id) { return D.Fav.Contains(id); }
        public static void ToggleFav(string id) { if (!D.Fav.Remove(id)) D.Fav.Insert(0, id); Save(); }
        public static bool IsFavArtist(string a) { return D.FavArtists.Contains(a); }
        public static void ToggleFavArtist(string a) { if (!D.FavArtists.Remove(a)) D.FavArtists.Insert(0, a); Save(); }
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
        public static int AddFiles(IEnumerable<string> paths)
        {
            int n = 0;
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p)) { n += AddFiles(Directory.GetFiles(p, "*.*", SearchOption.AllDirectories)); continue; }
                    if (!File.Exists(p) || !AudioExt.Contains(Path.GetExtension(p).ToLowerInvariant())) continue;
                    var full = Path.GetFullPath(p);
                    if (D.Local.Any(l => string.Equals(l.Path, full, StringComparison.OrdinalIgnoreCase))) continue;
                    var name = Path.GetFileNameWithoutExtension(full);
                    string artist = "Неизвестный исполнитель", title = name.Trim();
                    int dash = name.IndexOf(" - ", StringComparison.Ordinal);
                    if (dash > 0) { artist = name.Substring(0, dash).Trim(); title = name.Substring(dash + 3).Trim(); }
                    D.Local.Insert(0, new LocalFile { Id = "local_" + Hash(full.ToLowerInvariant()), Path = full, Title = title, Artist = artist, AddedAt = Now });
                    n++;
                }
                catch (Exception ex) { Log.W("add file: " + ex.Message); }
            }
            if (n > 0) Save(false);
            return n;
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
                ["playlists"] = D.Playlists.Select(p => new Dictionary<string, object> { ["id"] = p.Id, ["name"] = p.Name, ["color"] = p.Color, ["trackIds"] = p.TrackIds }).ToList(),
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
                    TrackIds = x.ContainsKey("trackIds") ? Strs(x["trackIds"]) : new List<string>()
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
                var body = J.Ser(new Dictionary<string, object> { ["name"] = Current.Name ?? "", ["email"] = Current.Email ?? "", ["photo"] = Current.Photo ?? "", ["updatedAt"] = Library.Now, ["client"] = "SONORA Windows" });
                var r = await Net.Send("PATCH", UserUrl("profile", tok), body);
                if (!r.Ok) Log.W("profile save: " + r.Code + " " + r.Body);
            }
            catch (Exception ex) { Log.W("profile save: " + ex.Message); }
        }

        static string B64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        static byte[] Rnd(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }

        public static async Task Google()
        {
            var verifier = B64Url(Rnd(32));
            string challenge;
            using (var sha = SHA256.Create()) challenge = B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var state = B64Url(Rnd(16));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string code;
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = "http://127.0.0.1:" + port;
            try
            {
                var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Net.Form(
                    "client_id", GoogleClientId, "redirect_uri", redirect, "response_type", "code",
                    "scope", "openid email profile", "code_challenge", challenge, "code_challenge_method", "S256",
                    "state", state, "prompt", "select_account");
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { throw new AuthException("Не удалось открыть браузер: " + ex.Message); }
                code = await WaitForCode(listener, state);
            }
            finally { try { listener.Stop(); } catch { } }

            var tr = await Net.Send("POST", "https://oauth2.googleapis.com/token", form: Net.Form(
                "client_id", GoogleClientId, "code", code, "code_verifier", verifier, "grant_type", "authorization_code", "redirect_uri", redirect));
            if (!tr.Ok) throw new AuthException("Google не выдал токен: " + (ErrorOf(tr.Body) ?? ("HTTP " + tr.Code)));
            var access = J.Str(J.D(J.Parse(tr.Body)), "access_token");
            if (string.IsNullOrEmpty(access)) throw new AuthException("Google не вернул токен доступа.");
            var d = await Call("signInWithIdp", new
            {
                postBody = "access_token=" + Uri.EscapeDataString(access) + "&providerId=google.com",
                requestUri = redirect,
                returnIdpCredential = true,
                returnSecureToken = true
            });
            FromResp(d, "google");
            await LoadProfile();
            Done();
        }

        static async Task<string> WaitForCode(TcpListener listener, string state)
        {
            var deadline = DateTime.UtcNow.AddMinutes(4);
            while (DateTime.UtcNow < deadline)
            {
                var acceptTask = listener.AcceptTcpClientAsync();
                var done = await Task.WhenAny(acceptTask, Task.Delay(deadline - DateTime.UtcNow));
                if (done != acceptTask) break;
                using (var client = acceptTask.Result)
                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = 5000;
                    var buf = new byte[8192];
                    int n = 0;
                    try { n = stream.Read(buf, 0, buf.Length); } catch { }
                    var req = Encoding.ASCII.GetString(buf, 0, n);
                    var first = req.Split('\n')[0];
                    var parts = first.Split(' ');
                    var target = parts.Length > 1 ? parts[1] : "/";
                    var q = ParseQuery(target);
                    bool ok = q.ContainsKey("code") || q.ContainsKey("error");
                    var html = ok
                        ? "<!doctype html><html><head><meta charset='utf-8'><title>SONORA</title></head><body style='margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#0A0A12;color:#F5F5F8;font-family:Segoe UI,sans-serif'><div style='text-align:center'><div style='width:64px;height:64px;border-radius:18px;margin:0 auto 18px;background:linear-gradient(135deg,#8b5cf6,#06b6d4)'></div><h2 style='margin:0 0 8px'>" + (q.ContainsKey("code") ? "Готово!" : "Вход отменён") + "</h2><p style='color:#82829A'>Можно закрыть вкладку и вернуться в SONORA.</p></div></body></html>"
                        : "<!doctype html><html><body></body></html>";
                    var bytes = Encoding.UTF8.GetBytes(html);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + (ok ? "200 OK" : "404 Not Found") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                    try { stream.Write(head, 0, head.Length); stream.Write(bytes, 0, bytes.Length); stream.Flush(); } catch { }
                    if (!ok) continue;
                    if (q.ContainsKey("error")) throw new AuthException(q["error"] == "access_denied" ? "Вход через Google отменён." : "Google: " + q["error"]);
                    if (!q.TryGetValue("state", out var st) || st != state) throw new AuthException("Ответ Google не прошёл проверку. Попробуй ещё раз.");
                    return q["code"];
                }
            }
            throw new AuthException("Время ожидания входа через Google истекло.");
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
