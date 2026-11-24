package com.sonora.app

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.Logout
import androidx.compose.material.icons.automirrored.rounded.QueueMusic
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.Email
import androidx.compose.material.icons.rounded.Favorite
import androidx.compose.material.icons.rounded.FavoriteBorder
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.Person
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Shuffle
import androidx.compose.material.icons.rounded.WorkspacePremium
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.util.Calendar
import java.util.TimeZone

// ---------------- Жанр получаса: один жанр на получасовой отрезок по местному времени ----------------
fun halfHourSlot(): Long { val t = now(); return (t + TimeZone.getDefault().getOffset(t)) / (30 * 60_000L) }
fun halfHourLeft(): Long { val t = now() / 1000 * 1000; val local = t + TimeZone.getDefault().getOffset(t); val slotMs = 30 * 60_000L; return (slotMs - local % slotMs) / 1000 }

@Composable
fun Countdown(): String {
    var left by remember { mutableLongStateOf(halfHourLeft()) }
    LaunchedEffect(Unit) {
        while (true) {
            left = halfHourLeft()
            delay(1020L - now() % 1000)      // тик сразу после смены секунды — отсчёт не перескакивает
        }
    }
    return "%02d:%02d".format(left / 60, left % 60)
}

// ---------------- Главная ----------------
@Composable
fun HomeScreen() {
    val tracks = Catalog.tracks
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val slot = halfHourSlot()
    val (genre, genreTracks) = remember(slot, tracks) {
        val genres = Catalog.genres().sorted()
        if (genres.isEmpty()) "" to emptyList()
        else {
            var g = genres[java.util.Random(slot * 7919).nextInt(genres.size)]
            val prev = genres[java.util.Random((slot - 1) * 7919).nextInt(genres.size)]
            if (g == prev && genres.size > 1) g = genres[(genres.indexOf(g) + 1) % genres.size]
            g to tracks.filter { t -> Catalog.splitGenres(t.genre).any { it.equals(g, true) } }.shuffled(java.util.Random(slot))
        }
    }
    val hour = Calendar.getInstance().get(Calendar.HOUR_OF_DAY)
    val hello = when (hour) { in 5..11 -> "Доброе утро"; in 12..17 -> "Добрый день"; in 18..22 -> "Добрый вечер"; else -> "Доброй ночи" }

    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 16.dp)) {
        item {
            Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 12.dp, top = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                Logo(30.dp)
                Spacer(Modifier.weight(1f))
                PlusPill()
                Spacer(Modifier.width(10.dp))
                Avatar(Auth.current?.name, 34.dp) { Nav.tab(Screen.Profile) }
            }
        }
        item { Text(hello + (Auth.current?.name?.let { ", $it" } ?: ""), color = Th.Txt, fontSize = 26.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(start = 16.dp, top = 18.dp, end = 16.dp)) }
        if (!Catalog.loaded) item { Box(Modifier.fillMaxWidth().padding(40.dp), contentAlignment = Alignment.Center) { CircularProgressIndicator(color = Th.AccentLight) } }
        Catalog.error?.let { e -> item { Text(e, color = Th.Dim, modifier = Modifier.padding(16.dp)) } }

        item {
            val tiles = mutableListOf<Triple<String, Brush, () -> Unit>>()
            tiles.add(Triple("Мне нравится", Th.Pink) { Nav.go(Screen.Liked) })
            tiles.add(Triple("Недавнее", Th.Brand) { Nav.go(Screen.Recent) })
            Lib.playlists.take(4).forEach { p -> tiles.add(Triple(p.name, Brush.linearGradient(listOf(Th.hex(p.color), Color(0xFF0B1220)))) { Nav.go(Screen.Pl(p.id)) }) }
            Column(Modifier.padding(start = 16.dp, end = 16.dp, top = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                tiles.chunked(2).forEach { row ->
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        row.forEach { (name, brush, click) ->
                            Row(Modifier.weight(1f).height(56.dp).clip(RoundedCornerShape(6.dp)).background(Th.Card2).clickable { click() }, verticalAlignment = Alignment.CenterVertically) {
                                Box(Modifier.size(56.dp).background(brush), contentAlignment = Alignment.Center) {
                                    Icon(if (name == "Мне нравится") Icons.Rounded.Favorite else if (name == "Недавнее") Icons.Rounded.History else Icons.AutoMirrored.Rounded.QueueMusic, null, tint = Color.White)
                                }
                                Text(name, color = Th.Txt, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(horizontal = 10.dp))
                            }
                        }
                        if (row.size == 1) Spacer(Modifier.weight(1f))
                    }
                }
            }
        }

        if (genreTracks.isNotEmpty()) {
            item {
                SectionTitle("Жанр получаса: $genre", "Каждые 30 минут новый жанр · " + Countdown())
                LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                    itemsIndexed(genreTracks, key = { _, t -> "g" + t.id }) { i, t -> TrackCard(t, genreTracks, i) }
                }
            }
        }
        if (tracks.isNotEmpty()) {
            item {
                SectionTitle("Новое в SONORA")
                val fresh = tracks.take(20)
                LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                    itemsIndexed(fresh, key = { _, t -> "n" + t.id }) { i, t -> TrackCard(t, fresh, i) }
                }
            }
            item {
                SectionTitle("Исполнители")
                val artists = Catalog.artists().take(20)
                LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    itemsIndexed(artists, key = { _, a -> "a$a" }) { _, a ->
                        Column(Modifier.width(110.dp).clickable { Nav.go(Screen.Artist(a)) }, horizontalAlignment = Alignment.CenterHorizontally) {
                            Cover(Catalog.byArtist(a).firstOrNull(), 110.dp, 55.dp)
                            Spacer(Modifier.height(8.dp))
                            Text(a, color = Th.Txt, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text("Исполнитель", color = Th.Dim, fontSize = 12.sp)
                        }
                    }
                }
            }
            item { SectionTitle("Популярное") }
            val top = tracks.sortedByDescending { it.plays }.take(10)
            itemsIndexed(top, key = { _, t -> "p" + t.id }) { i, t -> TrackRow(t, top, i, number = i + 1) }
        }
    }
}

@Composable
fun PlusPill() {
    val active = Plus.active
    Box(Modifier.clip(RoundedCornerShape(14.dp)).background(if (active) Th.PlusBrush else Brush.linearGradient(listOf(Color(0x293B82F6), Color(0x298B5CF6))))
        .border(1.dp, Color(0x7360A5FA), RoundedCornerShape(14.dp)).clickable { Nav.go(Screen.PlusS) }.padding(horizontal = 12.dp, vertical = 6.dp)) {
        Text(if (active) "PLUS · " + ((Plus.until - now()) / 86_400_000L).coerceAtLeast(0) + " дн" else "PLUS", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.Bold)
    }
}

// ---------------- Поиск ----------------
@Composable
fun SearchScreen() {
    var q by remember { mutableStateOf("") }
    val results = remember(q, Catalog.tracks) { Catalog.search(q) }
    val artists = remember(q, Catalog.tracks) { if (q.isBlank()) emptyList() else Catalog.artists().filter { it.contains(q.trim(), true) }.take(10) }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 16.dp)) {
        item {
            Text("Поиск", color = Th.Txt, fontSize = 26.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(start = 16.dp, top = 18.dp))
            SonoraField(q, { q = it }, "Трек, исполнитель или жанр", Modifier.fillMaxWidth().padding(16.dp), leading = Icons.Rounded.Search)
        }
        if (q.isBlank()) {
            item { SectionTitle("Жанры") }
            val genres = Catalog.genres()
            val palette = listOf(0xFF1D4ED8, 0xFF2563EB, 0xFF0369A1, 0xFF1E40AF, 0xFF3B82F6, 0xFF0284C7, 0xFF1E3A8A, 0xFF60A5FA)
            item {
                Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    genres.chunked(2).forEachIndexed { ri, row ->
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            row.forEachIndexed { ci, g ->
                                val c = Color(palette[(ri * 2 + ci) % palette.size])
                                Box(Modifier.weight(1f).height(84.dp).clip(RoundedCornerShape(8.dp)).background(Brush.linearGradient(listOf(c, Color(0xFF0B1220)))).clickable { Nav.go(Screen.Genre(g)) }.padding(12.dp)) {
                                    Text(g, color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.Bold, maxLines = 2)
                                }
                            }
                            if (row.size == 1) Spacer(Modifier.weight(1f))
                        }
                    }
                }
            }
        } else {
            if (artists.isNotEmpty()) item {
                SectionTitle("Исполнители")
                LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    itemsIndexed(artists) { _, a ->
                        Column(Modifier.width(96.dp).clickable { Nav.go(Screen.Artist(a)) }, horizontalAlignment = Alignment.CenterHorizontally) {
                            Cover(Catalog.byArtist(a).firstOrNull(), 96.dp, 48.dp)
                            Text(a, color = Th.Txt, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 6.dp))
                        }
                    }
                }
            }
            item { SectionTitle("Треки", if (results.isEmpty()) "Ничего не найдено" else null) }
            itemsIndexed(results, key = { _, t -> "s" + t.id }) { i, t -> TrackRow(t, results, i) }
        }
    }
}

// ---------------- Моя музыка ----------------
@Composable
fun LibraryScreen() {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    var create by remember { mutableStateOf(false) }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 16.dp)) {
        item {
            Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 8.dp, top = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                Avatar(Auth.current?.name, 34.dp) { Nav.tab(Screen.Profile) }
                Text("Моя музыка", color = Th.Txt, fontSize = 24.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(start = 12.dp).weight(1f))
                IconButton(onClick = { if (need("Плейлисты")) create = true }) { Icon(Icons.Rounded.Add, "Новый плейлист", tint = Th.Txt) }
            }
        }
        if (Auth.current == null) item {
            Column(Modifier.padding(16.dp).fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Th.Card2).padding(16.dp)) {
                Text("Войди, чтобы лайкать треки и собирать плейлисты", color = Th.Txt, fontWeight = FontWeight.SemiBold)
                Text("Медиатека синхронизируется с версией для ПК.", color = Th.Dim, fontSize = 13.sp, modifier = Modifier.padding(top = 4.dp, bottom = 12.dp))
                GradientButton("Войти") { Nav.auth = true }
            }
        }
        item { LibRow(Th.Pink, Icons.Rounded.Favorite, "Мне нравится", "Плейлист · ${Lib.fav.size} тр.") { Nav.go(Screen.Liked) } }
        item { LibRow(Th.Brand, Icons.Rounded.History, "Недавно прослушанные", "${Lib.recent.size} тр.") { Nav.go(Screen.Recent) } }
        itemsIndexed(Lib.playlists.toList(), key = { _, p -> p.id }) { _, p ->
            LibRow(Brush.linearGradient(listOf(Th.hex(p.color), Color(0xFF0B1220))), Icons.AutoMirrored.Rounded.QueueMusic, p.name, "Плейлист · ${p.trackIds.size} тр.") { Nav.go(Screen.Pl(p.id)) }
        }
        if (Lib.favArtists.isNotEmpty()) {
            item { SectionTitle("Любимые исполнители") }
            itemsIndexed(Lib.favArtists.toList(), key = { _, a -> "fa$a" }) { _, a ->
                Row(Modifier.fillMaxWidth().clickable { Nav.go(Screen.Artist(a)) }.padding(horizontal = 16.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Cover(Catalog.byArtist(a).firstOrNull(), 56.dp, 28.dp)
                    Column(Modifier.padding(start = 12.dp)) { Text(a, color = Th.Txt, fontSize = 15.sp); Text("Исполнитель", color = Th.Dim, fontSize = 13.sp) }
                }
            }
        }
    }
    if (create) NameDialog("Новый плейлист", "") { name -> create = false; if (name != null) { val p = Lib.createPlaylist(name); Nav.go(Screen.Pl(p.id)) } }
}

@Composable
fun LibRow(brush: Brush, icon: androidx.compose.ui.graphics.vector.ImageVector, title: String, sub: String, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { onClick() }.padding(horizontal = 16.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(56.dp).clip(RoundedCornerShape(6.dp)).background(brush), contentAlignment = Alignment.Center) { Icon(icon, null, tint = Color.White) }
        Column(Modifier.padding(start = 12.dp)) {
            Text(title, color = Th.Txt, fontSize = 15.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(sub, color = Th.Dim, fontSize = 13.sp)
        }
    }
}

@Composable
fun NameDialog(title: String, initial: String, done: (String?) -> Unit) {
    var name by remember { mutableStateOf(initial) }
    AlertDialog(onDismissRequest = { done(null) }, containerColor = Th.Card2,
        title = { Text(title, color = Th.Txt) },
        text = { SonoraField(name, { name = it }, "Название", Modifier.fillMaxWidth()) },
        confirmButton = { TextButton(onClick = { done(name) }) { Text("Готово", color = Th.AccentLight) } },
        dismissButton = { TextButton(onClick = { done(null) }) { Text("Отмена", color = Th.Dim) } })
}

// ---------------- Список треков (плейлист, любимые, исполнитель, жанр) ----------------
@Composable
fun TrackListScreen(title: String, subtitle: String, color: Color, ids: List<String>?, list: List<Track>, playlist: Playlist? = null, artist: String? = null, coverTrack: Track? = null, likeAll: Boolean = true) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    var rename by remember { mutableStateOf(false) }
    var del by remember { mutableStateOf(false) }
    val missing = if (ids == null) 0 else ids.size - list.size
    val allFav = list.isNotEmpty() && list.all { Lib.isFav(it.id) }
    val playingThis = Audio.playing && list.any { it.id == Audio.current?.id }
    LazyColumn(Modifier.fillMaxSize().background(Brush.verticalGradient(0f to color.copy(alpha = 0.55f), 0.35f to Th.Bg)), contentPadding = PaddingValues(bottom = 16.dp)) {
        item {
            IconButton(onClick = { Nav.back() }, modifier = Modifier.padding(top = 4.dp, start = 4.dp)) { Icon(Icons.AutoMirrored.Rounded.ArrowBack, "Назад", tint = Th.Txt) }
            Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                if (coverTrack != null) Cover(coverTrack, 200.dp, if (artist != null) 100.dp else 8.dp)
                else Box(Modifier.size(200.dp).clip(RoundedCornerShape(8.dp)).background(Brush.linearGradient(listOf(color, Color(0xFF0B1220)))), contentAlignment = Alignment.Center) {
                    Icon(if (title == "Мне нравится") Icons.Rounded.Favorite else Icons.AutoMirrored.Rounded.QueueMusic, null, tint = Color.White, modifier = Modifier.size(80.dp))
                }
            }
            Text(title, color = Th.Txt, fontSize = 26.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 18.dp), maxLines = 2, overflow = TextOverflow.Ellipsis)
            Text(subtitle, color = Th.Dim, fontSize = 13.sp, modifier = Modifier.padding(start = 16.dp, top = 4.dp))
            Row(Modifier.fillMaxWidth().padding(start = 8.dp, end = 16.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                if (likeAll && list.isNotEmpty()) IconButton(onClick = {
                    if (need("Лайки")) { val n = Lib.setFavAll(list.map { it.id }, !allFav); toast(if (!allFav) "Добавлено в любимые: $n" else "Убрано из любимых: $n") }
                }) { Icon(if (allFav) Icons.Rounded.Favorite else Icons.Rounded.FavoriteBorder, "Лайкнуть всё", tint = if (allFav) Th.Heart else Th.Dim, modifier = Modifier.size(26.dp)) }
                if (artist != null) {
                    val on = Lib.isFavArtist(artist)
                    Box(Modifier.padding(start = 8.dp).clip(RoundedCornerShape(16.dp)).border(1.dp, if (on) Th.AccentLight else Color(0x55FFFFFF), RoundedCornerShape(16.dp))
                        .clickable { if (need("Любимые исполнители")) Lib.toggleFavArtist(artist) }.padding(horizontal = 14.dp, vertical = 6.dp)) {
                        Text(if (on) "Вы подписаны" else "Подписаться", color = Th.Txt, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
                    }
                }
                if (playlist != null) {
                    IconButton(onClick = { rename = true }) { Icon(Icons.Rounded.Edit, "Переименовать", tint = Th.Dim) }
                    IconButton(onClick = { del = true }) { Icon(Icons.Rounded.Delete, "Удалить", tint = Th.Dim) }
                }
                Spacer(Modifier.weight(1f))
                IconButton(onClick = { Audio.play(list, 0, shuffleOn = true) }) { Icon(Icons.Rounded.Shuffle, "Перемешать", tint = if (Audio.shuffle) Th.AccentLight else Th.Dim, modifier = Modifier.size(28.dp)) }
                Spacer(Modifier.width(6.dp))
                Box(Modifier.size(56.dp).clip(CircleShape).background(Th.Brand).clickable(enabled = list.isNotEmpty()) { if (playingThis) Audio.toggle() else Audio.play(list, 0, shuffleOn = false) }, contentAlignment = Alignment.Center) {
                    Icon(if (playingThis) Icons.Rounded.Pause else Icons.Rounded.PlayArrow, "Слушать", tint = Color.White, modifier = Modifier.size(32.dp))
                }
            }
            if (list.isEmpty()) Text(if (missing > 0) "" else "Здесь пока пусто", color = Th.Dim, modifier = Modifier.padding(16.dp))
        }
        itemsIndexed(list, key = { i, t -> "$i" + t.id }) { i, t -> TrackRow(t, list, i, playlist) }
        if (missing > 0) item {
            Text("Ещё $missing тр. из Яндекс Музыки, Spotify, Deezer или с компьютера. Пока они играют только в версии для ПК.",
                color = Th.Dim, fontSize = 12.5.sp, modifier = Modifier.padding(16.dp))
        }
    }
    if (rename && playlist != null) NameDialog("Переименовать", playlist.name) { n -> rename = false; if (n != null) Lib.rename(playlist, n) }
    if (del && playlist != null) AlertDialog(onDismissRequest = { del = false }, containerColor = Th.Card2,
        title = { Text("Удалить «${playlist.name}»?", color = Th.Txt) },
        text = { Text("Сами треки останутся в SONORA.", color = Th.Dim) },
        confirmButton = { TextButton(onClick = { del = false; Lib.delete(playlist); Nav.back() }) { Text("Удалить", color = Th.Heart) } },
        dismissButton = { TextButton(onClick = { del = false }) { Text("Отмена", color = Th.Dim) } })
}

@Composable
fun ScreenFor(s: Screen) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    when (s) {
        Screen.Home -> HomeScreen()
        Screen.Search -> SearchScreen()
        Screen.Library -> LibraryScreen()
        Screen.Profile -> ProfileScreen()
        Screen.PlusS -> PlusScreen()
        Screen.Liked -> { val l = Lib.fav.mapNotNull { Catalog.get(it) }; TrackListScreen("Мне нравится", "${Lib.fav.size} тр.", Color(0xFFD81B72), Lib.fav, l, likeAll = false) }
        Screen.Recent -> { val l = Lib.recent.mapNotNull { Catalog.get(it) }; TrackListScreen("Недавно прослушанные", "${l.size} тр.", Th.Accent, Lib.recent, l) }
        is Screen.Pl -> {
            val p = Lib.playlist(s.id)
            if (p == null) { LaunchedEffect(s) { Nav.back() } }
            else { val l = p.trackIds.mapNotNull { Catalog.get(it) }; TrackListScreen(p.name, "Плейлист · ${p.trackIds.size} тр.", Th.hex(p.color), p.trackIds, l, playlist = p, coverTrack = l.firstOrNull()) }
        }
        is Screen.Artist -> { val l = Catalog.byArtist(s.name); TrackListScreen(s.name, "Исполнитель · ${l.size} тр.", Covers.color(l.firstOrNull()) ?: Th.Accent, null, l, artist = s.name, coverTrack = l.firstOrNull()) }
        is Screen.Genre -> { val l = Catalog.tracks.filter { t -> Catalog.splitGenres(t.genre).any { it.equals(s.name, true) } }; TrackListScreen(s.name, "Жанр · ${l.size} тр.", Th.Accent, null, l) }
    }
}

// ---------------- Профиль ----------------
@Composable
fun ProfileScreen() {
    val s = Auth.current
    val scope = rememberCoroutineScope()
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Spacer(Modifier.height(20.dp))
        Avatar(s?.name, 110.dp)
        Spacer(Modifier.height(14.dp))
        Text(s?.name ?: "Гость", color = Th.Txt, fontSize = 24.sp, fontWeight = FontWeight.Bold)
        Text(s?.email ?: "Без аккаунта можно только слушать", color = Th.Dim, fontSize = 14.sp)
        Spacer(Modifier.height(22.dp))
        if (s == null) {
            GradientButton("Войти или зарегистрироваться", modifier = Modifier.fillMaxWidth()) { Nav.auth = true }
        } else {
            Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Brush.linearGradient(listOf(Color(0x293B82F6), Color(0x298B5CF6))))
                .border(1.dp, Color(0x5560A5FA), RoundedCornerShape(16.dp)).clickable { Nav.go(Screen.PlusS) }.padding(16.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Rounded.WorkspacePremium, null, tint = Color(0xFFA5B4FC))
                    Text("SONORA Plus", color = Th.Txt, fontWeight = FontWeight.Bold, fontSize = 16.sp, modifier = Modifier.padding(start = 8.dp))
                }
                Text(if (Plus.active) "Активен · осталось " + ((Plus.until - now()) / 86_400_000L) + " дн" else "${Plus.PRICE} ₽ за ${Plus.DAYS} дней · без автосписаний", color = Th.Dim, fontSize = 13.sp, modifier = Modifier.padding(top = 4.dp))
            }
            Spacer(Modifier.height(12.dp))
            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Th.Card2).padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("Синхронизация с ПК", color = Th.Txt, fontWeight = FontWeight.SemiBold)
                    Text(Lib.syncStatus.ifEmpty { "Лайки и плейлисты общие с компьютером" }, color = Th.Dim, fontSize = 13.sp)
                }
                IconButton(onClick = { scope.launch { Cloud.syncNow() } }) { Icon(Icons.Rounded.Refresh, "Синхронизировать", tint = Th.AccentLight) }
            }
            Spacer(Modifier.height(20.dp))
            GhostButton("Выйти из аккаунта", Modifier.fillMaxWidth()) { Auth.signOut(); toast("Ты вышел из аккаунта") }
        }
        Spacer(Modifier.height(30.dp))
        Text("SONORA для Android · версия " + BuildConfig.VERSION_NAME, color = Th.Dim, fontSize = 12.sp)
    }
}

// ---------------- SONORA Plus ----------------
@Composable
fun PlusScreen() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var tick by remember { mutableLongStateOf(0L) }
    LaunchedEffect(Unit) { Plus.refresh(); while (true) { tick = now(); delay(1020L - now() % 1000) } }
    val active = Plus.active
    fun pay(type: String) {
        if (!need("SONORA Plus")) return
        ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(Plus.payUrl(type))))
        Plus.poll { toast("SONORA Plus активен — спасибо!") }
    }
    Column(Modifier.fillMaxSize().background(Brush.verticalGradient(0f to Color(0x553B82F6), 0.4f to Th.Bg)).verticalScroll(rememberScrollState())) {
        IconButton(onClick = { Nav.back() }, modifier = Modifier.padding(4.dp)) { Icon(Icons.AutoMirrored.Rounded.ArrowBack, "Назад", tint = Th.Txt) }
        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Box(Modifier.size(72.dp).clip(RoundedCornerShape(20.dp)).background(Th.PlusBrush), contentAlignment = Alignment.Center) {
                Icon(Icons.Rounded.WorkspacePremium, null, tint = Color.White, modifier = Modifier.size(40.dp))
            }
            Text("SONORA Plus", color = Th.Txt, fontSize = 28.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 14.dp))
            Text(if (active) "Подписка активна" else "${Plus.PRICE} ₽ за ${Plus.DAYS} дней · без автосписаний", color = Th.Dim, fontSize = 14.sp, modifier = Modifier.padding(top = 4.dp))
            Spacer(Modifier.height(20.dp))
            if (active) {
                @Suppress("UNUSED_VARIABLE") val t = tick
                Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0x0FFFFFFF)).border(1.dp, Color(0x29FFFFFF), RoundedCornerShape(16.dp)).padding(16.dp)) {
                    Text("До окончания Plus", color = Th.Dim, fontSize = 12.5.sp)
                    Text(Plus.leftText(), color = Th.Txt, fontSize = 26.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(vertical = 4.dp))
                    val frac = ((Plus.until - now()).toFloat() / (Plus.DAYS * 86_400_000f)).coerceIn(0.02f, 1f)
                    Box(Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)).background(Color(0x14FFFFFF))) {
                        Box(Modifier.fillMaxWidth(frac).height(6.dp).clip(RoundedCornerShape(3.dp)).background(Th.PlusBrush))
                    }
                }
                Spacer(Modifier.height(16.dp))
            }
            listOf("До 100 публичных треков вместо двух", "Файлы до 40 МБ", "Значок PLUS в профиле", "Ранний доступ к новым функциям", "Поддержка независимой площадки").forEach {
                Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Rounded.Check, null, tint = Color(0xFFA5B4FC), modifier = Modifier.size(20.dp))
                    Text(it, color = Th.Txt, fontSize = 15.sp, modifier = Modifier.padding(start = 10.dp))
                }
            }
            Spacer(Modifier.height(20.dp))
            GradientButton((if (active) "Продлить картой · " else "Оплатить картой · ") + "${Plus.PRICE} ₽", Th.PlusBrush, Modifier.fillMaxWidth()) { pay("AC") }
            Spacer(Modifier.height(10.dp))
            GhostButton("Кошельком ЮMoney", Modifier.fillMaxWidth()) { pay("PC") }
            Spacer(Modifier.height(10.dp))
            GhostButton("Я оплатил — проверить", Modifier.fillMaxWidth()) {
                scope.launch {
                    val was = Plus.until
                    Plus.refresh()
                    toast(if (Plus.active) (if (Plus.until > was) "SONORA Plus активен — спасибо!" else "Plus активен") else "Оплата пока не пришла. Обычно это до минуты. Если прошло больше 10 минут — напиши в поддержку свой ID")
                }
            }
            if (active) Text("Продление прибавит ${Plus.DAYS} дней к текущему сроку.", color = Th.Dim, fontSize = 12.5.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 12.dp))
            Auth.current?.let { s ->
                Row(Modifier.padding(top = 18.dp).clickable {
                    (ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("SONORA ID", s.uid)); toast("ID скопирован")
                }, verticalAlignment = Alignment.CenterVertically) {
                    Text("ID аккаунта: " + s.uid, color = Th.Dim, fontSize = 11.5.sp)
                    Icon(Icons.Rounded.ContentCopy, "Скопировать", tint = Th.Dim, modifier = Modifier.padding(start = 6.dp).size(14.dp))
                }
            }
            Spacer(Modifier.height(30.dp))
        }
    }
}

// ---------------- Вход и регистрация ----------------
@Composable
fun AuthScreen() {
    var reg by remember { mutableStateOf(false) }
    var name by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var pass by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var err by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    fun submit() {
        if (busy) return
        err = null; busy = true
        scope.launch {
            try {
                if (reg) Auth.signUp(name, email, pass) else Auth.signIn(email, pass)
                Nav.auth = false
                toast("Привет, " + (Auth.current?.name ?: "") + "!")
            } catch (e: Exception) { err = e.message ?: "Ошибка" }
            busy = false
        }
    }
    Column(Modifier.fillMaxSize().background(Brush.verticalGradient(0f to Color(0xFF0B1A3A), 0.5f to Th.Bg)).clickable(enabled = false) {}.verticalScroll(rememberScrollState()).padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally) {
        Row(Modifier.fillMaxWidth()) { Spacer(Modifier.weight(1f)); TextButton(onClick = { Nav.auth = false }) { Text("Позже", color = Th.Dim) } }
        Spacer(Modifier.height(20.dp))
        Logo(44.dp)
        Text("Вся твоя музыка в одном месте", color = Th.Dim, fontSize = 14.sp, modifier = Modifier.padding(top = 10.dp, bottom = 28.dp))
        Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Th.Card2).padding(4.dp)) {
            listOf(false to "Вход", true to "Регистрация").forEach { (v, label) ->
                Box(Modifier.weight(1f).height(40.dp).clip(RoundedCornerShape(10.dp)).background(if (reg == v) Th.Brand else Brush.linearGradient(listOf(Color.Transparent, Color.Transparent)))
                    .clickable { reg = v; err = null }, contentAlignment = Alignment.Center) { Text(label, color = Th.Txt, fontWeight = FontWeight.SemiBold) }
            }
        }
        Spacer(Modifier.height(18.dp))
        if (reg) { SonoraField(name, { name = it }, "Имя", Modifier.fillMaxWidth(), leading = Icons.Rounded.Person); Spacer(Modifier.height(10.dp)) }
        SonoraField(email, { email = it }, "Почта", Modifier.fillMaxWidth(), leading = Icons.Rounded.Email)
        Spacer(Modifier.height(10.dp))
        SonoraField(pass, { pass = it }, "Пароль", Modifier.fillMaxWidth(), password = true, leading = Icons.Rounded.Lock)
        err?.let { Text(it, color = Color(0xFFFF7A9C), fontSize = 13.sp, modifier = Modifier.padding(top = 10.dp)) }
        Spacer(Modifier.height(18.dp))
        if (busy) CircularProgressIndicator(color = Th.AccentLight)
        else GradientButton(if (reg) "Создать аккаунт" else "Войти", modifier = Modifier.fillMaxWidth()) { submit() }
        if (!reg) TextButton(onClick = {
            if (email.isBlank()) { err = "Введи почту, и мы пришлём ссылку для сброса пароля"; return@TextButton }
            scope.launch { try { Auth.reset(email); toast("Письмо для сброса пароля отправлено") } catch (e: Exception) { err = e.message } }
        }, modifier = Modifier.padding(top = 6.dp)) { Text("Забыли пароль?", color = Th.AccentLight) }
        Spacer(Modifier.height(12.dp))
        Text("Аккаунт тот же, что в SONORA для ПК: лайки, плейлисты и Plus общие.", color = Th.Dim, fontSize = 12.5.sp, textAlign = TextAlign.Center)
        Spacer(Modifier.height(8.dp))
        TextButton(onClick = { Nav.auth = false }) { Text("Продолжить без входа — только слушать", color = Th.Dim, fontSize = 13.sp) }
        Icon(Icons.AutoMirrored.Rounded.Logout, null, tint = Color.Transparent, modifier = Modifier.size(1.dp))
    }
}
