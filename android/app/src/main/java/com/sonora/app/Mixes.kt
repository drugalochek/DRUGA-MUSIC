package com.sonora.app

import android.content.Context
import android.content.SharedPreferences
import androidx.compose.runtime.*
import org.json.JSONArray
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale
import java.util.TimeZone

// Подборки — те же правила, что в Mixes.cs на ПК
class Mix(val id: String, val name: String, val tag: String, val sub: String, val tracks: List<Track>, val icon: String,
          val from: String, val to: String, val playlist: Playlist? = null) {
    val brush get() = Th.grad(from, to)
}

class ArtistInfo(val name: String, val count: Int, val coverTrack: Track?, val verified: Boolean)

fun halfHourSlot(): Long { val t = now(); return (t + TimeZone.getDefault().getOffset(t)) / (30 * 60_000L) }
fun halfHourLeft(): Long { val t = now() / 1000 * 1000; val local = t + TimeZone.getDefault().getOffset(t); val slotMs = 30 * 60_000L; return (slotMs - local % slotMs) / 1000 }

object Mixes {
    const val LIKED = "__liked__"
    const val RECENT = "__recent__"
    const val HOUR = "__hour_genre__"
    private lateinit var prefs: SharedPreferences

    fun init(ctx: Context) { prefs = ctx.getSharedPreferences("mixes", Context.MODE_PRIVATE) }

    fun favTracks(): List<Track> = Lib.fav.mapNotNull { trackOf(it) }
    fun favPool(): List<Track> {
        val seen = HashSet<String>(); val res = mutableListOf<Track>()
        for (t in favTracks()) if (seen.add(t.id)) res.add(t)
        for (a in Lib.favArtists) for (t in Catalog.tracks) if (t.artist == a && seen.add(t.id)) res.add(t)
        return res
    }
    fun canon(s: String): String = s.trim().trimEnd('.').lowercase(Locale.ROOT).replace("hip hop", "hip-hop").replace("r&b", "rnb")
    fun genres(g: String): List<String> = if (g.isBlank()) emptyList() else g.split(',', '/', ';', '|', '&').map { canon(it) }.filter { it.isNotEmpty() }.distinct()
    fun genreName(g: String): String = when (g) { "rnb" -> "R&B"; "k-pop" -> "K-Pop"; "" -> g; else -> g.replaceFirstChar { it.titlecase() } }

    fun artistsOf(t: Track): List<String> = if (t.artist.isBlank() || t.artist == "—") emptyList() else t.artists.distinctBy { it.lowercase() }

    private fun seed(s: String): Long { var h = 23; for (c in s) h = h * 31 + c.code; return h.toLong() }
    fun shuffle(src: List<Track>, key: String): List<Track> {
        val l = src.toMutableList(); val r = java.util.Random(seed(key))
        for (i in l.size - 1 downTo 1) { val j = r.nextInt(i + 1); val t = l[i]; l[i] = l[j]; l[j] = t }
        return l
    }
    private fun dayKey() = SimpleDateFormat("yyyy-MM-dd", Locale.ROOT).format(Date())

    fun forYou(): List<Mix> {
        val pool = favPool()
        val res = mutableListOf<Mix>()
        if (pool.isEmpty()) return res
        val c = Calendar.getInstance()
        val week = "${c.get(Calendar.YEAR)}_${c.get(Calendar.DAY_OF_YEAR) / 7}"
        val day = shuffle(pool, "day_" + dayKey()).take(30)
        res.add(Mix("__mix_day__", "Микс дня", "Сегодня", day.map { it.artist }.distinct().take(3).joinToString(", "), day, "sun", "#f59e0b", "#ec4899"))
        res.add(Mix("__mix_week__", "Микс недели", "На эту неделю", "Обновляется по понедельникам", shuffle(pool, "week_$week").take(50), "calendar", "#06b6d4", "#8b5cf6"))
        val seen = pool.map { it.id }.toHashSet()
        val fy = pool.toMutableList()
        val my = pool.flatMap { genres(it.genre) }.toHashSet()
        if (my.isNotEmpty()) for (t in Catalog.tracks) if (t.id !in seen && genres(t.genre).any { it in my }) { seen.add(t.id); fy.add(t) }
        val list = shuffle(fy, "foryou_" + dayKey()).take(80)
        if (list.isNotEmpty()) res.add(Mix("__mix_foryou__", "Для тебя", "Персонально", "Из любимых", list, "music", "#ec4899", "#8b5cf6"))
        return res
    }

    fun countdown(): String { val left = halfHourLeft(); return "%02d:%02d".format(left / 60, left % 60) }

    // Жанр получаса: один жанр на получасовой слот по местному времени, подряд не повторяется, порядок треков в слоте не меняется
    fun genreOfHour(): Mix? {
        val slot = halfHourSlot()
        var hSlot = prefs.getLong("slot", -1)
        var hGenre = prefs.getString("genre", "") ?: ""
        var hPrev = prefs.getString("prev", "") ?: ""
        val hIds = (try { JSONArray(prefs.getString("ids", "[]")).strings() } catch (_: Exception) { mutableListOf() })
        if (hSlot != slot || hGenre.isEmpty()) {
            val pool = favPool().ifEmpty { Catalog.tracks }
            val gs = pool.flatMap { genres(it.genre) }.distinct().sorted()
            if (gs.isEmpty()) return null
            val last = if (hSlot == slot) hPrev else hGenre
            var pick = gs[java.util.Random(seed("hourpick_$slot")).nextInt(gs.size)]
            if (gs.size > 1 && pick == last) pick = gs[(gs.indexOf(pick) + 1) % gs.size]
            val list = Catalog.tracks.filter { pick in genres(it.genre) }
            if (list.isEmpty()) return null
            hPrev = last; hSlot = slot; hGenre = pick
            hIds.clear(); hIds.addAll(shuffle(list, "hour_$slot").map { it.id })
            save(hSlot, hGenre, hPrev, hIds)
        } else {
            val have = hIds.toHashSet()
            val extra = Catalog.tracks.filter { it.id !in have && hGenre in genres(it.genre) }.map { it.id }
            if (extra.isNotEmpty()) { hIds.addAll(extra); save(hSlot, hGenre, hPrev, hIds) }
        }
        val tracks = hIds.mapNotNull { trackOf(it) }
        if (tracks.isEmpty()) return null
        return Mix(HOUR, genreName(hGenre), "Сейчас", tn(tracks.size), tracks, "clock", "#f59e0b", "#8b5cf6")
    }
    private fun save(slot: Long, g: String, prev: String, ids: List<String>) {
        prefs.edit().putLong("slot", slot).putString("genre", g).putString("prev", prev).putString("ids", JSONArray(ids).toString()).apply()
    }

    // «100% для вас»: исполнители из лайков, любимых исполнителей и истории
    fun pct(): List<Pair<String, List<Track>>> {
        val score = LinkedHashMap<String, Double>(); val display = HashMap<String, String>()
        fun add(a: String, w: Double) { if (a.isBlank()) return; val k = a.lowercase(); score[k] = (score[k] ?: 0.0) + w; display.putIfAbsent(k, a) }
        for (t in favTracks()) for (a in artistsOf(t)) add(a, 2.0)
        for (a in Lib.favArtists) add(a, 3.0)
        for (t in Lib.recent.mapNotNull { trackOf(it) }.take(60)) for (a in artistsOf(t)) add(a, .5)
        if (score.isEmpty()) return emptyList()
        val by = HashMap<String, MutableList<Track>>()
        for (t in Catalog.tracks) for (a in artistsOf(t)) { val k = a.lowercase(); if (k in score) by.getOrPut(k) { mutableListOf() }.add(t) }
        return score.entries.filter { by.containsKey(it.key) }
            .sortedWith(compareByDescending<Map.Entry<String, Double>> { it.value }.thenByDescending { by[it.key]!!.size }).take(12)
            .map { display[it.key]!! to by[it.key]!! }
    }

    fun artists(src: List<Track>): List<ArtistInfo> {
        val m = LinkedHashMap<String, MutableList<Track>>(); val disp = HashMap<String, String>()
        for (t in src) for (a in artistsOf(t)) { val k = a.lowercase(); m.getOrPut(k) { mutableListOf() }.add(t); disp.putIfAbsent(k, a) }
        return m.map { (k, l) -> ArtistInfo(disp[k]!!, l.size, l.firstOrNull { !it.cover.isNullOrEmpty() }, l.any { it.verified }) }
            .sortedWith(compareByDescending<ArtistInfo> { it.count }.thenBy { it.name })
    }
    fun artist(name: String): ArtistInfo = artists(Catalog.byArtist(name)).firstOrNull { it.name.equals(name, true) } ?: ArtistInfo(name, 0, null, false)

    fun fresh(): List<Track> {
        val day = 86_400_000L; val n = now()
        var f = Catalog.tracks.filter { it.addedAt > 0 && n - it.addedAt < day }
        if (f.size < 3) f = Catalog.tracks.filter { it.addedAt > 0 && n - it.addedAt < 7 * day }
        return f.sortedByDescending { it.addedAt }.take(12)
    }

    fun albumId(artist: String, album: String) = "__album_" + artist + "\u0001" + album
    fun mine(): List<Track> { val uid = Auth.current?.uid ?: return emptyList(); return Catalog.tracks.filter { it.ownerId == uid } }

    fun get(id: String): Mix? {
        when {
            id == LIKED -> return Mix(id, "Любимые треки", "Избранное", "", favTracks(), "heart", "#FF6BAE", "#D81B72")
            id == RECENT -> return Mix(id, "Недавно прослушанные", "История", "", Lib.recent.mapNotNull { trackOf(it) }, "clock", "#8b5cf6", "#06b6d4")
            id == HOUR -> return genreOfHour()
            id == "__mine__" -> return Mix(id, "Мои треки", "Мои", "", mine(), "upload", "#06b6d4", "#10b981")
            id.startsWith("__mix_") -> return forYou().firstOrNull { it.id == id }
            id.startsWith("__pct_") -> {
                val name = id.removePrefix("__pct_")
                val list = Catalog.tracks.filter { t -> artistsOf(t).any { it.equals(name, true) } }
                return Mix(id, "100% $name", "100%", "", list, "star", "#f59e0b", "#ef4444")
            }
            id.startsWith("__genre_") -> {
                val g = id.removePrefix("__genre_")
                return Mix(id, genreName(g), "Жанр", "", Catalog.tracks.filter { g in genres(it.genre) }, "music", "#06b6d4", "#8b5cf6")
            }
            id.startsWith("__album_") -> {
                val (a, al) = id.removePrefix("__album_").split('\u0001').let { (it.getOrNull(0) ?: "") to (it.getOrNull(1) ?: "") }
                return Mix(id, al, "Альбом", a, Catalog.byArtist(a).filter { it.album == al }, "music", "#3b82f6", "#1d4ed8")
            }
        }
        val pl = Lib.playlist(id) ?: return null
        val c = Th.hex(pl.color)
        val to = Th.shade(c, -30)
        val hex = String.format("#%06X", 0xFFFFFF and android.graphics.Color.rgb((to.red * 255).toInt(), (to.green * 255).toInt(), (to.blue * 255).toInt()))
        return Mix(pl.id, pl.name, "Плейлист", "", pl.trackIds.mapNotNull { trackOf(it) }, "music", pl.color, hex, pl)
    }
}

// Текст песни: LRC «[мм:сс.xx] строка» — караоке, иначе обычный текст
class LyricLine(val ms: Long, val text: String)
fun parseLrc(s: String): List<LyricLine>? {
    val re = Regex("""\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?]""")
    val out = mutableListOf<LyricLine>()
    for (raw in s.lines()) {
        val ms = re.findAll(raw).toList()
        if (ms.isEmpty()) continue
        val text = raw.replace(re, "").trim()
        for (m in ms) {
            val mm = m.groupValues[1].toLong(); val ss = m.groupValues[2].toLong()
            val fr = m.groupValues[3]
            val frac = if (fr.isEmpty()) 0L else when (fr.length) { 1 -> fr.toLong() * 100; 2 -> fr.toLong() * 10; else -> fr.take(3).toLong() }
            out.add(LyricLine(mm * 60_000 + ss * 1000 + frac, text))
        }
    }
    return if (out.size >= 2) out.sortedBy { it.ms } else null
}

// Уведомления (колокольчик в шапке)
object Notifs {
    class N(val title: String, val text: String, val at: Long)
    val list = androidx.compose.runtime.mutableStateListOf<N>()
    var unread by androidx.compose.runtime.mutableIntStateOf(0)
    private lateinit var prefs: SharedPreferences
    fun init(ctx: Context) {
        prefs = ctx.getSharedPreferences("notifs", Context.MODE_PRIVATE)
        try {
            val a = JSONArray(prefs.getString("list", "[]"))
            for (i in 0 until a.length()) { val o = a.getJSONObject(i); list.add(N(o.optString("t"), o.optString("x"), o.optLong("at"))) }
        } catch (_: Exception) { }
        unread = prefs.getInt("unread", 0)
        if (!prefs.getBoolean("hello", false)) {
            prefs.edit().putBoolean("hello", true).apply()
            add("Добро пожаловать в SONORA", "Тот же аккаунт, что на компьютере: лайки, плейлисты и Plus синхронизируются.")
        }
    }
    fun add(title: String, text: String) {
        list.add(0, N(title, text, now()))
        while (list.size > 30) list.removeAt(list.size - 1)
        unread++
        save()
    }
    fun read() { unread = 0; save() }
    fun clear() { list.clear(); unread = 0; save() }
    private fun save() {
        val a = JSONArray(); for (n in list) a.put(JSONObject().put("t", n.title).put("x", n.text).put("at", n.at))
        prefs.edit().putString("list", a.toString()).putInt("unread", unread).apply()
    }
}

// Настройки приложения
object Prefs {
    private lateinit var p: SharedPreferences
    var liquid by androidx.compose.runtime.mutableStateOf(true)
    var openFull by androidx.compose.runtime.mutableStateOf(true)
    var welcomed = false
    fun init(ctx: Context) {
        p = ctx.getSharedPreferences("settings", Context.MODE_PRIVATE)
        liquid = p.getBoolean("liquid", true); openFull = p.getBoolean("openFull", true); welcomed = p.getBoolean("welcomed", false)
    }
    fun save() { p.edit().putBoolean("liquid", liquid).putBoolean("openFull", openFull).putBoolean("welcomed", welcomed).apply() }
}
