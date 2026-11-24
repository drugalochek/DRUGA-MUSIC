using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Sonora
{
    // ---------- Язык интерфейса ----------
    public static partial class L
    {
        public static bool En;
        static Dictionary<string, string> map;
        static List<KeyValuePair<string, string>> pre, suf;

        public static void Init(string lang)
        {
            En = lang == "en";
            if (!En) return;
            map = new Dictionary<string, string>();
            for (int i = 0; i + 1 < Pairs.Length; i += 2) map[Pairs[i]] = Pairs[i + 1];
            pre = map.Where(kv => kv.Key.Length > 1 && (kv.Key.EndsWith(" ") || kv.Key.EndsWith(":") || kv.Key.EndsWith("«") || kv.Key.EndsWith("(") || kv.Key.EndsWith("\u00a0")))
                     .OrderByDescending(kv => kv.Key.Length).ToList();
            suf = map.Where(kv => kv.Key.Length > 1 && (kv.Key.StartsWith(" ") || kv.Key.StartsWith("»")))
                     .OrderByDescending(kv => kv.Key.Length).ToList();
        }
        static bool Cyr(string s) { foreach (var c in s) if (c >= '\u0400' && c <= '\u04FF') return true; return false; }
        public static string T(string s)
        {
            if (!En || string.IsNullOrEmpty(s) || !Cyr(s)) return s;
            string r;
            if (map.TryGetValue(s, out r)) return r;
            foreach (var kv in pre)
                if (s.StartsWith(kv.Key, StringComparison.Ordinal))
                {
                    var rest = s.Substring(kv.Key.Length);
                    return kv.Value + Suffix(rest);
                }
            return Suffix(s);
        }
        static string Suffix(string s)
        {
            string r;
            if (map.TryGetValue(s, out r)) return r;
            foreach (var kv in suf)
                if (s.EndsWith(kv.Key, StringComparison.Ordinal))
                {
                    var head = s.Substring(0, s.Length - kv.Key.Length);
                    string hr;
                    return (map.TryGetValue(head, out hr) ? hr : head) + kv.Value;
                }
            // составные строки вида «История · 5 треков»
            int dot = s.IndexOf(" · ", StringComparison.Ordinal);
            if (dot > 0)
            {
                string a = s.Substring(0, dot), b = s.Substring(dot + 3), ar, br;
                bool ha = map.TryGetValue(a, out ar), hb = map.TryGetValue(b, out br);
                if (ha || hb) return (ha ? ar : a) + " · " + (hb ? br : b);
            }
            return s;
        }
        public static string Plural(int n, string one, string few, string many)
        {
            string[] en;
            if (En && PluralEn.TryGetValue(one, out en)) return n == 1 ? en[0] : en[1];
            return null;
        }
        static readonly Dictionary<string, string[]> PluralEn = new Dictionary<string, string[]>
        {
            ["трек"] = new[] { "track", "tracks" },
            ["исполнитель"] = new[] { "artist", "artists" },
            ["альбом"] = new[] { "album", "albums" },
            ["файл"] = new[] { "file", "files" },
            ["плейлист"] = new[] { "playlist", "playlists" },
        };
    }

    // ---------- Гостевой режим ----------
    public static class Guard
    {
        public const double GuestSeconds = 30;
        public static bool Guest { get { return Auth.Current == null && Program.ShotsDir == null; } }
        public static bool Need(string what)
        {
            if (!Guest) return true;
            var sh = Shell.I;
            if (sh != null) sh.Dispatcher.BeginInvoke(new Action(() => sh.NeedAccount(what)));
            return false;
        }
    }

    // ---------- Скачанные треки (офлайн) ----------
    public sealed class OfflineItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public string Lyrics { get; set; }
        public int Year { get; set; }
        public double Duration { get; set; }
        public string File { get; set; }
        public string CoverUrl { get; set; }
        public string Ext { get; set; }
        public long At { get; set; }
    }

    public static class Offline
    {
        public static string Dir { get { return Paths.Sub("offline"); } }
        static readonly HashSet<string> busy = new HashSet<string>();
        static readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();
        public static event Action Changed;

        public static bool CanDownload(Track t) { return t != null && !t.IsLocal && t.Source == null && !string.IsNullOrEmpty(t.Id); }
        public static bool Has(string id) { return id != null && Library.D.Offline.Any(o => o.Id == id); }
        public static bool Busy(string id) { lock (busy) return busy.Contains(id); }
        // Скачанное хранится внутри SONORA в зашифрованном виде (.sdat) — это не файлы для проводника,
        // а треки для прослушивания без интернета. Для воспроизведения расшифровываем во временную папку.
        static readonly Dictionary<string, string> playable = new Dictionary<string, string>();
        static byte[] key;
        static string TempDir { get { var d = Path.Combine(Path.GetTempPath(), "SONORA-play"); Directory.CreateDirectory(d); return d; } }
        static byte[] Key()
        {
            if (key != null) return key;
            var kf = Path.Combine(Paths.Data, "offline.key");
            try
            {
                if (File.Exists(kf)) key = System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(kf), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            catch (Exception ex) { Log.W("offline key: " + ex.Message); }
            if (key == null || key.Length != 32)
            {
                key = new byte[32];
                using (var r = System.Security.Cryptography.RandomNumberGenerator.Create()) r.GetBytes(key);
                File.WriteAllBytes(kf, System.Security.Cryptography.ProtectedData.Protect(key, null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            }
            return key;
        }
        static void Encrypt(string src, string dst)
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Key = Key(); aes.GenerateIV();
                using (var o = File.Create(dst + ".tmp"))
                {
                    o.Write(new byte[] { (byte)'S', (byte)'N', (byte)'D', 1 }, 0, 4);
                    o.Write(aes.IV, 0, 16);
                    using (var cs = new System.Security.Cryptography.CryptoStream(o, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
                    using (var i = File.OpenRead(src)) i.CopyTo(cs);
                }
            }
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(dst + ".tmp", dst);
        }
        static void Decrypt(string src, string dst)
        {
            using (var i = File.OpenRead(src))
            {
                var head = new byte[4]; i.Read(head, 0, 4);
                var iv = new byte[16]; i.Read(iv, 0, 16);
                using (var aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.Key = Key(); aes.IV = iv;
                    using (var cs = new System.Security.Cryptography.CryptoStream(i, aes.CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Read))
                    using (var o = File.Create(dst)) cs.CopyTo(o);
                }
            }
        }
        public static string FileOf(string id)
        {
            var o = Library.D.Offline.FirstOrDefault(x => x.Id == id);
            if (o == null || string.IsNullOrEmpty(o.File)) return null;
            var f = Path.Combine(Dir, o.File);
            if (!File.Exists(f)) return null;
            if (!f.EndsWith(".sdat", StringComparison.OrdinalIgnoreCase)) return f;
            lock (playable)
            {
                if (playable.TryGetValue(id, out var p) && File.Exists(p)) return p;
                try
                {
                    var tmp = Path.Combine(TempDir, Guid.NewGuid().ToString("N").Substring(0, 12) + (string.IsNullOrEmpty(o.Ext) ? ".mp3" : o.Ext));
                    Decrypt(f, tmp);
                    try { File.SetAttributes(tmp, FileAttributes.Hidden | FileAttributes.Temporary); } catch { }
                    playable[id] = tmp;
                    return tmp;
                }
                catch (Exception ex) { Log.W("offline decrypt: " + ex.Message); return null; }
            }
        }
        public static void CleanupTemp()
        {
            try
            {
                var d = Path.Combine(Path.GetTempPath(), "SONORA-play");
                if (!Directory.Exists(d)) return;
                foreach (var f in Directory.GetFiles(d)) { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { } }
            }
            catch { }
            lock (playable) playable.Clear();
        }
        // старые (1.5) скачанные файлы переносим во внутреннее хранилище
        public static void Migrate()
        {
            var items = Library.D.Offline.Where(o => !string.IsNullOrEmpty(o.File) && !o.File.EndsWith(".sdat", StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) return;
            Task.Run(() =>
            {
                foreach (var o in items)
                {
                    try
                    {
                        var src = Path.Combine(Dir, o.File);
                        if (!File.Exists(src)) continue;
                        var name = Catalog.Safe(o.Id) + ".sdat";
                        Encrypt(src, Path.Combine(Dir, name));
                        File.Delete(src);
                        o.Ext = Path.GetExtension(src);
                        o.File = name;
                    }
                    catch (Exception ex) { Log.W("offline migrate: " + ex.Message); }
                }
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => Library.Save(false)));
            });
        }
        public static List<Track> Tracks()
        {
            return Library.D.Offline.Select(ToTrack).Where(t => t != null).ToList();
        }
        public static Track ToTrack(OfflineItem o)
        {
            Track t;
            if (tracks.TryGetValue(o.Id, out t)) return t;
            string cover = o.CoverUrl;
            if (string.IsNullOrEmpty(cover))
            {
                var cf = Path.Combine(Dir, Catalog.Safe(o.Id) + ".cover");
                try { if (File.Exists(cf)) cover = File.ReadAllText(cf); } catch { }
            }
            t = new Track { Id = o.Id, Title = o.Title, Artist = o.Artist, Album = o.Album ?? "", Genre = o.Genre ?? "", Lyrics = o.Lyrics ?? "", Year = o.Year, Duration = o.Duration, Cover = cover ?? "", AddedAt = o.At };
            tracks[o.Id] = t;
            return t;
        }
        public static long Size()
        {
            try { return Directory.GetFiles(Dir).Sum(f => new FileInfo(f).Length); } catch { return 0; }
        }

        public static async Task<bool> Download(Track t, bool quiet = false)
        {
            if (!CanDownload(t)) { if (!quiet) Shell.Toast("Этот трек нельзя скачать — только треки каталога SONORA"); return false; }
            if (Has(t.Id)) return true;
            lock (busy) { if (!busy.Add(t.Id)) return false; }
            Changed?.Invoke();
            try
            {
                var src = await Catalog.AudioFile(t);
                var name = Catalog.Safe(t.Id) + ".sdat";
                var dst = Path.Combine(Dir, name);
                var ext = Path.GetExtension(src);
                await Task.Run(() => Encrypt(src, dst));
                string coverUrl = null;
                if (!string.IsNullOrEmpty(t.Cover))
                {
                    if (t.Cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)) coverUrl = t.Cover;
                    else File.WriteAllText(Path.Combine(Dir, Catalog.Safe(t.Id) + ".cover"), t.Cover);
                }
                Library.D.Offline.RemoveAll(o => o.Id == t.Id);
                Library.D.Offline.Insert(0, new OfflineItem { Id = t.Id, Title = t.Title, Artist = t.Artist, Album = t.Album, Genre = t.Genre, Lyrics = t.Lyrics, Year = t.Year, Duration = t.Duration, File = name, Ext = ext, CoverUrl = coverUrl, At = Library.Now });
                tracks.Remove(t.Id);
                Library.Save(false);
                if (!quiet) Shell.Toast("Сохранено в SONORA: " + t.Artist + " — " + t.Title + " · можно слушать без интернета");
                return true;
            }
            catch (Exception ex)
            {
                Log.W("offline: " + ex.Message);
                if (!quiet) Shell.Toast("Не удалось скачать трек — проверь интернет");
                return false;
            }
            finally
            {
                lock (busy) busy.Remove(t.Id);
                Changed?.Invoke();
            }
        }
        public static async void DownloadAll(IEnumerable<Track> list)
        {
            var items = list.Where(t => CanDownload(t) && !Has(t.Id)).ToList();
            if (items.Count == 0) { Shell.Toast("Всё уже скачано"); return; }
            Shell.Toast("Скачиваем: " + U.Tn(items.Count));
            int ok = 0;
            foreach (var t in items) if (await Download(t, true)) ok++;
            Shell.Toast("Скачано: " + U.Tn(ok) + (ok < items.Count ? " · не удалось: " + (items.Count - ok) : ""));
        }
        public static void Remove(string id)
        {
            var o = Library.D.Offline.FirstOrDefault(x => x.Id == id);
            if (o == null) return;
            try { if (!string.IsNullOrEmpty(o.File)) File.Delete(Path.Combine(Dir, o.File)); } catch { }
            try { File.Delete(Path.Combine(Dir, Catalog.Safe(id) + ".cover")); } catch { }
            Library.D.Offline.Remove(o);
            tracks.Remove(id);
            lock (playable) playable.Remove(id);
            Library.Save(false);
            Shell.Toast("Удалено из скачанных");
        }
        public static void Clear()
        {
            foreach (var o in Library.D.Offline.ToList())
            {
                try { if (!string.IsNullOrEmpty(o.File)) File.Delete(Path.Combine(Dir, o.File)); } catch { }
                try { File.Delete(Path.Combine(Dir, Catalog.Safe(o.Id) + ".cover")); } catch { }
            }
            Library.D.Offline.Clear();
            tracks.Clear();
            Library.Save(false);
        }
    }

    // ---------- Фильтр «мусора» при добавлении файлов ----------
    public static class Junk
    {
        public sealed class Result
        {
            public string Path, Reason, Sig;
            public double Duration;
            public bool Ok { get { return Reason == null; } }
        }
        public const double MinSec = 30, MaxSec = 20 * 60;
        static readonly Regex VoiceName = new Regex(@"^(ptt-|aud-|audio[_ -]?\d|voice|recording|rec[_ -]?\d|запись|новая запись|голосов|диктофон|whatsapp audio|telegram voice|звонок|call[_ -]?rec)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex JunkName = new Regex(@"^(test|тест|asdf|qwerty|untitled|без названия|new|1|123|1234|a|aaa|sound|звук)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Result Check(string path)
        {
            var r = new Result { Path = path };
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length < 16 * 1024) { r.Reason = "файл пустой или повреждён"; return r; }
                var head = new byte[16];
                using (var f = File.OpenRead(path)) f.Read(head, 0, head.Length);
                if (!LooksLikeAudio(head, Path.GetExtension(path).ToLowerInvariant())) { r.Reason = "внутри не аудио (неверный формат файла)"; return r; }
                var name = Path.GetFileNameWithoutExtension(path).Trim();
                if (VoiceName.IsMatch(name)) { r.Reason = "похоже на голосовое сообщение или запись диктофона"; return r; }
                if (JunkName.IsMatch(name)) { r.Reason = "бессмысленное название файла"; return r; }
                r.Sig = Signature(path, fi.Length);
                try
                {
                    var p = ProbeSafe(path);
                    if (p != null)
                    {
                        r.Duration = p.Duration;
                        if (p.Duration > 0 && p.Duration < MinSec) { r.Reason = "слишком короткий (меньше 30 секунд)"; return r; }
                        if (p.Duration > MaxSec) { r.Reason = "слишком длинный (больше 20 минут — похоже на запись эфира или подкаст)"; return r; }
                        if (p.Loudness < 0.003) { r.Reason = "в файле тишина"; return r; }
                    }
                }
                catch (COMException ex)
                {
                    Log.W("probe: " + ex.Message);
                    // MF_E_UNSUPPORTED_BYTESTREAM_TYPE / MF_E_INVALID_FILE_FORMAT — файл битый или это не звук
                    if ((uint)ex.HResult == 0xC00D36C4 || (uint)ex.HResult == 0xC00D3E8C) { r.Reason = "файл не читается — повреждён или неподдерживаемый кодек"; return r; }
                }
                catch (Exception ex) { Log.W("probe: " + ex.Message); }
            }
            catch (Exception ex) { r.Reason = "не удалось прочитать файл"; Log.W("junk: " + ex.Message); }
            return r;
        }
        static Audio.ProbeResult ProbeSafe(string path)
        {
            if (!Beat.Available) return null;
            try { return Audio.Probe(path); }
            catch (FileNotFoundException) { Beat.Available = false; return null; }
            catch (TypeLoadException) { Beat.Available = false; return null; }
        }
        static bool LooksLikeAudio(byte[] h, string ext)
        {
            Func<string, bool> at0 = s => Encoding.ASCII.GetString(h, 0, s.Length) == s;
            bool id3 = at0("ID3");
            bool mpeg = h[0] == 0xFF && (h[1] & 0xE0) == 0xE0;
            bool ftyp = Encoding.ASCII.GetString(h, 4, 4) == "ftyp";
            bool riff = at0("RIFF") && Encoding.ASCII.GetString(h, 8, 4) == "WAVE";
            bool flac = at0("fLaC");
            bool asf = h[0] == 0x30 && h[1] == 0x26 && h[2] == 0xB2 && h[3] == 0x75;
            bool ogg = at0("OggS");
            switch (ext)
            {
                case ".mp3": return id3 || mpeg;
                case ".aac": return id3 || mpeg;
                case ".m4a": case ".mp4": return ftyp;
                case ".wav": return riff;
                case ".flac": return flac || id3;
                case ".wma": return asf;
                default: return id3 || mpeg || ftyp || riff || flac || asf || ogg;
            }
        }
        public static string Signature(string path, long len)
        {
            using (var f = File.OpenRead(path))
            using (var sha = SHA1.Create())
            {
                var buf = new byte[256 * 1024];
                int n = f.Read(buf, 0, buf.Length);
                return len + ":" + BitConverter.ToString(sha.ComputeHash(buf, 0, n)).Replace("-", "").Substring(0, 20);
            }
        }
    }

    // ---------- Ссылки на треки (как в Sonic Wave) ----------
    public static class Share
    {
        public const string Site = "https://drugalochek.github.io/Sonic-Wave/";
        public static string Link(Track t)
        {
            if (t == null || t.IsLocal || t.Source != null) return null;
            return Site + "?t=" + Uri.EscapeDataString(t.Id);
        }
        public static string Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            var m = Regex.Match(s, @"[?&]t=([^&#\s]+)");
            if (m.Success && (s.Contains("://") || s.StartsWith("?"))) return Uri.UnescapeDataString(m.Groups[1].Value);
            m = Regex.Match(s, @"^sonora:(?://)?(?:track/)?([^/?#\s]+)", RegexOptions.IgnoreCase);
            if (m.Success) return Uri.UnescapeDataString(m.Groups[1].Value);
            return null;
        }
    }

    // ---------- Регистрация в Windows: «Открыть с помощью», приложения по умолчанию, ссылки sonora:// ----------
    public static class WinReg
    {
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, int flags, IntPtr a, IntPtr b);
        public static void Register()
        {
            try
            {
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\SONORA"))
                {
                    if (string.Equals(root.GetValue("ExePath") as string, exe, StringComparison.OrdinalIgnoreCase) && (root.GetValue("RegVer") as string) == "2") return;
                    root.SetValue("ExePath", exe);
                    root.SetValue("RegVer", "2");
                }
                var cmd = "\"" + exe + "\" \"%1\"";
                var cu = Microsoft.Win32.Registry.CurrentUser;
                using (var k = cu.CreateSubKey(@"Software\Classes\SONORA.Audio"))
                {
                    k.SetValue("", "Аудиофайл SONORA");
                    k.SetValue("FriendlyTypeName", "Аудиофайл SONORA");
                    using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", "\"" + exe + "\",0");
                    using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
                    using (var c = k.CreateSubKey(@"shell\play")) c.SetValue("", "Слушать в SONORA");
                    using (var c = k.CreateSubKey(@"shell\play\command")) c.SetValue("", cmd);
                }
                using (var k = cu.CreateSubKey(@"Software\Classes\Applications\SONORA.exe"))
                {
                    k.SetValue("FriendlyAppName", "SONORA");
                    using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
                    using (var st = k.CreateSubKey("SupportedTypes")) foreach (var e in Library.AudioExt) st.SetValue(e, "");
                }
                foreach (var e in Library.AudioExt)
                    using (var k = cu.CreateSubKey(@"Software\Classes\" + e + @"\OpenWithProgids")) k.SetValue("SONORA.Audio", "");
                using (var k = cu.CreateSubKey(@"Software\Classes\sonora"))
                {
                    k.SetValue("", "URL:SONORA");
                    k.SetValue("URL Protocol", "");
                    using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", "\"" + exe + "\",0");
                    using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
                }
                using (var k = cu.CreateSubKey(@"Software\SONORA\Capabilities"))
                {
                    k.SetValue("ApplicationName", "SONORA");
                    k.SetValue("ApplicationDescription", "SONORA — музыкальный плеер для Windows");
                    k.SetValue("ApplicationIcon", "\"" + exe + "\",0");
                    using (var fa = k.CreateSubKey("FileAssociations")) foreach (var e in Library.AudioExt) fa.SetValue(e, "SONORA.Audio");
                    using (var ua = k.CreateSubKey("URLAssociations")) ua.SetValue("sonora", "sonora");
                }
                using (var k = cu.CreateSubKey(@"Software\RegisteredApplications")) k.SetValue("SONORA", @"Software\SONORA\Capabilities");
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { Log.W("reg: " + ex.Message); }
        }
    }

    // ---------- Один экземпляр: повторный запуск передаёт файлы/ссылки в уже открытое окно ----------
    public static class Single
    {
        const string Name = "SONORA-single-instance-v1";
        static Mutex mutex;
        static bool stop;
        public static event Action<string[]> Received;

        public static bool TryFirst(string[] args)
        {
            bool created;
            mutex = new Mutex(true, Name, out created);
            if (created) { StartServer(); return true; }
            try
            {
                using (var c = new NamedPipeClientStream(".", Name, PipeDirection.Out))
                {
                    c.Connect(1500);
                    var data = Encoding.UTF8.GetBytes(string.Join("\n", args ?? new string[0]) + "\n\u0004");
                    c.Write(data, 0, data.Length);
                    c.Flush();
                }
            }
            catch (Exception ex) { Log.W("single: " + ex.Message); }
            return false;
        }
        static void StartServer()
        {
            var th = new Thread(() =>
            {
                while (!stop)
                {
                    try
                    {
                        using (var s = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte))
                        {
                            s.WaitForConnection();
                            var ms = new MemoryStream();
                            var buf = new byte[4096];
                            int n;
                            while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (buf[n - 1] == 4) break; }
                            var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\u0004', '\n');
                            var items = text.Split('\n').Where(x => x.Length > 0).ToArray();
                            Received?.Invoke(items);
                        }
                    }
                    catch (Exception ex) { if (!stop) { Log.W("pipe: " + ex.Message); Thread.Sleep(300); } }
                }
            }) { IsBackground = true, Name = "sonora-pipe" };
            th.Start();
        }
        public static void Release()
        {
            stop = true;
            try { mutex?.ReleaseMutex(); } catch { }
            try { mutex?.Dispose(); } catch { }
            mutex = null;
        }
    }
}
