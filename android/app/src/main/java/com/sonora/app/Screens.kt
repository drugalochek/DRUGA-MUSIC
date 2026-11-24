package com.sonora.app

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
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
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

// ---------------- Общее ----------------
object Pages {
    private val states = HashMap<String, LazyListState>()
    fun state(key: String): LazyListState = states.getOrPut(key) { LazyListState() }
    fun forget(depth: Int) { states.keys.filter { (it.substringBefore(':').toIntOrNull() ?: 0) > depth }.forEach { states.remove(it) } }
    var bottomPad: Dp = 170.dp
}

@Composable
fun Page(key: String, content: LazyListScope.() -> Unit) {
    val k = "${Nav.stack.size}:$key"
    val st = remember(k) { Pages.state(k) }
    LazyColumn(Modifier.fillMaxSize(), state = st, contentPadding = PaddingValues(top = 4.dp, bottom = Pages.bottomPad), content = content)
}

@Composable
fun HRow(content: LazyListScope.() -> Unit) {
    LazyRow(contentPadding = PaddingValues(horizontal = 20.dp), horizontalArrangement = Arrangement.spacedBy(14.dp), modifier = Modifier.padding(top = 2.dp, bottom = 6.dp), content = content)
}

@Composable
fun rememberTick(): Long {
    var t by remember { mutableLongStateOf(now()) }
    LaunchedEffect(Unit) { while (true) { delay(1020L - now() % 1000); t = now() } }
    return t
}

// нажали на трек: играть и (как на ПК) открыть полноэкранный плеер
fun playTrack(list: List<Track>, idx: Int, from: String) {
    val t = list.getOrNull(idx) ?: return
    if (Audio.current?.id == t.id) { if (!Audio.playing) Audio.toggle(); Nav.fullPlayer = true; return }
    Audio.play(list, idx, from = from)
    if (Prefs.openFull) Nav.fullPlayer = true
}

// ---------------- Строка трека (TrackRow на ПК) ----------------
@Composable
fun TrackRow(t: Track, list: List<Track>, idx: Int, from: String, playlist: Playlist? = null) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val playing = Audio.current?.id == t.id
    val shape = RoundedCornerShape(12.dp)
    Row(Modifier.padding(horizontal = 12.dp).fillMaxWidth().clip(shape)
        .then(if (playing) Modifier.background(Th.Soft).border(1.dp, Color(0x4D3B82F6), shape) else Modifier)
        .tap(.99f, onLong = { openTrackMenu(t, playlist, list) }) { playTrack(list, idx, from) }
        .padding(start = 8.dp, end = 2.dp, top = 7.dp, bottom = 7.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(46.dp)) {
            Cover(t, 46.dp, 10.dp, icon = 18.dp)
            if (playing) Box(Modifier.fillMaxSize().clip(RoundedCornerShape(10.dp)).background(Color(0x59000000)), contentAlignment = Alignment.Center) {
                SIcon(if (Audio.playing) "pause" else "play", 16.dp, Color.White)
            }
        }
        Column(Modifier.weight(1f).padding(start = 12.dp, end = 6.dp)) {
            Tx(t.title, 14f, if (playing) Th.Accent3 else Th.Txt, Modifier.padding(bottom = 2.dp), logo = false)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Tx(t.artist + (if (t.album.isEmpty()) "" else " · " + t.album), 11.5f, Th.Dim, Modifier.weight(1f, fill = false))
                if (t.explicit) Badge("18+", Color(0x33FFFFFF), Th.Txt)
                if (t.hasLyrics) Badge("ТЕКСТ", Color(0x3360A5FA), Th.Accent3)
                if (t.ownerId != null && t.ownerId == Auth.current?.uid) Badge("МОЙ", Color(0x333B82F6), Th.Accent3)
            }
        }
        Tx(fmtSec(t.duration), 11.5f, Th.Dim, Modifier.padding(end = 2.dp))
        HeartBtn(t.id, 34.dp, 18.dp)
        IconBtn("dots", 30.dp, 15.dp, Th.Dim) { openTrackMenu(t, playlist, list) }
    }
}

fun LazyListScope.trackItems(list: List<Track>, from: String, playlist: Playlist? = null, max: Int = Int.MAX_VALUE) {
    val shown = if (list.size > max) list.take(max) else list
    itemsIndexed(shown) { i, t -> TrackRow(t, list, i, from, playlist) }
}

// ---------------- Карточки ----------------
@Composable
fun MixCard(m: Mix, size: Dp = 168.dp) {
    Column(Modifier.width(size).tap(.97f) { Nav.go(Screen.Mix(m.id)) }) {
        Box(Modifier.size(size).shadow(14.dp, RoundedCornerShape(16.dp), spotColor = Color(0xFF1D4ED8))) {
            Mosaic(m.tracks, size, 16.dp, m.brush, m.icon, 64.dp)
            Box(Modifier.size(size).clip(RoundedCornerShape(16.dp))) {
                TopShade(size * .5f)
                CardKicker(m.tag, if (size >= 160.dp) 19f else 15f)
                CardStripe(m.name)
            }
            Box(Modifier.align(Alignment.BottomEnd).padding(end = 10.dp, bottom = 38.dp).size(44.dp).shadow(8.dp, CircleShape).clip(CircleShape).background(Color.White)
                .tap(.9f) { if (m.tracks.isNotEmpty()) { Audio.play(m.tracks, 0, from = m.name); if (Prefs.openFull) Nav.fullPlayer = true } }, contentAlignment = Alignment.Center) {
                SIcon("play", 18.dp, Color.Black)
            }
        }
        Tx(m.name, 14f, modifier = Modifier.padding(top = 10.dp, bottom = 3.dp), lines = 2, logo = false)
        if (m.sub.isNotEmpty()) Tx(m.sub, 11.5f, Th.Dim, lines = 2)
    }
}

@Composable
fun PctCard(name: String, tracks: List<Track>, size: Dp = 168.dp) {
    Column(Modifier.width(size).tap(.97f) { Nav.go(Screen.Mix("__pct_$name")) }) {
        Box(Modifier.size(size).shadow(14.dp, RoundedCornerShape(16.dp), spotColor = Color(0xFF1D4ED8)).clip(RoundedCornerShape(16.dp))) {
            CoverFill(tracks.firstOrNull { !it.cover.isNullOrEmpty() }, Modifier.fillMaxSize(), Th.Brand, "user", 56.dp)
            TopShade(size * .5f)
            Text("100%", Modifier.align(Alignment.TopStart).padding(start = 12.dp, top = 8.dp), color = Color.White, fontFamily = Th.Logo, fontSize = 24.sp, fontWeight = FontWeight.Black,
                style = androidx.compose.ui.text.TextStyle(shadow = androidx.compose.ui.graphics.Shadow(Color(0xD9000000), blurRadius = 10f)))
            CardStripe(name)
        }
        Tx("100% $name", 13.5f, modifier = Modifier.padding(top = 10.dp, bottom = 3.dp), logo = false)
        Tx(tn(tracks.size), 11.5f, Th.Dim)
    }
}

@Composable
fun ArtistCard(a: ArtistInfo) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    Column(Modifier.width(92.dp).tap(.95f) { Nav.go(Screen.Artist(a.name)) }, horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.width(92.dp).height(84.dp)) {
            Box(Modifier.align(Alignment.Center).size(84.dp).shadow(10.dp, CircleShape).clip(CircleShape)) {
                CoverFill(a.coverTrack, Modifier.fillMaxSize(), Th.Hot, "user", 30.dp)
            }
            if (Lib.isFavArtist(a.name)) Box(Modifier.align(Alignment.TopEnd).padding(end = 4.dp).size(22.dp).clip(CircleShape).background(Th.Pink), contentAlignment = Alignment.Center) {
                SIcon("heartFill", 11.dp, Color.White)
            }
        }
        Tx(a.name, 12f, modifier = Modifier.padding(top = 8.dp), lines = 2, align = TextAlign.Center, lineHeight = 15.sp)
    }
}

@Composable
fun PlaylistCard(p: Playlist, size: Dp = 132.dp) {
    val tracks = p.trackIds.mapNotNull { trackOf(it) }
    Column(Modifier.width(size).tap(.97f) { Nav.go(Screen.Mix(p.id)) }) {
        Box(Modifier.size(size).shadow(10.dp, RoundedCornerShape(14.dp))) {
            Mosaic(tracks, size, 14.dp, Th.grad(p.color, p.color).let { Brush.linearGradient(listOf(Th.hex(p.color), Th.shade(Th.hex(p.color), -30))) }, "music", 40.dp)
            Box(Modifier.size(size).clip(RoundedCornerShape(14.dp))) { TopShade(60.dp, .55f); CardStripe(null) }
            Box(Modifier.align(Alignment.TopEnd).padding(6.dp).size(26.dp).clip(CircleShape).background(Color(0x99000000))
                .tap(.9f) { confirm("Удалить «${p.name}»?", "Сами треки останутся в SONORA.", "Удалить") { Lib.delete(p); toast("Плейлист удалён") } }, contentAlignment = Alignment.Center) {
                SIcon("close", 12.dp, Color.White.copy(alpha = .8f))
            }
        }
        Tx(p.name, 13.5f, modifier = Modifier.padding(top = 10.dp, bottom = 3.dp), lines = 2, logo = false)
        Tx(tn(tracks.size), 11f, Th.Dim)
    }
}

@Composable
fun NewPlaylistCard(size: Dp = 132.dp) {
    Column(Modifier.width(size).tap(.97f) { if (need("Плейлисты")) Sheets.open("Новый плейлист") { PlaylistForm(null, null) } }) {
        Box(Modifier.size(size).clip(RoundedCornerShape(14.dp)).background(Th.Card).dashed(Th.BorderHi, 14.dp), contentAlignment = Alignment.Center) { SIcon("plus", 38.dp, Th.Accent) }
        Tx("Новый плейлист", 13.5f, modifier = Modifier.padding(top = 10.dp), logo = false)
    }
}

@Composable
fun LCover(brush: Brush, icon: String, round: Boolean = false) {
    Box(Modifier.size(50.dp).clip(RoundedCornerShape(if (round) 25.dp else 10.dp)).background(brush), contentAlignment = Alignment.Center) {
        SIcon(icon, 20.dp, Color.White, fill = if (icon == "heartFill") true else null)
    }
}

@Composable
fun LRow(cover: @Composable () -> Unit, title: String, meta: String, onClick: () -> Unit) {
    Row(Modifier.padding(horizontal = 16.dp).fillMaxWidth().clip(RoundedCornerShape(12.dp)).tap(.99f) { onClick() }.padding(horizontal = 4.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically) {
        cover()
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Tx(title, 14f, modifier = Modifier.padding(bottom = 3.dp), logo = false)
            Tx(meta, 11.5f, Th.Dim)
        }
        SIcon("chevR", 15.dp, Th.Dim)
    }
}

// баннер SONORA Plus на главной — виден, пока подписки нет или она заканчивается
@Composable
fun PlusBanner() {
    val ending = Plus.active && Plus.until - now() < 3 * 86_400_000L
    val shape = RoundedCornerShape(16.dp)
    Row(Modifier.padding(horizontal = 16.dp, vertical = 8.dp).fillMaxWidth().clip(shape).background(Th.PlusSoft).border(1.dp, Th.PlusEdge, shape).tap(.99f) { openPlus() }
        .padding(horizontal = 14.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(42.dp).clip(CircleShape).background(Th.PlusGold), contentAlignment = Alignment.Center) { SIcon("crown", 20.dp, Color.White, stroke = 2.3f) }
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text("SONORA PLUS", color = Th.Txt, fontFamily = Th.Logo, fontSize = 16.sp)
            Tx(if (ending) "Подписка заканчивается — продли, и дни прибавятся к текущему сроку."
               else "До 100 публичных треков, файлы до 40 МБ и значок PLUS — ${Plus.PRICE} ₽ за ${Plus.DAYS} дней.", 12f, Th.Dim, Modifier.padding(top = 2.dp), lines = 3, lineHeight = 16.sp)
        }
        Box(Modifier.clip(RoundedCornerShape(12.dp)).background(Th.PlusGold).padding(horizontal = 14.dp, vertical = 8.dp)) { Tx(if (ending) "Продлить" else "Подключить", 12.5f, Color.White) }
    }
}

@Composable
fun GuestBanner() {
    val shape = RoundedCornerShape(12.dp)
    Row(Modifier.padding(horizontal = 16.dp, vertical = 8.dp).fillMaxWidth().glass(shape).padding(start = 14.dp, end = 12.dp, top = 12.dp, bottom = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(22.dp).clip(CircleShape).background(Th.Accent), contentAlignment = Alignment.Center) { SIcon("user", 12.dp, Color.White) }
        Tx("Вы в гостевом режиме — данные сохраняются только на этом телефоне.", 12.5f, Th.Txt, Modifier.weight(1f).padding(horizontal = 12.dp), lines = 3, weight = FontWeight.Normal)
        Box(Modifier.clip(RoundedCornerShape(10.dp)).background(Color.White).tap(.95f) { openWelcome() }.padding(horizontal = 12.dp, vertical = 7.dp)) { Tx("Войти", 13f, Color.Black) }
    }
}

// ---------------- Главная ----------------
@Composable
fun HomePage() {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val tracks = Catalog.tracks
    val slot = halfHourSlot()
    val hour = remember(slot, tracks, r) { Mixes.genreOfHour() }
    val mixes = remember(tracks, r) { Mixes.forYou() }
    val pct = remember(tracks, r) { Mixes.pct() }
    val fresh = remember(tracks) { Mixes.fresh() }
    val favArtists = remember(tracks, r) { Lib.favArtists.map { Mixes.artist(it) } }
    Page("home") {
        if (Auth.current == null) item { GuestBanner() }
        if (!Plus.active || Plus.until - now() < 3 * 86_400_000L) item { PlusBanner() }
        if (!Catalog.loaded) { item { Empty("music", "Загружаем каталог…", "Подключаемся к серверу.") }; return@Page }
        Catalog.error?.let { e -> item { Note(e) } }
        if (favArtists.isNotEmpty()) {
            item { SHead("Любимые исполнители", favArtists.size.toString()) }
            item { HRow { items(favArtists.size) { ArtistCard(favArtists[it]) } } }
        }
        if (hour != null) {
            item { val tk = rememberTick(); SHead("Жанр получаса", "Каждые 30 минут новый жанр · " + Mixes.countdown().also { tk.hashCode() }) }
            item { HRow { item { MixCard(hour) } } }
        }
        item { SHead("Миксы для тебя", if (mixes.isNotEmpty()) "Из любимых" else "") }
        item { if (mixes.isEmpty()) Note("Лайкай треки и исполнителей ♥ — они появятся здесь.") else HRow { items(mixes.size) { MixCard(mixes[it]) } } }
        item { SHead("100% для вас") }
        item { if (pct.isEmpty()) Note("Лайкай треки и исполнителей ♥ — они появятся здесь.") else HRow { items(pct.size) { PctCard(pct[it].first, pct[it].second) } } }
        item { SHead("Новинки для вас", if (fresh.isNotEmpty()) tn(fresh.size) else "") }
        if (fresh.isEmpty()) {
            item { Note("Свежих треков пока нет") }
            val popular = tracks.sortedByDescending { it.plays }.take(8)
            if (popular.isNotEmpty()) { item { SHead("Популярное", tn(popular.size)) }; trackItems(popular, "Популярное") }
        } else trackItems(fresh, "Новинки для вас")
    }
}

// ---------------- Исследовать ----------------
@Composable
fun ExplorePage() {
    var filter by remember { mutableStateOf("all") }
    var seed by remember { mutableLongStateOf(now()) }
    val tracks = Catalog.tracks
    val artists = remember(tracks) { Mixes.artists(tracks) }
    val (title, list) = remember(filter, seed, tracks) {
        when (filter) {
            "recent" -> "Недавние" to tracks.sortedByDescending { it.addedAt }
            "long" -> "Долгие" to tracks.sortedByDescending { it.duration }
            "short" -> "Короткие" to tracks.sortedBy { it.duration }
            "random" -> "Случайно" to Mixes.shuffle(tracks, "r$seed")
            else -> "Все треки" to tracks
        }
    }
    Page("explore") {
        item { PTitle("Исследовать") }
        item { SHead("Все исполнители", if (artists.isNotEmpty()) artists.size.toString() else "") }
        item { if (artists.isEmpty()) Note("—") else HRow { items(minOf(artists.size, 80)) { ArtistCard(artists[it]) } } }
        item { Spacer(Modifier.height(14.dp)) }
        item {
            Pills(listOf("all", "recent", "long", "short", "random"), listOf("Все треки", "Недавние", "Долгие", "Короткие", "Случайно"), filter) {
                filter = it; if (it == "random") seed = now()
            }
        }
        item { SHead(title, tn(list.size)) }
        trackItems(list, title)
    }
}

// ---------------- Поиск ----------------
@Composable
fun SearchPage() {
    var q by remember { mutableStateOf(Search.q) }
    val qq = q.trim()
    val tracks = remember(qq, Catalog.tracks) { Catalog.search(qq) }
    val artists = remember(qq, Catalog.tracks) { if (qq.isEmpty()) emptyList() else Mixes.artists(Catalog.tracks).filter { it.name.contains(qq, true) }.take(20) }
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val pls = if (qq.isEmpty()) emptyList() else Lib.playlists.filter { it.name.contains(qq, true) }
    @Suppress("UNUSED_VARIABLE") val er = Ext.rev
    var ext by remember { mutableStateOf<Pair<List<Track>, List<Track>>?>(null) }
    var extBusy by remember { mutableStateOf(false) }
    LaunchedEffect(qq, er) {
        ext = if (qq.length < 2) null else Ext.cachedSearch(qq)
        if (qq.length < 2 || ext != null) { extBusy = false; return@LaunchedEffect }
        extBusy = true
        delay(450)
        ext = try { Ext.searchAll(qq) } catch (_: Exception) { null }
        extBusy = false
    }
    Page("search") {
        item { PTitle("Поиск") }
        item { Box(Modifier.padding(horizontal = 16.dp)) { Input(q, { q = it; Search.q = it }, "Треки, исполнители, плейлисты...", icon = "search") } }
        if (qq.isEmpty()) {
            val genres = Catalog.tracks.flatMap { Mixes.genres(it.genre) }.groupingBy { it }.eachCount().entries.sortedByDescending { it.value }.map { it.key }.take(24)
            if (genres.isNotEmpty()) {
                item { SHead("Жанры", genres.size.toString()) }
                val palette = listOf(0xFF1D4ED8, 0xFF2563EB, 0xFF0369A1, 0xFF1E40AF, 0xFF3B82F6, 0xFF0284C7, 0xFF1E3A8A, 0xFF60A5FA)
                genres.chunked(2).forEachIndexed { ri, row ->
                    item {
                        Row(Modifier.padding(horizontal = 16.dp, vertical = 5.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            row.forEachIndexed { ci, g ->
                                val c = Color(palette[(ri * 2 + ci) % palette.size])
                                val shape = RoundedCornerShape(16.dp)
                                Box(Modifier.weight(1f).height(84.dp).clip(shape).background(Brush.linearGradient(listOf(c, Th.shade(c, -55)))).border(1.dp, Th.GlassEdge, shape)
                                    .tap(.97f) { Nav.go(Screen.Mix("__genre_$g")) }.padding(14.dp)) {
                                    Text(Mixes.genreName(g).uppercase(), color = Color.White, fontFamily = Th.Logo, fontSize = 15.sp, maxLines = 2)
                                }
                            }
                            if (row.size == 1) Spacer(Modifier.weight(1f))
                        }
                    }
                }
            }
            return@Page
        }
        val extEmpty = ext == null || (ext!!.first.isEmpty() && ext!!.second.isEmpty())
        if (artists.isEmpty() && tracks.isEmpty() && pls.isEmpty() && extEmpty && !extBusy) { item { Empty("search", "Ничего не найдено", "Попробуй другое название или имя исполнителя.") }; return@Page }
        if (artists.isNotEmpty()) {
            item { SHead("Исполнители", artists.size.toString()) }
            item { HRow { items(artists.size) { ArtistCard(artists[it]) } } }
        }
        if (tracks.isNotEmpty()) { item { SHead("Треки", tn(tracks.size)) }; trackItems(tracks, "Поиск", max = 60) }
        if (pls.isNotEmpty()) {
            item { SHead("Плейлисты", pls.size.toString()) }
            item { HRow { items(pls.size) { PlaylistCard(pls[it]) } } }
        }
        extSearchItems(qq, ext, extBusy)
    }
}
object Search { var q = "" }

// ---------------- Моя музыка (Избранное) ----------------
@Composable
fun FavPage() {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val favs = Mixes.favTracks()
    val mine = Mixes.mine()
    Page("fav") {
        item { PTitle("Избранное") }
        if (Auth.current == null) item { GuestBanner() }
        item { LRow({ LCover(Th.Pink, "heartFill") }, "Любимые треки", tn(Lib.fav.size)) { Nav.go(Screen.Mix(Mixes.LIKED)) } }
        item { LRow({ LCover(Th.Hot, "globe") }, "Музыкальные сервисы", listOfNotNull(if (Ext.yaOn) "Яндекс" else null, if (Ext.spOn) "Spotify" else null, if (Ext.dzOn) "Deezer" else null).joinToString(" · ").ifEmpty { "Яндекс Музыка, Spotify, Deezer" }) { Nav.go(Screen.Services) } }
        item { LRow({ LCover(Th.Brand, "user", true) }, "Любимые исполнители", "${Lib.favArtists.size} исп.") { Nav.go(Screen.FavArtists) } }
        item { LRow({ LCover(Th.grad("#8b5cf6", "#06b6d4"), "clock") }, "Недавно прослушанные", tn(Lib.recent.count { trackOf(it) != null })) { Nav.go(Screen.Mix(Mixes.RECENT)) } }
        if (mine.isNotEmpty()) item { LRow({ LCover(Th.grad("#06b6d4", "#10b981"), "upload") }, "Мои треки", tn(mine.size)) { Nav.go(Screen.Mix("__mine__")) } }
        item { SHead("Любимые треки", if (favs.isNotEmpty()) tn(favs.size) else "", if (favs.size > 5) "Все" else null) { Nav.go(Screen.Mix(Mixes.LIKED)) } }
        if (favs.isEmpty()) item { Note("Нажми ♥ у трека — он появится здесь.") } else trackItems(favs, "Любимые треки", max = 5)
        item { SHead("Плейлисты", Lib.playlists.size.toString()) }
        item { HRow { items(Lib.playlists.size) { PlaylistCard(Lib.playlists[it]) }; item { NewPlaylistCard() } } }
        item { Note("Медиатека общая с SONORA для ПК: лайки, плейлисты и история синхронизируются через аккаунт.") }
    }
}

@Composable
fun FavArtistsPage() {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val list = Lib.favArtists.map { Mixes.artist(it) }
    Page("favartists") {
        item { PTitle("Любимые исполнители") }
        if (list.isEmpty()) item { Empty("user", "Пока никого", "Открой исполнителя и нажми «Нравится».") }
        list.chunked(3).forEach { row ->
            item {
                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.SpaceEvenly) {
                    row.forEach { Box(Modifier.weight(1f), contentAlignment = Alignment.Center) { ArtistCard(it) } }
                    repeat(3 - row.size) { Spacer(Modifier.weight(1f)) }
                }
            }
        }
    }
}

// ---------------- Микс / плейлист ----------------
@Composable
fun MixPage(id: String) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val m = remember(id, r, Catalog.tracks) { Mixes.get(id) }
    if (m == null) { LaunchedEffect(id) { Nav.back() }; return }
    val list = m.tracks
    val pl = m.playlist
    val allFav = list.isNotEmpty() && list.all { Lib.isFav(it.id) }
    val playingThis = Audio.playing && Audio.source == m.name && list.any { it.id == Audio.current?.id }
    val mins = (list.sumOf { it.duration } / 60).toInt()
    Page("mix:$id") {
        item {
            Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Box(Modifier.size(210.dp).shadow(24.dp, RoundedCornerShape(16.dp), spotColor = Color(0xFF1D4ED8))) {
                    if (id.startsWith("__pct_") || id.startsWith("__album_")) Box(Modifier.fillMaxSize().clip(RoundedCornerShape(16.dp))) {
                        CoverFill(list.firstOrNull { !it.cover.isNullOrEmpty() }, Modifier.fillMaxSize(), m.brush, m.icon, 70.dp)
                        if (id.startsWith("__pct_")) { TopShade(100.dp); CardKicker("100%", 24f) }
                        CardStripe(null)
                    } else Mosaic(list, 210.dp, 16.dp, m.brush, m.icon, 76.dp)
                }
                Tx(m.tag.uppercase(), 11.5f, Th.Dim, Modifier.padding(top = 18.dp), weight = FontWeight.Bold, spacing = 1.sp)
                Tx(m.name, 26f, modifier = Modifier.padding(top = 4.dp), lines = 2, align = TextAlign.Center)
                Tx(listOfNotNull(m.sub.ifEmpty { null }, tn(list.size), if (mins > 0) "$mins мин" else null).joinToString(" · "), 12.5f, Th.Dim, Modifier.padding(top = 6.dp), lines = 2, align = TextAlign.Center)
            }
        }
        item {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                if (id != Mixes.LIKED && list.isNotEmpty()) {
                    RoundBtn("heart", 46.dp, if (allFav) Th.Heart.copy(alpha = .16f) else Th.Card, if (allFav) Th.Heart else Th.Txt, 19.dp, fill = allFav) {
                        if (need("Лайки")) { val n = Lib.setFavAll(list.map { it.id }, !allFav); toast(if (!allFav) "Лайкнуто: ${tn(n)}" else "Убрано из любимых: ${tn(n)}") }
                    }
                    Spacer(Modifier.width(12.dp))
                }
                HotBtn(if (playingThis) "Пауза" else "Слушать", if (playingThis) "pause" else "play", height = 50.dp, enabled = list.isNotEmpty()) {
                    if (playingThis) Audio.toggle() else { Audio.play(list, 0, shuffleOn = false, from = m.name); if (Prefs.openFull) Nav.fullPlayer = true }
                }
                Spacer(Modifier.width(12.dp))
                RoundBtn("shuffle", 46.dp, iconSize = 19.dp) { if (list.isNotEmpty()) { Audio.play(list, 0, shuffleOn = true, from = m.name); if (Prefs.openFull) Nav.fullPlayer = true } }
                if (pl != null) {
                    Spacer(Modifier.width(12.dp))
                    RoundBtn("edit", 46.dp, iconSize = 18.dp) { Sheets.open("Изменить плейлист") { PlaylistForm(pl, null) } }
                    Spacer(Modifier.width(12.dp))
                    RoundBtn("trash", 46.dp, tint = Th.Danger, iconSize = 18.dp) { confirm("Удалить «${pl.name}»?", "Сами треки останутся в SONORA.", "Удалить") { Lib.delete(pl); Nav.back(); toast("Плейлист удалён") } }
                }
            }
        }
        if (list.isEmpty()) item { Empty(m.icon, "Здесь пока пусто", if (pl != null) "Добавляй треки через «⋮» → «Добавить в плейлист»." else "Треки появятся, когда ты начнёшь слушать и лайкать.") }
        val missing = if (pl != null) pl.trackIds.size - list.size else if (id == Mixes.LIKED) Lib.fav.size - list.size else 0
        trackItems(list, m.name, pl)
        if (missing > 0) item { Note("Ещё ${tn(missing)} из Яндекс Музыки, Spotify или Deezer — подключи сервис во вкладке «Сервисы», и они появятся здесь.") }
    }
}

// ---------------- Исполнитель ----------------
@Composable
fun ArtistPage(name: String) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val tracks = remember(name, Catalog.tracks) { Catalog.byArtist(name).sortedByDescending { it.plays } }
    val albums = remember(tracks) { tracks.filter { it.album.isNotEmpty() }.groupBy { it.album }.entries.toList() }
    val plays = tracks.sumOf { it.plays }
    val fav = Lib.isFavArtist(name)
    val cover = tracks.firstOrNull { !it.cover.isNullOrEmpty() }
    val playingThis = Audio.playing && Audio.source == name
    Page("artist:$name") {
        item {
            Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Box(Modifier.size(150.dp).shadow(26.dp, CircleShape, spotColor = Covers.color(cover) ?: Th.Accent).clip(CircleShape)) {
                    CoverFill(cover, Modifier.fillMaxSize(), Th.Hot, "user", 56.dp)
                }
                Row(Modifier.padding(top = 18.dp), verticalAlignment = Alignment.CenterVertically) {
                    Tx(name, 26f, modifier = Modifier.weight(1f, fill = false), lines = 2, align = TextAlign.Center)
                }
                Box(Modifier.padding(top = 8.dp).clip(RoundedCornerShape(10.dp)).background(Th.Hot).padding(horizontal = 10.dp, vertical = 3.dp)) {
                    Text("АРТИСТ", color = Color.White, fontSize = 10.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp)
                }
                Tx(listOfNotNull(tn(tracks.size), if (albums.isNotEmpty()) "альбомов: ${albums.size}" else null, if (plays > 0) "прослушиваний: $plays" else null).joinToString(" · "),
                    12.5f, Th.Dim, Modifier.padding(top = 8.dp), lines = 2, align = TextAlign.Center)
            }
        }
        item {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                HotBtn(if (playingThis) "Пауза" else "Слушать всё", if (playingThis) "pause" else "play", height = 48.dp, enabled = tracks.isNotEmpty()) {
                    if (playingThis) Audio.toggle() else { Audio.play(tracks, 0, shuffleOn = false, from = name); if (Prefs.openFull) Nav.fullPlayer = true }
                }
                Spacer(Modifier.width(10.dp))
                Row(Modifier.height(48.dp).clip(RoundedCornerShape(24.dp)).then(if (fav) Modifier.background(Th.Pink) else Modifier.background(Th.Card).border(1.dp, Th.Border, RoundedCornerShape(24.dp)))
                    .tap(.95f) { if (need("Любимые исполнители")) { Lib.toggleFavArtist(name); toast(if (!fav) "$name — в любимых" else "Убрано из любимых") } }.padding(horizontal = 18.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    SIcon("heart", 17.dp, Color.White, fill = fav)
                    Spacer(Modifier.width(8.dp))
                    Tx(if (fav) "В любимых" else "Нравится", 14f, Color.White)
                }
                Spacer(Modifier.width(10.dp))
                RoundBtn("shuffle", 48.dp, iconSize = 19.dp) { if (tracks.isNotEmpty()) { Audio.play(tracks, 0, shuffleOn = true, from = name); if (Prefs.openFull) Nav.fullPlayer = true } }
            }
        }
        item { SHead("Треки", tn(tracks.size)) }
        trackItems(tracks, name)
        if (albums.isNotEmpty()) {
            item { SHead("Альбомы", albums.size.toString()) }
            item {
                HRow {
                    items(albums.size) { i ->
                        val (al, ts) = albums[i]
                        Column(Modifier.width(132.dp).tap(.97f) { Nav.go(Screen.Mix(Mixes.albumId(name, al))) }) {
                            Box(Modifier.size(132.dp).shadow(10.dp, RoundedCornerShape(14.dp)).clip(RoundedCornerShape(14.dp))) {
                                CoverFill(ts.firstOrNull { !it.cover.isNullOrEmpty() }, Modifier.fillMaxSize())
                                CardStripe(null)
                            }
                            Tx(al, 13.5f, modifier = Modifier.padding(top = 10.dp, bottom = 3.dp), lines = 2, logo = false)
                            Tx(listOfNotNull(ts.firstOrNull { it.year > 0 }?.year?.toString(), tn(ts.size)).joinToString(" · "), 11f, Th.Dim)
                        }
                    }
                }
            }
        }
    }
}

// ---------------- Профиль ----------------
@Composable
fun ProfilePage() {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    @Suppress("UNUSED_VARIABLE") val ar = Auth.rev
    val s = Auth.current
    val col = profileColor()
    val scope = rememberCoroutineScope()
    val mine = Mixes.mine()
    LaunchedEffect(s?.uid) { if (s != null) Auth.loadProfile() }
    Page("profile") {
        item {
            val shape = RoundedCornerShape(24.dp)
            Column(Modifier.padding(horizontal = 16.dp, vertical = 6.dp).fillMaxWidth().clip(shape)
                .background(Brush.linearGradient(listOf(Th.shade(col, -10).copy(alpha = .85f), Color(0xE60B0E14)))).border(1.dp, Th.GlassEdge, shape)
                .padding(horizontal = 22.dp, vertical = 24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Box(Modifier.shadow(30.dp, CircleShape, spotColor = col)) { AvatarRing(124.dp, 3.dp) }
                Tx(if (s != null) (if (Plus.active) "ПРОФИЛЬ · PLUS" else "ПРОФИЛЬ") else "ГОСТЬ", 11.5f, Color.White.copy(alpha = .75f), Modifier.padding(top = 16.dp), weight = FontWeight.Bold, spacing = 1.sp)
                Tx(s?.name?.ifBlank { null } ?: s?.email ?: "Гость", 28f, Color.White, Modifier.padding(top = 4.dp, bottom = 6.dp), lines = 2, align = TextAlign.Center, logo = true)
                if (s != null && s.bio.isNotBlank()) Tx(s.bio, 14f, Color.White.copy(alpha = .9f), Modifier.padding(bottom = 6.dp), lines = 3, align = TextAlign.Center, weight = FontWeight.Normal, lineHeight = 20.sp)
                Tx(if (s != null) s.email + (if (s.provider == "google") " · Google" else "") + " · " + tn(Lib.fav.size).replace("трек", "трек") + " в любимых" else "Войди, чтобы сохранять лайки, плейлисты и свою музыку в облаке",
                    12.5f, Color.White.copy(alpha = .7f), lines = 2, align = TextAlign.Center, weight = FontWeight.Normal)
                Spacer(Modifier.height(14.dp))
                WhiteBtn(if (s != null) "Редактировать профиль" else "Войти или создать аккаунт", if (s != null) "edit" else "login") { if (s == null) openWelcome() else openEditProfile() }
            }
        }
        item {
            val limit = if (Plus.active) 100 else 2
            val stats = listOf(Lib.fav.size.toString() to "в любимых", Lib.playlists.size.toString() to "плейлистов", Lib.recent.size.toString() to "прослушано", "${mine.size} / $limit" to "публичных треков")
            Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
                stats.chunked(2).forEach { row ->
                    Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        row.forEach { (n, l) ->
                            Column(Modifier.weight(1f).glass(RoundedCornerShape(16.dp), .04f, soft = true).padding(vertical = 14.dp, horizontal = 10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                                Tx(n, 22f)
                                Tx(l, 11.5f, Th.Dim, Modifier.padding(top = 2.dp))
                            }
                        }
                    }
                }
            }
        }
        if (mine.isNotEmpty()) {
            item { SHead("Мои треки", "публичных: ${mine.size}") }
            trackItems(mine, "Мои треки", max = 8)
        }
        item { SetTitle("Аккаунт") }
        if (s == null) item { SetBtn("login", "Войти или создать аккаунт") { openWelcome() } }
        else {
            item { SetBtn("crown", "SONORA Plus", if (Plus.active) "активен · осталось " + ((Plus.until - now()) / 86_400_000L) + " " + Plus.dn((Plus.until - now()) / 86_400_000L) else "${Plus.PRICE} ₽ / ${Plus.DAYS} дней") { openPlus() } }
            item { SetBtn("edit", "Редактировать профиль") { openEditProfile() } }
            item { SetBtn("refresh", "Синхронизировать сейчас", Lib.syncStatus.ifEmpty { "Лайки и плейлисты общие с компьютером" }) { scope.launch { Cloud.syncNow(); toast(Lib.syncStatus) } } }
            item { SetBtn("logout", "Выйти из аккаунта", danger = true) { confirm("Выйти из аккаунта?", "Лайки и плейлисты останутся в облаке — войди снова, и они вернутся.", "Выйти") { Auth.signOut(); toast("Ты вышел из аккаунта") } } }
        }
        item { SetBtn("globe", "Музыкальные сервисы", "Яндекс Музыка, Spotify, Deezer") { Nav.go(Screen.Services) } }
        item { SetBtn("gear", "Настройки", "Живой фон, плеер, кэш") { openSettings() } }
        item { SetTitle("О приложении") }
        item { SetBtn("star", "SONORA для Android", "версия " + BuildConfig.VERSION_NAME) { toast("Свайп по плееру — следующий трек, долгое нажатие на трек — меню") } }
    }
}

