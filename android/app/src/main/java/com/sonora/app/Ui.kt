package com.sonora.app

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
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
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.Shadow
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

// ---------------- Палитра SONORA — та же, что в версии для ПК (Ui.cs → Th) ----------------
object Th {
    val Bg = Color(0xFF050608)
    val Panel = Color(0xFF101216)
    val Bg2 = Color(0xFF15171C)
    val Card = Color(0xFF1A1D23)
    val Card2 = Color(0xFF22262E)
    val Border = Color(0x0FFFFFFF)
    val BorderHi = Color(0x1FFFFFFF)
    val Txt = Color(0xFFF5F7FA)
    val Dim = Color(0xFF8C93A1)
    val Accent = Color(0xFF3B82F6)
    val Accent2 = Color(0xFF60A5FA)
    val Accent3 = Color(0xFF7AB0FF)
    val Soft = Color(0x243B82F6)
    val Heart = Color(0xFFFF4F9A)
    val Danger = Color(0xFFF43F5E)
    val White = Color.White
    val Brand = Brush.linearGradient(listOf(Color(0xFF60A5FA), Color(0xFF1D4ED8)))
    val Hot = Brand
    val Pink = Brush.linearGradient(listOf(Color(0xFFFF6BAE), Color(0xFFD81B72)))
    val Ring = Brush.linearGradient(listOf(Color(0xFF93C5FD), Color(0xFF1D4ED8)))
    val Stripe = Brush.horizontalGradient(listOf(Color(0xFF60A5FA), Color(0xFF1D4ED8)))
    val PlusGold = Brush.linearGradient(listOf(Color(0xFF3B82F6), Color(0xFF8B5CF6)))
    val PlusSoft = Brush.linearGradient(listOf(Color(0x293B82F6), Color(0x298B5CF6)))
    val PlusEdge = Brush.linearGradient(listOf(Color(0x7360A5FA), Color(0x73A78BFA)))
    val PlusIcon = Color(0xFFA5B4FC)
    val GlassEdge = Brush.verticalGradient(listOf(Color(0x29FFFFFF), Color(0x09FFFFFF)))
    val GlassEdgeSoft = Brush.verticalGradient(listOf(Color(0x1AFFFFFF), Color(0x05FFFFFF)))
    fun glass(a: Float) = Color.White.copy(alpha = a)
    val Logo = FontFamily(Font(R.font.widock))

    // цвета плейлистов и профиля с ПК, переведённые в синюю гамму (как Remap в Ui.cs)
    private val remap = mapOf(
        "#8b5cf6" to "#3b82f6", "#ec4899" to "#1d4ed8", "#06b6d4" to "#60a5fa", "#f59e0b" to "#0284c7", "#ef4444" to "#1e3a8a",
        "#10b981" to "#2563eb", "#6366f1" to "#3b82f6", "#14b8a6" to "#1d4ed8", "#a78bfa" to "#93c5fd", "#2dd4bf" to "#60a5fa",
    )
    fun hex(s: String?): Color {
        val k = (s ?: "").trim().lowercase()
        val v = remap[k] ?: k.ifEmpty { "#3b82f6" }
        return try { Color(android.graphics.Color.parseColor(v)) } catch (_: Exception) { Accent }
    }
    // Th.Shade: +pct — светлее (к белому), −pct — темнее (к чёрному)
    fun shade(c: Color, pct: Int): Color = if (pct >= 0) Pal.mix(c, Color.White, pct / 100f) else Pal.mix(c, Color.Black, -pct / 100f)
    fun grad(from: String, to: String) = Brush.linearGradient(listOf(hex(from), hex(to)))
}

val sonoraColors = darkColorScheme(primary = Th.Accent, secondary = Th.Accent2, background = Th.Bg, surface = Th.Card, onPrimary = Color.White, onSurface = Th.Txt, onBackground = Th.Txt)

@Composable
fun SonoraTheme(content: @Composable () -> Unit) = MaterialTheme(colorScheme = sonoraColors, content = content)

// ---------------- Всплывающее сообщение (как Shell.Toast на ПК) ----------------
object Toasts {
    var msg by mutableStateOf<String?>(null)
    var id by mutableIntStateOf(0)
}
fun toast(msg: String) { Toasts.msg = msg; Toasts.id++ }

// ---------------- Навигация ----------------
sealed class Screen {
    data object Home : Screen()
    data object Search : Screen()
    data object Explore : Screen()
    data object Fav : Screen()
    data object Profile : Screen()
    data object FavArtists : Screen()
    data class Mix(val id: String) : Screen()
    data class Artist(val name: String) : Screen()
}

object Nav {
    val stack = mutableStateListOf<Screen>(Screen.Home)
    var fullPlayer by mutableStateOf(false)
    val top: Screen get() = stack.last()
    val tab: Screen get() = stack.first()
    fun go(s: Screen) { if (stack.last() != s) stack.add(s) }
    fun tab(s: Screen) { stack.clear(); stack.add(s) }
    fun back(): Boolean {
        if (Sheets.stack.isNotEmpty()) { Sheets.close(); return true }
        if (fullPlayer) { fullPlayer = false; return true }
        if (stack.size > 1) { stack.removeAt(stack.size - 1); return true }
        if (stack.first() != Screen.Home) { tab(Screen.Home); return true }
        return false
    }
}

// без аккаунта можно только слушать
fun need(what: String): Boolean {
    if (Auth.current != null) return true
    toast("$what — только с аккаунтом. Войди или создай его.")
    openWelcome()
    return false
}

fun tn(n: Int): String { val a = n % 100; val b = n % 10; return "$n " + if (a in 11..14) "треков" else if (b == 1) "трек" else if (b in 2..4) "трека" else "треков" }
fun fmt(ms: Long): String { val s = (ms / 1000).coerceAtLeast(0); return "%d:%02d".format(s / 60, s % 60) }
fun fmtSec(sec: Double): String = if (sec > 0) fmt((sec * 1000).toLong()) else ""

// ---------------- Нажатие с «пружинкой» (U.Click на ПК) ----------------
fun Modifier.tap(scale: Float = .96f, enabled: Boolean = true, onLong: (() -> Unit)? = null, onClick: () -> Unit): Modifier = composed {
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val s by animateFloatAsState(if (pressed && enabled) scale else 1f, label = "tap")
    this.graphicsLayer { scaleX = s; scaleY = s }.then(
        if (onLong != null) Modifier.longTap(src, enabled, onLong, onClick)
        else Modifier.clickable(src, null, enabled = enabled, onClick = onClick)
    )
}

@OptIn(ExperimentalFoundationApi::class)
private fun Modifier.longTap(src: MutableInteractionSource, enabled: Boolean, onLong: () -> Unit, onClick: () -> Unit) =
    this.combinedClickable(interactionSource = src, indication = null, enabled = enabled, onLongClick = onLong, onClick = onClick)

fun Modifier.glass(shape: androidx.compose.ui.graphics.Shape, a: Float = .045f, soft: Boolean = false) =
    this.clip(shape).background(Th.glass(a)).border(1.dp, if (soft) Th.GlassEdgeSoft else Th.GlassEdge, shape)

fun Modifier.dashed(color: Color, radius: Dp) = this.drawBehind {
    drawRoundRect(color, cornerRadius = CornerRadius(radius.toPx()), style = Stroke(width = 1.5.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(), 3.dp.toPx()))))
}

// ---------------- Текст: Widock — для крупных заголовков, остальное — системный шрифт (как U.Tx) ----------------
@Composable
fun Tx(text: String, size: Float, color: Color = Th.Txt, modifier: Modifier = Modifier, weight: FontWeight? = null, lines: Int = 1,
       align: TextAlign? = null, logo: Boolean? = null, spacing: TextUnit = TextUnit.Unspecified, shadow: Boolean = false, lineHeight: TextUnit = TextUnit.Unspecified) {
    val dim = color == Th.Dim
    val useLogo = logo ?: (!dim && size >= 17f)
    val w = weight ?: if (useLogo) FontWeight.Normal else if (dim) (if (size >= 13) FontWeight.Medium else FontWeight.Normal) else if (size >= 12.5f) FontWeight.SemiBold else FontWeight.Normal
    Text(text, modifier, color = color, fontSize = size.sp, fontWeight = w, fontFamily = if (useLogo) Th.Logo else null, maxLines = lines,
        overflow = TextOverflow.Ellipsis, textAlign = align, letterSpacing = spacing, lineHeight = lineHeight,
        style = if (shadow) TextStyle(shadow = Shadow(Color(0xD9000000), blurRadius = 10f)) else TextStyle.Default)
}

@Composable
fun PTitle(s: String) = Tx(s, 30f, modifier = Modifier.padding(start = 20.dp, end = 20.dp, top = 6.dp, bottom = 10.dp))

@Composable
fun SHead(title: String, sub: String? = null, action: String? = null, onAction: (() -> Unit)? = null) {
    Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 30.dp, bottom = 12.dp), verticalAlignment = Alignment.Bottom) {
        Tx(title, 22f, modifier = Modifier.weight(1f, fill = false))
        if (!sub.isNullOrEmpty()) Tx(sub, 12f, Th.Dim, Modifier.padding(start = 8.dp, bottom = 2.dp))
        Spacer(Modifier.weight(1f))
        if (action != null && onAction != null) Tx(action, 12.5f, Th.Accent3, Modifier.padding(bottom = 2.dp).tap { onAction() })
    }
}

@Composable
fun Note(s: String) = Tx(s, 12.5f, Th.Dim, Modifier.padding(horizontal = 20.dp, vertical = 12.dp), lines = 4, lineHeight = 17.sp)

@Composable
fun Empty(icon: String, title: String, text: String) {
    Column(Modifier.fillMaxWidth().padding(horizontal = 30.dp, vertical = 36.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.size(64.dp).clip(RoundedCornerShape(20.dp)).background(Th.Card).border(1.dp, Th.Border, RoundedCornerShape(20.dp)), contentAlignment = Alignment.Center) {
            SIcon(icon, 26.dp, Th.Dim)
        }
        Tx(title, 15f, modifier = Modifier.padding(top = 14.dp, bottom = 5.dp))
        Tx(text, 13f, Th.Dim, lines = 4, align = TextAlign.Center, lineHeight = 20.sp)
    }
}

@Composable
fun Logo(size: Dp = 30.dp) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Image(painterResource(R.drawable.logo), null, Modifier.size(size))
        Spacer(Modifier.width(9.dp))
        Text("SONORA", color = Th.Accent, fontFamily = Th.Logo, fontSize = (size.value * 0.62f).sp, maxLines = 1)
    }
}

// ---------------- Обложки ----------------
@Composable
fun coverImage(t: Track?): ImageBitmap? {
    val img by produceState(t?.let { Covers.peek(it) }, t?.id) { value = t?.let { Covers.get(it) } }
    return img
}

@Composable
fun Cover(t: Track?, size: Dp, corner: Dp = 10.dp, modifier: Modifier = Modifier, icon: Dp = size * 0.4f) {
    val img = coverImage(t)
    Box(modifier.size(size).clip(RoundedCornerShape(corner)).background(Th.Brand), contentAlignment = Alignment.Center) {
        if (img != null) Image(img, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        else SIcon("music", icon, Color.White.copy(alpha = .85f))
    }
}

@Composable
fun CoverFill(t: Track?, modifier: Modifier = Modifier, fallback: Brush = Th.Brand, icon: String = "music", iconSize: Dp = 30.dp) {
    val img = coverImage(t)
    Box(modifier.background(fallback), contentAlignment = Alignment.Center) {
        if (img != null) Image(img, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        else SIcon(icon, iconSize, Color.White.copy(alpha = .8f))
    }
}

// мозаика 2×2 из обложек (Mosaic на ПК)
@Composable
fun Mosaic(tracks: List<Track>, size: Dp, radius: Dp, fallback: Brush, icon: String, iconSize: Dp) {
    val withCovers = tracks.filter { !it.cover.isNullOrEmpty() }
    Box(Modifier.size(size).clip(RoundedCornerShape(radius)).background(fallback), contentAlignment = Alignment.Center) {
        if (withCovers.size >= 4 || (withCovers.isNotEmpty() && tracks.size >= 4)) {
            val picks = tracks.take(4)
            Column {
                for (r in 0..1) Row {
                    for (c in 0..1) {
                        val i = r * 2 + c
                        val m = Modifier.size(size / 2)
                        if (i < picks.size) CoverFill(picks[i], m, Brush.linearGradient(listOf(Color(0xFF222222), Color(0xFF222222))), iconSize = 26.dp)
                        else Box(m.background(Brush.linearGradient(listOf(Color(0x663B82F6), Color(0x6660A5FA)))))
                    }
                }
            }
        } else if (withCovers.isNotEmpty()) CoverFill(withCovers[0], Modifier.fillMaxSize(), fallback)
        else SIcon(icon, iconSize, Color.White.copy(alpha = .8f))
    }
}

@Composable
fun BoxScope.TopShade(h: Dp, a: Float = .62f) {
    Box(Modifier.fillMaxWidth().height(h).align(Alignment.TopCenter).background(Brush.verticalGradient(listOf(Color.Black.copy(alpha = a), Color.Transparent))))
}

@Composable
fun BoxScope.CardStripe(text: String?, h: Dp = 28.dp) {
    Box(Modifier.fillMaxWidth().height(if (text == null) 6.dp else h).align(Alignment.BottomCenter).background(Th.Stripe).padding(horizontal = 10.dp), contentAlignment = Alignment.Center) {
        if (text != null) Text(text.uppercase(), color = Color.White, fontFamily = Th.Logo, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
fun BoxScope.CardKicker(text: String, size: Float) {
    Text(text, Modifier.align(Alignment.TopStart).padding(start = 12.dp, top = 10.dp, end = 12.dp), color = Color.White, fontFamily = Th.Logo,
        fontSize = size.sp, fontWeight = FontWeight.Black, maxLines = 1, overflow = TextOverflow.Ellipsis,
        style = TextStyle(shadow = Shadow(Color(0xB3000000), blurRadius = 8f)))
}

// ---------------- Аватар и кольцо профиля ----------------
val ProfileColors = listOf("#3B82F6", "#60A5FA", "#1E40AF", "#7DD3FC", "#FF4F9A", "#94A3B8")
val ProfileColorNames = listOf("Синий", "Голубой", "Глубокий синий", "Небесный", "Розовый", "Графит")
fun profileColor(): Color = Th.hex(Auth.current?.color?.ifBlank { null } ?: "#3B82F6")
fun profileRing(): Brush { val c = profileColor(); return Brush.linearGradient(listOf(Th.shade(c, 40), Th.shade(c, -45))) }

@Composable
fun Avatar(size: Dp, modifier: Modifier = Modifier) {
    @Suppress("UNUSED_VARIABLE") val r = Auth.rev
    val s = Auth.current
    val c = profileColor()
    val photo = s?.photo
    val img by produceState<ImageBitmap?>(null, photo) {
        value = if (photo.isNullOrBlank()) null else kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            try {
                val b = if (photo.startsWith("data:")) android.util.Base64.decode(photo.substringAfter(','), android.util.Base64.DEFAULT)
                else java.net.URL(photo).openStream().use { it.readBytes() }
                android.graphics.BitmapFactory.decodeByteArray(b, 0, b.size)?.asImageBitmap()
            } catch (_: Exception) { null }
        }
    }
    Box(modifier.size(size).clip(CircleShape).background(Brush.linearGradient(listOf(Th.shade(c, 25), Th.shade(c, -40)))), contentAlignment = Alignment.Center) {
        val i = img
        if (i != null) Image(i, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        else if (s != null && s.name.isNotBlank()) Text(s.name.trim().take(1).uppercase(), color = Color.White, fontFamily = Th.Logo, fontSize = (size.value * .42f).sp)
        else SIcon("user", size * .45f, Color.White)
    }
}

@Composable
fun AvatarRing(size: Dp, pad: Dp = 2.5.dp, modifier: Modifier = Modifier) {
    Box(modifier.size(size).clip(CircleShape).background(profileRing()).padding(pad), contentAlignment = Alignment.Center) {
        Box(Modifier.fillMaxSize().clip(CircleShape).background(Th.Bg).padding(pad), contentAlignment = Alignment.Center) { Avatar(size - pad * 4) }
    }
}

// ---------------- Кнопки ----------------
@Composable
fun HotBtn(text: String, icon: String? = null, modifier: Modifier = Modifier, brush: Brush = Th.Hot, height: Dp = 46.dp, txtColor: Color = Color.White, enabled: Boolean = true, onClick: () -> Unit) {
    Row(modifier.height(height).clip(RoundedCornerShape(height / 2)).background(if (enabled) brush else SolidColor(Th.Card2)).tap(.95f, enabled) { onClick() }.padding(horizontal = 20.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center) {
        if (icon != null) { SIcon(icon, 17.dp, txtColor); Spacer(Modifier.width(8.dp)) }
        Tx(text, 14f, txtColor, weight = FontWeight.SemiBold)
    }
}

@Composable
fun WhiteBtn(text: String, icon: String? = null, modifier: Modifier = Modifier, onClick: () -> Unit) =
    HotBtn(text, icon, modifier, SolidColor(Color.White), 40.dp, Color.Black, onClick = onClick)

@Composable
fun RoundBtn(icon: String, size: Dp = 50.dp, bg: Color = Th.Card, tint: Color = Th.Txt, iconSize: Dp = 20.dp, fill: Boolean? = null, border: Boolean = true, onClick: () -> Unit) {
    Box(Modifier.size(size).clip(CircleShape).background(bg).then(if (border) Modifier.border(1.dp, Th.Border, CircleShape) else Modifier).tap(.9f) { onClick() }, contentAlignment = Alignment.Center) {
        SIcon(icon, iconSize, tint, fill = fill)
    }
}

@Composable
fun IconBtn(icon: String, size: Dp = 40.dp, iconSize: Dp = 20.dp, tint: Color = Th.Txt, fill: Boolean? = null, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Box(modifier.size(size).clip(CircleShape).tap(.88f) { onClick() }, contentAlignment = Alignment.Center) { SIcon(icon, iconSize, tint, fill = fill) }
}

// большая кнопка листа (Provider на ПК)
@Composable
fun ProviderBtn(text: String, icon: String? = null, primary: Boolean = false, danger: Boolean = false, brush: Brush? = null, enabled: Boolean = true, onClick: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Row(Modifier.fillMaxWidth().padding(vertical = 4.dp).height(50.dp).clip(shape)
        .then(if (brush != null) Modifier.background(brush) else if (primary) Modifier.background(Th.Hot) else Modifier.background(Th.Card).border(1.dp, Th.Border, shape))
        .tap(.97f, enabled) { onClick() }.padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center) {
        val c = if (danger) Th.Danger else Th.Txt
        if (icon != null) { SIcon(icon, 18.dp, c); Spacer(Modifier.width(10.dp)) }
        Tx(text, 14f, c, weight = FontWeight.SemiBold)
    }
}

@Composable
fun HeartBtn(id: String, size: Dp = 34.dp, iconSize: Dp = 19.dp, idle: Color = Th.Dim) {
    @Suppress("UNUSED_VARIABLE") val r = Lib.rev
    val on = Lib.isFav(id)
    IconBtn("heart", size, iconSize, if (on) Th.Heart else idle, fill = on) { if (need("Лайки")) Lib.toggleFav(id) }
}

// фильтры-«пилюли» (Pills на ПК)
@Composable
fun Pills(ids: List<String>, names: List<String>, active: String, pick: (String) -> Unit) {
    LazyRow(contentPadding = PaddingValues(horizontal = 20.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(bottom = 10.dp)) {
        items(ids.size) { i ->
            val on = ids[i] == active
            val shape = RoundedCornerShape(20.dp)
            Box(Modifier.clip(shape).background(if (on) Th.Soft else Th.Card).border(1.dp, if (on) Color(0x663B82F6) else Th.Border, shape).tap(.95f) { pick(ids[i]) }
                .padding(horizontal = 14.dp, vertical = 8.dp)) { Tx(names[i], 12.5f, if (on) Th.Accent3 else Th.Dim) }
        }
    }
}

// поле ввода (Input на ПК)
@Composable
fun Input(value: String, onChange: (String) -> Unit, placeholder: String, modifier: Modifier = Modifier, icon: String? = null, password: Boolean = false,
          keyboard: KeyboardType = KeyboardType.Text, lines: Int = 1) {
    val shape = RoundedCornerShape(14.dp)
    Row(modifier.fillMaxWidth().clip(shape).background(Th.Card).border(1.dp, Th.BorderHi, shape).padding(horizontal = 14.dp, vertical = 13.dp), verticalAlignment = Alignment.CenterVertically) {
        if (icon != null) { SIcon(icon, 17.dp, Th.Dim); Spacer(Modifier.width(10.dp)) }
        Box(Modifier.weight(1f)) {
            if (value.isEmpty()) Tx(placeholder, 14f, Th.Dim, weight = FontWeight.Normal)
            BasicTextField(value, onChange, Modifier.fillMaxWidth(), singleLine = lines == 1, maxLines = lines,
                textStyle = TextStyle(color = Th.Txt, fontSize = 14.sp), cursorBrush = SolidColor(Th.Accent3),
                visualTransformation = if (password) PasswordVisualTransformation() else VisualTransformation.None,
                keyboardOptions = KeyboardOptions(keyboardType = if (password) KeyboardType.Password else keyboard))
        }
        if (value.isNotEmpty() && !password && lines == 1) IconBtn("close", 22.dp, 12.dp, Th.Dim) { onChange("") }
    }
}

@Composable
fun SetTitle(s: String) = Tx(s.uppercase(), 11f, Th.Dim, Modifier.padding(start = 24.dp, top = 26.dp, bottom = 8.dp), weight = FontWeight.Bold, spacing = 1.sp)

@Composable
fun SetBtn(icon: String, title: String, sub: String? = null, danger: Boolean = false, onClick: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Row(Modifier.padding(horizontal = 16.dp, vertical = 3.dp).fillMaxWidth().glass(shape, .04f, soft = true).tap(.98f) { onClick() }.padding(horizontal = 12.dp, vertical = 11.dp),
        verticalAlignment = Alignment.CenterVertically) {
        val c = if (danger) Th.Danger else Th.Txt
        Box(Modifier.size(36.dp).clip(RoundedCornerShape(11.dp)).background(if (danger) Th.Danger.copy(alpha = .14f) else Th.Card2), contentAlignment = Alignment.Center) { SIcon(icon, 17.dp, if (danger) Th.Danger else Th.Accent3) }
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Tx(title, 14f, c)
            if (!sub.isNullOrEmpty()) Tx(sub, 11.5f, Th.Dim, lines = 2)
        }
        SIcon("chevR", 15.dp, Th.Dim)
    }
}

@Composable
fun SwitchRow(title: String, sub: String?, on: Boolean, onChange: (Boolean) -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Row(Modifier.padding(vertical = 3.dp).fillMaxWidth().glass(shape, .04f, soft = true).tap(.98f) { onChange(!on) }.padding(horizontal = 14.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Tx(title, 14f)
            if (!sub.isNullOrEmpty()) Tx(sub, 11.5f, Th.Dim, lines = 3)
        }
        Box(Modifier.width(44.dp).height(26.dp).clip(RoundedCornerShape(13.dp)).background(if (on) Th.Accent else Th.Card2).padding(3.dp),
            contentAlignment = if (on) Alignment.CenterEnd else Alignment.CenterStart) {
            Box(Modifier.size(20.dp).clip(CircleShape).background(Color.White))
        }
    }
}

@Composable
fun Badge(text: String, bg: Color, fg: Color) {
    Box(Modifier.padding(start = 6.dp).clip(RoundedCornerShape(9.dp)).background(bg).padding(horizontal = 6.dp, vertical = 1.dp)) {
        Text(text, color = fg, fontSize = 8.5.sp, fontWeight = FontWeight.Bold, maxLines = 1, letterSpacing = .5.sp)
    }
}

@Composable
fun Shadowed(shape: androidx.compose.ui.graphics.Shape, elevation: Dp, modifier: Modifier = Modifier, color: Color = Color.Black, content: @Composable BoxScope.() -> Unit) {
    Box(modifier.shadow(elevation, shape, ambientColor = color, spotColor = color), content = content)
}
