package com.sonora.app

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.annotation.OptIn
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import kotlinx.coroutines.launch
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.ResolvingDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService

// Плеер один на всё приложение: им управляют и экран, и шторка/экран блокировки (через PlaybackService).
object Audio {
    private var exo: ExoPlayer? = null
    lateinit var ctx: Context

    var queue by mutableStateOf<List<Track>>(emptyList())
        private set
    var index by mutableIntStateOf(-1)
        private set
    var playing by mutableStateOf(false)
        private set
    var buffering by mutableStateOf(false)
        private set
    var shuffle by mutableStateOf(false)
        private set
    var repeat by mutableIntStateOf(Player.REPEAT_MODE_OFF)
        private set

    val current: Track? get() = queue.getOrNull(index)
    var source by mutableStateOf("")          // откуда играет: «Микс дня», имя исполнителя…
    var sleepAt by mutableLongStateOf(0L)     // таймер сна (0 — выключен)
        private set
    private var sleepJob: kotlinx.coroutines.Job? = null

    fun setSleep(minutes: Int) {
        sleepJob?.cancel()
        if (minutes <= 0) { sleepAt = 0L; return }
        sleepAt = now() + minutes * 60_000L
        sleepJob = appScope.launch {
            while (sleepAt > 0 && now() < sleepAt) kotlinx.coroutines.delay(1000)
            if (sleepAt > 0) { sleepAt = 0L; exo?.pause(); toast("Таймер сна: воспроизведение остановлено") }
        }
    }

    fun init(c: Context) { ctx = c.applicationContext }

    @OptIn(UnstableApi::class)
    fun player(): ExoPlayer {
        exo?.let { return it }
        val upstream = DefaultDataSource.Factory(ctx)
        // sonora://track?db=…&id=… → скачиваем аудио из базы в кэш и отдаём плееру локальный файл
        val resolving = ResolvingDataSource.Factory(upstream) { spec ->
            val u = spec.uri
            if (u.scheme == "sonora") {
                val f = Catalog.audioFile(ctx, u.getQueryParameter("db") ?: Catalog.SONIC, u.getQueryParameter("id") ?: "")
                spec.withUri(Uri.fromFile(f))
            } else spec
        }
        val p = ExoPlayer.Builder(ctx)
            .setMediaSourceFactory(DefaultMediaSourceFactory(resolving))
            .setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true)
            .setHandleAudioBecomingNoisy(true)
            .setWakeMode(C.WAKE_MODE_LOCAL)
            .build()
        p.addListener(object : Player.Listener {
            override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
                index = p.currentMediaItemIndex
                current?.let { Lib.addRecent(it.id) }
            }
            override fun onIsPlayingChanged(isPlaying: Boolean) { playing = isPlaying }
            override fun onPlaybackStateChanged(state: Int) { buffering = state == Player.STATE_BUFFERING }
            override fun onShuffleModeEnabledChanged(enabled: Boolean) { shuffle = enabled }
            override fun onRepeatModeChanged(mode: Int) { repeat = mode }
            override fun onPlayerError(error: PlaybackException) {
                val cause = generateSequence(error as Throwable) { it.cause }.lastOrNull()?.message
                toast("Не удалось воспроизвести: " + (cause ?: error.errorCodeName))
            }
        })
        exo = p
        return p
    }

    private fun item(t: Track): MediaItem {
        val uri = Uri.Builder().scheme("sonora").authority("track").appendQueryParameter("db", t.db).appendQueryParameter("id", t.id).build()
        val meta = MediaMetadata.Builder().setTitle(t.title).setArtist(t.artist).setAlbumTitle(t.album.ifEmpty { null })
        Covers.bytes(t)?.let { meta.setArtworkData(it, MediaMetadata.PICTURE_TYPE_FRONT_COVER) }
        return MediaItem.Builder().setMediaId(t.id).setUri(uri).setMediaMetadata(meta.build()).build()
    }

    fun play(list: List<Track>, start: Int = 0, shuffleOn: Boolean? = null, from: String? = null) {
        if (list.isEmpty()) return
        if (from != null) source = from
        val p = player()
        queue = list
        val s = if (shuffleOn == true) list.indices.random() else start.coerceIn(0, list.size - 1)
        p.setMediaItems(list.map { item(it) }, s, 0L)
        if (shuffleOn != null) p.shuffleModeEnabled = shuffleOn
        index = s
        p.prepare()
        p.play()
    }

    fun toggle() {
        val p = player()
        if (p.isPlaying) { p.pause(); return }
        if (p.playbackState == Player.STATE_ENDED) p.seekToDefaultPosition()
        if (p.playbackState == Player.STATE_IDLE) p.prepare()
        p.play()
    }

    fun next() { val p = player(); if (p.hasNextMediaItem()) p.seekToNextMediaItem() }
    fun prev() { val p = player(); if (p.currentPosition > 3000 || !p.hasPreviousMediaItem()) p.seekTo(0) else p.seekToPreviousMediaItem() }
    fun seek(ms: Long) { player().seekTo(ms) }
    fun jump(i: Int) { val p = player(); if (i in queue.indices) { p.seekToDefaultPosition(i); p.play() } }
    fun toggleShuffle() { val p = player(); p.shuffleModeEnabled = !p.shuffleModeEnabled }
    fun cycleRepeat() {
        val p = player()
        p.repeatMode = when (p.repeatMode) { Player.REPEAT_MODE_OFF -> Player.REPEAT_MODE_ALL; Player.REPEAT_MODE_ALL -> Player.REPEAT_MODE_ONE; else -> Player.REPEAT_MODE_OFF }
    }
    fun position(): Long = exo?.currentPosition ?: 0L
    fun duration(): Long = exo?.duration?.takeIf { it > 0 } ?: ((current?.duration ?: 0.0) * 1000).toLong()

    fun playNext(t: Track) {
        if (queue.isEmpty()) { play(listOf(t)); return }
        val p = player()
        val at = (index + 1).coerceAtMost(queue.size)
        p.addMediaItem(at, item(t))
        queue = queue.toMutableList().apply { add(at, t) }
    }

    fun removeAt(i: Int) {
        if (i !in queue.indices || i == index) return
        player().removeMediaItem(i)
        queue = queue.toMutableList().apply { removeAt(i) }
        index = player().currentMediaItemIndex
    }

    fun addToQueue(t: Track) {
        if (queue.isEmpty()) { play(listOf(t)); return }
        player().addMediaItem(item(t))
        queue = queue + t
    }
}

class PlaybackService : MediaSessionService() {
    private var session: MediaSession? = null

    override fun onCreate() {
        super.onCreate()
        Audio.init(this)
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        session = MediaSession.Builder(this, Audio.player()).setSessionActivity(open).build()
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session

    override fun onTaskRemoved(rootIntent: Intent?) {
        val p = session?.player
        if (p == null || !p.playWhenReady || p.mediaItemCount == 0) stopSelf()
    }

    override fun onDestroy() {
        session?.release()
        session = null
        super.onDestroy()
    }
}
