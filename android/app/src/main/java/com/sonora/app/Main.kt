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
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.QueueMusic
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.LibraryMusic
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Person
import androidx.compose.material.icons.rounded.Repeat
import androidx.compose.material.icons.rounded.RepeatOne
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Shuffle
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class SonoraApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Audio.init(this)
        Auth.init(this)
        Catalog.init(this)
        Lib.init(this)
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

@Composable
fun Root() {
    BackHandler(enabled = Nav.stack.size > 1 || Nav.fullPlayer || Nav.auth || Nav.addTrack != null || Nav.tab != Screen.Home) { Nav.back() }
    LaunchedEffect(Auth.current?.uid) { if (Auth.current != null) { try { Cloud.syncNow() } catch (_: Exception) { }; try { Plus.refresh() } catch (_: Exception) { } } }
    Box(Modifier.fillMaxSize().background(Th.Bg)) {
        Column(Modifier.fillMaxSize()) {
            Box(Modifier.weight(1f).fillMaxWidth().statusBarsPadding()) { ScreenFor(Nav.top) }
            if (Audio.current != null) MiniPlayer()
            BottomBar()
        }
        AnimatedVisibility(Nav.fullPlayer, enter = slideInVertically { it }, exit = slideOutVertically { it }) { FullPlayer() }
        if (Nav.auth) Box(Modifier.fillMaxSize().background(Th.Bg).statusBarsPadding().navigationBarsPadding()) { AuthScreen() }
        if (Nav.addTrack != null) AddToPlaylistDialog()
    }
}

@Composable
fun BottomBar() {
    val items = listOf(Triple(Screen.Home, Icons.Rounded.Home, "Главная"), Triple(Screen.Search, Icons.Rounded.Search, "Поиск"),
        Triple(Screen.Library, Icons.Rounded.LibraryMusic, "Медиатека"), Triple(Screen.Profile, Icons.Rounded.Person, "Профиль"))
    Row(Modifier.fillMaxWidth().background(Brush.verticalGradient(listOf(Color(0xF0000000), Color.Black))).navigationBarsPadding().height(60.dp),
        horizontalArrangement = Arrangement.SpaceAround, verticalAlignment = Alignment.CenterVertically) {
        for ((s, icon, label) in items) {
            val on = Nav.tab == s
            Column(Modifier.weight(1f).clickable { if (s == Screen.Profile && Auth.current == null) Nav.auth = true else Nav.tab(s) }.padding(vertical = 6.dp),
                horizontalAlignment = Alignment.CenterHorizontally) {
                Icon(icon, label, tint = if (on) Th.AccentLight else Th.Dim, modifier = Modifier.size(24.dp))
                Text(label, color = if (on) Th.Txt else Th.Dim, fontSize = 10.5.sp)
            }
        }
    }
}

@Composable
fun progress(): Pair<Long, Long> {
    var pos by remember { mutableLongStateOf(0L) }
    var dur by remember { mutableLongStateOf(0L) }
    LaunchedEffect(Unit) { while (true) { pos = Audio.position(); dur = Audio.duration(); delay(250) } }
    return pos to dur
}

@Composable
fun MiniPlayer() {
    val t = Audio.current ?: return
    val c = Covers.color(t) ?: Th.Accent
    val (pos, dur) = progress()
    Box(Modifier.padding(horizontal = 8.dp, vertical = 4.dp).clip(RoundedCornerShape(10.dp))
        .background(Brush.horizontalGradient(listOf(c.copy(alpha = .55f).compositeOver(Color(0xFF101010)), Color(0xFF141414))))
        .clickable { Nav.fullPlayer = true }) {
        Column {
            Row(Modifier.fillMaxWidth().padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Cover(t, 42.dp, 6.dp)
                Column(Modifier.weight(1f).padding(horizontal = 10.dp)) {
                    Text(t.title, color = Th.Txt, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(t.artist, color = Th.Dim, fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                HeartButton(t.id)
                IconButton(onClick = { Audio.toggle() }) {
                    Icon(if (Audio.playing) Icons.Rounded.Pause else Icons.Rounded.PlayArrow, "Играть", tint = Th.Txt, modifier = Modifier.size(30.dp))
                }
            }
            val f = if (dur > 0) (pos.toFloat() / dur).coerceIn(0f, 1f) else 0f
            Box(Modifier.fillMaxWidth().height(2.dp).background(Color(0x33FFFFFF))) {
                Box(Modifier.fillMaxWidth(f).height(2.dp).background(Th.Brand))
            }
        }
    }
}

private fun Color.compositeOver(bg: Color): Color {
    val a = alpha
    return Color(red * a + bg.red * (1 - a), green * a + bg.green * (1 - a), blue * a + bg.blue * (1 - a), 1f)
}

@Composable
fun FullPlayer() {
    val t = Audio.current
    if (t == null) { LaunchedEffect(Unit) { Nav.fullPlayer = false }; return }
    val c = Covers.color(t) ?: Th.Accent
    val (pos, dur) = progress()
    var dragging by remember { mutableStateOf(false) }
    var dragVal by remember { mutableFloatStateOf(0f) }
    var showQueue by remember { mutableStateOf(false) }
    Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(c.copy(alpha = .85f).compositeOver(Color.Black), Color(0xFF0A0A0A), Color.Black)))
        .clickable(enabled = false) { }) {
        Column(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().padding(horizontal = 22.dp)) {
            Row(Modifier.fillMaxWidth().padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = { Nav.fullPlayer = false }) { Icon(Icons.Rounded.KeyboardArrowDown, "Свернуть", tint = Th.Txt, modifier = Modifier.size(32.dp)) }
                Text("Сейчас играет", color = Th.Txt, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f), textAlign = androidx.compose.ui.text.style.TextAlign.Center)
                IconButton(onClick = { showQueue = !showQueue }) { Icon(Icons.AutoMirrored.Rounded.QueueMusic, "Очередь", tint = if (showQueue) Th.AccentLight else Th.Txt) }
            }
            if (showQueue) {
                Column(Modifier.weight(1f).padding(top = 8.dp)) {
                    Text("Очередь", color = Th.Txt, fontSize = 18.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(bottom = 8.dp))
                    androidx.compose.foundation.lazy.LazyColumn(Modifier.weight(1f)) {
                        items(Audio.queue.size) { i ->
                            val q = Audio.queue[i]
                            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(8.dp)).clickable { Audio.jump(i) }.padding(6.dp), verticalAlignment = Alignment.CenterVertically) {
                                Cover(q, 40.dp, 5.dp)
                                Column(Modifier.weight(1f).padding(start = 10.dp)) {
                                    Text(q.title, color = if (i == Audio.index) Th.AccentLight else Th.Txt, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                    Text(q.artist, color = Th.Dim, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                }
                            }
                        }
                    }
                }
            } else {
                Spacer(Modifier.weight(.6f))
                Box(Modifier.fillMaxWidth().aspectRatio(1f).clip(RoundedCornerShape(14.dp)).background(Color(0xFF1C1C1E)), contentAlignment = Alignment.Center) {
                    Cover(t, 400.dp, 14.dp, Modifier.fillMaxSize())
                }
                Spacer(Modifier.weight(.5f))
            }
            Row(Modifier.fillMaxWidth().padding(top = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(t.title, color = Th.Txt, fontSize = 22.sp, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(t.artist, color = Th.Dim, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.clickable { Nav.fullPlayer = false; Nav.go(Screen.Artist(t.artists.first())) })
                }
                HeartButton(t.id, 28.dp)
            }
            val f = if (dur > 0) (pos.toFloat() / dur).coerceIn(0f, 1f) else 0f
            Slider(value = if (dragging) dragVal else f, onValueChange = { dragging = true; dragVal = it },
                onValueChangeFinished = { Audio.seek((dragVal * dur).toLong()); dragging = false },
                colors = SliderDefaults.colors(thumbColor = Color.White, activeTrackColor = Color.White, inactiveTrackColor = Color(0x40FFFFFF)),
                modifier = Modifier.padding(top = 8.dp))
            Row(Modifier.fillMaxWidth()) {
                Text(fmt(if (dragging) (dragVal * dur).toLong() else pos), color = Th.Dim, fontSize = 12.sp)
                Spacer(Modifier.weight(1f))
                Text(fmt(dur), color = Th.Dim, fontSize = 12.sp)
            }
            Row(Modifier.fillMaxWidth().padding(vertical = 14.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Ctl(Icons.Rounded.Shuffle, if (Audio.shuffle) Th.AccentLight else Th.Txt, 26) { Audio.toggleShuffle() }
                Ctl(Icons.Rounded.SkipPrevious, Th.Txt, 40) { Audio.prev() }
                Box(Modifier.size(70.dp).clip(CircleShape).background(Color.White).clickable { Audio.toggle() }, contentAlignment = Alignment.Center) {
                    Icon(if (Audio.playing) Icons.Rounded.Pause else Icons.Rounded.PlayArrow, "Играть", tint = Color.Black, modifier = Modifier.size(40.dp))
                }
                Ctl(Icons.Rounded.SkipNext, Th.Txt, 40) { Audio.next() }
                Ctl(if (Audio.repeat == Player.REPEAT_MODE_ONE) Icons.Rounded.RepeatOne else Icons.Rounded.Repeat,
                    if (Audio.repeat != Player.REPEAT_MODE_OFF) Th.AccentLight else Th.Txt, 26) { Audio.cycleRepeat() }
            }
            Spacer(Modifier.height(10.dp))
        }
    }
}

@Composable
private fun Ctl(icon: ImageVector, tint: Color, size: Int, onClick: () -> Unit) {
    IconButton(onClick = onClick, modifier = Modifier.size((size + 16).dp)) { Icon(icon, null, tint = tint, modifier = Modifier.size(size.dp)) }
}

@Suppress("unused") private val keepWidth = Modifier.width(0.dp)
