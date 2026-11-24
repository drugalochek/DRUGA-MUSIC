package com.sonora.app

import android.widget.Toast
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.PlaylistAdd
import androidx.compose.material.icons.automirrored.rounded.QueueMusic
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Favorite
import androidx.compose.material.icons.rounded.FavoriteBorder
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.Person
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

// ---------------- Цвета и шрифты (как в версии для ПК) ----------------
object Th {
    val Bg = Color(0xFF000000)
    val Card = Color(0xFF121212)
    val Card2 = Color(0xFF1C1C1E)
    val Txt = Color(0xFFFFFFFF)
    val Dim = Color(0xFFA7A7A7)
    val Accent = Color(0xFF3B82F6)
    val AccentLight = Color(0xFF60A5FA)
    val Heart = Color(0xFFFF4D94)
    val Brand = Brush.linearGradient(listOf(Color(0xFF60A5FA), Color(0xFF1D4ED8)))
    val Pink = Brush.linearGradient(listOf(Color(0xFFFF6BAE), Color(0xFFD81B72)))
    val PlusBrush = Brush.linearGradient(listOf(Color(0xFF3B82F6), Color(0xFF8B5CF6)))
    val Logo = FontFamily(Font(R.font.widock))
    fun hex(s: String?): Color = try { Color(android.graphics.Color.parseColor(if (s.isNullOrBlank()) "#3b82f6" else s)) } catch (_: Exception) { Accent }
}

val sonoraColors = darkColorScheme(primary = Th.Accent, secondary = Th.AccentLight, background = Th.Bg, surface = Th.Card2, onPrimary = Color.White, onSurface = Th.Txt, onBackground = Th.Txt)

fun toast(msg: String) = Toast.makeText(Audio.ctx, msg, Toast.LENGTH_SHORT).show()

// ---------------- Навигация ----------------
sealed class Screen {
    data object Home : Screen()
    data object Search : Screen()
    data object Library : Screen()
    data object Profile : Screen()
    data object Liked : Screen()
    data object Recent : Screen()
    data object PlusS : Screen()
    data class Pl(val id: String) : Screen()
    data class Artist(val name: String) : Screen()
    data class Genre(val name: String) : Screen()
}

object Nav {
    val stack = mutableStateListOf<Screen>(Screen.Home)
    var fullPlayer by mutableStateOf(false)
    var auth by mutableStateOf(false)
    var addTrack by mutableStateOf<Track?>(null)
    val top: Screen get() = stack.last()
    val tab: Screen get() = stack.first()
    fun go(s: Screen) { stack.add(s) }
    fun tab(s: Screen) { stack.clear(); stack.add(s) }
    fun back(): Boolean {
        if (addTrack != null) { addTrack = null; return true }
        if (auth) { auth = false; return true }
        if (fullPlayer) { fullPlayer = false; return true }
        if (stack.size > 1) { stack.removeAt(stack.size - 1); return true }
        if (stack.first() != Screen.Home) { tab(Screen.Home); return true }
        return false
    }
}

// без аккаунта можно только слушать
fun need(what: String): Boolean {
    if (Auth.current != null) return true
    toast("$what — только для зарегистрированных. Войди или создай аккаунт.")
    Nav.auth = true
    return false
}

// ---------------- Общие элементы ----------------
@Composable
fun SonoraTheme(content: @Composable () -> Unit) = MaterialTheme(colorScheme = sonoraColors, content = content)

@Composable
fun Cover(t: Track?, size: Dp, corner: Dp = 6.dp, modifier: Modifier = Modifier) {
    val img by produceState(t?.let { Covers.peek(it) }, t?.id) { value = t?.let { Covers.get(it) } }
    val shape = RoundedCornerShape(corner)
    Box(modifier.size(size).clip(shape).background(Th.Brand), contentAlignment = Alignment.Center) {
        val i = img
        if (i != null) Image(i, null, Modifier.size(size), contentScale = ContentScale.Crop)
        else Icon(Icons.Rounded.MusicNote, null, tint = Color.White, modifier = Modifier.size(size * 0.42f))
    }
}

@Composable
fun Logo(size: Dp = 30.dp) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Image(painterResource(R.drawable.logo), null, Modifier.size(size))
        Spacer(Modifier.width(8.dp))
        Text("SONORA", color = Th.Txt, fontFamily = Th.Logo, fontSize = (size.value * 0.72f).sp, letterSpacing = 1.sp)
    }
}

@Composable
fun Avatar(name: String?, size: Dp, onClick: (() -> Unit)? = null) {
    val m = Modifier.size(size).clip(CircleShape).background(Th.Brand).let { if (onClick != null) it.clickable { onClick() } else it }
    Box(m, contentAlignment = Alignment.Center) {
        if (name.isNullOrBlank()) Icon(Icons.Rounded.Person, null, tint = Color.White, modifier = Modifier.size(size * 0.55f))
        else Text(name.take(1).uppercase(), color = Color.White, fontWeight = FontWeight.Bold, fontSize = (size.value * 0.42f).sp)
    }
}

@Composable
fun SectionTitle(text: String, sub: String? = null) {
    Column(Modifier.padding(start = 16.dp, end = 16.dp, top = 22.dp, bottom = 10.dp)) {
        Text(text, color = Th.Txt, fontSize = 21.sp, fontWeight = FontWeight.Bold)
        if (sub != null) Text(sub, color = Th.Dim, fontSize = 13.sp)
    }
}

@Composable
fun GradientButton(text: String, brush: Brush = Th.Brand, modifier: Modifier = Modifier, enabled: Boolean = true, onClick: () -> Unit) {
    Box(modifier.height(50.dp).clip(RoundedCornerShape(25.dp)).background(if (enabled) brush else Brush.linearGradient(listOf(Th.Card2, Th.Card2)))
        .clickable(enabled = enabled) { onClick() }.padding(horizontal = 22.dp), contentAlignment = Alignment.Center) {
        Text(text, color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp)
    }
}

@Composable
fun GhostButton(text: String, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Box(modifier.height(46.dp).clip(RoundedCornerShape(23.dp)).border(1.dp, Color(0x33FFFFFF), RoundedCornerShape(23.dp))
        .clickable { onClick() }.padding(horizontal = 20.dp), contentAlignment = Alignment.Center) {
        Text(text, color = Th.Txt, fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
    }
}

@Composable
fun HeartButton(id: String, size: Dp = 22.dp) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val on = Lib.isFav(id)
    IconButton(onClick = { if (need("Лайки")) Lib.toggleFav(id) }) {
        Icon(if (on) Icons.Rounded.Favorite else Icons.Rounded.FavoriteBorder, if (on) "Убрать из любимых" else "В любимые",
            tint = if (on) Th.Heart else Th.Dim, modifier = Modifier.size(size))
    }
}

@Composable
fun SonoraField(value: String, onChange: (String) -> Unit, placeholder: String, modifier: Modifier = Modifier, password: Boolean = false, leading: ImageVector? = null) {
    TextField(
        value = value, onValueChange = onChange, singleLine = true, modifier = modifier,
        placeholder = { Text(placeholder, color = Th.Dim) },
        leadingIcon = leading?.let { { Icon(it, null, tint = Th.Dim) } },
        visualTransformation = if (password) androidx.compose.ui.text.input.PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None,
        shape = RoundedCornerShape(12.dp),
        colors = TextFieldDefaults.colors(
            focusedContainerColor = Th.Card2, unfocusedContainerColor = Th.Card2,
            focusedIndicatorColor = Color.Transparent, unfocusedIndicatorColor = Color.Transparent,
            cursorColor = Th.AccentLight, focusedTextColor = Th.Txt, unfocusedTextColor = Th.Txt,
        ),
        textStyle = TextStyle(fontSize = 15.sp),
    )
}

// Строка трека: обложка, название, исполнитель, сердечко и меню
@Composable
fun TrackRow(t: Track, list: List<Track>, index: Int, playlist: Playlist? = null, number: Int? = null) {
    val cur = Audio.current?.id == t.id
    var menu by remember { mutableStateOf(false) }
    Row(Modifier.fillMaxWidth().clickable { Audio.play(list, index) }.padding(start = 16.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically) {
        if (number != null) { Text("$number", color = if (cur) Th.AccentLight else Th.Dim, fontSize = 14.sp, modifier = Modifier.width(26.dp)) }
        Cover(t, 48.dp, 4.dp)
        Column(Modifier.weight(1f).padding(start = 12.dp)) {
            Text(t.title, color = if (cur) Th.AccentLight else Th.Txt, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Medium)
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (t.explicit) {
                    Box(Modifier.padding(end = 5.dp).clip(RoundedCornerShape(2.dp)).background(Th.Dim).padding(horizontal = 4.dp)) { Text("E", color = Color.Black, fontSize = 9.sp, fontWeight = FontWeight.Bold) }
                }
                Text(t.artist, color = Th.Dim, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
        HeartButton(t.id)
        Box {
            IconButton(onClick = { menu = true }) { Icon(Icons.Rounded.MoreVert, "Ещё", tint = Th.Dim) }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }, modifier = Modifier.background(Th.Card2)) {
                DropdownMenuItem(text = { Text("Играть следующим") }, onClick = { menu = false; Audio.playNext(t); toast("Будет следующим") })
                DropdownMenuItem(text = { Text("Добавить в очередь") }, onClick = { menu = false; Audio.addToQueue(t); toast("Добавлено в очередь") })
                DropdownMenuItem(text = { Text("Добавить в плейлист") }, onClick = { menu = false; if (need("Плейлисты")) Nav.addTrack = t })
                DropdownMenuItem(text = { Text("К исполнителю") }, onClick = { menu = false; Nav.fullPlayer = false; Nav.go(Screen.Artist(t.artists.first())) })
                if (playlist != null) DropdownMenuItem(text = { Text("Убрать из плейлиста") }, onClick = { menu = false; Lib.removeFrom(playlist, t.id) })
            }
        }
    }
}

// Карточка трека для горизонтальных лент
@Composable
fun TrackCard(t: Track, list: List<Track>, index: Int, size: Dp = 140.dp) {
    Column(Modifier.width(size).clickable { Audio.play(list, index) }) {
        Cover(t, size, 8.dp)
        Spacer(Modifier.height(8.dp))
        Text(t.title, color = if (Audio.current?.id == t.id) Th.AccentLight else Th.Txt, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(t.artist, color = Th.Dim, fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

// Диалог «Добавить в плейлист»
@Composable
fun AddToPlaylistDialog() {
    val t = Nav.addTrack ?: return
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    var name by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = { Nav.addTrack = null },
        containerColor = Th.Card2,
        title = { Text("Добавить в плейлист", color = Th.Txt) },
        text = {
            Column {
                Text(t.artist + " — " + t.title, color = Th.Dim, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Spacer(Modifier.height(10.dp))
                LazyColumn(Modifier.height((minOf(Lib.playlists.size, 5) * 52).dp)) {
                    items(Lib.playlists, key = { it.id }) { p ->
                        Row(Modifier.fillMaxWidth().clickable { if (Lib.addTo(p, t.id)) toast("Добавлено в «${p.name}»") else toast("Уже есть в «${p.name}»"); Nav.addTrack = null }.padding(vertical = 6.dp),
                            verticalAlignment = Alignment.CenterVertically) {
                            Box(Modifier.size(40.dp).clip(RoundedCornerShape(6.dp)).background(Th.hex(p.color)), contentAlignment = Alignment.Center) {
                                Icon(Icons.AutoMirrored.Rounded.QueueMusic, null, tint = Color.White)
                            }
                            Column(Modifier.padding(start = 12.dp)) {
                                Text(p.name, color = Th.Txt, fontSize = 15.sp)
                                Text("${p.trackIds.size} тр.", color = Th.Dim, fontSize = 12.sp)
                            }
                        }
                    }
                }
                Spacer(Modifier.height(10.dp))
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    SonoraField(name, { name = it }, "Новый плейлист", Modifier.weight(1f), leading = Icons.AutoMirrored.Rounded.PlaylistAdd)
                    IconButton(onClick = { val p = Lib.createPlaylist(name); Lib.addTo(p, t.id); toast("Создан «${p.name}»"); Nav.addTrack = null }) {
                        Icon(Icons.Rounded.Add, "Создать", tint = Th.AccentLight)
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = { Nav.addTrack = null }) { Text("Закрыть", color = Th.AccentLight) } },
    )
}

fun fmt(ms: Long): String { val s = (ms / 1000).coerceAtLeast(0); return "%d:%02d".format(s / 60, s % 60) }
