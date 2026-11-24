using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sonora
{
    public sealed class Mix
    {
        public string Id, Name, Tag, Sub, Icon, From, To;
        public List<Track> Tracks = new List<Track>();
    }
    public sealed class ArtistInfo
    {
        public string Name;
        public Track CoverTrack;
        public int Count;
        public bool Verified;
    }

    public static class Mixes
    {
        public const string Liked = "__liked__", LikedArtists = "__liked_artists__", Uploaded = "__uploaded__", Recent = "__recent__", Downloaded = "__offline__";
        const long HalfHour = 30 * 60 * 1000;

        public static List<Track> FavTracks() { return Library.D.Fav.Select(Catalog.Get).Where(t => t != null).ToList(); }
        public static List<Track> FavPool()
        {
            var seen = new HashSet<string>();
            var res = new List<Track>();
            foreach (var t in FavTracks()) if (seen.Add(t.Id)) res.Add(t);
            foreach (var a in Library.D.FavArtists)
                foreach (var t in Catalog.All.Where(x => x.Artist == a)) if (seen.Add(t.Id)) res.Add(t);
            return res;
        }
        public static List<string> Genres(string g)
        {
            if (string.IsNullOrWhiteSpace(g)) return new List<string>();
            return g.Split(new[] { ',', '/', ';', '|', '&' }, StringSplitOptions.RemoveEmptyEntries).Select(GenreDetect.Canon).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        }
        // Все известные треки: каталог SONORA + треки подключённых сервисов
        public static List<Track> Universe()
        {
            var seen = new HashSet<string>();
            var res = new List<Track>();
            foreach (var t in Catalog.All) if (seen.Add(t.Id)) res.Add(t);
            foreach (var t in Ext.Known()) if (seen.Add(t.Id)) res.Add(t);
            return res;
        }
        // «A, B feat. C» → A, B, C
        static readonly System.Text.RegularExpressions.Regex ArtSplit = new System.Text.RegularExpressions.Regex(@"\s*(?:,|;|&|\bfeat\.?|\bft\.?|\bfeaturing\b)\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        public static IEnumerable<string> ArtistsOf(Track t)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.Artist) || t.Artist == "—") return Enumerable.Empty<string>();
            return ArtSplit.Split(t.Artist).Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
        }
        public static List<Track> PctTracks(string name)
        {
            return Universe().Where(t => ArtistsOf(t).Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        public static string GenreName(string g)
        {
            if (string.IsNullOrEmpty(g)) return g;
            if (g == "rnb") return "R&B";
            if (g == "k-pop") return "K-Pop";
            return char.ToUpper(g[0], CultureInfo.CurrentCulture) + g.Substring(1);
        }
        static int Seed(string s) { unchecked { int h = 23; foreach (var c in s) h = h * 31 + c; return h; } }
        public static List<Track> Shuffle(IEnumerable<Track> src, string seed)
        {
            var l = src.ToList();
            var r = new Random(Seed(seed));
            for (int i = l.Count - 1; i > 0; i--) { int j = r.Next(i + 1); var t = l[i]; l[i] = l[j]; l[j] = t; }
            return l;
        }
        static string DayKey { get { return DateTime.Now.ToString("yyyy-MM-dd"); } }

        public static List<Mix> ForYou()
        {
            var pool = FavPool();
            var res = new List<Mix>();
            if (pool.Count < 1) return res;
            var now = DateTime.Now;
            var week = now.Year + "_" + (now.DayOfYear / 7);
            var day = Shuffle(pool, "day_" + DayKey).Take(30).ToList();
            res.Add(new Mix { Id = "__mix_day__", Name = "Микс дня", Tag = "Сегодня", Sub = string.Join(", ", day.Select(t => t.Artist).Distinct().Take(3)), Tracks = day, Icon = "sun", From = "#f59e0b", To = "#ec4899" });
            res.Add(new Mix { Id = "__mix_week__", Name = "Микс недели", Tag = "На эту неделю", Sub = "Обновляется по понедельникам", Tracks = Shuffle(pool, "week_" + week).Take(50).ToList(), Icon = "calendar", From = "#06b6d4", To = "#8b5cf6" });
            var seen = new HashSet<string>(pool.Select(t => t.Id));
            var forYou = pool.ToList();
            var myGenres = new HashSet<string>(pool.SelectMany(t => Genres(t.Genre)));
            if (myGenres.Count > 0)
                foreach (var t in Universe())
                    if (!seen.Contains(t.Id) && Genres(t.Genre).Any(myGenres.Contains)) { seen.Add(t.Id); forYou.Add(t); }
            forYou = Shuffle(forYou, "foryou_" + DayKey).Take(80).ToList();
            if (forYou.Count > 0) res.Add(new Mix { Id = "__mix_foryou__", Name = "Для тебя", Tag = "Персонально", Sub = "Из любимых", Tracks = forYou, Icon = "music", From = "#ec4899", To = "#8b5cf6" });
            return res;
        }

        public static long Slot { get { return Library.Now / HalfHour; } }
        public static string HourCountdown()
        {
            var now = DateTime.Now;
            var next = now.Minute < 30 ? new DateTime(now.Year, now.Month, now.Day, now.Hour, 30, 0) : new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
            var left = next - now;
            return ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
        }
        public static Mix GenreOfHour()
        {
            var pool = FavPool();
            if (pool.Count == 0) return null;
            var genres = pool.SelectMany(t => Genres(t.Genre)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (genres.Count == 0) return null;
            var pick = genres[(int)(Slot % genres.Count)];
            var list = Universe().Where(t => Genres(t.Genre).Contains(pick)).ToList();
            if (list.Count == 0) return null;
            return new Mix { Id = "__hour_genre__", Name = GenreName(pick), Tag = "Сейчас", Sub = U.Tn(list.Count), Tracks = Shuffle(list, "hour_" + Slot), Icon = "clock", From = "#f59e0b", To = "#8b5cf6" };
        }

        // «100% для вас»: исполнители из лайков, любимых исполнителей и истории — из каталога И из сервисов.
        // Раньше брались только 5 исполнителей и только те, что есть в маленьком каталоге SONORA, поэтому оставался один.
        public static List<KeyValuePair<string, List<Track>>> Pct()
        {
            var score = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Action<string, double> add = (a, w) => { if (string.IsNullOrWhiteSpace(a)) return; score[a] = (score.TryGetValue(a, out var v) ? v : 0) + w; if (!display.ContainsKey(a)) display[a] = a; };
            foreach (var t in FavTracks()) foreach (var a in ArtistsOf(t)) add(a, 2);
            foreach (var a in Library.D.FavArtists) add(a, 3);
            foreach (var t in Library.D.Recent.Select(Catalog.Get).Where(x => x != null).Take(60)) foreach (var a in ArtistsOf(t)) add(a, .5);
            if (score.Count == 0) return new List<KeyValuePair<string, List<Track>>>();
            var byArtist = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Universe())
                foreach (var a in ArtistsOf(t))
                {
                    if (!score.ContainsKey(a)) continue;
                    if (!byArtist.TryGetValue(a, out var l)) byArtist[a] = l = new List<Track>();
                    l.Add(t);
                }
            return score.Where(x => byArtist.ContainsKey(x.Key))
                .OrderByDescending(x => x.Value).ThenByDescending(x => byArtist[x.Key].Count).Take(12)
                .Select(x => new KeyValuePair<string, List<Track>>(display[x.Key], byArtist[x.Key])).ToList();
        }

        public static List<ArtistInfo> Artists(IEnumerable<Track> src)
        {
            return src.Where(t => !string.IsNullOrEmpty(t.Artist)).GroupBy(t => t.Artist)
                .Select(g => new ArtistInfo { Name = g.Key, Count = g.Count(), CoverTrack = g.FirstOrDefault(t => !string.IsNullOrEmpty(t.Cover)), Verified = g.Any(t => t.Verified) })
                .OrderByDescending(a => a.Count).ThenBy(a => a.Name).ToList();
        }
        public static ArtistInfo Artist(string name) { return Artists(Catalog.All.Where(t => t.Artist == name)).FirstOrDefault() ?? new ArtistInfo { Name = name }; }

        public static List<Track> Fresh()
        {
            var day = 86400000L;
            var now = Library.Now;
            var fresh = Catalog.All.Where(t => t.AddedAt > 0 && now - t.AddedAt < day).ToList();
            if (fresh.Count < 3) fresh = Catalog.All.Where(t => t.AddedAt > 0 && now - t.AddedAt < 7 * day).ToList();
            return fresh.OrderByDescending(t => t.AddedAt).Take(12).ToList();
        }

        public static Mix Get(string id)
        {
            if (id == null) return null;
            if (id == Liked) return new Mix { Id = id, Name = "Любимые треки", Tag = "Избранное", Tracks = FavTracks(), Icon = "heart", From = "#FF6BAE", To = "#D81B72" };
            if (id == Downloaded) return new Mix { Id = id, Name = "Скачанные", Tag = "Офлайн", Tracks = Offline.Tracks(), Icon = "download", From = "#2DD4BF", To = "#2563EB" };
            if (id == Uploaded) return new Mix { Id = id, Name = "Загруженная музыка", Tag = "С компьютера", Tracks = Library.LocalTracks(), Icon = "upload", From = "#06b6d4", To = "#10b981" };
            if (id == Recent) return new Mix { Id = id, Name = "Недавно прослушанные", Tag = "История", Tracks = Library.D.Recent.Select(Catalog.Get).Where(t => t != null).ToList(), Icon = "clock", From = "#8b5cf6", To = "#06b6d4" };
            if (id == "__hour_genre__") return GenreOfHour();
            if (id.StartsWith("__mix_")) return ForYou().FirstOrDefault(m => m.Id == id);
            if (id.StartsWith("__pct_"))
            {
                var name = id.Substring(6);
                var list = PctTracks(name);
                return new Mix { Id = id, Name = "100% " + name, Tag = "100%", Tracks = list, Icon = "star", From = "#f59e0b", To = "#ef4444" };
            }
            var pl = Library.D.Playlists.FirstOrDefault(p => p.Id == id);
            if (pl != null)
                return new Mix { Id = id, Name = pl.Name, Tag = "Плейлист", Tracks = pl.TrackIds.Select(Catalog.Get).Where(t => t != null).ToList(), Icon = "music", From = pl.Color ?? "#8b5cf6", To = Th.Shade(pl.Color ?? "#8b5cf6", -30) };
            return null;
        }
    }
}
