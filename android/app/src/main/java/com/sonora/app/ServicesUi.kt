package com.sonora.app

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

// ================= Вход и регистрация через Google =================
@Composable
fun GoogleG(size: Dp = 18.dp) {
    Canvas(Modifier.size(size)) {
        val w = this.size.width; val st = w * .2f
        val tl = Offset(st / 2, st / 2); val sz = Size(w - st, w - st)
        drawArc(Color(0xFFEA4335), 200f, 110f, false, tl, sz, style = Stroke(st))
        drawArc(Color(0xFFFBBC05), 140f, 60f, false, tl, sz, style = Stroke(st))
        drawArc(Color(0xFF34A853), 50f, 90f, false, tl, sz, style = Stroke(st))
        drawArc(Color(0xFF4285F4), -45f, 95f, false, tl, sz, style = Stroke(st))
        drawRect(Color(0xFF4285F4), Offset(w / 2, w / 2 - st / 2), Size(w / 2 - st / 4, st))
    }
}

// белая кнопка «Войти через Google» — и вход, и регистрация (аккаунт создаётся сам)
@Composable
fun GoogleButton(text: String = "Войти через Google", onError: (String?) -> Unit = {}) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var busy by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(14.dp)
    Row(Modifier.fillMaxWidth().padding(vertical = 4.dp).height(50.dp).clip(shape).background(Color.White)
        .tap(.97f, !busy) {
            val act = ctx as? Activity ?: return@tap
            busy = true; onError(null)
            scope.launch {
                try {
                    val isNew = GoogleAuth.signIn(act) { toast("Открываем вход Google в браузере…") }
                    Sheets.closeAll()
                    toast((if (isNew) "Аккаунт создан. Привет, " else "Привет, ") + (Auth.current?.name ?: "") + "!")
                    if (isNew) Notifs.add("Аккаунт создан через Google", "Лайки и плейлисты сохраняются в облаке и видны на ПК. На компьютере входи тоже через Google.")
                } catch (e: Exception) { onError(e.message ?: "Не удалось войти через Google") }
                busy = false
            }
        }.padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center) {
        if (busy) CircularProgressIndicator(color = Color(0xFF1D4ED8), modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
        else GoogleG(19.dp)
        Spacer(Modifier.width(10.dp))
        Text(if (busy) "Ждём Google…" else text, color = Color(0xFF111111), fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
fun OrDivider() {
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.weight(1f).height(1.dp).background(Th.Border))
        Tx("или по почте", 11.5f, Th.Dim, Modifier.padding(horizontal = 10.dp))
        Box(Modifier.weight(1f).height(1.dp).background(Th.Border))
    }
}

// ================= Музыкальные сервисы =================
private fun copy(ctx: Context, label: String, text: String) {
    try { (ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText(label, text)) } catch (_: Exception) { }
}

@Composable
fun SvcBadge(letter: String, brush: Brush, size: Dp = 50.dp) {
    Box(Modifier.size(size).clip(RoundedCornerShape(14.dp)).background(brush), contentAlignment = Alignment.Center) {
        Text(letter, color = Color.White, fontSize = (size.value * .42f).sp, fontWeight = FontWeight.Bold)
    }
}

val YaBrush = Brush.linearGradient(listOf(Color(0xFFFFD43B), Color(0xFFFF8A00)))
val SpBrush = Brush.linearGradient(listOf(Color(0xFF3B82F6), Color(0xFF1E3A8A)))
val DzBrush = Brush.linearGradient(listOf(Color(0xFFA238FF), Color(0xFF1D4ED8)))

@Composable
fun ServiceCard(letter: String, brush: Brush, title: String, status: String, on: Boolean, onConnect: () -> Unit, onOff: () -> Unit, body: @Composable () -> Unit) {
    val shape = RoundedCornerShape(20.dp)
    Column(Modifier.padding(horizontal = 16.dp, vertical = 6.dp).fillMaxWidth().glass(shape, .045f).padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            SvcBadge(letter, brush, 46.dp)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Tx(title, 16f, logo = false, weight = FontWeight.SemiBold)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(7.dp).clip(CircleShape).background(if (on) Color(0xFF34D399) else Th.Dim))
                    Tx(status, 12f, Th.Dim, Modifier.padding(start = 6.dp))
                }
            }
            val bShape = RoundedCornerShape(18.dp)
            Box(Modifier.clip(bShape).then(if (on) Modifier.background(Th.Card).border(1.dp, Th.Border, bShape) else Modifier.background(Th.Hot))
                .tap(.95f) { if (on) onOff() else onConnect() }.padding(horizontal = 14.dp, vertical = 8.dp)) {
                Tx(if (on) "Отключить" else "Подключить", 12.5f, if (on) Th.Dim else Color.White, weight = FontWeight.SemiBold)
            }
        }
        Spacer(Modifier.height(10.dp))
        body()
    }
}

@Composable
fun SvcRow(cover: @Composable () -> Unit, title: String, meta: String, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).tap(.99f) { onClick() }.padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        cover()
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Tx(title, 14f, modifier = Modifier.padding(bottom = 2.dp), logo = false)
            Tx(meta, 11.5f, Th.Dim)
        }
        SIcon("chevR", 15.dp, Th.Dim)
    }
}

@Composable
fun SmallCover(url: String?, brush: Brush, icon: String) {
    val t = remember(url) { url?.let { Track(id = "cov:" + it.hashCode(), title = "", artist = "", album = "", genre = "", lyrics = "", cover = it, ownerName = "", ownerId = null,
        year = 0, duration = 0.0, plays = 0, addedAt = 0, verified = false, explicit = false, db = "ext") } }
    Box(Modifier.size(46.dp).clip(RoundedCornerShape(10.dp))) { CoverFill(t, Modifier.fillMaxSize(), brush, icon, 18.dp) }
}

// списки сервиса подгружаются при открытии страницы
@Composable
fun SvcLists(src: String) {
    @Suppress("UNUSED_VARIABLE") val r = Ext.rev
    var lists by remember(src, r) { mutableStateOf(when (src) { "ya" -> Ext.yaLists; "sp" -> Ext.spLists; else -> Ext.dzLists }) }
    var err by remember(src, r) { mutableStateOf<String?>(null) }
    LaunchedEffect(src, r) {
        if (lists == null) try {
            lists = withContext(Dispatchers.IO) { when (src) { "ya" -> Ext.yaPlaylists(); "sp" -> Ext.spPlaylists(); else -> Ext.dzPlaylists() } }
        } catch (e: Exception) { err = e.message }
    }
    val brush = when (src) { "ya" -> YaBrush; "sp" -> SpBrush; else -> DzBrush }
    val name = Ext.sourceName(src)
    SvcRow({ LCover(Th.Pink, "heartFill") }, if (src == "ya") "Мне нравится" else "Любимые треки", name) { Nav.go(Screen.ExtList("ext:$src:likes", if (src == "ya") "Мне нравится" else "Любимые треки")) }
    when {
        err != null -> Tx("Плейлисты не загрузились: $err", 12f, Th.Danger, Modifier.padding(top = 4.dp), lines = 3)
        lists == null -> Tx("Загружаем плейлисты…", 12f, Th.Dim, Modifier.padding(top = 4.dp))
        else -> lists!!.take(30).forEach { p -> SvcRow({ SmallCover(p.cover, brush, "music") }, p.title, tn(p.count)) { Nav.go(Screen.ExtList(p.id, p.title)) } }
    }
}

@Composable
fun ServicesPage() {
    @Suppress("UNUSED_VARIABLE") val r = Ext.rev
    Page("services") {
        item { PTitle("Сервисы") }
        item { Note("Подключи свои аккаунты — лайки и плейлисты появятся здесь, а поиск SONORA будет искать и в Яндекс Музыке, и в Deezer. Всё играет в общем плеере и общей очереди, а лайки синхронизируются с ПК.") }
        item {
            ServiceCard("Я", YaBrush, "Яндекс Музыка",
                if (Ext.yaOn) (Ext.yaName ?: "Яндекс") + if (Ext.yaPlus) " · Плюс" else " · без Плюса" else "Не подключена", Ext.yaOn,
                { if (need("Музыкальные сервисы")) openYandex() },
                { confirm("Отключить Яндекс Музыку?", "Лайки и плейлисты Яндекса пропадут из SONORA. Подключить снова можно в любой момент.", "Отключить") { Ext.yaDisconnect(); toast("Яндекс Музыка отключена") } }) {
                if (Ext.yaOn) SvcLists("ya") else Tx("Лайки, плейлисты и полные треки (с Яндекс Плюсом). Без Плюса — отрывки по 30 секунд.", 12.5f, Th.Dim, lines = 3, lineHeight = 18.sp)
            }
        }
        item {
            ServiceCard("S", SpBrush, "Spotify", if (Ext.spOn) (Ext.spName ?: "Spotify") + (Ext.spProduct?.let { " · $it" } ?: "") else "Не подключён", Ext.spOn,
                { if (need("Музыкальные сервисы")) openSpotify() },
                { confirm("Отключить Spotify?", "Лайки и плейлисты Spotify пропадут из SONORA.", "Отключить") { Ext.spDisconnect(); toast("Spotify отключён") } }) {
                if (Ext.spOn) {
                    Tx(if (Ext.yaOn) "Треки Spotify играют целиком через Яндекс Музыку." else "Spotify не отдаёт звук сторонним плеерам — треки играют отрывками через Deezer. Подключи Яндекс Музыку, чтобы они звучали целиком.", 12f, Th.Dim, Modifier.padding(bottom = 6.dp), lines = 3, lineHeight = 17.sp)
                    SvcLists("sp")
                } else Tx("Лайки и плейлисты из Spotify. Звук берётся из Яндекс Музыки (целиком) или Deezer (отрывки).", 12.5f, Th.Dim, lines = 3, lineHeight = 18.sp)
            }
        }
        item {
            ServiceCard("D", DzBrush, "Deezer", if (Ext.dzOn) Ext.dzName ?: "Deezer" else "Профиль не указан", Ext.dzOn,
                { if (need("Музыкальные сервисы")) openDeezer(false) },
                { Ext.dzDisconnect(); toast("Профиль Deezer отключён") }) {
                if (Ext.dzOn) SvcLists("dz") else Tx("Поиск по Deezer работает и без входа (отрывки по 30 секунд). Укажи свой профиль — появятся любимые треки и плейлисты.", 12.5f, Th.Dim, lines = 3, lineHeight = 18.sp)
                SvcRow({ LCover(Th.Hot, "link") }, "Открыть плейлист или альбом по ссылке", "deezer.com/playlist/…") { openDeezer(true) }
            }
        }
    }
}

// ---------- Яндекс: вход по коду (ya.ru/device), как на ПК ----------
fun openYandex() = Sheets.open("Яндекс Музыка") { YandexSheet() }

@Composable
fun YandexSheet() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var code by remember { mutableStateOf<Ext.YaCode?>(null) }
    var msg by remember { mutableStateOf("Получаем код у Яндекса…") }
    var bad by remember { mutableStateOf(false) }
    var manual by remember { mutableStateOf(false) }
    var input by remember { mutableStateOf("") }
    var attempt by remember { mutableStateOf(0) }
    var job by remember { mutableStateOf<Job?>(null) }
    LaunchedEffect(attempt) {
        code = null; bad = false; msg = "Получаем код у Яндекса…"
        try {
            val c = withContext(Dispatchers.IO) { Ext.yaDeviceStart() }
            code = c
            copy(ctx, "Код Яндекса", c.user)
            msg = "Код скопирован. Открой страницу Яндекса, войди и вставь код — SONORA подключится сама."
            Ext.yaDeviceWait(c)
            Sheets.close()
            toast("Яндекс Музыка подключена: " + (Ext.yaName ?: "") + if (Ext.yaPlus) " · Плюс" else "")
        } catch (e: kotlinx.coroutines.CancellationException) { throw e
        } catch (e: Exception) { msg = e.message ?: "Ошибка"; bad = true }
    }
    if (!manual) {
        val c = code
        Box(Modifier.fillMaxWidth().padding(vertical = 6.dp).clip(RoundedCornerShape(16.dp)).background(Th.Card).border(1.dp, Th.BorderHi, RoundedCornerShape(16.dp))
            .tap(.98f) { if (c != null) { copy(ctx, "Код Яндекса", c.user); toast("Код скопирован") } }.padding(vertical = 18.dp), contentAlignment = Alignment.Center) {
            if (c == null && !bad) CircularProgressIndicator(color = Th.Accent3, modifier = Modifier.size(26.dp), strokeWidth = 2.5.dp)
            else Text(c?.user ?: "—", color = Th.Txt, fontSize = 30.sp, fontWeight = FontWeight.Bold, letterSpacing = 4.sp)
        }
        Tx(msg, 12.5f, if (bad) Th.Danger else Th.Dim, Modifier.padding(vertical = 8.dp), lines = 4, lineHeight = 18.sp)
        if (c != null) ProviderBtn("Открыть ya.ru/device", "globe", primary = true) { copy(ctx, "Код Яндекса", c.user); openUrl(ctx, c.url) }
        ProviderBtn("Получить новый код", "refresh") { attempt++ }
        Tx("Подключить вручную (по токену)", 12.5f, Th.Accent3, Modifier.padding(top = 10.dp).align(Alignment.CenterHorizontally).tap { manual = true })
    } else {
        SheetText("Открой ссылку, войди в Яндекс и скопируй адрес страницы, на которую попадёшь, целиком — в нём есть access_token=…")
        ProviderBtn("Открыть вход Яндекса", "globe") { openUrl(ctx, "https://oauth.yandex.ru/authorize?response_type=token&client_id=23cabbbdc6cd418abb4b39c32c41195d") }
        Spacer(Modifier.height(6.dp))
        Input(input, { input = it }, "Токен или адрес с access_token=…", icon = "link")
        if (bad) Tx(msg, 12.5f, Th.Danger, Modifier.padding(top = 8.dp), lines = 3)
        Spacer(Modifier.height(8.dp))
        ProviderBtn("Подключить", "check", primary = true) {
            job?.cancel()
            job = scope.launch {
                try { withContext(Dispatchers.IO) { Ext.yaConnect(input) }; Sheets.close(); toast("Яндекс Музыка подключена: " + (Ext.yaName ?: "")) }
                catch (e: Exception) { msg = e.message ?: "Ошибка"; bad = true }
            }
        }
    }
}

// ---------- Spotify: свой Client ID + вход на сайте Spotify ----------
fun openSpotify() = Sheets.open("Spotify") { SpotifySheet() }

@Composable
fun SpotifySheet() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var id by remember { mutableStateOf(Ext.spClient ?: "") }
    var err by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    SheetText("Spotify не даёт сторонним плеерам общий ключ, поэтому один раз нужен свой Client ID (бесплатно, 2 минуты):\n" +
        "1. «Открыть кабинет Spotify» → Create app, название любое.\n2. В Redirect URIs вставь ${Ext.SP_REDIRECT} → Add.\n3. Отметь Web API, согласись с условиями → Save.\n4. Settings → скопируй Client ID и вставь сюда.\n" +
        "Тот же Client ID подходит и для ПК. Если сайт Spotify не открывается, может понадобиться VPN.")
    ProviderBtn("Открыть кабинет Spotify", "globe") { openUrl(ctx, "https://developer.spotify.com/dashboard/create") }
    ProviderBtn("Скопировать Redirect URI", "share") { copy(ctx, "Redirect URI", Ext.SP_REDIRECT); toast("Скопировано: " + Ext.SP_REDIRECT) }
    Spacer(Modifier.height(6.dp))
    Input(id, { id = it.trim() }, "Client ID (32 символа)", icon = "lock")
    err?.let { Tx(it, 12.5f, Th.Danger, Modifier.padding(top = 8.dp), lines = 3) }
    Spacer(Modifier.height(8.dp))
    if (busy) {
        Tx("Разреши доступ в браузере и вернись в SONORA…", 12.5f, Th.Dim, Modifier.padding(vertical = 8.dp).fillMaxWidth(), lines = 2, align = TextAlign.Center)
        ProviderBtn("Отменить") { Ext.spCancel() }
    } else ProviderBtn("Войти через Spotify", "login", primary = true) {
        busy = true; err = null
        scope.launch {
            try { Ext.spConnect(ctx, id); Sheets.close(); toast("Spotify подключён: " + (Ext.spName ?: "")) }
            catch (e: Exception) { err = e.message ?: "Ошибка" }
            busy = false
        }
    }
}

// ---------- Deezer: публичный профиль или ссылка на плейлист/альбом ----------
fun openDeezer(linkOnly: Boolean) = Sheets.open(if (linkOnly) "Плейлист Deezer" else "Профиль Deezer") { DeezerSheet(linkOnly) }

@Composable
fun DeezerSheet(linkOnly: Boolean) {
    val scope = rememberCoroutineScope()
    var inp by remember { mutableStateOf("") }
    var err by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    SheetText(if (linkOnly) "Вставь ссылку на любой публичный плейлист или альбом Deezer — он откроется в SONORA, и его можно сохранить в медиатеку."
        else "Открой свой профиль на deezer.com и скопируй адрес (deezer.com/profile/…). Профиль должен быть публичным.")
    Input(inp, { inp = it }, if (linkOnly) "https://www.deezer.com/playlist/…" else "Ссылка на профиль или его номер", icon = "link")
    err?.let { Tx(it, 12.5f, Th.Danger, Modifier.padding(top = 8.dp), lines = 3) }
    Spacer(Modifier.height(8.dp))
    ProviderBtn(if (busy) "Проверяем…" else if (linkOnly) "Открыть" else "Подключить", "check", primary = true, enabled = !busy) {
        busy = true; err = null
        scope.launch {
            try {
                val r = withContext(Dispatchers.IO) { Ext.dzParseLink(inp) } ?: throw ExtException("Не похоже на ссылку Deezer. Пример: https://www.deezer.com/profile/123456")
                when (r.first) {
                    "profile" -> { withContext(Dispatchers.IO) { Ext.dzConnect(r.second) }; Sheets.close(); toast("Профиль Deezer подключён: " + (Ext.dzName ?: "")) }
                    "playlist" -> { Sheets.close(); Nav.go(Screen.ExtList("ext:dz:pl:" + r.second, "Плейлист Deezer")) }
                    "album" -> { Sheets.close(); Nav.go(Screen.ExtList("ext:dz:al:" + r.second, "Альбом Deezer")) }
                    else -> throw ExtException("Это ссылка на исполнителя — вставь ссылку на плейлист, альбом или профиль")
                }
            } catch (e: Exception) { err = e.message ?: "Ошибка" }
            busy = false
        }
    }
}

// ---------- страница списка сервиса (лайки / плейлист / альбом) ----------
@Composable
fun ExtListPage(id: String, title: String) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    var m by remember(id) { mutableStateOf(Ext.cachedList(id)) }
    var err by remember(id) { mutableStateOf<String?>(null) }
    var reload by remember(id) { mutableStateOf(0) }
    LaunchedEffect(id, reload) {
        if (m == null || reload > 0) try { err = null; m = withContext(Dispatchers.IO) { Ext.loadList(id, title, reload > 0) } } catch (e: Exception) { err = e.message ?: "Ошибка" }
    }
    val src = id.split(':').getOrElse(1) { "" }
    val brush = when (src) { "ya" -> YaBrush; "sp" -> SpBrush; else -> DzBrush }
    val list = m?.tracks ?: emptyList()
    val name = m?.name ?: title
    Page("ext:$id") {
        item {
            Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Mosaic(list, 200.dp, 16.dp, brush, if (id.endsWith(":likes")) "heartFill" else "music", 70.dp)
                Tx(Ext.sourceName(src).uppercase(), 11.5f, Th.Dim, Modifier.padding(top = 16.dp), weight = FontWeight.Bold, spacing = 1.sp)
                Tx(name, 24f, modifier = Modifier.padding(top = 4.dp), lines = 2, align = TextAlign.Center)
                Tx(if (m == null && err == null) "Загружаем…" else tn(list.size) + (Ext.playbackNote(list.firstOrNull())?.let { " · $it" } ?: ""), 12.5f, Th.Dim, Modifier.padding(top = 6.dp), lines = 2, align = TextAlign.Center)
            }
        }
        item {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                HotBtn("Слушать", "play", height = 50.dp, enabled = list.isNotEmpty()) { Audio.play(list, 0, shuffleOn = false, from = name); if (Prefs.openFull) Nav.fullPlayer = true }
                Spacer(Modifier.width(12.dp))
                RoundBtn("shuffle", 46.dp, iconSize = 19.dp) { if (list.isNotEmpty()) { Audio.play(list, 0, shuffleOn = true, from = name); if (Prefs.openFull) Nav.fullPlayer = true } }
                Spacer(Modifier.width(12.dp))
                RoundBtn("plus", 46.dp, iconSize = 19.dp) {
                    if (list.isNotEmpty() && need("Плейлисты")) {
                        val p = Lib.createPlaylist(name)
                        list.forEach { if (!p.trackIds.contains(it.id)) p.trackIds.add(it.id) }
                        Lib.touch(); Ext.save()
                        toast("Сохранено в медиатеку: «$name»")
                    }
                }
                Spacer(Modifier.width(12.dp))
                RoundBtn("refresh", 46.dp, iconSize = 18.dp) { reload++ }
            }
        }
        if (err != null) item { Empty("warn", "Не удалось загрузить", err ?: "") }
        else if (m != null && list.isEmpty()) item { Empty("music", "Здесь пока пусто", "В этом списке нет доступных треков.") }
        trackItems(list, name)
    }
}

// ---------- результаты поиска из сервисов ----------
fun androidx.compose.foundation.lazy.LazyListScope.extSearchItems(qq: String, res: Pair<List<Track>, List<Track>>?, loading: Boolean) {
    if (qq.isEmpty()) return
    if (loading && res == null) { item { Tx(if (Ext.yaOn) "Ищем в Яндекс Музыке и Deezer…" else "Ищем в Deezer…", 12.5f, Th.Dim, Modifier.padding(horizontal = 20.dp, vertical = 12.dp)) }; return }
    val (ya, dz) = res ?: return
    if (ya.isNotEmpty()) { item { SHead("Яндекс Музыка", tn(ya.size)) }; trackItems(ya, "Яндекс Музыка", max = 30) }
    if (dz.isNotEmpty()) { item { SHead(if (Ext.yaOn) "Deezer" else "Deezer · отрывки 30 с", tn(dz.size)) }; trackItems(dz, "Deezer", max = 30) }
    if (!Ext.yaOn && dz.isNotEmpty()) item { LRow({ LCover(Th.Hot, "globe") }, "Подключи Яндекс Музыку", "Тогда найденные треки будут играть целиком") { Nav.go(Screen.Services) } }
}
