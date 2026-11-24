package com.sonora.app

import android.content.Context
import android.content.SharedPreferences
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Base64
import android.util.LruCache
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
fun now() = System.currentTimeMillis()
fun enc(s: String): String = URLEncoder.encode(s, "UTF-8")

// ---------------- Сеть ----------------
object Net {
    class Resp(val code: Int, val body: String) { val ok get() = code in 200..299 }

    fun send(method: String, url: String, body: String? = null, contentType: String = "application/json; charset=utf-8"): Resp {
        val c = URL(url).openConnection() as HttpURLConnection
        try {
            c.requestMethod = method
            c.connectTimeout = 15000
            c.readTimeout = 90000
            c.setRequestProperty("User-Agent", "SONORA-Android/" + BuildConfig.VERSION_NAME)
            if (body != null) {
                c.doOutput = true
                c.setRequestProperty("Content-Type", contentType)
                c.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            val code = c.responseCode
            val stream = if (code in 200..299) c.inputStream else c.errorStream
            val text = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() } ?: ""
            return Resp(code, text)
        } finally {
            c.disconnect()
        }
    }

    fun get(url: String): String {
        val r = send("GET", url)
        if (!r.ok) throw IOException("HTTP " + r.code)
        return r.body
    }
}

fun JSONObject.str(k: String): String? { val v = opt(k); return if (v == null || v == JSONObject.NULL) null else v.toString() }
fun JSONObject.num(k: String): Double = when (val v = opt(k)) { is Number -> v.toDouble(); is String -> v.toDoubleOrNull() ?: 0.0; else -> 0.0 }
fun JSONObject.bool(k: String): Boolean = when (val v = opt(k)) { is Boolean -> v; is String -> v == "true"; is Number -> v.toInt() != 0; else -> false }
fun JSONArray?.strings(): MutableList<String> {
    val out = mutableListOf<String>()
    if (this == null) return out
    for (i in 0 until length()) { val s = optString(i, ""); if (s.isNotEmpty()) out.add(s) }
    return out
}

// ---------------- Модели ----------------
data class Track(
    val id: String, val title: String, val artist: String, val album: String, val genre: String,
    val lyrics: String, val cover: String?, val ownerName: String, val ownerId: String?,
    val year: Int, val duration: Double, val plays: Long, val addedAt: Long,
    val verified: Boolean, val explicit: Boolean, val db: String,
    val src: String? = null, val ref: String? = null, val isrc: String? = null,   // трек из сервиса: ya / sp / dz
) {
    // «A feat. B», «A & B», «A, B» — трек засчитывается каждому исполнителю
    val hasLyrics: Boolean get() = lyrics.isNotBlank()
    val artists: List<String> get() = artist.split(Regex("\\s*(,|&| feat\\.? | ft\\.? )\\s*", RegexOption.IGNORE_CASE)).map { it.trim() }.filter { it.isNotEmpty() }.ifEmpty { listOf(artist) }
}

// трек по id: каталог SONORA или подключённые сервисы (ya:/sp:/dz:)
fun trackOf(id: String): Track? = Catalog.get(id) ?: Ext.get(id)

class Playlist(val id: String, var name: String, var color: String, val trackIds: MutableList<String>, var cover: String?)

// ---------------- Аккаунт (Firebase Auth через REST — те же аккаунты, что на ПК) ----------------
class AuthException(msg: String) : Exception(msg)

object Auth {
    private const val API_KEY = "AIzaSyCgBtQNo-cebuAoJ7u9vkOGHHpHI0DxSWQ"
    const val DB = "https://sonora-ab012-default-rtdb.firebaseio.com"

    class Session(val uid: String, val email: String, var name: String, var idToken: String, var refreshToken: String, var expiresAt: Long,
                  var photo: String = "", var bio: String = "", var color: String = "", var provider: String = "password")

    var current by mutableStateOf<Session?>(null)
        private set
    var rev by mutableIntStateOf(0)        // имя/фото/описание профиля изменились
    private lateinit var prefs: SharedPreferences

    fun init(ctx: Context) {
        prefs = ctx.getSharedPreferences("auth", Context.MODE_PRIVATE)
        val raw = prefs.getString("session", null) ?: return
        try {
            val j = JSONObject(raw)
            current = Session(j.getString("uid"), j.optString("email"), j.optString("name"), j.optString("idToken"), j.optString("refreshToken"), j.optLong("expiresAt"),
                j.optString("photo"), j.optString("bio"), j.optString("color"), j.optString("provider").ifEmpty { "password" })
        } catch (_: Exception) { }
    }

    private fun persist() {
        val s = current
        if (s == null) { prefs.edit().remove("session").apply(); return }
        prefs.edit().putString("session", JSONObject().put("uid", s.uid).put("email", s.email).put("name", s.name)
            .put("idToken", s.idToken).put("refreshToken", s.refreshToken).put("expiresAt", s.expiresAt)
            .put("photo", s.photo).put("bio", s.bio).put("color", s.color).put("provider", s.provider).toString()).apply()
    }

    private fun itk(m: String) = "https://identitytoolkit.googleapis.com/v1/accounts:$m?key=$API_KEY"

    private fun human(code: String?): String {
        val key = (code ?: "").trim().split(' ', ':').firstOrNull() ?: ""
        return when (key) {
            "EMAIL_NOT_FOUND", "INVALID_PASSWORD", "INVALID_LOGIN_CREDENTIALS" -> "Неверная почта или пароль."
            "USER_DISABLED" -> "Аккаунт отключён."
            "EMAIL_EXISTS" -> "Эта почта уже зарегистрирована. Попробуй войти."
            "INVALID_EMAIL" -> "Некорректная почта."
            "WEAK_PASSWORD" -> "Слишком простой пароль — минимум 6 символов."
            "MISSING_PASSWORD" -> "Введи пароль."
            "MISSING_EMAIL" -> "Введи почту."
            "TOO_MANY_ATTEMPTS_TRY_LATER" -> "Слишком много попыток. Попробуй позже."
            "TOKEN_EXPIRED", "INVALID_REFRESH_TOKEN", "USER_NOT_FOUND" -> "Сессия устарела — войди снова."
            "" -> "Не удалось выполнить запрос."
            else -> key
        }
    }

    private fun errorOf(body: String): String? = try {
        val j = JSONObject(body)
        j.optJSONObject("error")?.optString("message") ?: j.optString("error_description").ifEmpty { j.optString("error") }
    } catch (_: Exception) { null }

    private fun call(method: String, payload: JSONObject): JSONObject {
        val r = try { Net.send("POST", itk(method), payload.toString()) } catch (e: Exception) { throw AuthException("Нет соединения с сервером: " + e.message) }
        if (!r.ok) throw AuthException(human(errorOf(r.body)))
        return JSONObject(r.body)
    }

    private fun fromResp(d: JSONObject): Session {
        val exp = d.num("expiresIn").let { if (it > 0) it else 3600.0 }
        return Session(d.getString("localId"), d.optString("email"), d.str("displayName") ?: "", d.getString("idToken"), d.getString("refreshToken"), now() + (exp * 1000).toLong())
    }

    // профиль общий с ПК: имя, фото, описание и цвет
    private fun loadProfileName(s: Session) {
        try {
            val r = Net.send("GET", "$DB/users/${enc(s.uid)}/profile.json?auth=${enc(s.idToken)}")
            if (r.ok && r.body.trim().startsWith("{")) {
                val d = JSONObject(r.body)
                val n = d.str("name")
                if (!n.isNullOrBlank()) s.name = n
                d.str("photo")?.let { if (it.isNotBlank()) s.photo = it }
                d.str("bio")?.let { s.bio = it }
                d.str("color")?.let { s.color = it }
            }
        } catch (_: Exception) { }
        if (s.name.isBlank()) s.name = s.email.substringBefore('@')
    }

    suspend fun loadProfile() {
        val s = current ?: return
        try { token() } catch (_: Exception) { return }
        withContext(Dispatchers.IO) { loadProfileName(s) }
        persist(); rev++
    }

    suspend fun saveProfile(name: String, bio: String, color: String): Boolean {
        val s = current ?: return false
        val tok = try { token() } catch (_: Exception) { null } ?: return false
        val body = JSONObject().put("name", name.trim()).put("bio", bio.trim()).put("color", color).put("email", s.email)
            .put("updatedAt", now()).put("client", "SONORA Android").toString()
        val r = try { withContext(Dispatchers.IO) { Net.send("PATCH", "$DB/users/${enc(s.uid)}/profile.json?auth=${enc(tok)}", body) } } catch (_: Exception) { null }
        if (r == null || !r.ok) return false
        s.name = name.trim().ifEmpty { s.name }; s.bio = bio.trim(); s.color = color
        persist(); rev++
        return true
    }

    suspend fun signIn(email: String, pass: String) {
        val s = withContext(Dispatchers.IO) {
            val s = fromResp(call("signInWithPassword", JSONObject().put("email", email.trim()).put("password", pass).put("returnSecureToken", true)))
            loadProfileName(s); s
        }
        current = s; persist()
        Lib.onLogin()
    }

    suspend fun signUp(name: String, email: String, pass: String) {
        val s = withContext(Dispatchers.IO) {
            val s = fromResp(call("signUp", JSONObject().put("email", email.trim()).put("password", pass).put("returnSecureToken", true)))
            s.name = name.trim().ifEmpty { email.substringBefore('@') }
            try { call("update", JSONObject().put("idToken", s.idToken).put("displayName", s.name).put("returnSecureToken", false)) } catch (_: Exception) { }
            try { Net.send("PUT", "$DB/users/${enc(s.uid)}/profile/name.json?auth=${enc(s.idToken)}", JSONObject.quote(s.name)) } catch (_: Exception) { }
            s
        }
        current = s; persist()
        Lib.onLogin()
    }

    // Вход и регистрация через Google: токен Google (id_token или access_token) меняем на аккаунт SONORA.
    // Если аккаунта с этой почтой ещё нет — Firebase создаёт его сам, имя и фото берём из Google.
    suspend fun signInGoogle(idToken: String?, accessToken: String?): Boolean {
        val post = when {
            !idToken.isNullOrBlank() -> "id_token=" + enc(idToken)
            !accessToken.isNullOrBlank() -> "access_token=" + enc(accessToken)
            else -> throw AuthException("Google не вернул данные для входа. Попробуй ещё раз.")
        }
        var isNew = false
        val s = withContext(Dispatchers.IO) {
            val d = call("signInWithIdp", JSONObject().put("postBody", "$post&providerId=google.com").put("requestUri", "http://localhost")
                .put("returnIdpCredential", true).put("returnSecureToken", true))
            if (d.optString("errorMessage").isNotEmpty()) throw AuthException(human(d.optString("errorMessage")))
            isNew = d.bool("isNewUser")
            val s = fromResp(d)
            s.provider = "google"
            val gName = d.str("displayName") ?: d.str("fullName") ?: ""
            val gPhoto = d.str("photoUrl") ?: ""
            loadProfileName(s)
            // новый аккаунт или профиль без имени — заполняем из Google, чтобы на ПК было то же самое
            val hasName = s.name.isNotBlank() && s.name != s.email.substringBefore('@')
            if (isNew || !hasName) {
                if (gName.isNotBlank()) s.name = gName
                if (s.photo.isBlank() && gPhoto.isNotBlank()) s.photo = gPhoto
                try {
                    val body = JSONObject().put("name", s.name).put("email", s.email).put("updatedAt", now()).put("client", "SONORA Android")
                    if (s.photo.isNotBlank()) body.put("photo", s.photo)
                    Net.send("PATCH", "$DB/users/${enc(s.uid)}/profile.json?auth=${enc(s.idToken)}", body.toString())
                } catch (_: Exception) { }
            }
            if (s.name.isBlank()) s.name = gName.ifBlank { s.email.substringBefore('@') }
            s
        }
        current = s; persist(); rev++
        Lib.onLogin()
        return isNew
    }

    suspend fun reset(email: String) {
        withContext(Dispatchers.IO) { call("sendOobCode", JSONObject().put("requestType", "PASSWORD_RESET").put("email", email.trim())) }
    }

    fun signOut() {
        current = null; persist()
        Plus.until = 0
    }

    suspend fun token(): String? {
        val s = current ?: return null
        if (now() < s.expiresAt - 120_000) return s.idToken
        return withContext(Dispatchers.IO) {
            val r = Net.send("POST", "https://securetoken.googleapis.com/v1/token?key=$API_KEY",
                "grant_type=refresh_token&refresh_token=" + enc(s.refreshToken), "application/x-www-form-urlencoded")
            if (!r.ok) throw AuthException(human(errorOf(r.body)))
            val d = JSONObject(r.body)
            s.idToken = d.getString("id_token")
            s.refreshToken = d.str("refresh_token") ?: s.refreshToken
            s.expiresAt = now() + (maxOf(300.0, d.num("expires_in")) * 1000).toLong()
            persist()
            s.idToken
        }
    }
}

// ---------------- Каталог ----------------
object Catalog {
    const val SONIC = "https://sonic-563fa-default-rtdb.firebaseio.com"
    const val USER = Auth.DB + "/catalog"

    var tracks by mutableStateOf<List<Track>>(emptyList())
        private set
    var loaded by mutableStateOf(false)
        private set
    var error by mutableStateOf<String?>(null)
        private set
    private var byId = mapOf<String, Track>()
    private lateinit var dir: File

    fun init(ctx: Context) { dir = File(ctx.filesDir, "cache").apply { mkdirs() } }

    fun get(id: String): Track? = byId[id]

    private fun cached(url: String, name: String): String? {
        val f = File(dir, name)
        return try { Net.get(url).also { f.writeText(it) } } catch (e: Exception) { if (f.exists()) f.readText() else null }
    }

    private fun parse(body: String?, db: String, out: MutableList<Track>) {
        if (body.isNullOrBlank() || !body.trim().startsWith("{")) return
        val d = JSONObject(body)
        for (k in d.keys()) {
            val t = d.optJSONObject(k) ?: continue
            out.add(Track(
                id = k, title = t.str("title") ?: "—", artist = t.str("artist") ?: "—", album = t.str("album") ?: "",
                genre = t.str("genre") ?: "", lyrics = t.str("lyrics") ?: "", cover = t.str("cover"),
                ownerName = t.str("ownerName") ?: "", ownerId = t.str("ownerId"), year = t.num("year").toInt(),
                duration = t.num("duration"), plays = t.num("plays").toLong(), addedAt = t.num("addedAt").toLong(),
                verified = t.bool("verified"), explicit = t.bool("explicit"), db = db,
            ))
        }
    }

    suspend fun load() {
        try {
            val list = withContext(Dispatchers.IO) {
                val a = cached("$SONIC/tracks.json", "tracks.json")
                val b = cached("$USER/tracks.json", "tracks-sonora.json")
                val l = mutableListOf<Track>()
                parse(a, SONIC, l)
                val own = mutableListOf<Track>()
                parse(b, USER, own)
                val ids = l.map { it.id }.toHashSet()
                l.addAll(own.filter { it.id !in ids })
                l.sortedByDescending { it.addedAt }
            }
            byId = list.associateBy { it.id }
            tracks = list
            error = if (list.isEmpty()) "Не удалось загрузить каталог. Проверь интернет." else null
        } catch (e: Exception) {
            error = "Ошибка каталога: " + e.message
        }
        loaded = true
    }

    fun artists(): List<String> = tracks.flatMap { it.artists }.groupingBy { it }.eachCount().entries.sortedByDescending { it.value }.map { it.key }
    fun byArtist(a: String) = tracks.filter { t -> t.artists.any { it.equals(a, true) } }
    fun genres(): List<String> = tracks.flatMap { splitGenres(it.genre) }.distinct()
    fun splitGenres(g: String) = g.split(',', '/', ';').map { it.trim().trimEnd('.').replaceFirstChar { c -> c.uppercase() } }.filter { it.isNotEmpty() }
    fun search(q: String): List<Track> {
        val s = q.trim().lowercase()
        if (s.isEmpty()) return emptyList()
        return tracks.filter { it.title.lowercase().contains(s) || it.artist.lowercase().contains(s) || it.album.lowercase().contains(s) || it.genre.lowercase().contains(s) }
    }

    // Аудио хранится в базе как base64 (большие треки — частями). Скачиваем один раз в кэш.
    fun audioFile(ctx: Context, db: String, id: String): File {
        val adir = File(ctx.cacheDir, "audio").apply { mkdirs() }
        val safe = id.map { if (it.isLetterOrDigit() || it == '-' || it == '_') it else '_' }.joinToString("")
        adir.listFiles()?.firstOrNull { it.name.startsWith("$safe.") && !it.name.endsWith(".tmp") }?.let { return it }
        val body = Net.get("$db/audio/${enc(id)}.json")
        if (!body.trim().startsWith("{")) throw IOException("У трека нет аудио")
        val d = JSONObject(body)
        var data = d.str("data") ?: ""
        val parts = d.num("parts").toInt()
        if (data.isEmpty() && parts > 0) {
            val sb = StringBuilder()
            for (i in 0 until parts) sb.append(d.str("c$i") ?: throw IOException("Трек загружен не полностью"))
            data = sb.toString()
        }
        if (data.isEmpty()) throw IOException("У трека нет аудио")
        val comma = data.indexOf(',')
        if (data.startsWith("data:") && comma > 0) data = data.substring(comma + 1)
        val bytes = Base64.decode(data, Base64.DEFAULT)
        val mime = (d.str("mime") ?: "").lowercase()
        val ext = when {
            "wav" in mime -> "wav"; "flac" in mime -> "flac"; "ogg" in mime -> "ogg"; "webm" in mime -> "webm"
            "aac" in mime -> "aac"; "mp4" in mime || "m4a" in mime -> "m4a"; else -> "mp3"
        }
        val f = File(adir, "$safe.$ext")
        val tmp = File(adir, "$safe.$ext.tmp")
        tmp.writeBytes(bytes)
        tmp.renameTo(f)
        return f
    }
}

// ---------------- Обложки и цвет под обложку ----------------
object Covers {
    private val cache = object : LruCache<String, ImageBitmap>(48) {}
    private val colors = HashMap<String, List<Color>>()
    private val bytesCache = object : LruCache<String, ByteArray>(8) {}

    fun bytes(t: Track): ByteArray? {
        val c = t.cover ?: return null
        bytesCache.get(t.id)?.let { return it }
        val b = try {
            if (c.startsWith("data:")) Base64.decode(c.substringAfter(','), Base64.DEFAULT)
            else if (c.startsWith("http")) URL(c).openStream().use { it.readBytes() } else null
        } catch (_: Exception) { null }
        if (b != null) bytesCache.put(t.id, b)
        return b
    }

    fun peek(t: Track): ImageBitmap? = cache.get(t.id)

    suspend fun get(t: Track): ImageBitmap? {
        cache.get(t.id)?.let { return it }
        return withContext(Dispatchers.IO) {
            val b = bytes(t) ?: return@withContext null
            val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeByteArray(b, 0, b.size, opts)
            var sample = 1
            while (opts.outWidth / (sample * 2) >= 600) sample *= 2
            val bmp = BitmapFactory.decodeByteArray(b, 0, b.size, BitmapFactory.Options().apply { inSampleSize = sample }) ?: return@withContext null
            val pal = dominant(bmp)
            synchronized(colors) { colors[t.id] = pal }
            rev++
            bmp.asImageBitmap().also { cache.put(t.id, it) }
        }
    }

    fun color(t: Track?): Color? = palette(t)?.get(0)
    fun palette(t: Track?): List<Color>? = if (t == null) null else synchronized(colors) { colors[t.id] }
    var rev by mutableIntStateOf(0)
        private set

    val Fallback = listOf(Color(0xFF3B82F6), Color(0xFF38BDF8), Color(0xFF1E3A8A))

    // Палитра обложки как на ПК: основной цвет, второй оттенок и третий (12 корзин по тону)
    private fun dominant(src: Bitmap): List<Color> {
        val bmp = Bitmap.createScaledBitmap(src, 36, 36, true)
        val bins = 12
        val ws = DoubleArray(bins); val rs = DoubleArray(bins); val gs = DoubleArray(bins); val bs = DoubleArray(bins)
        var ar = 0.0; var ag = 0.0; var ab = 0.0; var an = 0
        for (x in 0 until 36) for (y in 0 until 36) {
            val p = bmp.getPixel(x, y)
            val r = p shr 16 and 255; val g = p shr 8 and 255; val b = p and 255
            ar += r; ag += g; ab += b; an++
            val hsl = Pal.hsl(r, g, b)
            if (hsl[2] < .08f || hsl[2] > .94f) continue
            val wt = (.12 + hsl[1]) * (1 - Math.abs(hsl[2] - .5) * 1.1) + .01
            val bin = (hsl[0] / 360f * bins).toInt() % bins
            ws[bin] += wt; rs[bin] += r * wt; gs[bin] += g * wt; bs[bin] += b * wt
        }
        val order = (0 until bins).sortedByDescending { ws[it] }
        val o0 = order[0]
        fun col(i: Int) = Color((rs[i] / ws[i]).toInt(), (gs[i] / ws[i]).toInt(), (bs[i] / ws[i]).toInt())
        fun far(i: Int) = minOf(Math.abs(i - o0), bins - Math.abs(i - o0)) >= 2
        if (ws[o0] > 0.05) {
            val c1 = col(o0)
            val second = order.drop(1).firstOrNull { ws[it] > ws[o0] * .12 && far(it) }
            val c2 = if (second != null && ws[second] > 0) col(second) else Pal.shift(c1, 28f)
            val third = order.drop(1).firstOrNull { it != second && ws[it] > ws[o0] * .08 && far(it) }
            val c3 = if (third != null && ws[third] > 0) col(third) else Pal.shift(c1, -24f)
            return listOf(Pal.natural(c1), Pal.natural(c2), Pal.natural(c3))
        }
        if (an == 0) return Fallback
        val g0 = Pal.natural(Color((ar / an).toInt(), (ag / an).toInt(), (ab / an).toInt()))
        return listOf(g0, Pal.mix(g0, Color.White, .18f), Pal.mix(g0, Color.Black, .35f))
    }
}

// Цветовая математика палитры (как Palette на ПК)
object Pal {
    fun hsl(r: Int, g: Int, b: Int): FloatArray {
        val R = r / 255f; val G = g / 255f; val B = b / 255f
        val mx = maxOf(R, G, B); val mn = minOf(R, G, B)
        val l = (mx + mn) / 2
        if (mx == mn) return floatArrayOf(0f, 0f, l)
        val d = mx - mn
        val s = if (l > .5f) d / (2 - mx - mn) else d / (mx + mn)
        var h = when (mx) { R -> (G - B) / d + (if (G < B) 6 else 0); G -> (B - R) / d + 2; else -> (R - G) / d + 4 }
        h *= 60
        return floatArrayOf(h, s, l)
    }
    fun hsl(c: Color) = hsl((c.red * 255).toInt(), (c.green * 255).toInt(), (c.blue * 255).toInt())
    fun fromHsl(h0: Float, s: Float, l: Float, a: Float = 1f): Color {
        val h = (((h0 % 360) + 360) % 360) / 360f
        fun f(p: Float, q: Float, t0: Float): Float {
            var t = t0; if (t < 0) t += 1; if (t > 1) t -= 1
            return when { t < 1f / 6 -> p + (q - p) * 6 * t; t < .5f -> q; t < 2f / 3 -> p + (q - p) * (2f / 3 - t) * 6; else -> p }
        }
        if (s == 0f) return Color(l, l, l, a)
        val q = if (l < .5f) l * (1 + s) else l + s - l * s
        val p = 2 * l - q
        return Color(f(p, q, h + 1f / 3).coerceIn(0f, 1f), f(p, q, h).coerceIn(0f, 1f), f(p, q, h - 1f / 3).coerceIn(0f, 1f), a)
    }
    fun natural(c: Color): Color { val x = hsl(c); return fromHsl(x[0], minOf(.9f, x[1] * 1.08f), x[2].coerceIn(.32f, .64f)) }
    fun surface(c: Color, l: Float): Color { val x = hsl(c); return fromHsl(x[0], minOf(.78f, x[1] * 1.05f), l) }
    fun light(c: Color): Color { val x = hsl(c); return fromHsl(x[0], x[1].coerceIn(.45f, .9f), .72f) }
    fun shift(c: Color, deg: Float): Color { val x = hsl(c); return fromHsl(x[0] + deg, x[1], x[2]) }
    fun mix(a: Color, b: Color, k: Float) = Color(a.red + (b.red - a.red) * k, a.green + (b.green - a.green) * k, a.blue + (b.blue - a.blue) * k, 1f)
}

// ---------------- Медиатека (лайки, плейлисты, история) + синхронизация с ПК ----------------
object Lib {
    var rev by mutableIntStateOf(0)
        private set
    val fav = mutableListOf<String>()
    val favArtists = mutableListOf<String>()
    val recent = mutableListOf<String>()
    val playlists = mutableListOf<Playlist>()
    var updatedAt = 0L
    var syncStatus by mutableStateOf("")
    private lateinit var prefs: SharedPreferences
    val plColors = listOf("#3b82f6", "#1d4ed8", "#60a5fa", "#2563eb", "#0284c7", "#1e3a8a", "#93c5fd", "#1e40af")

    fun init(ctx: Context) {
        prefs = ctx.getSharedPreferences("library", Context.MODE_PRIVATE)
        try {
            val raw = prefs.getString("data", null)
            if (raw != null) apply(JSONObject(raw), prefs.getLong("updatedAt", 0L), save = false)
        } catch (_: Exception) { }
    }

    fun portable(): JSONObject {
        val pls = JSONArray()
        for (p in playlists) pls.put(JSONObject().put("id", p.id).put("name", p.name).put("color", p.color).put("trackIds", JSONArray(p.trackIds)).put("cover", p.cover ?: ""))
        return JSONObject().put("fav", JSONArray(fav)).put("favArtists", JSONArray(favArtists)).put("recent", JSONArray(recent)).put("playlists", pls)
    }

    fun apply(d: JSONObject, at: Long, save: Boolean = true) {
        if (d.has("fav")) { fav.clear(); fav.addAll(d.optJSONArray("fav").strings()) }
        if (d.has("favArtists")) { favArtists.clear(); favArtists.addAll(d.optJSONArray("favArtists").strings()) }
        if (d.has("recent")) { recent.clear(); recent.addAll(d.optJSONArray("recent").strings()) }
        if (d.has("playlists")) {
            playlists.clear()
            val arr = d.optJSONArray("playlists") ?: JSONArray()
            for (i in 0 until arr.length()) {
                val x = arr.optJSONObject(i) ?: continue
                playlists.add(Playlist(x.str("id") ?: "pl_${now()}_$i", x.str("name") ?: "Плейлист", x.str("color") ?: plColors[0],
                    x.optJSONArray("trackIds").strings(), x.str("cover")?.ifEmpty { null }))
            }
        }
        updatedAt = at
        if (save) save()
        rev++
    }

    private fun save() {
        prefs.edit().putString("data", portable().toString()).putLong("updatedAt", updatedAt).apply()
    }

    private fun changed(push: Boolean = true) {
        updatedAt = now(); save(); rev++
        if (push) Cloud.schedule()
    }

    fun touch() = changed()
    fun bump() { rev++ }      // данные треков догрузились (например, из сервисов) — перерисовать списки
    fun isFav(id: String) = fav.contains(id)
    fun toggleFav(id: String) { if (!fav.remove(id)) fav.add(0, id); changed() }
    fun setFavAll(ids: List<String>, on: Boolean): Int {
        var n = 0
        if (on) { for (id in ids.reversed()) if (!fav.contains(id)) { fav.add(0, id); n++ } } else { for (id in ids) if (fav.remove(id)) n++ }
        if (n > 0) changed()
        return n
    }
    fun isFavArtist(a: String) = favArtists.any { it.equals(a, true) }
    fun toggleFavArtist(a: String) { if (favArtists.removeAll { it.equals(a, true) }.not()) favArtists.add(0, a); changed() }
    fun addRecent(id: String) { recent.remove(id); recent.add(0, id); while (recent.size > 100) recent.removeAt(recent.size - 1); changed() }

    fun playlist(id: String) = playlists.firstOrNull { it.id == id }
    fun createPlaylist(name: String): Playlist {
        val p = Playlist("pl_" + now(), name.ifBlank { "Мой плейлист ${playlists.size + 1}" }, plColors[playlists.size % plColors.size], mutableListOf(), null)
        playlists.add(p); changed(); return p
    }
    fun addTo(p: Playlist, id: String): Boolean { if (p.trackIds.contains(id)) return false; p.trackIds.add(id); changed(); return true }
    fun removeFrom(p: Playlist, id: String) { if (p.trackIds.remove(id)) changed() }
    fun rename(p: Playlist, name: String) { if (name.isNotBlank()) { p.name = name.trim(); changed() } }
    fun delete(p: Playlist) { playlists.remove(p); changed() }

    fun onLogin() { appScope.launch { Cloud.syncNow(); Plus.refresh() } }
}

object Cloud {
    private var job: Job? = null

    private suspend fun url(): String? {
        val s = Auth.current ?: return null
        val tok = Auth.token() ?: return null
        return "${Auth.DB}/users/${enc(s.uid)}/sonora_v2.json?auth=${enc(tok)}"
    }

    fun schedule() {
        if (Auth.current == null) return
        job?.cancel()
        job = appScope.launch { delay(2000); push() }
    }

    suspend fun push() {
        try {
            val u = url() ?: return
            Lib.syncStatus = "Синхронизация…"
            val body = JSONObject().put("data", Lib.portable().toString()).put("updatedAt", Lib.updatedAt).put("device", "android").toString()
            val r = withContext(Dispatchers.IO) { Net.send("PUT", u, body) }
            Lib.syncStatus = if (r.ok) "Синхронизировано" else "Ошибка синхронизации"
        } catch (e: Exception) { Lib.syncStatus = "Нет связи с облаком" }
    }

    suspend fun syncNow() {
        try {
            val u = url() ?: return
            Lib.syncStatus = "Синхронизация…"
            val r = withContext(Dispatchers.IO) { Net.send("GET", u) }
            if (r.ok && r.body.trim().startsWith("{")) {
                val d = JSONObject(r.body)
                val at = d.num("updatedAt").toLong()
                val data = d.str("data")
                if (!data.isNullOrEmpty() && at > Lib.updatedAt) {
                    Lib.apply(JSONObject(data), at)
                    Lib.syncStatus = "Синхронизировано"
                    return
                }
            }
            push()
        } catch (e: Exception) { Lib.syncStatus = "Нет связи с облаком" }
    }
}

// ---------------- SONORA Plus (тот же сервер и та же оплата, что на ПК) ----------------
object Plus {
    const val PRICE = 49
    const val DAYS = 30
    private const val WALLET = "4100117325240454"
    var until by mutableLongStateOf(0L)
    val active get() = Auth.current != null && until > now()
    private var pollJob: Job? = null

    suspend fun refresh() {
        val s = Auth.current ?: run { until = 0; return }
        try {
            val tok = Auth.token() ?: return
            val r = withContext(Dispatchers.IO) { Net.send("GET", "${Auth.DB}/plus/${enc(s.uid)}.json?auth=${enc(tok)}") }
            if (r.ok) until = if (r.body.trim().startsWith("{")) JSONObject(r.body).num("until").toLong() else 0L
        } catch (_: Exception) { }
    }

    fun payUrl(type: String): String {
        val uid = Auth.current?.uid ?: ""
        return "https://yoomoney.ru/quickpay/confirm?receiver=$WALLET&quickpay-form=button&paymentType=$type&sum=$PRICE&label=" + enc(uid)
    }

    // после перехода к оплате 15 минут раз в 10 секунд проверяем, не включился ли Plus
    fun poll(onPaid: () -> Unit) {
        pollJob?.cancel()
        val before = until
        pollJob = appScope.launch {
            repeat(90) {
                delay(10_000)
                refresh()
                if (until > before && active) { onPaid(); return@launch }
            }
        }
    }

    fun leftText(): String {
        val ms = until - now()
        if (ms <= 0) return ""
        val s = ms / 1000
        val d = s / 86400; val h = s % 86400 / 3600; val m = s % 3600 / 60; val sec = s % 60
        return (if (d > 0) "$d ${dn(d)}  " else "") + "%02d:%02d:%02d".format(h, m, sec)
    }

    fun dn(n: Long): String { val a = n % 100; val b = n % 10; return if (a in 11..14) "дней" else if (b == 1L) "день" else if (b in 2..4) "дня" else "дней" }
}
