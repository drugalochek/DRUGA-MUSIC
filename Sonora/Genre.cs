using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sonora
{
    // ---------- Автоопределение жанров для треков из сервисов (Яндекс, Spotify, Deezer) ----------
    // У Spotify и Deezer в треке жанра нет, у Яндекса он есть только у альбома и в виде кода (rusrap, electronics…).
    // Порядок: жанр альбома Яндекса → iTunes Search (primaryGenreName) → Deezer (жанр альбома) → жанр исполнителя из кэша.
    // Результаты кэшируются в genres.json, поэтому каждый трек проверяется один раз.
    public static class GenreDetect
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();   // "t:<id>" / "a:<artist>" → жанр ("" — не нашли)
        static readonly Queue<Track> queue = new Queue<Track>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static bool running, loaded, dirty;
        static string FileP { get { return Path.Combine(Paths.Data, "genres.json"); } }

        // Сводим всё к одному написанию: «Hip-Hop/Rap», «rusrap», «Rap/Hip Hop» → «хип-хоп».
        static readonly Dictionary<string, string> map = new Dictionary<string, string>
        {
            ["rusrap"] = "рэп", ["rap"] = "рэп", ["рэп"] = "рэп", ["russian rap"] = "рэп", ["foreignrap"] = "рэп", ["trap"] = "рэп",
            ["hiphop"] = "хип-хоп", ["hip hop"] = "хип-хоп", ["hip-hop"] = "хип-хоп", ["hip-hop/rap"] = "хип-хоп", ["rap/hip hop"] = "хип-хоп", ["хип-хоп"] = "хип-хоп", ["хип хоп"] = "хип-хоп",
            ["pop"] = "поп", ["ruspop"] = "поп", ["russian pop"] = "поп", ["k-pop"] = "k-pop", ["kpop"] = "k-pop", ["j-pop"] = "поп", ["поп"] = "поп", ["estrada"] = "эстрада", ["эстрада"] = "эстрада",
            ["rock"] = "рок", ["rusrock"] = "рок", ["russian rock"] = "рок", ["hard rock"] = "рок", ["classic rock"] = "рок", ["рок"] = "рок", ["rock-n-roll"] = "рок-н-ролл", ["rock & roll"] = "рок-н-ролл",
            ["alternative"] = "альтернатива", ["alternative rock"] = "альтернатива", ["альтернатива"] = "альтернатива",
            ["indie"] = "инди", ["indie pop"] = "инди", ["indie rock"] = "инди", ["local-indie"] = "инди", ["инди"] = "инди",
            ["electronics"] = "электроника", ["electronic"] = "электроника", ["electro"] = "электроника", ["edm"] = "электроника", ["электроника"] = "электроника",
            ["dance"] = "дэнс", ["house"] = "хаус", ["techno"] = "техно", ["trance"] = "транс", ["dnb"] = "драм-н-бэйс", ["drum & bass"] = "драм-н-бэйс", ["dubstep"] = "дабстеп", ["phonk"] = "фонк", ["фонк"] = "фонк",
            ["metal"] = "метал", ["heavy metal"] = "метал", ["рок-метал"] = "метал", ["punk"] = "панк", ["панк"] = "панк",
            ["rnb"] = "rnb", ["r&b"] = "rnb", ["r&b/soul"] = "rnb", ["soul"] = "соул", ["soul & funk"] = "соул", ["funk"] = "фанк",
            ["jazz"] = "джаз", ["vocal jazz"] = "джаз", ["blues"] = "блюз", ["блюз"] = "блюз", ["classical"] = "классика", ["classic"] = "классика", ["классика"] = "классика",
            ["soundtrack"] = "саундтрек", ["films/games"] = "саундтрек", ["films & games"] = "саундтрек", ["videogame"] = "саундтрек", ["anime"] = "аниме",
            ["reggae"] = "регги", ["country"] = "кантри", ["folk"] = "фолк", ["singer-songwriter"] = "фолк", ["shanson"] = "шансон", ["шансон"] = "шансон",
            ["latin"] = "латино", ["latin music"] = "латино", ["latinfolk"] = "латино", ["reggaeton"] = "латино", ["ambient"] = "эмбиент", ["lounge"] = "лаунж", ["relax"] = "лаунж",
            ["kids"] = "детская", ["children's music"] = "детская", ["children"] = "детская",
        };
        static readonly HashSet<string> junk = new HashSet<string> { "music", "musique", "all", "other", "others", "various", "worldwide", "world", "", "unknown" };

        // Один токен жанра → каноническое имя (нижний регистр). Незнакомые жанры оставляем как есть.
        public static string Canon(string g)
        {
            if (string.IsNullOrWhiteSpace(g)) return null;
            var k = g.Trim().Trim('.', '!', ' ').ToLowerInvariant();
            if (k.Length == 0) return null;
            if (map.TryGetValue(k, out var v)) return v;
            return k;
        }
        // Полный жанр из внешнего источника → строка для Track.Genre (сначала целиком, потом по частям)
        static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var k = raw.Trim().ToLowerInvariant();
            if (junk.Contains(k)) return null;
            if (map.TryGetValue(k, out var whole)) return whole;
            var parts = Mixes.Genres(raw).Where(p => !junk.Contains(p)).Distinct().Take(2).ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        static void LoadCache()
        {
            if (loaded) return;
            loaded = true;
            try { if (File.Exists(FileP)) foreach (var kv in J.To<Dictionary<string, string>>(File.ReadAllText(FileP)) ?? new Dictionary<string, string>()) cache[kv.Key] = kv.Value; }
            catch (Exception ex) { Log.W("genres load: " + ex.Message); }
        }
        static void SaveCache()
        {
            if (!dirty || !Library.Persist) return;
            dirty = false;
            try { Directory.CreateDirectory(Paths.Data); lock (cache) File.WriteAllText(FileP, J.Ser(cache)); }
            catch (Exception ex) { Log.W("genres save: " + ex.Message); }
        }
        static string MainArtist(string a)
        {
            if (string.IsNullOrEmpty(a)) return "";
            return Regex.Split(a, @"\s*(?:,|;|&|\bfeat\.?|\bft\.?|\bfeaturing\b)\s*", RegexOptions.IgnoreCase).FirstOrDefault(s => s.Trim().Length > 0)?.Trim().ToLowerInvariant() ?? "";
        }

        // Вызывается при появлении трека из сервиса. Если жанр уже известен — подставляем сразу, иначе ставим в очередь.
        public static void Fill(Track t)
        {
            if (t == null || t.Source == null) return;
            lock (cache)
            {
                LoadCache();
                if (!string.IsNullOrEmpty(t.Genre))
                {
                    var n = Normalize(t.Genre);
                    if (n != null) { t.Genre = n; var ak = "a:" + MainArtist(t.Artist); if (!cache.ContainsKey(ak)) { cache[ak] = n; dirty = true; } }
                    return;
                }
                if (cache.TryGetValue("t:" + t.Id, out var g)) { t.Genre = g; return; }
                if (cache.TryGetValue("a:" + MainArtist(t.Artist), out var ag) && !string.IsNullOrEmpty(ag)) { t.Genre = ag; return; }
                if (!queued.Add(t.Id)) return;
                queue.Enqueue(t);
                if (running) return;
                running = true;
            }
            _ = Run();
        }

        static async Task Run()
        {
            int done = 0;
            await Task.Delay(1500).ConfigureAwait(false);
            while (true)
            {
                Track t;
                lock (cache)
                {
                    if (queue.Count == 0) { running = false; break; }
                    t = queue.Dequeue();
                }
                if (!string.IsNullOrEmpty(t.Genre)) continue;
                string g = null;
                var ak = "a:" + MainArtist(t.Artist);
                lock (cache) if (cache.TryGetValue(ak, out var ag) && !string.IsNullOrEmpty(ag)) g = ag;
                if (g == null)
                {
                    try { g = await FromItunes(t).ConfigureAwait(false); } catch (Exception ex) { Log.W("genre itunes: " + ex.Message); }
                    if (g == null) try { g = await FromDeezer(t).ConfigureAwait(false); } catch (Exception ex) { Log.W("genre deezer: " + ex.Message); }
                    await Task.Delay(700).ConfigureAwait(false);   // бережём лимиты iTunes (~20 запросов в минуту)
                }
                lock (cache)
                {
                    cache["t:" + t.Id] = g ?? "";
                    if (g != null && !cache.ContainsKey(ak)) cache[ak] = g;
                    dirty = true;
                }
                if (g != null) { t.Genre = g; done++; }
                if (done > 0 && (done % 15 == 0)) Publish();
            }
            if (done > 0) Publish();
            SaveCache();
        }
        static void Publish() { SaveCache(); Ext.NotifyGenres(); }

        static string Q(string s) { return Uri.EscapeDataString(s ?? ""); }
        static string Clean(string title) { return Regex.Replace(title ?? "", @"\s*[\(\[].*?[\)\]]", "").Trim(); }
        static bool Same(string a, string b)
        {
            a = MainArtist(a); b = MainArtist(b);
            return a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b) || b.Contains(a));
        }

        static async Task<string> FromItunes(Track t)
        {
            var term = MainArtist(t.Artist) + " " + Clean(t.Title);
            var r = await Net.Send("GET", "https://itunes.apple.com/search?media=music&entity=song&limit=8&term=" + Q(term)).ConfigureAwait(false);
            if (!r.Ok) return null;
            var res = J.D(J.Parse(r.Body));
            var arr = res != null && res.ContainsKey("results") ? res["results"] as object[] : null;
            if (arr == null) return null;
            foreach (var o in arr)
            {
                var d = J.D(o);
                if (d == null || !Same(J.Str(d, "artistName"), t.Artist)) continue;
                var g = Normalize(J.Str(d, "primaryGenreName"));
                if (g != null) return g;
            }
            return null;
        }

        static async Task<string> FromDeezer(Track t)
        {
            string albumId = null;
            if (t.Source == "dz" && !string.IsNullOrEmpty(t.Ref))
            {
                var r0 = await Net.Send("GET", "https://api.deezer.com/track/" + Q(t.Ref)).ConfigureAwait(false);
                var d0 = r0.Ok ? J.D(J.Parse(r0.Body)) : null;
                if (d0 != null && d0.ContainsKey("album")) albumId = J.Str(J.D(d0["album"]), "id");
            }
            if (albumId == null)
            {
                var q = "artist:\"" + MainArtist(t.Artist) + "\" track:\"" + Clean(t.Title) + "\"";
                var r1 = await Net.Send("GET", "https://api.deezer.com/search?limit=5&q=" + Q(q)).ConfigureAwait(false);
                if (!r1.Ok) return null;
                var d1 = J.D(J.Parse(r1.Body));
                var arr = d1 != null && d1.ContainsKey("data") ? d1["data"] as object[] : null;
                if (arr == null) return null;
                foreach (var o in arr)
                {
                    var x = J.D(o);
                    var ar = x != null && x.ContainsKey("artist") ? J.D(x["artist"]) : null;
                    if (!Same(J.Str(ar, "name"), t.Artist)) continue;
                    var al = x.ContainsKey("album") ? J.D(x["album"]) : null;
                    albumId = J.Str(al, "id");
                    if (albumId != null) break;
                }
            }
            if (albumId == null) return null;
            var r2 = await Net.Send("GET", "https://api.deezer.com/album/" + Q(albumId)).ConfigureAwait(false);
            if (!r2.Ok) return null;
            var a = J.D(J.Parse(r2.Body));
            var gs = a != null && a.ContainsKey("genres") ? J.D(a["genres"]) : null;
            var data = gs != null && gs.ContainsKey("data") ? gs["data"] as object[] : null;
            if (data == null) return null;
            foreach (var o in data) { var g = Normalize(J.Str(J.D(o), "name")); if (g != null) return g; }
            return null;
        }
    }
}
