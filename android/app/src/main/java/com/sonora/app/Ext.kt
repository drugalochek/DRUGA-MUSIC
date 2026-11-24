package com.sonora.app

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap

// ---------- Подключённые музыкальные сервисы: Яндекс Музыка, Spotify, Deezer (как Services.cs на ПК) ----------
class ExtException(m: String) : Exception(m)
class ExtList(val id: String, val title: String, val count: Int, val cover: String?)
class ExtMix(val id: String, val name: String, val src: String, val tracks: List<Track>)

object Ext {
    const val SP_PORT = 43117
    const val SP_REDIRECT = "http://127.0.0.1:$SP_PORT/callback"
    private const val YA_CLIENT = "23cabbbdc6cd418abb4b39c32c41195d"
    private const val YA_SECRET = "53bc75238f0c4d08a118e51fe9203300"
    private const val YA_API = "https://api.music.yandex.net"
    private const val DZ_API = "https://api.deezer.com"
    private const val SP_API = "https://api.spotify.com/v1"

    var rev by mutableIntStateOf(0)
        private set
    private lateinit var prefs: SharedPreferences
    private val known = ConcurrentHashMap<String, Track>()
    private val match = ConcurrentHashMap<String, String>()     // id трека → где он нашёлся для звука (ya:… / dz:…)
    private val lists = ConcurrentHashMap<String, ExtMix>()
    private val tried = HashSet<String>()
    var yaLists: List<ExtList>? = null; private set
    var spLists: List<ExtList>? = null; private set
    var dzLists: List<ExtList>? = null; private set

    // состояние подключений
    var yaToken: String? = null; var yaUid: String? = null; var yaName: String? = null; var yaPlus = false
    var spClient: String? = null; var spAccess: String? = null; var spRefresh: String? = null; var spExpires = 0L; var spName: String? = null; var spProduct: String? = null
    var dzUser: String? = null; var dzName: String? = null

    val yaOn get() = !yaToken.isNullOrEmpty() && !yaUid.isNullOrEmpty()
    val spOn get() = !spRefresh.isNullOrEmpty()
    val dzOn get() = !dzUser.isNullOrEmpty()
    fun isExt(id: String) = id.startsWith("ya:") || id.startsWith("sp:") || id.startsWith("dz:")
    fun get(id: String): Track? = known[id]
    fun sourceName(src: String?) = when (src) { "ya" -> "Яндекс Музыка"; "sp" -> "Spotify"; "dz" -> "Deezer"; else -> "SONORA" }

    // ---------- хранение ----------
    fun init(ctx: Context) {
        prefs = ctx.getSharedPreferences("services", Context.MODE_PRIVATE)
        try {
            val j = JSONObject(prefs.getString("state", null) ?: return)
            yaToken = j.str("yaToken"); yaUid = j.str("yaUid"); yaName = j.str("yaName"); yaPlus = j.bool("yaPlus")
            spClient = j.str("spClient"); spAccess = j.str("spAccess"); spRefresh = j.str("spRefresh"); spExpires = j.optLong("spExpires"); spName = j.str("spName"); spProduct = j.str("spProduct")
            dzUser = j.str("dzUser"); dzName = j.str("dzName")
            val arr = j.optJSONArray("tracks") ?: JSONArray()
            for (i in 0 until arr.length()) {
                val x = arr.optJSONObject(i) ?: continue
                val id = x.str("id") ?: continue
                known[id] = mk(id, x.str("src") ?: id.substringBefore(':'), x.str("ref") ?: id.substringAfter(':'), x.str("title") ?: "—", x.str("artist") ?: "—",
                    x.str("album") ?: "", x.str("cover"), x.num("dur"), x.str("isrc"), x.str("genre") ?: "", x.num("year").toInt(), x.bool("explicit"))
                x.str("match")?.let { match[id] = it }
            }
        } catch (e: Exception) { Log.w("SONORA", "services load: $e") }
    }

    @Synchronized fun save() {
        if (!::prefs.isInitialized) return
        try {
            val used = HashSet<String>().apply { addAll(Lib.fav); addAll(Lib.recent); Lib.playlists.forEach { addAll(it.trackIds) }; Audio.queue.forEach { add(it.id) } }
            val arr = JSONArray()
            for (t in known.values) if (t.id in used) arr.put(JSONObject().put("id", t.id).put("src", t.src).put("ref", t.ref).put("title", t.title).put("artist", t.artist)
                .put("album", t.album).put("cover", t.cover ?: "").put("dur", t.duration).put("isrc", t.isrc ?: "").put("genre", t.genre).put("year", t.year)
                .put("explicit", t.explicit).put("match", match[t.id] ?: ""))
            val j = JSONObject().put("yaToken", yaToken).put("yaUid", yaUid).put("yaName", yaName).put("yaPlus", yaPlus)
                .put("spClient", spClient).put("spAccess", spAccess).put("spRefresh", spRefresh).put("spExpires", spExpires).put("spName", spName).put("spProduct", spProduct)
                .put("dzUser", dzUser).put("dzName", dzName).put("tracks", arr)
            prefs.edit().putString("state", j.toString()).apply()
        } catch (e: Exception) { Log.w("SONORA", "services save: $e") }
    }

    private fun fire() { save(); synchronized(tried) { tried.clear() }; appScope.launch { rev++; Lib.bump(); resolveMissing() } }

    private fun mk(id: String, src: String, ref: String, title: String, artist: String, album: String, cover: String?, dur: Double, isrc: String?,
                   genre: String = "", year: Int = 0, explicit: Boolean = false) =
        Track(id = id, title = title, artist = artist, album = album, genre = genre, lyrics = "", cover = cover?.ifEmpty { null }, ownerName = "", ownerId = null,
            year = year, duration = dur, plays = 0, addedAt = 0, verified = false, explicit = explicit, db = "ext", src = src, ref = ref, isrc = isrc?.ifEmpty { null })

    private fun remember(t: Track): Track {
        val old = known[t.id]
        val r = if (old != null && t.cover == null && old.cover != null) t.copy(cover = old.cover) else t
        known[t.id] = r
        return r
    }

    // ---------- сеть ----------
    class Resp(val code: Int, val body: String, val finalUrl: String) { val ok get() = code in 200..299 }

    fun req(method: String, url: String, headers: Map<String, String>? = null, form: String? = null): Resp {
        val c = try { URL(url).openConnection() as HttpURLConnection } catch (e: Exception) { throw ExtException("Неверный адрес") }
        try {
            c.requestMethod = method; c.connectTimeout = 15000; c.readTimeout = 30000
            c.setRequestProperty("User-Agent", "SONORA-Android/" + BuildConfig.VERSION_NAME)
            headers?.forEach { (k, v) -> c.setRequestProperty(k, v) }
            if (form != null) {
                c.doOutput = true
                c.setRequestProperty("Content-Type", "application/x-www-form-urlencoded")
                c.outputStream.use { it.write(form.toByteArray(Charsets.UTF_8)) }
            }
            val code = c.responseCode
            val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader(Charsets.UTF_8)?.use { it.readText() } ?: ""
            return Resp(code, text, c.url.toString())
        } catch (e: ExtException) { throw e
        } catch (e: java.net.SocketTimeoutException) { throw ExtException("Сервер слишком долго не отвечает")
        } catch (e: java.io.IOException) { throw ExtException("Нет соединения с сервером — проверь интернет")
        } finally { c.disconnect() }
    }
    fun form(vararg kv: String): String = kv.toList().chunked(2).joinToString("&") { enc(it[0]) + "=" + enc(it.getOrElse(1) { "" }) }
    private fun js(s: String): JSONObject? = try { JSONObject(s) } catch (_: Exception) { null }
    private fun JSONArray?.objs(): List<JSONObject> { val o = mutableListOf<JSONObject>(); if (this == null) return o; for (i in 0 until length()) optJSONObject(i)?.let { o.add(it) }; return o }
    private fun md5(s: String) = MessageDigest.getInstance("MD5").digest(s.toByteArray()).joinToString("") { "%02x".format(it) }
    private fun norm(s: String?): String {
        if (s.isNullOrEmpty()) return ""
        var x = s.lowercase().replace(Regex("\\(.*?\\)|\\[.*?\\]|feat\\..*$|ft\\..*$"), " ").replace('ё', 'е')
        x = x.replace(Regex("[^\\p{L}\\p{N}]+"), " ")
        return x.trim()
    }
    private fun best(cands: List<Track>, want: Track): Track? {
        val wt = norm(want.title); val wa = norm(want.artist.split(',')[0])
        var best: Track? = null; var bs = -1
        for (c in cands.take(10)) {
            val ct = norm(c.title); val ca = norm(c.artist)
            var sc = 0
            if (ct == wt) sc += 4 else if (ct.isNotEmpty() && wt.isNotEmpty() && (ct.contains(wt) || wt.contains(ct))) sc += 2
            if (wa.isNotEmpty() && ca.isNotEmpty() && (ca.contains(wa) || wa.contains(ca))) sc += 3
            if (want.duration > 0 && c.duration > 0 && Math.abs(want.duration - c.duration) < 6) sc += 1
            if (sc > bs) { bs = sc; best = c }
        }
        return if (bs >= 4) best else null
    }
    private fun q(t: Track) = (t.artist.split(',')[0] + " " + t.title).trim()

    // ================= Яндекс Музыка =================
    private fun yaHeaders(): Map<String, String> {
        val h = hashMapOf("X-Yandex-Music-Client" to "YandexMusicAndroid/24023621", "Accept-Language" to "ru")
        yaToken?.let { h["Authorization"] = "OAuth $it" }
        return h
    }
    private fun ya(method: String, path: String, form: String? = null): Any? {
        val r = req(method, if (path.startsWith("http")) path else YA_API + path, yaHeaders(), form)
        if (r.code == 401) throw ExtException("Вход в Яндекс Музыку устарел — подключи её заново")
        if (r.code == 451) throw ExtException("Яндекс Музыка недоступна в этом регионе")
        if (!r.ok) throw ExtException("Яндекс Музыка ответила ошибкой ${r.code}")
        return js(r.body)?.opt("result")
    }
    fun parseYaToken(s: String?): String? {
        if (s.isNullOrBlank()) return null
        Regex("access_token=([^&\\s#]+)").find(s)?.let { return java.net.URLDecoder.decode(it.groupValues[1], "UTF-8") }
        val t = s.trim()
        return if (Regex("^[A-Za-z0-9_\\-.]{20,}$").matches(t)) t else null
    }
    fun yaConnect(input: String) {
        val token = parseYaToken(input) ?: throw ExtException("Не нашли токен. Скопируй адрес страницы целиком — в нём есть access_token=…")
        val old = yaToken; yaToken = token
        try {
            val res = ya("GET", "/account/status") as? JSONObject ?: throw ExtException("Яндекс не принял токен")
            val acc = res.optJSONObject("account")
            val uid = acc?.str("uid")
            if (uid.isNullOrEmpty() || uid == "0") throw ExtException("Яндекс не принял токен — попробуй войти ещё раз")
            yaUid = uid
            yaName = acc.str("displayName") ?: acc.str("login") ?: acc.str("fullName") ?: "Яндекс"
            yaPlus = res.optJSONObject("plus")?.bool("hasPlus") ?: false
        } catch (e: Exception) { yaToken = old; throw e }
        yaLists = null
        fire()
    }
    class YaCode(val device: String, val user: String, val url: String, val interval: Int, val expires: Int)
    fun yaDeviceStart(): YaCode {
        val r = req("POST", "https://oauth.yandex.ru/device/code", null, form("client_id", YA_CLIENT, "device_name", "SONORA для Android"))
        val d = js(r.body)
        val dev = d?.str("device_code")
        if (!r.ok || dev.isNullOrEmpty()) throw ExtException("Яндекс не выдал код входа: " + (d?.str("error_description") ?: d?.str("error") ?: "HTTP ${r.code}"))
        return YaCode(dev, d.str("user_code") ?: "", d.str("verification_url") ?: "https://ya.ru/device", maxOf(2, d.num("interval").toInt()), maxOf(60, d.num("expires_in").toInt()))
    }
    suspend fun yaDeviceWait(c: YaCode) {
        val until = now() + c.expires * 1000L
        var iv = c.interval
        while (now() < until) {
            delay(iv * 1000L)
            val r = try { withContext(Dispatchers.IO) { req("POST", "https://oauth.yandex.ru/token", null, form("grant_type", "device_code", "code", c.device, "client_id", YA_CLIENT, "client_secret", YA_SECRET)) } } catch (_: ExtException) { continue }
            val d = js(r.body)
            val tok = d?.str("access_token")
            if (r.ok && !tok.isNullOrEmpty()) { withContext(Dispatchers.IO) { yaConnect(tok) }; return }
            when (d?.str("error")) {
                "authorization_pending" -> continue
                "slow_down" -> { iv += 2; continue }
                "expired_token" -> break
                "access_denied" -> throw ExtException("Вход в Яндекс отменён. Нажми «Получить новый код».")
                else -> throw ExtException("Яндекс отклонил вход: " + (d?.str("error_description") ?: d?.str("error") ?: "HTTP ${r.code}"))
            }
        }
        throw ExtException("Код устарел. Нажми «Получить новый код».")
    }
    fun yaDisconnect() { yaToken = null; yaUid = null; yaName = null; yaPlus = false; yaLists = null; lists.keys.filter { it.startsWith("ext:ya:") }.forEach { lists.remove(it) }; fire() }

    private fun yaTrack(t: JSONObject?): Track? {
        if (t == null) return null
        val id = t.str("id") ?: return null
        if (t.has("available") && !t.bool("available")) return null
        var title = t.str("title") ?: "—"
        t.str("version")?.let { if (it.isNotBlank()) title += " ($it)" }
        val artists = t.optJSONArray("artists").objs().mapNotNull { it.str("name") }.joinToString(", ")
        val album = t.optJSONArray("albums").objs().firstOrNull()
        val cover = t.str("coverUri") ?: album?.str("coverUri")
        return remember(mk("ya:$id", "ya", id, title, artists.ifEmpty { "—" }, album?.str("title") ?: "", if (cover.isNullOrEmpty()) null else "https://" + cover.replace("%%", "400x400"),
            t.num("durationMs") / 1000.0, null, album?.str("genre") ?: "", album?.num("year")?.toInt() ?: 0, t.str("contentWarning") == "explicit"))
    }
    fun yaSearch(q: String): List<Track> {
        val res = ya("GET", "/search?" + form("text", q, "type", "track", "page", "0", "nocorrect", "false")) as? JSONObject
        return res?.optJSONObject("tracks")?.optJSONArray("results").objs().mapNotNull { yaTrack(it) }
    }
    private fun yaByIds(ids: List<String>): List<Track> {
        val res = mutableListOf<Track>()
        for (chunk in ids.chunked(200)) {
            val r = ya("POST", "/tracks", form("track-ids", chunk.joinToString(","), "with-positions", "false")) as? JSONArray
            val map = HashMap<String, Track>()
            r.objs().mapNotNull { yaTrack(it) }.forEach { map[it.ref!!] = it }
            for (id in chunk) map[id.split(':')[0]]?.let { res.add(it) }
        }
        return res
    }
    fun yaPlaylists(): List<ExtList> {
        val r = ya("GET", "/users/$yaUid/playlists/list") as? JSONArray
        val list = r.objs().mapNotNull { p ->
            val kind = p.str("kind") ?: return@mapNotNull null
            var cover = p.optJSONObject("cover")?.str("uri")
            if (cover == null) cover = p.optJSONObject("cover")?.optJSONArray("itemsUri")?.optString(0)?.ifEmpty { null }
            if (cover == null) cover = p.str("ogImage")
            ExtList("ext:ya:pl:$kind", p.str("title") ?: "Плейлист", p.num("trackCount").toInt(), cover?.let { "https://" + it.replace("%%", "200x200") })
        }
        yaLists = list; return list
    }
    private fun yaLikes(): List<Track> {
        val r = ya("GET", "/users/$yaUid/likes/tracks") as? JSONObject
        val ids = r?.optJSONObject("library")?.optJSONArray("tracks").objs().mapNotNull { it.str("id") }.take(2000)
        return yaByIds(ids)
    }
    private fun yaPlaylist(kind: String): List<Track> {
        val r = ya("GET", "/users/$yaUid/playlists/$kind") as? JSONObject
        val res = mutableListOf<Track>(); val missing = mutableListOf<String>()
        for (x in r?.optJSONArray("tracks").objs()) {
            val t = yaTrack(x.optJSONObject("track"))
            if (t != null) res.add(t) else x.str("id")?.let { missing.add(it) }
        }
        if (res.isEmpty() && missing.isNotEmpty()) return yaByIds(missing)
        return res
    }
    private fun yaStream(id: String): String {
        val opts = (ya("GET", "/tracks/$id/download-info") as? JSONArray).objs()
        if (opts.isEmpty()) throw ExtException("Яндекс не отдал этот трек")
        val pick = opts.filter { it.optString("codec") == "mp3" }.sortedWith(compareBy<JSONObject> { if (it.bool("preview")) 1 else 0 }.thenByDescending { it.num("bitrateInKbps") }).firstOrNull() ?: opts[0]
        val x = req("GET", pick.optString("downloadInfoUrl"), yaHeaders())
        if (!x.ok) throw ExtException("Яндекс не отдал ссылку на трек (${x.code})")
        fun tag(n: String) = Regex("<$n>(.*?)</$n>", RegexOption.DOT_MATCHES_ALL).find(x.body)?.groupValues?.get(1) ?: ""
        val host = tag("host"); val path = tag("path"); val ts = tag("ts"); val s = tag("s")
        if (host.isEmpty() || path.isEmpty()) throw ExtException("Неожиданный ответ Яндекса")
        return "https://$host/get-mp3/" + md5("XGRlBW9FXlekgbPrRHuSiA" + path.substring(1) + s) + "/$ts$path"
    }

    // ================= Deezer =================
    private fun dz(path: String): JSONObject {
        val r = req("GET", if (path.startsWith("http")) path else DZ_API + path)
        if (!r.ok) throw ExtException("Deezer ответил ошибкой ${r.code}")
        val d = js(r.body) ?: throw ExtException("Deezer: неожиданный ответ")
        d.optJSONObject("error")?.let { throw ExtException(if (it.num("code").toInt() == 800) "Deezer: не найдено" else "Deezer: " + (it.str("message") ?: "ошибка")) }
        return d
    }
    private fun dzTrack(t: JSONObject?, albumOverride: JSONObject? = null): Track? {
        if (t == null) return null
        val id = t.str("id") ?: return null
        if (t.has("readable") && !t.bool("readable")) return null
        var al = t.optJSONObject("album") ?: albumOverride
        if (albumOverride != null && al?.str("cover_big") == null) al = albumOverride
        return remember(mk("dz:$id", "dz", id, t.str("title") ?: "—", t.optJSONObject("artist")?.str("name") ?: "—", al?.str("title") ?: "",
            al?.str("cover_big") ?: al?.str("cover_medium"), t.num("duration"), t.str("isrc"), explicit = t.bool("explicit_lyrics")))
    }
    fun dzSearch(q: String): List<Track> = dz("/search?" + form("q", q, "limit", "25")).optJSONArray("data").objs().mapNotNull { dzTrack(it) }
    private fun dzPaged(first: String, max: Int = 2000): List<Track> {
        val res = mutableListOf<Track>(); var next: String? = first
        while (next != null && res.size < max) { val d = dz(next); res.addAll(d.optJSONArray("data").objs().mapNotNull { dzTrack(it) }); next = d.str("next") }
        return res
    }
    // ссылка на профиль/плейлист/альбом Deezer → (тип, id)
    fun dzParseLink(link0: String?): Pair<String, String>? {
        val link = link0?.trim() ?: return null
        if (link.isEmpty()) return null
        val re = Regex("(playlist|profile|user|album|artist)/(\\d+)")
        var m = re.find(link)
        if (m == null && Regex("^\\d+$").matches(link)) return "profile" to link
        if (m == null && (link.contains("link.deezer.com") || link.contains("deezer.page.link"))) {
            try { val r = req("GET", link); m = re.find(r.finalUrl) ?: Regex("deezer\\.com/(?:\\w{2}/)?(playlist|profile|album|artist)/(\\d+)").find(r.body) } catch (_: Exception) { }
        }
        m ?: return null
        return (if (m.groupValues[1] == "user") "profile" else m.groupValues[1]) to m.groupValues[2]
    }
    fun dzConnect(userId: String) { val d = dz("/user/$userId"); dzUser = userId; dzName = d.str("name") ?: "Deezer $userId"; dzLists = null; fire() }
    fun dzDisconnect() { dzUser = null; dzName = null; dzLists = null; lists.keys.filter { it.startsWith("ext:dz:") }.forEach { lists.remove(it) }; fire() }
    fun dzPlaylists(): List<ExtList> {
        val l = dz("/user/$dzUser/playlists?limit=100").optJSONArray("data").objs().filter { !it.bool("is_loved_track") }
            .map { ExtList("ext:dz:pl:" + it.str("id"), it.str("title") ?: "Плейлист", it.num("nb_tracks").toInt(), it.str("picture_medium")) }
        dzLists = l; return l
    }

    // ================= Spotify (PKCE + приём кода на 127.0.0.1:43117, как на ПК) =================
    private var spPending: CompletableDeferred<String>? = null
    fun spCancel() { spPending?.completeExceptionally(ExtException("Подключение Spotify отменено")) }

    suspend fun spConnect(ctx: Context, clientId0: String) {
        spCancel()
        val clientId = clientId0.trim()
        if (!Regex("^[0-9a-fA-F]{32}$").matches(clientId)) throw ExtException("Client ID — это 32 символа (буквы a–f и цифры) из кабинета Spotify")
        val verifier = rnd64(48)
        val challenge = android.util.Base64.encodeToString(MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII)),
            android.util.Base64.URL_SAFE or android.util.Base64.NO_WRAP or android.util.Base64.NO_PADDING)
        val state = rnd64(16)
        val d = CompletableDeferred<String>()
        spPending = d
        val server = try { withContext(Dispatchers.IO) { LoopServer(SP_PORT) { r -> spHandle(r, state, d) } } }
            catch (e: Exception) { throw ExtException("Порт $SP_PORT занят — закрой другие плееры и попробуй снова") }
        KeepAliveService.start(ctx, "Подключение Spotify: разреши доступ в браузере")
        val code = try {
            val url = "https://accounts.spotify.com/authorize?" + form("client_id", clientId, "response_type", "code", "redirect_uri", SP_REDIRECT,
                "code_challenge_method", "S256", "code_challenge", challenge, "state", state,
                "scope", "user-library-read playlist-read-private playlist-read-collaborative user-read-private user-read-email")
            if (!openUrl(ctx, url)) throw ExtException("Не удалось открыть браузер")
            withTimeout(5 * 60_000L) { d.await() }
        } catch (e: TimeoutCancellationException) { throw ExtException("Время ожидания входа в Spotify истекло")
        } finally { server.close(); KeepAliveService.stop(ctx); if (spPending === d) spPending = null }
        withContext(Dispatchers.IO) {
            val tr = req("POST", "https://accounts.spotify.com/api/token", null, form("grant_type", "authorization_code", "code", code, "redirect_uri", SP_REDIRECT, "client_id", clientId, "code_verifier", verifier))
            val td = js(tr.body)
            if (!tr.ok || td?.str("access_token") == null) throw ExtException("Spotify не выдал доступ: " + (td?.str("error_description") ?: td?.str("error") ?: "HTTP ${tr.code}"))
            spClient = clientId; spAccess = td.str("access_token"); spRefresh = td.str("refresh_token"); spExpires = now() + (td.num("expires_in") * 1000).toLong()
            try { val me = sp("$SP_API/me") as? JSONObject; spName = me?.str("display_name") ?: me?.str("id") ?: "Spotify"; spProduct = me?.str("product") } catch (_: Exception) { spName = "Spotify" }
            spLists = null
            fire()
        }
    }
    private fun spHandle(r: LoopReq, state: String, d: CompletableDeferred<String>): LoopResp {
        val q = r.query
        if (!q.containsKey("code") && !q.containsKey("error")) return LoopResp("404 Not Found", "text/plain", "")
        val err = q["error"]
        when {
            err != null -> d.completeExceptionally(ExtException(if (err == "access_denied") "Подключение Spotify отменено" else "Spotify: $err"))
            q["state"] != state -> d.completeExceptionally(ExtException("Ответ Spotify не прошёл проверку — попробуй ещё раз"))
            else -> d.complete(q["code"]!!)
        }
        return LoopResp("200 OK", "text/html; charset=utf-8", if (err == null) doneHtml("Spotify подключён!", "Возвращайся в SONORA — лайки и плейлисты уже загружаются.")
            else doneHtml("Подключение отменено", "Можно вернуться в SONORA и попробовать ещё раз.", false))
    }
    fun spDisconnect() { spAccess = null; spRefresh = null; spExpires = 0; spName = null; spProduct = null; spLists = null; lists.keys.filter { it.startsWith("ext:sp:") }.forEach { lists.remove(it) }; fire() }
    @Synchronized private fun spToken(force: Boolean): String {
        if (!force && !spAccess.isNullOrEmpty() && spExpires > now() + 60_000) return spAccess!!
        if (spRefresh.isNullOrEmpty()) throw ExtException("Spotify не подключён")
        val r = req("POST", "https://accounts.spotify.com/api/token", null, form("grant_type", "refresh_token", "refresh_token", spRefresh!!, "client_id", spClient ?: ""))
        val d = js(r.body)
        if (!r.ok || d?.str("access_token") == null) {
            if (r.code == 400) { spDisconnect(); throw ExtException("Вход в Spotify устарел — подключи его заново") }
            throw ExtException("Spotify: не удалось обновить вход (${r.code})")
        }
        spAccess = d.str("access_token"); d.str("refresh_token")?.let { spRefresh = it }; spExpires = now() + (d.num("expires_in") * 1000).toLong()
        save()
        return spAccess!!
    }
    private fun sp(url: String): Any? {
        for (attempt in 0 until 3) {
            val r = req("GET", url, mapOf("Authorization" to "Bearer " + spToken(attempt > 0)))
            if (r.code == 401) continue
            if (r.code == 429) { Thread.sleep(1500); continue }
            if (r.code == 403) throw ExtException("Spotify запретил доступ. Проверь, что твой аккаунт добавлен в User Management приложения в кабинете Spotify")
            if (r.code == 404) return null
            if (!r.ok) throw ExtException("Spotify ответил ошибкой ${r.code}")
            return js(r.body)
        }
        throw ExtException("Spotify не отвечает — попробуй позже")
    }
    private fun spTrack(t: JSONObject?): Track? {
        if (t == null || (t.str("type") ?: "track") != "track") return null
        val id = t.str("id") ?: return null
        val al = t.optJSONObject("album")
        return remember(mk("sp:$id", "sp", id, t.str("name") ?: "—", t.optJSONArray("artists").objs().mapNotNull { it.str("name") }.joinToString(", ").ifEmpty { "—" },
            al?.str("name") ?: "", al?.optJSONArray("images").objs().firstOrNull()?.str("url"), t.num("duration_ms") / 1000.0,
            t.optJSONObject("external_ids")?.str("isrc"), explicit = t.bool("explicit")))
    }
    private fun spPaged(url0: String, max: Int = 2000): List<Track>? {
        val res = mutableListOf<Track>(); var url: String? = url0
        while (url != null && res.size < max) {
            val d = sp(url) as? JSONObject ?: return null
            for (it in d.optJSONArray("items").objs()) spTrack(it.optJSONObject("track") ?: it.optJSONObject("item"))?.let { t -> res.add(t) }
            url = d.str("next")
        }
        return res
    }
    fun spPlaylists(): List<ExtList> {
        val list = mutableListOf<ExtList>(); var url: String? = "$SP_API/me/playlists?limit=50"
        while (url != null && list.size < 500) {
            val d = sp(url) as? JSONObject ?: break
            for (p in d.optJSONArray("items").objs()) {
                var total = p.optJSONObject("tracks")?.num("total") ?: 0.0
                if (total <= 0) total = p.optJSONObject("items")?.num("total") ?: 0.0
                list.add(ExtList("ext:sp:pl:" + p.str("id"), p.str("name") ?: "Плейлист", total.toInt(), p.optJSONArray("images").objs().firstOrNull()?.str("url")))
            }
            url = d.str("next")
        }
        spLists = list; return list
    }

    // ================= списки =================
    fun cachedList(id: String): ExtMix? = lists[id]
    fun loadList(id: String, title: String? = null, force: Boolean = false): ExtMix {
        if (!force) lists[id]?.let { return it }
        val parts = id.split(':')         // ext:ya:pl:KIND
        val src = parts.getOrElse(1) { "" }; val kind = parts.getOrElse(2) { "" }; val key = parts.drop(3).joinToString(":")
        var name = title
        val tracks: List<Track>? = when (src) {
            "ya" -> {
                if (!yaOn) throw ExtException("Яндекс Музыка не подключена")
                if (name == null) name = if (kind == "likes") "Мне нравится" else yaLists?.firstOrNull { it.id == id }?.title ?: "Плейлист"
                if (kind == "likes") yaLikes() else yaPlaylist(key)
            }
            "sp" -> {
                if (!spOn) throw ExtException("Spotify не подключён")
                if (name == null) name = if (kind == "likes") "Любимые треки" else spLists?.firstOrNull { it.id == id }?.title ?: "Плейлист"
                if (kind == "likes") spPaged("$SP_API/me/tracks?limit=50") else spPaged("$SP_API/playlists/$key/items?limit=50") ?: spPaged("$SP_API/playlists/$key/tracks?limit=50")
            }
            "dz" -> when (kind) {
                "likes" -> { if (name == null) name = "Любимые треки"; dzPaged("/user/$dzUser/tracks?limit=100") }
                "al" -> { val al = dz("/album/$key"); if (name == null) name = al.str("title"); al.optJSONObject("tracks")?.optJSONArray("data").objs().mapNotNull { dzTrack(it, al) } }
                else -> { if (name == null) name = dz("/playlist/$key").str("title"); dzPaged("/playlist/$key/tracks?limit=100") }
            }
            else -> null
        }
        tracks ?: throw ExtException("Список не найден")
        val m = ExtMix(id, name ?: "Плейлист", src, tracks)
        lists[id] = m
        save()
        return m
    }

    // поиск по сервисам: Яндекс (если подключён) и Deezer (всегда, отрывки)
    private val searchCache = ConcurrentHashMap<String, Pair<List<Track>, List<Track>>>()
    fun cachedSearch(q: String) = searchCache[(if (yaOn) "y|" else "n|") + q]
    suspend fun searchAll(q: String): Pair<List<Track>, List<Track>> {
        val key = (if (yaOn) "y|" else "n|") + q
        searchCache[key]?.let { return it }
        val res = coroutineScope {
            val y = async(Dispatchers.IO) { if (yaOn) try { yaSearch(q) } catch (_: Exception) { emptyList() } else emptyList() }
            val d = async(Dispatchers.IO) { try { dzSearch(q) } catch (_: Exception) { emptyList() } }
            y.await() to d.await()
        }
        if (searchCache.size > 60) searchCache.clear()
        searchCache[key] = res
        return res
    }

    // лайки и плейлисты с ПК содержат треки сервисов (ya:/dz:/sp:) — догружаем их данные, чтобы они играли и на телефоне
    suspend fun resolveMissing() {
        val ids = synchronized(tried) {
            val all = LinkedHashSet<String>().apply { addAll(Lib.fav); addAll(Lib.recent); Lib.playlists.forEach { addAll(it.trackIds) } }
            all.filter { isExt(it) && Catalog.get(it) == null && known[it] == null && it !in tried }.also { tried.addAll(it) }
        }
        if (ids.isEmpty()) return
        var got = 0
        withContext(Dispatchers.IO) {
            val ya = ids.filter { it.startsWith("ya:") }.map { it.substring(3) }
            if (ya.isNotEmpty() && yaOn) try { got += yaByIds(ya).size } catch (e: Exception) { Log.w("SONORA", "resolve ya: $e") }
            val spIds = ids.filter { it.startsWith("sp:") }.map { it.substring(3) }
            if (spIds.isNotEmpty() && spOn) for (ch in spIds.chunked(50)) try {
                (sp("$SP_API/tracks?ids=" + ch.joinToString(",")) as? JSONObject)?.optJSONArray("tracks").objs().forEach { if (spTrack(it) != null) got++ }
            } catch (e: Exception) { Log.w("SONORA", "resolve sp: $e"); break }
            for (id in ids.filter { it.startsWith("dz:") }.take(400)) try { if (dzTrack(dz("/track/" + id.substring(3))) != null) got++ } catch (e: Exception) { if (e.message?.contains("соединени") == true) break }
        }
        if (got > 0) withContext(Dispatchers.Main) { save(); rev++; Lib.bump() }
    }

    // ================= откуда играть =================
    // Яндекс — целиком; Spotify/Deezer — тот же трек в Яндекс Музыке (если подключена), иначе отрывок Deezer или iTunes.
    fun playbackNote(t: Track?): String? {
        val s = t?.src ?: return null
        if (s == "ya") return if (yaPlus) null else "без Плюса — отрывок"
        return if (yaOn) "через Яндекс Музыку" else "отрывок 30 с"
    }

    fun streamUrl(id: String): String {
        val t = known[id] ?: throw ExtException("Трек сервиса не найден — открой его заново")
        val errs = mutableListOf<String>()
        if (t.src == "ya" || yaOn) {
            try {
                var yid = if (t.src == "ya") t.ref else match[id]?.takeIf { it.startsWith("ya:") }?.substring(3)
                if (yid == null) { best(yaSearch(q(t)), t)?.let { yid = it.ref; match[id] = "ya:${it.ref}" } }
                if (yid != null) return yaStream(yid!!)
                errs.add("Яндекс: не найден")
            } catch (e: Exception) { errs.add("Яндекс: " + e.message) }
        }
        try { dzPreview(t)?.let { return it } } catch (e: Exception) { errs.add("Deezer: " + e.message) }
        try { itPreview(t)?.let { return it } } catch (e: Exception) { errs.add("iTunes: " + e.message) }
        Log.w("SONORA", "play failed $id: " + errs.joinToString(" | "))
        if (errs.isNotEmpty() && errs.all { it.contains("соединени") || it.contains("долго") }) throw ExtException("Музыкальные сервисы недоступны — проверь интернет или VPN")
        throw ExtException(if (yaOn) "Этот трек не нашёлся ни в одном сервисе" else "Не нашли, откуда включить трек — подключи Яндекс Музыку")
    }
    private fun dzPreview(t: Track): String? {
        val did = match[t.id]?.takeIf { it.startsWith("dz:") }?.substring(3) ?: if (t.src == "dz") t.ref else null
        if (did != null) try { dz("/track/$did").str("preview")?.let { if (it.isNotEmpty()) return it } } catch (_: Exception) { }
        if (!t.isrc.isNullOrEmpty()) try {
            val d = dz("/track/isrc:" + t.isrc)
            d.str("preview")?.let { if (it.isNotEmpty()) { match[t.id] = "dz:" + d.str("id"); return it } }
        } catch (_: Exception) { }
        val prev = HashMap<String, String>()
        val list = dz("/search?" + form("q", q(t), "limit", "15")).optJSONArray("data").objs().mapNotNull { x ->
            val u = x.str("preview"); val id = x.str("id")
            if (u.isNullOrEmpty() || id == null) null else { prev[id] = u; mk("dz:$id", "dz", id, x.str("title") ?: "", x.optJSONObject("artist")?.str("name") ?: "", "", null, x.num("duration"), null) }
        }
        val m = best(list, t) ?: return null
        match[t.id] = "dz:" + m.ref
        return prev[m.ref]
    }
    private fun itPreview(t: Track): String? {
        val r = req("GET", "https://itunes.apple.com/search?" + form("term", q(t), "entity", "song", "limit", "15"))
        if (!r.ok) throw ExtException("ответ ${r.code}")
        val prev = HashMap<String, String>()
        val list = js(r.body)?.optJSONArray("results").objs().mapNotNull { x ->
            val u = x.str("previewUrl"); val id = x.str("trackId")
            if (u.isNullOrEmpty() || id == null) null else { prev[id] = u; mk("it:$id", "it", id, x.str("trackName") ?: "", x.str("artistName") ?: "", "", null, x.num("trackTimeMillis") / 1000, null) }
        }
        val m = best(list, t) ?: return null
        return prev[m.ref]
    }
}
