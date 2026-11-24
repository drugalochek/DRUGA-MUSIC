package com.sonora.app

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.clickable
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.roundToInt

// ---------------- Листы (OpenSheet на ПК): на телефоне выезжают снизу ----------------
class Sheet(val title: String?, val content: @Composable ColumnScope.() -> Unit)

object Sheets {
    val stack = mutableStateListOf<Sheet>()
    fun open(title: String?, content: @Composable ColumnScope.() -> Unit) { stack.add(Sheet(title, content)) }
    fun close() { if (stack.isNotEmpty()) stack.removeAt(stack.size - 1) }
    fun closeAll() { stack.clear() }
    fun replace(title: String?, content: @Composable ColumnScope.() -> Unit) { close(); open(title, content) }
}

@Composable
fun SheetHost() {
    val sheet = Sheets.stack.lastOrNull() ?: return
    val anim = remember(sheet) { Animatable(1f) }
    var drag by remember(sheet) { mutableFloatStateOf(0f) }
    val scope = rememberCoroutineScope()
    LaunchedEffect(sheet) { anim.animateTo(0f, tween(260)) }
    Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = .55f * (1 - anim.value)))
        .clickable(remember { MutableInteractionSource() }, null) { Sheets.close() }, contentAlignment = Alignment.BottomCenter) {
        val shape = RoundedCornerShape(topStart = 26.dp, topEnd = 26.dp)
        Column(Modifier.statusBarsPadding().padding(top = 40.dp).widthIn(max = 560.dp).fillMaxWidth()
            .graphicsLayer { translationY = anim.value * size.height + drag }
            .clip(shape).background(Color(0xF815181F)).border(1.dp, Th.GlassEdge, shape)
            .clickable(remember { MutableInteractionSource() }, null) { }
            .navigationBarsPadding().imePadding()) {
            // «ручка»: за неё лист можно стянуть вниз
            Column(Modifier.fillMaxWidth().pointerInput(sheet) {
                detectVerticalDragGestures(onDragEnd = {
                    if (drag > 140f) Sheets.close() else scope.launch { val a = Animatable(drag); a.animateTo(0f, tween(180)) { drag = value } }
                }) { _, dy -> drag = (drag + dy).coerceAtLeast(0f) }
            }) {
                Box(Modifier.padding(top = 9.dp).align(Alignment.CenterHorizontally).width(38.dp).height(4.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x40FFFFFF)))
                if (sheet.title != null) Row(Modifier.fillMaxWidth().padding(start = 22.dp, end = 14.dp, top = 12.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Tx(sheet.title, 20f, modifier = Modifier.weight(1f), lines = 2)
                    Box(Modifier.size(32.dp).clip(CircleShape).background(Th.glass(.08f)).tap(.9f) { Sheets.close() }, contentAlignment = Alignment.Center) { SIcon("close", 14.dp, Th.Txt) }
                } else Spacer(Modifier.height(8.dp))
            }
            Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(start = 20.dp, end = 20.dp, top = 8.dp, bottom = 18.dp)) { sheet.content(this) }
        }
    }
}

@Composable
fun SheetText(s: String) = Tx(s, 13f, Th.Dim, Modifier.padding(bottom = 12.dp), lines = 8, lineHeight = 19.sp)

fun confirm(title: String, text: String, ok: String, danger: Boolean = true, action: () -> Unit) {
    Sheets.open(title) {
        SheetText(text)
        ProviderBtn(ok, if (danger) "trash" else "check", primary = !danger, danger = danger) { Sheets.close(); action() }
        ProviderBtn("Отмена") { Sheets.close() }
    }
}

// ---------------- Вход ----------------
fun openWelcome() {
    if (Sheets.stack.isNotEmpty() && Sheets.stack.last().title == null) return
    Sheets.open(null) {
        Column(Modifier.fillMaxWidth().padding(top = 6.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Logo(54.dp)
            Tx("Добро пожаловать в SONORA", 21f, modifier = Modifier.padding(top = 22.dp), align = TextAlign.Center, lines = 2)
            Tx("Слушай музыку, собирай плейлисты и лайкай треки — всё синхронизируется с компьютером.", 13f, Th.Dim,
                Modifier.padding(top = 8.dp, bottom = 20.dp), lines = 3, align = TextAlign.Center, lineHeight = 19.sp)
        }
        GoogleButton("Продолжить с Google")
        ProviderBtn("Войти или создать аккаунт по почте", "mail", primary = true) { Sheets.replace("Вход в SONORA") { AuthForm() } }
        ProviderBtn("Продолжить как гость", "user") { Sheets.close() }
        Tx("Без аккаунта можно только слушать. Данные гостя хранятся на этом телефоне.", 11.5f, Th.Dim, Modifier.padding(top = 10.dp).fillMaxWidth(), lines = 3, align = TextAlign.Center)
    }
}

fun openAuth() = Sheets.open("Вход в SONORA") { AuthForm() }

@Composable
fun AuthForm() {
    var reg by remember { mutableStateOf(false) }
    var name by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var pass by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var err by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    fun submit() {
        if (busy) return
        if (email.isBlank()) { err = "Введи почту"; return }
        if (pass.length < 6) { err = "Пароль — минимум 6 символов"; return }
        err = null; busy = true
        scope.launch {
            try {
                if (reg) Auth.signUp(name, email, pass) else Auth.signIn(email, pass)
                Sheets.closeAll()
                toast("Привет, " + (Auth.current?.name ?: "") + "!")
                if (reg) Notifs.add("Аккаунт создан", "Лайки и плейлисты теперь сохраняются в облаке и видны на ПК.")
            } catch (e: Exception) { err = e.message ?: "Ошибка" }
            busy = false
        }
    }
    Column {
        GoogleButton(if (reg) "Регистрация через Google" else "Войти через Google") { err = it }
        OrDivider()
        Row(Modifier.padding(start = 0.dp, bottom = 14.dp)) {
            listOf(false to "Войти", true to "Регистрация").forEach { (v, label) ->
                val on = reg == v
                val shape = RoundedCornerShape(20.dp)
                Box(Modifier.padding(end = 8.dp).clip(shape).background(if (on) Th.Soft else Th.Card).border(1.dp, if (on) Color(0x663B82F6) else Th.Border, shape)
                    .tap(.95f) { reg = v; err = null }.padding(horizontal = 16.dp, vertical = 8.dp)) { Tx(label, 13f, if (on) Th.Accent3 else Th.Dim) }
            }
        }
        if (reg) { Input(name, { name = it }, "Имя", icon = "user"); Spacer(Modifier.height(10.dp)) }
        Input(email, { email = it.trim() }, "Почта", icon = "mail", keyboard = KeyboardType.Email)
        Spacer(Modifier.height(10.dp))
        Input(pass, { pass = it }, "Пароль", icon = "lock", password = true)
        err?.let { Tx(it, 12.5f, Th.Danger, Modifier.padding(top = 10.dp), lines = 3) }
        Spacer(Modifier.height(14.dp))
        if (busy) Box(Modifier.fillMaxWidth().height(58.dp), contentAlignment = Alignment.Center) { CircularProgressIndicator(color = Th.Accent3, modifier = Modifier.size(28.dp), strokeWidth = 2.5.dp) }
        else ProviderBtn(if (reg) "Создать аккаунт" else "Войти", if (reg) "check" else "login", primary = true) { submit() }
        if (!reg) Tx("Забыли пароль?", 13f, Th.Accent3, Modifier.padding(top = 10.dp).align(Alignment.CenterHorizontally).tap {
            if (email.isBlank()) { err = "Введи почту — пришлём ссылку для сброса пароля"; return@tap }
            scope.launch { try { Auth.reset(email); toast("Письмо для сброса пароля отправлено") } catch (e: Exception) { err = e.message } }
        })
        Tx("Аккаунт тот же, что в SONORA для ПК. Через Google аккаунт создаётся сам — на компьютере входи тоже через Google.", 11.5f, Th.Dim,
            Modifier.padding(top = 14.dp).fillMaxWidth(), lines = 3, align = TextAlign.Center)
    }
}

// ---------------- SONORA Plus ----------------
fun openPlus() = Sheets.open(null) { PlusSheet() }

@Composable
fun PlusSheet() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var tick by remember { mutableLongStateOf(0L) }
    LaunchedEffect(Unit) { Plus.refresh(); while (true) { tick = now(); delay(1020L - now() % 1000) } }
    @Suppress("UNUSED_VARIABLE") val tk = tick
    val active = Plus.active
    fun pay(type: String) {
        if (Auth.current == null) { Sheets.replace("Вход в SONORA") { AuthForm() }; return }
        ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(Plus.payUrl(type))))
        Plus.poll { toast("SONORA Plus активен — спасибо!"); Notifs.add("SONORA Plus подключён", "Спасибо за поддержку! Значок PLUS уже в профиле.") }
        toast("После оплаты Plus включится сам — обычно за минуту")
    }
    // шапка в фирменном градиенте Plus
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(Th.PlusGold).padding(18.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(46.dp).clip(CircleShape).background(Color(0x33FFFFFF)), contentAlignment = Alignment.Center) { SIcon("crown", 22.dp, Color.White, stroke = 2.3f) }
            Column(Modifier.padding(start = 14.dp)) {
                Text("SONORA PLUS", color = Color.White, fontFamily = Th.Logo, fontSize = 22.sp)
                Tx(if (active) "Подписка активна" else "${Plus.PRICE} ₽ за ${Plus.DAYS} дней · без автосписаний", 12.5f, Color.White.copy(alpha = .85f), weight = FontWeight.Medium)
            }
        }
    }
    if (active) {
        Column(Modifier.padding(top = 14.dp).fillMaxWidth().glass(RoundedCornerShape(16.dp), .05f).padding(16.dp)) {
            Tx("До окончания Plus", 12f, Th.Dim)
            Text(Plus.leftText(), color = Th.Txt, fontFamily = Th.Logo, fontSize = 26.sp, modifier = Modifier.padding(vertical = 6.dp))
            val frac = ((Plus.until - now()).toFloat() / (Plus.DAYS * 86_400_000f)).coerceIn(0.02f, 1f)
            Box(Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)).background(Th.glass(.08f))) {
                Box(Modifier.fillMaxWidth(frac).height(6.dp).clip(RoundedCornerShape(3.dp)).background(Th.PlusGold))
            }
            Tx("Действует до " + SimpleDateFormat("dd.MM.yyyy HH:mm", Locale("ru")).format(Date(Plus.until)), 11.5f, Th.Dim, Modifier.padding(top = 8.dp))
        }
    }
    Spacer(Modifier.height(10.dp))
    listOf("globe" to "До 100 публичных треков вместо 2", "upload" to "Файлы до 40 МБ и до 30 минут", "crown" to "Значок PLUS в профиле и ранний доступ",
        "heart" to "Поддержка SONORA — без рекламы").forEach { (ic, text) ->
        Row(Modifier.fillMaxWidth().padding(vertical = 5.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(34.dp).clip(CircleShape).background(Th.PlusSoft).border(1.dp, Th.PlusEdge, CircleShape), contentAlignment = Alignment.Center) { SIcon(ic, 16.dp, Th.PlusIcon) }
            Tx(text, 13.5f, modifier = Modifier.padding(start = 12.dp), lines = 2)
        }
    }
    Spacer(Modifier.height(12.dp))
    ProviderBtn((if (active) "Продлить картой · " else "Картой · ") + "${Plus.PRICE} ₽", "crown", brush = Th.PlusGold) { pay("AC") }
    ProviderBtn("Кошельком ЮMoney", "drop") { pay("PC") }
    ProviderBtn("Я оплатил — проверить", "refresh") {
        scope.launch {
            val was = Plus.until
            Plus.refresh()
            toast(if (Plus.active) (if (Plus.until > was) "SONORA Plus активен — спасибо!" else "Plus активен") else "Оплата пока не пришла. Обычно это до минуты. Если прошло больше 10 минут — напиши в поддержку свой ID")
        }
    }
    if (active) Tx("Продление прибавит ${Plus.DAYS} дней к текущему сроку.", 12f, Th.Dim, Modifier.padding(top = 8.dp).fillMaxWidth(), align = TextAlign.Center)
    Auth.current?.let { s ->
        Row(Modifier.padding(top = 14.dp).fillMaxWidth().glass(RoundedCornerShape(12.dp), .03f, soft = true).tap {
            (ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("SONORA ID", s.uid)); toast("ID скопирован")
        }.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Tx("ID аккаунта", 11f, Th.Dim)
                Tx(s.uid, 12f, Th.Txt, lines = 1)
            }
            SIcon("link", 16.dp, Th.Dim)
        }
    }
}

// ---------------- Плейлисты ----------------
fun openAddToPlaylist(t: Track) {
    if (!need("Плейлисты")) return
    Sheets.open("Добавить в плейлист") {
        @Suppress("UNUSED_VARIABLE") val r = Lib.rev
        Tx(t.artist + " — " + t.title, 12.5f, Th.Dim, Modifier.padding(bottom = 10.dp))
        LRowCompact(Modifier, { Box(Modifier.size(46.dp).clip(RoundedCornerShape(10.dp)).dashed(Th.BorderHi, 10.dp), contentAlignment = Alignment.Center) { SIcon("plus", 20.dp, Th.Accent) } },
            "Новый плейлист", "Создать и добавить трек") { Sheets.replace("Новый плейлист") { PlaylistForm(null, t) } }
        Lib.playlists.toList().forEach { p ->
            val tracks = p.trackIds.mapNotNull { trackOf(it) }
            val has = p.trackIds.contains(t.id)
            LRowCompact(Modifier, { Mosaic(tracks, 46.dp, 10.dp, Th.grad(p.color, p.color), "music", 18.dp) }, p.name, if (has) "Уже здесь" else tn(tracks.size)) {
                if (Lib.addTo(p, t.id)) toast("Добавлено в «${p.name}»") else toast("Уже есть в «${p.name}»")
                Sheets.close()
            }
        }
    }
}

@Composable
fun LRowCompact(modifier: Modifier, cover: @Composable () -> Unit, title: String, meta: String, onClick: () -> Unit) {
    Row(modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).tap(.98f) { onClick() }.padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        cover()
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) { Tx(title, 14f); Tx(meta, 11.5f, Th.Dim) }
    }
}

@Composable
fun PlaylistForm(edit: Playlist?, add: Track?) {
    var name by remember { mutableStateOf(edit?.name ?: "") }
    var color by remember { mutableStateOf(edit?.color ?: Lib.plColors[Lib.playlists.size % Lib.plColors.size]) }
    Input(name, { name = it }, "Название плейлиста", icon = "edit")
    Tx("Цвет", 12f, Th.Dim, Modifier.padding(top = 14.dp, bottom = 8.dp))
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Lib.plColors.forEach { c ->
            val on = c.equals(color, true)
            Box(Modifier.size(32.dp).clip(CircleShape).background(Th.grad(c, c)).border(2.dp, if (on) Color.White else Color.Transparent, CircleShape).tap(.9f) { color = c })
        }
    }
    Spacer(Modifier.height(16.dp))
    ProviderBtn(if (edit != null) "Сохранить" else "Создать", "check", primary = true) {
        if (edit != null) { edit.color = color; Lib.rename(edit, name); Lib.touch(); Sheets.close(); return@ProviderBtn }
        val p = Lib.createPlaylist(name)
        p.color = color; Lib.touch()
        if (add != null) { Lib.addTo(p, add.id); toast("Создан «${p.name}» — трек добавлен") } else toast("Плейлист «${p.name}» создан")
        Sheets.close()
        if (add == null) Nav.go(Screen.Mix(p.id))
    }
}

// ---------------- Меню трека «⋮» (TrackMenu на ПК) ----------------
fun openTrackMenu(t: Track, playlist: Playlist? = null, list: List<Track>? = null) {
    Sheets.open(null) {
        @Suppress("UNUSED_VARIABLE") val r = Lib.rev
        Row(Modifier.fillMaxWidth().padding(bottom = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            Cover(t, 54.dp, 12.dp)
            Column(Modifier.weight(1f).padding(start = 12.dp)) {
                Tx(t.title, 15f, logo = false, weight = FontWeight.SemiBold)
                Tx(t.artist + (if (t.album.isNotEmpty()) " · " + t.album else ""), 12f, Th.Dim)
            }
        }
        val fav = Lib.isFav(t.id)
        MenuItem(if (fav) "heartFill" else "heart", if (fav) "Убрать из любимых" else "В любимые", if (fav) Th.Heart else null) { if (need("Лайки")) Lib.toggleFav(t.id); Sheets.close() }
        MenuItem("plus", "Добавить в плейлист") { Sheets.close(); openAddToPlaylist(t) }
        MenuItem("next", "Играть следующим") { Audio.playNext(t); toast("Будет следующим"); Sheets.close() }
        MenuItem("queue", "Добавить в очередь") { Audio.addToQueue(t); toast("Добавлено в очередь"); Sheets.close() }
        t.artists.firstOrNull()?.let { a -> MenuItem("user", "К исполнителю · $a") { Sheets.closeAll(); Nav.fullPlayer = false; Nav.go(Screen.Artist(a)) } }
        if (t.album.isNotEmpty()) MenuItem("music", "К альбому · ${t.album}") { Sheets.closeAll(); Nav.fullPlayer = false; Nav.go(Screen.Mix(Mixes.albumId(t.artists.first(), t.album))) }
        if (t.hasLyrics) MenuItem("lyrics", "Текст песни") { Sheets.close(); if (Audio.current?.id != t.id) Audio.play(list ?: listOf(t), (list ?: listOf(t)).indexOfFirst { it.id == t.id }.coerceAtLeast(0)); FullState.lyrics = true; Nav.fullPlayer = true }
        MenuItem("share", "Поделиться") {
            Sheets.close()
            val i = Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, "${t.artist} — ${t.title} · слушай в SONORA")
            Audio.ctx.startActivity(Intent.createChooser(i, "Поделиться").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
        if (playlist != null) MenuItem("trash", "Убрать из плейлиста", Th.Danger) { Lib.removeFrom(playlist, t.id); toast("Убрано из «${playlist.name}»"); Sheets.close() }
    }
}

@Composable
fun MenuItem(icon: String, text: String, color: Color? = null, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).tap(.98f) { onClick() }.padding(vertical = 11.dp, horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        SIcon(icon, 19.dp, color ?: Th.Txt, fill = if (icon == "heartFill") true else null)
        Tx(text, 14f, color ?: Th.Txt, Modifier.padding(start = 14.dp), weight = FontWeight.Medium)
    }
}

// ---------------- Очередь ----------------
fun openQueue() = Sheets.open("Очередь") {
    val q = Audio.queue
    if (q.isEmpty()) { Empty("queue", "Очередь пуста", "Включи трек, микс или плейлист."); return@open }
    if (Audio.source.isNotEmpty()) Tx("Играет из: " + Audio.source, 12f, Th.Dim, Modifier.padding(bottom = 8.dp))
    q.forEachIndexed { i, t ->
        val cur = i == Audio.index
        Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(if (cur) Th.Soft else Color.Transparent).tap(.98f) { Audio.jump(i) }.padding(6.dp),
            verticalAlignment = Alignment.CenterVertically) {
            Tx("${i + 1}", 11.5f, if (cur) Th.Accent3 else Th.Dim, Modifier.width(26.dp))
            Cover(t, 40.dp, 8.dp)
            Column(Modifier.weight(1f).padding(horizontal = 10.dp)) {
                Tx(t.title, 13.5f, if (cur) Th.Accent3 else Th.Txt)
                Tx(t.artist, 11.5f, Th.Dim)
            }
            if (!cur) IconBtn("close", 30.dp, 12.dp, Th.Dim) { Audio.removeAt(i) }
            else SIcon(if (Audio.playing) "pause" else "play", 14.dp, Th.Accent3)
        }
    }
}

// ---------------- Таймер сна ----------------
fun openTimer() = Sheets.open("Таймер сна") {
    var tick by remember { mutableLongStateOf(0L) }
    LaunchedEffect(Unit) { while (true) { tick = now(); delay(1000) } }
    @Suppress("UNUSED_VARIABLE") val tk = tick
    if (Audio.sleepAt > 0) Tx("Музыка остановится через " + fmt(Audio.sleepAt - now()), 13f, Th.Accent3, Modifier.padding(bottom = 10.dp))
    else SheetText("Музыка плавно остановится через выбранное время.")
    val mins = listOf(5, 15, 30, 45, 60, 90)
    mins.chunked(3).forEach { row ->
        Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            row.forEach { m ->
                val shape = RoundedCornerShape(14.dp)
                Box(Modifier.weight(1f).height(48.dp).clip(shape).background(Th.Card).border(1.dp, Th.Border, shape).tap(.95f) { Audio.setSleep(m); toast("Таймер сна: $m минут"); Sheets.close() },
                    contentAlignment = Alignment.Center) { Tx("$m минут", 13.5f) }
            }
        }
    }
    if (Audio.sleepAt > 0) ProviderBtn("Выключить таймер", "close", danger = true) { Audio.setSleep(0); toast("Таймер сна выключен"); Sheets.close() }
}

// ---------------- Уведомления ----------------
fun openNotifs() {
    Notifs.read()
    Sheets.open("Уведомления") {
        if (Notifs.list.isEmpty()) { Empty("bell", "Пока пусто", "Здесь появятся новости SONORA и статус Plus."); return@open }
        val f = SimpleDateFormat("d MMM, HH:mm", Locale("ru"))
        Notifs.list.toList().forEach { n ->
            Row(Modifier.padding(vertical = 4.dp).fillMaxWidth().glass(RoundedCornerShape(14.dp), .04f, soft = true).padding(12.dp)) {
                Box(Modifier.size(34.dp).clip(CircleShape).background(Th.Soft), contentAlignment = Alignment.Center) { SIcon("bell", 16.dp, Th.Accent3) }
                Column(Modifier.weight(1f).padding(start = 12.dp)) {
                    Tx(n.title, 13.5f)
                    Tx(n.text, 12f, Th.Dim, lines = 4, lineHeight = 17.sp)
                    Tx(f.format(Date(n.at)), 10.5f, Th.Dim, Modifier.padding(top = 4.dp))
                }
            }
        }
        ProviderBtn("Очистить", "trash") { Notifs.clear(); Sheets.close() }
    }
}

// ---------------- Профиль ----------------
fun openEditProfile() {
    val s = Auth.current ?: run { openWelcome(); return }
    Sheets.open("Редактировать профиль") {
        var name by remember { mutableStateOf(s.name) }
        var bio by remember { mutableStateOf(s.bio) }
        var color by remember { mutableStateOf(s.color.ifBlank { "#3B82F6" }) }
        var busy by remember { mutableStateOf(false) }
        val scope = rememberCoroutineScope()
        Box(Modifier.fillMaxWidth().padding(bottom = 14.dp), contentAlignment = Alignment.Center) { AvatarRing(96.dp, 3.dp) }
        Input(name, { name = it }, "Имя", icon = "user")
        Spacer(Modifier.height(10.dp))
        Input(bio, { bio = it.take(160) }, "О себе", icon = "edit", lines = 3)
        Tx("Цвет профиля", 12f, Th.Dim, Modifier.padding(top = 14.dp, bottom = 8.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            ProfileColors.forEach { c ->
                val on = c.equals(color, true)
                Box(Modifier.size(34.dp).clip(CircleShape).background(Th.hex(c)).border(2.dp, if (on) Color.White else Color.Transparent, CircleShape).tap(.9f) { color = c })
            }
        }
        Tx("Фото профиля меняется в версии для ПК.", 11.5f, Th.Dim, Modifier.padding(top = 10.dp))
        Spacer(Modifier.height(14.dp))
        ProviderBtn(if (busy) "Сохраняем…" else "Сохранить", "check", primary = true, enabled = !busy) {
            busy = true
            scope.launch {
                val ok = Auth.saveProfile(name, bio, color)
                busy = false
                if (ok) { toast("Профиль сохранён"); Sheets.close() } else toast("Не удалось сохранить профиль. Проверь интернет.")
            }
        }
    }
}

// ---------------- Настройки ----------------
fun openSettings() = Sheets.open("Настройки") {
    Tx("ВНЕШНИЙ ВИД", 11f, Th.Dim, Modifier.padding(bottom = 6.dp), weight = FontWeight.Bold, spacing = 1.sp)
    SwitchRow("Живой фон в цветах обложки", "Плеер переливается цветами текущего трека", Prefs.liquid) { Prefs.liquid = it; Prefs.save() }
    SwitchRow("Открывать плеер при выборе трека", "Как на ПК: нажал на трек — открылся полноэкранный плеер", Prefs.openFull) { Prefs.openFull = it; Prefs.save() }
    Tx("ПАМЯТЬ", 11f, Th.Dim, Modifier.padding(top = 16.dp, bottom = 6.dp), weight = FontWeight.Bold, spacing = 1.sp)
    val dir = java.io.File(Audio.ctx.cacheDir, "audio")
    var size by remember { mutableLongStateOf(dir.listFiles()?.sumOf { it.length() } ?: 0L) }
    SetBtnPlain("trash", "Очистить кэш треков", "%.1f МБ".format(size / 1048576.0)) {
        dir.listFiles()?.forEach { if (Audio.current == null || !it.name.startsWith(Audio.current!!.id)) it.delete() }
        size = dir.listFiles()?.sumOf { it.length() } ?: 0L
        toast("Кэш очищен")
    }
    Tx("SONORA для Android · версия " + BuildConfig.VERSION_NAME, 11.5f, Th.Dim, Modifier.padding(top = 16.dp).fillMaxWidth(), align = TextAlign.Center)
}

@Composable
fun SetBtnPlain(icon: String, title: String, sub: String?, onClick: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Row(Modifier.padding(vertical = 3.dp).fillMaxWidth().glass(shape, .04f, soft = true).tap(.98f) { onClick() }.padding(horizontal = 14.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        SIcon(icon, 18.dp, Th.Accent3)
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) { Tx(title, 14f); if (sub != null) Tx(sub, 11.5f, Th.Dim) }
    }
}

