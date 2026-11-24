package com.sonora.app

import android.Manifest
import android.app.Application
import android.content.ComponentName
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.Crossfade
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin

class SonoraApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Audio.init(this)
        Auth.init(this)
        Catalog.init(this)
        Lib.init(this)
        Mixes.init(this)
        Notifs.init(this)
        Prefs.init(this)
        appScope.launch {
            Catalog.load()
            try { Cloud.syncNow() } catch (_: Exception) { }
            try { Plus.refresh() } catch (_: Exception) { }
        }
    }
}

class MainActivity : ComponentActivity() {
    private var controller: com.google.common.util.concurrent.ListenableFuture<MediaController>? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        setContent { SonoraTheme { Root() } }
        if (Auth.current == null && !Prefs.welcomed) { Prefs.welcomed = true; Prefs.save(); openWelcome() }
    }

    override fun onStart() {
        super.onStart()
        // подключаемся к сервису: он держит музыку в фоне и показывает управление в шторке и на экране блокировки
        controller = MediaController.Builder(this, SessionToken(this, ComponentName(this, PlaybackService::class.java))).buildAsync()
    }

    override fun onStop() {
        controller?.let { MediaController.releaseFuture(it) }
        controller = null
        super.onStop()
    }
}

object FullState { var lyrics by mutableStateOf(false) }

// ---------------- Оболочка (Shell на ПК): фон со свечением, шапка, страница, мини-плеер и нижняя навигация ----------------
@Composable
fun Root() {
    BackHandler(enabled = Sheets.stack.isNotEmpty() || Nav.fullPlayer || Nav.stack.size > 1 || Nav.tab != Screen.Home) { Nav.back() }
    LaunchedEffect(Auth.current?.uid) {
        if (Auth.current != null) { try { Cloud.syncNow() } catch (_: Exception) { }; try { Plus.refresh() } catch (_: Exception) { }; Auth.loadProfile() }
    }
    LaunchedEffect(Nav.stack.size) { Pages.forget(Nav.stack.size) }
    val t = Audio.current
    LaunchedEffect(t?.id) { t?.let { Covers.get(it) } }
    @Suppress("UNUSED_VARIABLE") val cr = Covers.rev
    val pal = Covers.palette(t) ?: Covers.Fallback
    val glow by animateColorAsState(if (t == null) Th.Accent else pal[0], tween(1200), label = "glow")
    val navInset = WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding()
    Pages.bottomPad = navInset + 62.dp + (if (t != null) 84.dp else 0.dp) + 24.dp

    Box(Modifier.fillMaxSize().background(Th.Bg)) {
        Ambient(glow, t != null)
        Column(Modifier.fillMaxSize()) {
            TopBar()
            Box(Modifier.weight(1f).fillMaxWidth()) {
                Crossfade(targetState = Nav.top to Nav.stack.size, animationSpec = tween(180), label = "page") { (s, _) -> ScreenFor(s) }
            }
        }
        Column(Modifier.align(Alignment.BottomCenter).fillMaxWidth()) {
            if (t != null) MiniPlayer(t, pal)
            BottomNav()
        }
        AnimatedVisibility(Nav.fullPlayer, enter = slideInVertically(tween(320)) { it } + fadeIn(tween(200)), exit = slideOutVertically(tween(260)) { it } + fadeOut(tween(260))) { FullPlayer() }
        SheetHost()
        ToastHost(if (Nav.fullPlayer || Sheets.stack.isNotEmpty()) navInset + 24.dp else Pages.bottomPad - 14.dp)
    }
}

@Composable
fun ScreenFor(s: Screen) {
    when (s) {
        Screen.Home -> HomePage()
        Screen.Search -> SearchPage()
        Screen.Explore -> ExplorePage()
        Screen.Fav -> FavPage()
        Screen.Profile -> ProfilePage()
        Screen.FavArtists -> FavArtistsPage()
        is Screen.Mix -> MixPage(s.id)
        is Screen.Artist -> ArtistPage(s.name)
    }
}

// мягкие синие свечения по углам + свечение цвета обложки (как фон окна на ПК)
@Composable
fun Ambient(glow: Color, hasTrack: Boolean) {
    Canvas(Modifier.fillMaxSize()) {
        val w = size.width; val h = size.height
        drawRect(Brush.radialGradient(listOf(Color(0xFF2563EB).copy(alpha = .30f), Color.Transparent), Offset(w * .05f, h * .02f), w * .95f))
        drawRect(Brush.radialGradient(listOf(Color(0xFF1D4ED8).copy(alpha = .16f), Color.Transparent), Offset(w * 1f, h * .92f), w * .9f))
        if (hasTrack) {
            drawRect(Brush.radialGradient(listOf(glow.copy(alpha = .14f), Color.Transparent), Offset(w * .5f, h * 1.02f), w * 1.0f))
            drawRect(Brush.verticalGradient(listOf(glow.copy(alpha = .27f), Color.Transparent), 0f, 300.dp.toPx()))
        }
    }
}

@Composable
fun TopBar() {
    Row(Modifier.fillMaxWidth().statusBarsPadding().height(58.dp).padding(horizontal = 14.dp), verticalAlignment = Alignment.CenterVertically) {
        if (Nav.stack.size > 1) {
            Box(Modifier.size(36.dp).clip(CircleShape).background(Color(0x73000000)).border(1.dp, Th.Border, CircleShape).tap(.9f) { Nav.back() }, contentAlignment = Alignment.Center) {
                SIcon("chevL", 17.dp, Th.Txt)
            }
        } else Logo(32.dp)
        Spacer(Modifier.weight(1f))
        PlusPill()
        Spacer(Modifier.width(8.dp))
        Box(Modifier.size(36.dp).clip(CircleShape).background(Th.glass(.06f)).border(1.dp, Th.Border, CircleShape).tap(.9f) { openNotifs() }, contentAlignment = Alignment.Center) {
            SIcon("bell", 17.dp, Th.Txt)
            if (Notifs.unread > 0) Box(Modifier.align(Alignment.TopEnd).padding(top = 6.dp, end = 7.dp).size(8.dp).clip(CircleShape).background(Th.Heart))
        }
        Spacer(Modifier.width(8.dp))
        AvatarRing(36.dp, 2.dp, Modifier.tap(.9f) { Nav.tab(Screen.Profile) })
    }
}

@Composable
fun PlusPill() {
    val active = Plus.active
    val shape = RoundedCornerShape(18.dp)
    Row(Modifier.height(34.dp).clip(shape).background(if (active) Th.PlusGold else Th.PlusSoft).border(1.dp, Th.PlusEdge, shape).tap(.95f) { openPlus() }.padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically) {
        SIcon("crown", 14.dp, if (active) Color.White else Th.PlusIcon, stroke = 2.3f)
        Spacer(Modifier.width(6.dp))
        val days = ((Plus.until - now()) / 86_400_000L).coerceAtLeast(0)
        Tx(if (active) "PLUS · $days дн" else "PLUS", 12f, Color.White, weight = FontWeight.Bold)
    }
}

@Composable
fun BottomNav() {
    val items = listOf(Triple(Screen.Home, "home", "Главная"), Triple(Screen.Search, "search", "Поиск"), Triple(Screen.Explore, "explore", "Исследовать"),
        Triple(Screen.Fav, "heart", "Моя музыка"), Triple(Screen.Profile, "user", "Профиль"))
    Column(Modifier.fillMaxWidth().background(Brush.verticalGradient(listOf(Color(0xEB0A0C10), Color(0xFA050608))))) {
        Box(Modifier.fillMaxWidth().height(1.dp).background(Th.Border))
        Row(Modifier.fillMaxWidth().navigationBarsPadding().height(62.dp), verticalAlignment = Alignment.CenterVertically) {
            for ((s, icon, label) in items) {
                val on = Nav.tab == s
                Column(Modifier.weight(1f).fillMaxHeight().tap(.92f) { Nav.tab(s) }, horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                    Box(Modifier.width(54.dp).height(30.dp).clip(RoundedCornerShape(15.dp)).background(if (on) Th.Soft else Color.Transparent), contentAlignment = Alignment.Center) {
                        SIcon(icon, 21.dp, if (on) Th.Accent3 else Th.Dim, fill = if (on && icon == "heart") true else null)
                    }
                    Text(label, color = if (on) Th.Txt else Th.Dim, fontSize = 10.5.sp, fontWeight = if (on) FontWeight.SemiBold else FontWeight.Normal, maxLines = 1, modifier = Modifier.padding(top = 3.dp))
                }
            }
        }
    }
}

@Composable
fun ToastHost(bottom: Dp) {
    val m = Toasts.msg
    var last by remember { mutableStateOf("") }
    if (m != null) last = m
    LaunchedEffect(Toasts.id) { if (Toasts.msg != null) { delay(2600); Toasts.msg = null } }
    Box(Modifier.fillMaxSize().padding(bottom = bottom, start = 24.dp, end = 24.dp), contentAlignment = Alignment.BottomCenter) {
        AnimatedVisibility(m != null, enter = fadeIn(tween(160)) + slideInVertically(tween(200)) { it / 2 }, exit = fadeOut(tween(220))) {
            val shape = RoundedCornerShape(16.dp)
            Box(Modifier.shadow(16.dp, shape).clip(shape).background(Color(0xF21A1D23)).border(1.dp, Th.GlassEdge, shape).padding(horizontal = 16.dp, vertical = 11.dp)) {
                Tx(last, 13f, Th.Txt, lines = 3, align = TextAlign.Center, weight = FontWeight.Medium)
            }
        }
    }
}

// ---------------- «Жидкий» фон в цветах обложки (Liquid на ПК) ----------------
@Composable
fun Liquid(pal: List<Color>, big: Boolean, modifier: Modifier) {
    val spec = tween<Color>(1200)
    val b0 by animateColorAsState(Pal.surface(pal[0], if (big) .40f else .34f), spec, label = "b0")
    val b1 by animateColorAsState(Pal.surface(pal[2], if (big) .28f else .26f), spec, label = "b1")
    val b2 by animateColorAsState(Pal.surface(pal[1], if (big) .33f else .30f), spec, label = "b2")
    val f0 by animateColorAsState(Pal.surface(pal[1], .46f).copy(alpha = if (big) .45f else .5f), spec, label = "f0")
    val f2 by animateColorAsState(Pal.surface(pal[0], .48f).copy(alpha = if (big) .40f else .46f), spec, label = "f2")
    val p0 by animateColorAsState(Pal.mix(pal[0], Color.White, .2f).copy(alpha = if (big) .32f else .4f), spec, label = "p0")
    val time = remember { mutableFloatStateOf(0f) }
    val playing = Audio.playing
    LaunchedEffect(playing) {
        var last = System.nanoTime()
        while (true) {
            delay(40)
            val n = System.nanoTime()
            val dt = min(.1f, (n - last) / 1e9f); last = n
            time.floatValue += dt * (if (playing) 1.25f else .35f)
        }
    }
    Canvas(modifier) {
        val tt = time.floatValue
        val w = size.width; val h = size.height
        fun ends(a: Float): Pair<Offset, Offset> { val c = cos(a) * .5f; val s = sin(a) * .5f; return Offset(w * (.5f - c), h * (.5f - s)) to Offset(w * (.5f + c), h * (.5f + s)) }
        val (s1, e1) = ends(.8f + tt * .05f)
        drawRect(Brush.linearGradient(0f to b0, (.5f + .18f * sin(tt * .09f)) to b1, 1f to b2, start = s1, end = e1))
        val (s2, e2) = ends(2.4f - tt * .07f + sin(tt * .11f) * .6f)
        drawRect(Brush.linearGradient(0f to f0, (.5f + .22f * sin(tt * .13f + 1f)) to pal[0].copy(alpha = 0f), 1f to f2, start = s2, end = e2), alpha = .75f + .25f * sin(tt * .17f))
        val energy = if (playing) .14f + .08f * sin(tt * 2f) else 0f
        val pb = if (big) Brush.verticalGradient(listOf(p0, Color.Transparent)) else Brush.horizontalGradient(listOf(p0, Color.Transparent))
        drawRect(pb, alpha = min(.9f, energy * (if (big) .8f else 1f)))
        if (big) drawRect(Brush.verticalGradient(0f to Color.Transparent, .45f to Color(0x1E000000), 1f to Color(0x8C000000)))
        else drawRect(Brush.horizontalGradient(listOf(Color(0x0A000000), Color(0x37000000))))
    }
}

@Composable
fun progressState(): Pair<Long, Long> {
    var pos by remember { mutableLongStateOf(Audio.position()) }
    var dur by remember { mutableLongStateOf(Audio.duration()) }
    LaunchedEffect(Unit) { while (true) { pos = Audio.position(); dur = Audio.duration(); delay(250) } }
    return pos to dur
}

@Composable
fun MiniProgress(modifier: Modifier) {
    val (pos, dur) = progressState()
    val f = if (dur > 0) (pos.toFloat() / dur).coerceIn(0f, 1f) else 0f
    Box(modifier.height(2.5.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x2EFFFFFF))) {
        Box(Modifier.fillMaxWidth(f).fillMaxHeight().background(Color.White))
    }
}

// ---------------- Мини-плеер (нижний бар на ПК): свайп — соседний трек, вверх или нажатие — полный плеер ----------------
@Composable
fun MiniPlayer(t: Track, pal: List<Color>) {
    val shape = RoundedCornerShape(20.dp)
    var dx by remember { mutableFloatStateOf(0f) }
    var dy by remember { mutableFloatStateOf(0f) }
    Box(Modifier.padding(horizontal = 10.dp, vertical = 8.dp).fillMaxWidth().height(68.dp).shadow(18.dp, shape).clip(shape).background(Color(0xE61A1D24))
        .border(1.dp, Th.GlassEdge, shape)
        .pointerInput(t.id) {
            detectDragGestures(onDragEnd = {
                if (abs(dx) > abs(dy) && abs(dx) > 110f) { if (dx < 0) Audio.next() else Audio.prev() }
                else if (dy < -60f) Nav.fullPlayer = true
                dx = 0f; dy = 0f
            }, onDragCancel = { dx = 0f; dy = 0f }) { ch, d -> ch.consume(); dx += d.x; dy += d.y }
        }
        .tap(.98f) { Nav.fullPlayer = true }) {
        if (Prefs.liquid) Liquid(pal, false, Modifier.matchParentSize())
        Row(Modifier.fillMaxSize().graphicsLayer { translationX = dx * .35f; alpha = 1f - min(.5f, abs(dx) / 600f) }.padding(start = 9.dp, end = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Cover(t, 48.dp, 10.dp, icon = 18.dp)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Tx(t.title, 14f, Color.White, logo = false)
                Tx(t.artist, 12f, Color.White.copy(alpha = .75f), weight = FontWeight.Normal)
            }
            HeartBtn(t.id, 38.dp, 20.dp, Color.White.copy(alpha = .85f))
            Spacer(Modifier.width(4.dp))
            Box(Modifier.size(44.dp).shadow(8.dp, CircleShape).clip(CircleShape).background(Color.White).tap(.9f) { Audio.toggle() }, contentAlignment = Alignment.Center) {
                SIcon(if (Audio.playing) "pause" else "play", 18.dp, Color.Black)
            }
        }
        MiniProgress(Modifier.align(Alignment.BottomCenter).fillMaxWidth().padding(horizontal = 16.dp).padding(bottom = 3.dp))
    }
}

// ---------------- Полноэкранный плеер (FullPlayer на ПК) ----------------
@Composable
fun FullPlayer() {
    val t = Audio.current
    if (t == null) { LaunchedEffect(Unit) { Nav.fullPlayer = false }; return }
    @Suppress("UNUSED_VARIABLE") val cr = Covers.rev
    val pal = Covers.palette(t) ?: Covers.Fallback
    val scope = rememberCoroutineScope()
    var dy by remember { mutableFloatStateOf(0f) }
    Box(Modifier.fillMaxSize().graphicsLayer { translationY = dy }.background(Color(0xFF07080B))
        .pointerInput(Unit) {
            detectVerticalDragGestures(onDragEnd = {
                if (dy > 260f) { Nav.fullPlayer = false; dy = 0f }
                else scope.launch { val a = Animatable(dy); a.animateTo(0f, tween(200)) { dy = value } }
            }) { _, d -> dy = (dy + d).coerceAtLeast(0f) }
        }
        .clickable(remember { MutableInteractionSource() }, null) { }) {
        if (Prefs.liquid) Liquid(pal, true, Modifier.matchParentSize())
        else Box(Modifier.matchParentSize().background(Brush.verticalGradient(listOf(Pal.surface(pal[0], .32f), Color(0xFF07080B)))))
        Column(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().padding(horizontal = 20.dp)) {
            Row(Modifier.fillMaxWidth().padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                IconBtn("chevD", 42.dp, 22.dp, Color.White) { Nav.fullPlayer = false }
                Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                    Tx("СЕЙЧАС ИГРАЕТ", 10f, Color.White.copy(alpha = .75f), weight = FontWeight.Bold, spacing = 1.5.sp)
                    Tx(Audio.source.ifEmpty { t.album.ifEmpty { "SONORA" } }, 13f, Color.White, Modifier.padding(top = 2.dp), logo = false)
                }
                IconBtn("dots", 42.dp, 18.dp, Color.White) { openTrackMenu(t, null, Audio.queue) }
            }
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                if (FullState.lyrics) LyricsView(t) else CoverRow(t)
            }
            Row(Modifier.fillMaxWidth().padding(top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                HeartBtn(t.id, 44.dp, 23.dp, Color.White.copy(alpha = .85f))
                Column(Modifier.weight(1f).padding(horizontal = 6.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Tx(t.title, 21f, Color.White, Modifier.padding(bottom = 5.dp), align = TextAlign.Center, logo = true)
                    Tx(t.artist, 13.5f, Color.White.copy(alpha = .85f), Modifier.tap { t.artists.firstOrNull()?.let { Nav.fullPlayer = false; Nav.go(Screen.Artist(it)) } }, align = TextAlign.Center)
                }
                IconBtn("plus", 44.dp, 22.dp, Color.White.copy(alpha = .85f)) { openAddToPlaylist(t) }
            }
            SeekBar()
            Row(Modifier.fillMaxWidth().padding(top = 6.dp, bottom = 10.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Ctrl("repeat", 46.dp, 22.dp, Audio.repeat != Player.REPEAT_MODE_OFF, badge = if (Audio.repeat == Player.REPEAT_MODE_ONE) "1" else null) { Audio.cycleRepeat() }
                Ctrl("prev", 56.dp, 30.dp, false) { Audio.prev() }
                Box(Modifier.size(74.dp).shadow(20.dp, CircleShape).clip(CircleShape).background(Color.White).tap(.93f) { Audio.toggle() }, contentAlignment = Alignment.Center) {
                    SIcon(if (Audio.playing) "pause" else "play", 30.dp, Color.Black)
                }
                Ctrl("next", 56.dp, 30.dp, false) { Audio.next() }
                Ctrl("shuffle", 46.dp, 22.dp, Audio.shuffle) { Audio.toggleShuffle() }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 30.dp).padding(bottom = 10.dp).alpha(.9f), horizontalArrangement = Arrangement.SpaceEvenly) {
                Ctrl("timer", 50.dp, 22.dp, Audio.sleepAt > 0) { openTimer() }
                Ctrl("lyrics", 50.dp, 22.dp, FullState.lyrics) { if (t.hasLyrics) FullState.lyrics = !FullState.lyrics else toast("У этого трека пока нет текста") }
                Ctrl("queue", 50.dp, 22.dp, false) { openQueue() }
            }
        }
        if (Audio.sleepAt > 0) SleepPill(Modifier.align(Alignment.TopEnd).statusBarsPadding().padding(top = 58.dp, end = 18.dp))
    }
}

@Composable
fun SleepPill(modifier: Modifier) {
    val tick = rememberTick()
    val shape = RoundedCornerShape(14.dp)
    Row(modifier.clip(shape).background(Color(0x80000000)).border(1.dp, Color(0x26FFFFFF), shape).padding(horizontal = 12.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        SIcon("timer", 13.dp, Color.White)
        Tx(fmt(Audio.sleepAt - tick), 11.5f, Color.White, Modifier.padding(start = 6.dp))
    }
}

@Composable
fun Ctrl(icon: String, size: Dp, iconSize: Dp, on: Boolean, badge: String? = null, onClick: () -> Unit) {
    Box(Modifier.size(size).clip(CircleShape).tap(.88f) { onClick() }, contentAlignment = Alignment.Center) {
        SIcon(icon, iconSize, if (on) Th.Accent3 else Color.White)
        if (badge != null) Text(badge, color = if (on) Th.Accent3 else Color.White, fontSize = 8.sp, fontWeight = FontWeight.Bold)
        if (on) Box(Modifier.align(Alignment.BottomCenter).padding(bottom = 3.dp).size(5.dp).clip(CircleShape).background(Th.Accent3))
    }
}

// обложка по центру и края соседних обложек по бокам; свайп по обложке — следующий/предыдущий трек
@Composable
fun CoverRow(t: Track) {
    val scope = rememberCoroutineScope()
    var dx by remember(t.id) { mutableFloatStateOf(0f) }
    BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        val side = 40.dp
        val size = minOf(maxWidth - (side + 10.dp) * 2, maxHeight - 16.dp, 380.dp)
        Row(verticalAlignment = Alignment.CenterVertically) {
            SidePeek(Audio.neighbor(-1), side, size * .82f, true)
            Spacer(Modifier.width(10.dp))
            Box(Modifier.size(size).graphicsLayer { translationX = dx; rotationZ = dx / 60f; alpha = 1f - min(.4f, abs(dx) / 900f) }
                .shadow(30.dp, RoundedCornerShape(14.dp), spotColor = Color.Black)
                .pointerInput(t.id) {
                    detectHorizontalDragGestures(onDragEnd = {
                        when {
                            dx < -140f -> Audio.next()
                            dx > 140f -> Audio.prev()
                        }
                        scope.launch { val a = Animatable(dx); a.animateTo(0f, tween(220)) { dx = value } }
                    }) { ch, d -> ch.consume(); dx += d }
                }
                .pointerInput(t.id) { detectTapGestures(onDoubleTap = { if (need("Лайки")) Lib.toggleFav(t.id) }) }) {
                Cover(t, size, 14.dp, icon = size * .3f)
            }
            Spacer(Modifier.width(10.dp))
            SidePeek(Audio.neighbor(1), side, size * .82f, false)
        }
    }
}

@Composable
fun SidePeek(t: Track?, w: Dp, h: Dp, left: Boolean) {
    val shape = if (left) RoundedCornerShape(topEnd = 10.dp, bottomEnd = 10.dp) else RoundedCornerShape(topStart = 10.dp, bottomStart = 10.dp)
    Box(Modifier.width(w).height(h).clip(shape).background(Color(0x59000000)).alpha(.5f).tap(.95f) { if (left) Audio.prev() else Audio.next() }) {
        if (t != null) {
            val img = coverImage(t)
            if (img != null) androidx.compose.foundation.Image(img, null, Modifier.fillMaxSize(), contentScale = androidx.compose.ui.layout.ContentScale.Crop,
                alignment = if (left) Alignment.CenterEnd else Alignment.CenterStart)
            else Box(Modifier.fillMaxSize().background(Th.Brand))
        }
    }
}

@Composable
fun SeekBar() {
    val (pos, dur) = progressState()
    var widthPx by remember { mutableFloatStateOf(1f) }
    var drag by remember { mutableFloatStateOf(-1f) }
    val f = if (drag >= 0) drag else if (dur > 0) (pos.toFloat() / dur).coerceIn(0f, 1f) else 0f
    Row(Modifier.fillMaxWidth().padding(top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Tx(fmt(if (drag >= 0) (drag * dur).toLong() else pos), 11f, Color.White.copy(alpha = .85f), Modifier.width(40.dp), align = TextAlign.Center, weight = FontWeight.Medium)
        Box(Modifier.weight(1f).height(28.dp).onSizeChanged { widthPx = it.width.toFloat().coerceAtLeast(1f) }
            .pointerInput(dur) { detectTapGestures { o -> if (dur > 0) Audio.seek(((o.x / widthPx).coerceIn(0f, 1f) * dur).toLong()) } }
            .pointerInput(dur) {
                detectHorizontalDragGestures(onDragStart = { o -> drag = (o.x / widthPx).coerceIn(0f, 1f) },
                    onDragEnd = { if (dur > 0 && drag >= 0) Audio.seek((drag * dur).toLong()); drag = -1f }, onDragCancel = { drag = -1f }) { ch, d ->
                    ch.consume(); drag = (drag + d / widthPx).coerceIn(0f, 1f)
                }
            }, contentAlignment = Alignment.CenterStart) {
            Box(Modifier.fillMaxWidth().height(3.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x4DFFFFFF)))
            Box(Modifier.fillMaxWidth(f).height(3.dp).clip(RoundedCornerShape(2.dp)).background(Color.White))
            Box(Modifier.graphicsLayer { translationX = f * widthPx - 6.dp.toPx() }.size(if (drag >= 0) 16.dp else 12.dp).shadow(4.dp, CircleShape).clip(CircleShape).background(Color.White))
        }
        Tx(fmt(dur), 11f, Color.White.copy(alpha = .85f), Modifier.width(40.dp), align = TextAlign.Center, weight = FontWeight.Medium)
    }
}

// текст песни: LRC — караоке с подсветкой строки, обычный текст — просто листается
@Composable
fun LyricsView(t: Track) {
    val lrc = remember(t.id) { parseLrc(t.lyrics) }
    if (!t.hasLyrics) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            SIcon("lyrics", 34.dp, Color.White.copy(alpha = .6f))
            Tx("У этого трека пока нет текста", 14f, Color.White.copy(alpha = .8f), Modifier.padding(top = 12.dp))
        }
        return
    }
    val shape = RoundedCornerShape(18.dp)
    Box(Modifier.fillMaxSize().padding(vertical = 10.dp).clip(shape).background(Color(0x33000000))) {
        if (lrc == null) {
            Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp)) {
                Text(t.lyrics.trim(), color = Color.White.copy(alpha = .92f), fontSize = 17.sp, lineHeight = 27.sp, fontWeight = FontWeight.SemiBold)
            }
        } else {
            val (pos, _) = progressState()
            val active = lrc.indexOfLast { it.ms <= pos + 250 }
            val st = rememberLazyListState()
            LaunchedEffect(active) { if (active >= 0) st.animateScrollToItem((active - 2).coerceAtLeast(0)) }
            LazyColumn(Modifier.fillMaxSize(), state = st, contentPadding = PaddingValues(vertical = 20.dp, horizontal = 18.dp)) {
                itemsIndexed(lrc) { i, l ->
                    val on = i == active
                    Text(l.text.ifEmpty { "♪" }, color = if (on) Color.White else Color.White.copy(alpha = if (i < active) .38f else .5f), fontSize = if (on) 21.sp else 18.sp,
                        fontWeight = FontWeight.Bold, lineHeight = 28.sp,
                        modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(10.dp)).background(if (on) Color(0x143B82F6) else Color.Transparent)
                            .tap(.98f) { Audio.seek(l.ms) }.padding(vertical = 7.dp, horizontal = 8.dp))
                }
            }
        }
    }
}
