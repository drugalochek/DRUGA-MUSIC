package com.sonora.app

import androidx.compose.foundation.Image
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.foundation.layout.size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

// Те же иконки, что в версии для ПК (Sonora/Icons.cs): контур 24×24, линия 2, скруглённые концы
object Icons2 {
    class Def(val d: String, val fill: Boolean)
    val all: Map<String, Def> = mapOf(
        "crown" to Def("M 2.5 7 L 7 11.5 L 12 4 L 17 11.5 L 21.5 7 L 19.5 18 H 4.5 Z M 5 21 H 19", false),
        "wave" to Def("M 3 12 c 1.5 -4 4 -6 6 -6 2 0 3 1 3 3 M 21 12 c -1.5 4 -4 6 -6 6 -2 0 -3 -1 -3 -3", false),
        "bell" to Def("M 18 8 A 6 6 0 0 0 6 8 c 0 7 -3 9 -3 9 h 18 s -3 -2 -3 -9 M 13.73 21 a 2 2 0 0 1 -3.46 0", false),
        "user" to Def("M 20 21 v -2 a 4 4 0 0 0 -4 -4 H 8 a 4 4 0 0 0 -4 4 v 2 M 8 7 A 4 4 0 1 0 16 7 A 4 4 0 1 0 8 7 Z", false),
        "heartFill" to Def("M 12 21.35 l -1.45 -1.32 C 5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3 c 1.74 0 3.41 0.81 4.5 2.09 C 13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5 c 0 3.78 -3.4 6.86 -8.55 11.54 L 12 21.35 z", true),
        "chevR" to Def("M 9 18 l 6 -6 -6 -6", false),
        "search" to Def("M 3 11 A 8 8 0 1 0 19 11 A 8 8 0 1 0 3 11 Z M 21 21 l -4.35 -4.35", false),
        "chevL" to Def("M 15 18 l -6 -6 6 -6", false),
        "edit" to Def("M 12 20 h 9 M 16.5 3.5 a 2.12 2.12 0 0 1 3 3 L 7 19 l -4 1 1 -4 L 16.5 3.5 z", false),
        "music" to Def("M 9 18 V 5 l 12 -2 v 13 M 3 18 A 3 3 0 1 0 9 18 A 3 3 0 1 0 3 18 Z M 15 16 A 3 3 0 1 0 21 16 A 3 3 0 1 0 15 16 Z", false),
        "globe" to Def("M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z M 2 12 h 20 M 12 2 a 15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1 -4 10 15.3 15.3 0 0 1 -4 -10 15.3 15.3 0 0 1 4 -10 z", false),
        "moon" to Def("M 21 12.79 A 9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79 z", false),
        "trash" to Def("M 3 6 h 18 M 8 6 V 4 a 2 2 0 0 1 2 -2 h 4 a 2 2 0 0 1 2 2 v 2 M 19 6 l -1 14 a 2 2 0 0 1 -2 2 H 8 a 2 2 0 0 1 -2 -2 L 5 6", false),
        "play" to Def("M 8 5 v 14 l 11 -7 z", true),
        "pause" to Def("M 6 5 h 4 v 14 H 6 z M 14 5 h 4 v 14 h -4 z", true),
        "heart" to Def("M 20.84 4.61 a 5.5 5.5 0 0 0 -7.78 0 L 12 5.67 l -1.06 -1.06 a 5.5 5.5 0 0 0 -7.78 7.78 l 1.06 1.06 L 12 21.23 l 7.78 -7.78 1.06 -1.06 a 5.5 5.5 0 0 0 0 -7.78 z", false),
        "close" to Def("M 18 6 L 6 18 M 6 6 l 12 12", false),
        "timer" to Def("M 4 13 A 8 8 0 1 0 20 13 A 8 8 0 1 0 4 13 Z M 12 9 v 4 l 2 2 M 9 2 h 6", false),
        "chevD" to Def("M 6 9 l 6 6 6 -6", false),
        "share" to Def("M 15 5 A 3 3 0 1 0 21 5 A 3 3 0 1 0 15 5 Z M 3 12 A 3 3 0 1 0 9 12 A 3 3 0 1 0 3 12 Z M 15 19 A 3 3 0 1 0 21 19 A 3 3 0 1 0 15 19 Z M 8.59 13.51 L 15.42 17.49 M 15.41 6.51 L 8.59 10.49", false),
        "dots" to Def("M 10 5 A 2 2 0 1 0 14 5 A 2 2 0 1 0 10 5 Z M 10 12 A 2 2 0 1 0 14 12 A 2 2 0 1 0 10 12 Z M 10 19 A 2 2 0 1 0 14 19 A 2 2 0 1 0 10 19 Z", true),
        "repeat" to Def("M 17 1 l 4 4 -4 4 M 3 11 V 9 a 4 4 0 0 1 4 -4 h 14 M 7 23 l -4 -4 4 -4 M 21 13 v 2 a 4 4 0 0 1 -4 4 H 3", false),
        "prev" to Def("M 6 6 h 3 v 12 H 6 z M 20 6 v 12 l -10 -6 z", true),
        "next" to Def("M 15 6 h 3 v 12 h -3 z M 4 6 l 10 6 -10 6 z", true),
        "shuffle" to Def("M 16 3 h 5 v 5 M 4 20 L 21 3 M 21 16 v 5 h -5 M 15 15 l 6 6 M 4 4 l 5 5", false),
        "lyrics" to Def("M 12 3 c -4.97 0 -9 3.13 -9 7 0 2.13 1.22 4.02 3.13 5.28 L 5 21 l 4.06 -2.03 c 0.93 0.2 1.92 0.31 2.94 0.31 4.97 0 9 -3.13 9 -7 s -4.03 -7 -9 -7 z", true),
        "queue" to Def("M 3 6 L 21 6 M 3 12 L 21 12 M 3 18 L 15 18", false),
        "home" to Def("M 3 10.5 L 12 3 l 9 7.5 V 20 a 2 2 0 0 1 -2 2 h -4 v -6 h -6 v 6 H 5 a 2 2 0 0 1 -2 -2 z", false),
        "explore" to Def("M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z M 16.24 7.76 L 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76 Z", false),
        "image" to Def("M 5 3 H 19 A 2 2 0 0 1 21 5 V 19 A 2 2 0 0 1 19 21 H 5 A 2 2 0 0 1 3 19 V 5 A 2 2 0 0 1 5 3 Z M 7 8.5 A 1.5 1.5 0 1 0 10 8.5 A 1.5 1.5 0 1 0 7 8.5 Z M 21 15 l -5 -5 L 5 21", false),
        "upload" to Def("M 21 15 v 4 a 2 2 0 0 1 -2 2 H 5 a 2 2 0 0 1 -2 -2 v -4 M 17 8 l -5 -5 -5 5 M 12 3 v 12", false),
        "mail" to Def("M 4 4 H 20 A 2 2 0 0 1 22 6 V 18 A 2 2 0 0 1 20 20 H 4 A 2 2 0 0 1 2 18 V 6 A 2 2 0 0 1 4 4 Z M 22 6 l -10 7 L 2 6", false),
        "check" to Def("M 9 16.2 L 4.8 12 l -1.4 1.4 L 9 19 21 7 l -1.4 -1.4 z", false),
        "login" to Def("M 15 3 h 4 a 2 2 0 0 1 2 2 v 14 a 2 2 0 0 1 -2 2 h -4 M 10 17 l 5 -5 -5 -5 M 15 12 H 3", false),
        "refresh" to Def("M 23 4 v 6 h -6 M 1 20 v -6 h 6 M 3.51 9 a 9 9 0 0 1 14.85 -3.36 L 23 10 M 1 14 l 4.64 4.36 A 9 9 0 0 0 20.49 15", false),
        "sun" to Def("M 8 12 A 4 4 0 1 0 16 12 A 4 4 0 1 0 8 12 Z M 12 2 v 2 M 12 20 v 2 M 4.93 4.93 l 1.41 1.41 M 17.66 17.66 l 1.41 1.41 M 2 12 h 2 M 20 12 h 2 M 4.93 19.07 l 1.41 -1.41 M 17.66 6.34 l 1.41 -1.41", false),
        "calendar" to Def("M 5 4 H 19 A 2 2 0 0 1 21 6 V 20 A 2 2 0 0 1 19 22 H 5 A 2 2 0 0 1 3 20 V 6 A 2 2 0 0 1 5 4 Z M 16 2 v 4 M 8 2 v 4 M 3 10 h 18", false),
        "star" to Def("M 12 2 l 3.09 6.26 L 22 9.27 l -5 4.87 1.18 6.88 L 12 17.77 l -6.18 3.25 L 7 14.14 2 9.27 l 6.91 -1.01 z", false),
        "clock" to Def("M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z M 12 6 v 6 l 4 2", false),
        "gear" to Def("M 9 12 A 3 3 0 1 0 15 12 A 3 3 0 1 0 9 12 Z M 19.4 15 a 1.65 1.65 0 0 0 0.33 1.82 l 0.06 0.06 a 2 2 0 1 1 -2.83 2.83 l -0.06 -0.06 a 1.65 1.65 0 0 0 -1.82 -0.33 1.65 1.65 0 0 0 -1 1.51 V 21 a 2 2 0 1 1 -4 0 v -0.09 A 1.65 1.65 0 0 0 9 19.4 a 1.65 1.65 0 0 0 -1.82 0.33 l -0.06 0.06 a 2 2 0 1 1 -2.83 -2.83 l 0.06 -0.06 a 1.65 1.65 0 0 0 0.33 -1.82 1.65 1.65 0 0 0 -1.51 -1 H 3 a 2 2 0 1 1 0 -4 h 0.09 A 1.65 1.65 0 0 0 4.6 9 a 1.65 1.65 0 0 0 -0.33 -1.82 l -0.06 -0.06 a 2 2 0 1 1 2.83 -2.83 l 0.06 0.06 a 1.65 1.65 0 0 0 1.82 0.33 H 9 a 1.65 1.65 0 0 0 1 -1.51 V 3 a 2 2 0 1 1 4 0 v 0.09 a 1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82 -0.33 l 0.06 -0.06 a 2 2 0 1 1 2.83 2.83 l -0.06 0.06 a 1.65 1.65 0 0 0 -0.33 1.82 V 9 a 1.65 1.65 0 0 0 1.51 1 H 21 a 2 2 0 1 1 0 4 h -0.09 a 1.65 1.65 0 0 0 -1.51 1 z", false),
        "plus" to Def("M 12 5 v 14 M 5 12 h 14", false),
        "logout" to Def("M 9 21 H 5 a 2 2 0 0 1 -2 -2 V 5 a 2 2 0 0 1 2 -2 h 4 M 16 17 l 5 -5 -5 -5 M 21 12 H 9", false),
        "stats" to Def("M 18 20 L 18 10 M 12 20 L 12 4 M 6 20 L 6 14", false),
        "expand" to Def("M 15 3 h 6 v 6 M 9 21 H 3 v -6 M 21 3 l -7 7 M 3 21 l 7 -7", false),
        "volume" to Def("M 11 5 L 6 9 H 2 v 6 h 4 l 5 4 V 5 z M 19.07 4.93 a 10 10 0 0 1 0 14.14 M 15.54 8.46 a 5 5 0 0 1 0 7.07", false),
        "volLow" to Def("M 11 5 L 6 9 H 2 v 6 h 4 l 5 4 V 5 z M 15.54 8.46 a 5 5 0 0 1 0 7.07", false),
        "mute" to Def("M 11 5 L 6 9 H 2 v 6 h 4 l 5 4 V 5 z M 23 9 l -6 6 M 17 9 l 6 6", false),
        "download" to Def("M 21 15 v 4 a 2 2 0 0 1 -2 2 H 5 a 2 2 0 0 1 -2 -2 v -4 M 7 10 l 5 5 5 -5 M 12 15 V 3", false),
        "link" to Def("M 10 13 a 5 5 0 0 0 7.54 0.54 l 3 -3 a 5 5 0 0 0 -7.07 -7.07 l -1.72 1.71 M 14 11 a 5 5 0 0 0 -7.54 -0.54 l -3 3 a 5 5 0 0 0 7.07 7.07 l 1.71 -1.71", false),
        "lock" to Def("M 5 11 H 19 A 2 2 0 0 1 21 13 V 20 A 2 2 0 0 1 19 22 H 5 A 2 2 0 0 1 3 20 V 13 A 2 2 0 0 1 5 11 Z M 7 11 V 7 a 5 5 0 0 1 10 0 v 4", false),
        "shield" to Def("M 12 22 c 0 0 8 -4 8 -10 V 5 l -8 -3 -8 3 v 7 c 0 6 8 10 8 10 z", false),
        "drop" to Def("M 12 2.69 l 5.66 5.66 a 8 8 0 1 1 -11.31 0 z", false),
        "windows" to Def("M 3 5 L 11 4 V 11 H 3 Z M 13 3.7 L 21 2.5 V 11 H 13 Z M 3 13 H 11 V 20 L 3 19 Z M 13 13 H 21 V 21.5 L 13 20.3 Z", false),
        "warn" to Def("M 12 2 L 1 21 h 22 L 12 2 z m 1 16 h -2 v -2 h 2 v 2 z m 0 -4 h -2 v -4 h 2 v 4 z", true)
    )
    private val cache = HashMap<String, ImageVector>()
    fun vec(name: String, fill: Boolean? = null, stroke: Float = 2f): ImageVector {
        val key = "$name|$fill|$stroke"
        cache[key]?.let { return it }
        val def = all[name] ?: all.getValue("music")
        val f = fill ?: def.fill
        val strokeOn = !def.fill
        val v = ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).addPath(
            pathData = addPathNodes(def.d),
            fill = if (f) SolidColor(Color.White) else null,
            stroke = if (strokeOn) SolidColor(Color.White) else null,
            strokeLineWidth = stroke, strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round,
        ).build()
        cache[key] = v
        return v
    }
}

@Composable
fun SIcon(name: String, size: Dp, tint: Color, modifier: Modifier = Modifier, fill: Boolean? = null, stroke: Float = 2f) {
    Image(Icons2.vec(name, fill, stroke), null, modifier.size(size), colorFilter = ColorFilter.tint(tint))
}
