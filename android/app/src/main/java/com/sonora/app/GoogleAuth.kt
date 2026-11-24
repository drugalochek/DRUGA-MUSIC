package com.sonora.app

import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.Base64
import android.util.Log
import androidx.credentials.CredentialManager
import androidx.credentials.CustomCredential
import androidx.credentials.GetCredentialRequest
import androidx.credentials.exceptions.GetCredentialCancellationException
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.json.JSONObject
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URLDecoder
import java.security.SecureRandom
import kotlin.concurrent.thread

fun rnd64(n: Int): String { val b = ByteArray(n); SecureRandom().nextBytes(b); return Base64.encodeToString(b, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING) }

fun openUrl(ctx: Context, url: String): Boolean = try {
    ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); true
} catch (e: Exception) { toast("Не удалось открыть браузер: " + (e.message ?: "")); false }

// Пока человек в браузере, Android может «заморозить» приложение в фоне, и локальная страница входа
// перестанет отвечать. Короткая служба с уведомлением держит приложение живым на время входа.
class KeepAliveService : Service() {
    private val h = Handler(Looper.getMainLooper())
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val nm = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) nm.createNotificationChannel(NotificationChannel("signin", "Вход в аккаунт", NotificationManager.IMPORTANCE_LOW))
        val text = intent?.getStringExtra("text") ?: "Ждём ответ из браузера…"
        val open = android.app.PendingIntent.getActivity(this, 2, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            android.app.PendingIntent.FLAG_IMMUTABLE or android.app.PendingIntent.FLAG_UPDATE_CURRENT)
        val n = (if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, "signin") else @Suppress("DEPRECATION") Notification.Builder(this))
            .setSmallIcon(android.R.drawable.ic_popup_sync).setContentTitle("SONORA").setContentText(text).setContentIntent(open).setOngoing(true).build()
        try {
            if (Build.VERSION.SDK_INT >= 29) startForeground(7, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC) else startForeground(7, n)
        } catch (e: Exception) { Log.w("SONORA", "keepalive: $e"); stopSelf(); return START_NOT_STICKY }
        h.removeCallbacksAndMessages(null)
        h.postDelayed({ stopSelf() }, 6 * 60_000L)
        return START_NOT_STICKY
    }
    override fun onDestroy() { h.removeCallbacksAndMessages(null); super.onDestroy() }

    companion object {
        fun start(ctx: Context, text: String) {
            try {
                val i = Intent(ctx, KeepAliveService::class.java).putExtra("text", text)
                if (Build.VERSION.SDK_INT >= 26) ctx.startForegroundService(i) else ctx.startService(i)
            } catch (e: Exception) { Log.w("SONORA", "keepalive start: $e") }
        }
        fun stop(ctx: Context) { try { ctx.stopService(Intent(ctx, KeepAliveService::class.java)) } catch (_: Exception) { } }
    }
}

// ---------- мини-HTTP на 127.0.0.1 (как на ПК): страница входа Google и приём кода Spotify ----------
class LoopReq(val method: String, val path: String, val query: Map<String, String>, val host: String, val body: String)
class LoopResp(val status: String, val type: String, val text: String, val extra: String = "")

class LoopServer(port: Int, private val handler: (LoopReq) -> LoopResp) {
    private val v4 = ServerSocket(port, 16, InetAddress.getByName("127.0.0.1"))
    val port: Int = v4.localPort
    private val v6: ServerSocket? = try { ServerSocket(this.port, 16, InetAddress.getByName("::1")) } catch (_: Exception) { null }
    @Volatile private var open = true

    init { for (s in listOfNotNull(v4, v6)) thread(isDaemon = true, name = "sonora-loop") { loop(s) } }

    private fun loop(ss: ServerSocket) {
        while (open) {
            val c = try { ss.accept() } catch (_: Exception) { if (!open) return; continue }
            thread(isDaemon = true) { try { serve(c) } catch (e: Exception) { Log.w("SONORA", "loop: $e") } finally { try { c.close() } catch (_: Exception) { } } }
        }
    }

    private fun serve(c: Socket) {
        c.soTimeout = 8000
        val inp: InputStream = c.getInputStream()
        val buf = java.io.ByteArrayOutputStream()
        val tmp = ByteArray(8192)
        var headEnd = -1; var len = 0
        while (buf.size() < 256 * 1024) {
            val n = try { inp.read(tmp) } catch (_: Exception) { -1 }
            if (n <= 0) break
            buf.write(tmp, 0, n)
            val all = buf.toByteArray()
            if (headEnd < 0) {
                val s = String(all, Charsets.ISO_8859_1)
                headEnd = s.indexOf("\r\n\r\n")
                if (headEnd >= 0) len = Regex("(?im)^content-length:\\s*(\\d+)").find(s.substring(0, headEnd))?.groupValues?.get(1)?.toIntOrNull() ?: 0
            }
            if (headEnd >= 0 && all.size >= headEnd + 4 + len) break
        }
        val raw = buf.toByteArray()
        if (headEnd < 0) return
        val head = String(raw, 0, headEnd, Charsets.ISO_8859_1)
        val body = String(raw, headEnd + 4, maxOf(0, minOf(len, raw.size - headEnd - 4)), Charsets.UTF_8)
        val first = head.lineSequence().first().trim().split(' ')
        val target = first.getOrElse(1) { "/" }
        val host = Regex("(?im)^host:\\s*([^\\r\\n:]+)").find(head)?.groupValues?.get(1)?.trim()?.lowercase() ?: "localhost"
        val r = handler(LoopReq(first.getOrElse(0) { "GET" }, target.substringBefore('?'), parseQuery(target), host, body))
        val bytes = r.text.toByteArray(Charsets.UTF_8)
        val out: OutputStream = c.getOutputStream()
        out.write(("HTTP/1.1 ${r.status}\r\nContent-Type: ${r.type}\r\nContent-Length: ${bytes.size}\r\nCache-Control: no-store\r\n${r.extra}Connection: close\r\n\r\n").toByteArray(Charsets.ISO_8859_1))
        out.write(bytes); out.flush()
    }

    fun close() { open = false; try { v4.close() } catch (_: Exception) { }; try { v6?.close() } catch (_: Exception) { } }

    companion object {
        fun parseQuery(target: String): Map<String, String> {
            val i = target.indexOf('?'); if (i < 0) return emptyMap()
            val res = HashMap<String, String>()
            for (pair in target.substring(i + 1).split('&')) {
                val kv = pair.split('=', limit = 2)
                if (kv[0].isEmpty()) continue
                res[URLDecoder.decode(kv[0], "UTF-8")] = if (kv.size > 1) URLDecoder.decode(kv[1], "UTF-8") else ""
            }
            return res
        }
    }
}

const val BACK_TO_APP = "intent://auth#Intent;scheme=sonora;package=com.sonora.app;end"

// страница «готово» для браузера: с кнопкой возврата в приложение
fun doneHtml(title: String, text: String, ok: Boolean = true): String =
    "<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>SONORA</title>" +
    "<style>html,body{margin:0;height:100%;background:#050608;color:#F5F7FA;font-family:system-ui,sans-serif}.w{min-height:100%;display:flex;align-items:center;justify-content:center}" +
    ".c{width:380px;max-width:88vw;text-align:center;padding:34px 26px;border-radius:24px;background:#101216;border:1px solid rgba(255,255,255,.08)}" +
    ".l{width:64px;height:64px;border-radius:18px;margin:0 auto 18px;background:linear-gradient(135deg,#60a5fa,#1d4ed8)}h2{margin:0 0 8px;font-size:22px}p{color:#8C93A1;font-size:14px;line-height:1.45;margin:0 0 22px}" +
    "a{display:block;border-radius:14px;padding:14px;font-size:15px;font-weight:600;text-decoration:none;color:#fff;background:linear-gradient(135deg,#60a5fa,#1d4ed8)}</style></head>" +
    "<body><div class='w'><div class='c'><div class='l'></div><h2>${if (ok) "✓ " else ""}$title</h2><p>$text</p><a href='$BACK_TO_APP'>Вернуться в SONORA</a></div></div></body></html>"

object GoogleAuth {
    // веб-клиент Google проекта sonora-ab012 (тот же, через который Firebase входит на ПК)
    const val WEB_CLIENT_ID = "614449855011-hhun41dk8mm6gvnd9e5olg933sd6bobg.apps.googleusercontent.com"
    private const val API_KEY = "AIzaSyCgBtQNo-cebuAoJ7u9vkOGHHpHI0DxSWQ"
    private var pending: CompletableDeferred<JSONObject>? = null
    var lastNativeError: String? = null
        private set

    fun cancel() { pending?.completeExceptionally(AuthException("Вход через Google отменён.")) }

    // 1) окно выбора аккаунта Google прямо в телефоне; 2) если оно недоступно — вход через браузер, как на ПК
    suspend fun signIn(activity: Activity, onBrowser: () -> Unit): Boolean {
        val native = try { nativeToken(activity) } catch (e: GetCredentialCancellationException) {
            throw AuthException("Вход через Google отменён.")
        } catch (e: Exception) {
            lastNativeError = e.javaClass.simpleName + ": " + (e.message ?: ""); Log.w("SONORA", "google native: $lastNativeError"); null
        }
        if (native != null) return Auth.signInGoogle(native, null)
        onBrowser()
        val got = browser(activity)
        return Auth.signInGoogle(got.optString("idToken").ifEmpty { null }, got.optString("accessToken").ifEmpty { null })
    }

    private suspend fun nativeToken(activity: Activity): String? {
        val cm = CredentialManager.create(activity)
        val req = GetCredentialRequest.Builder().addCredentialOption(GetSignInWithGoogleOption.Builder(WEB_CLIENT_ID).build()).build()
        val cred = cm.getCredential(activity, req).credential
        if (cred is CustomCredential && cred.type == GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL)
            return GoogleIdTokenCredential.createFrom(cred.data).idToken
        return null
    }

    private suspend fun browser(ctx: Context): JSONObject {
        cancel()
        val state = rnd64(16)
        val d = CompletableDeferred<JSONObject>()
        pending = d
        val server = withContext(Dispatchers.IO) {
            var srv: LoopServer? = null
            srv = LoopServer(0) { r -> handle(r, srv?.port ?: 0, state, d) }
            srv!!
        }
        KeepAliveService.start(ctx, "Вход через Google: выбери аккаунт в браузере")
        try {
            if (!openUrl(ctx, "http://localhost:${server.port}/?s=$state")) throw AuthException("Не удалось открыть браузер")
            return withTimeout(5 * 60_000L) { d.await() }
        } catch (e: TimeoutCancellationException) {
            throw AuthException("Время ожидания входа через Google истекло. Нажми «Войти через Google» ещё раз.")
        } finally {
            server.close(); KeepAliveService.stop(ctx)
            if (pending === d) pending = null
        }
    }

    private fun handle(r: LoopReq, port: Int, state: String, d: CompletableDeferred<JSONObject>): LoopResp {
        val ok = r.query["s"] == state
        if (r.method == "GET" && r.path == "/" && r.host != "localhost")   // Firebase разрешает только домен localhost
            return LoopResp("302 Found", "text/plain", "", "Location: http://localhost:$port/?s=${Uri.encode(r.query["s"] ?: "")}\r\n")
        if (r.method == "GET" && r.path == "/") return if (ok) LoopResp("200 OK", "text/html; charset=utf-8", page(state))
            else LoopResp("403 Forbidden", "text/plain; charset=utf-8", "Ссылка устарела. Нажми «Войти через Google» в SONORA ещё раз.")
        if (r.method == "POST" && ok && r.path == "/done") {
            try { d.complete(JSONObject(r.body)) } catch (_: Exception) { }
            return LoopResp("200 OK", "text/plain", "ok")
        }
        if (r.method == "POST" && ok && r.path == "/log") { Log.w("SONORA", "google page: " + r.body); return LoopResp("200 OK", "text/plain", "ok") }
        return LoopResp("404 Not Found", "text/plain", "")
    }

    private fun page(state: String) = """<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>SONORA — вход через Google</title>
<style>html,body{margin:0;height:100%;background:#050608;color:#F5F7FA;font-family:system-ui,sans-serif}
.w{min-height:100%;display:flex;align-items:center;justify-content:center}.c{width:380px;max-width:88vw;text-align:center;padding:34px 26px;border-radius:24px;background:#101216;border:1px solid rgba(255,255,255,.08)}
.l{width:64px;height:64px;border-radius:18px;margin:0 auto 18px;background:linear-gradient(135deg,#60a5fa,#1d4ed8)}h2{margin:0 0 8px;font-size:22px}p{color:#8C93A1;font-size:14px;line-height:1.45;margin:0 0 22px}
button,a.b{width:100%;box-sizing:border-box;border:0;border-radius:14px;padding:14px;font-size:15px;font-weight:600;cursor:pointer;background:#fff;color:#111;display:flex;align-items:center;justify-content:center;gap:10px;text-decoration:none}
a.b{background:linear-gradient(135deg,#60a5fa,#1d4ed8);color:#fff;display:none}button:disabled{opacity:.6}.e{color:#F87171}.ok{color:#60A5FA}</style></head><body><div class='w'><div class='c'><div class='l'></div>
<h2 id='t'>Вход в SONORA</h2><p id='m'>Нажми кнопку и выбери аккаунт Google. Если аккаунта SONORA ещё нет — он создастся сам.</p>
<button id='b'><svg width='18' height='18' viewBox='0 0 48 48'><path fill='#FFC107' d='M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.2 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.3-.1-2.4-.4-3.5z'/><path fill='#FF3D00' d='M6.3 14.7l6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z'/><path fill='#4CAF50' d='M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-8l-6.5 5C9.5 39.6 16.2 44 24 44z'/><path fill='#1976D2' d='M43.6 20.5H42V20H24v8h11.3c-.8 2.2-2.2 4.2-4.1 5.6l6.2 5.2C37 39.2 44 34 44 24c0-1.3-.1-2.4-.4-3.5z'/></svg><span>Войти через Google</span></button>
<a class='b' id='r' href='$BACK_TO_APP'>Вернуться в SONORA</a></div></div>
<script src='https://www.gstatic.com/firebasejs/10.12.2/firebase-app-compat.js'></script>
<script src='https://www.gstatic.com/firebasejs/10.12.2/firebase-auth-compat.js'></script>
<script>
var S='$state',b=document.getElementById('b'),m=document.getElementById('m'),t=document.getElementById('t'),R=document.getElementById('r');
function msg(x,c){m.textContent=x;m.className=c||''}
function send(path,obj){return fetch(path+'?s='+S,{method:'POST',headers:{'Content-Type':'text/plain'},body:JSON.stringify(obj||{})})}
function done(r){var c=r&&r.credential;if(!c){msg('Google не вернул данные. Попробуй ещё раз.','e');b.disabled=false;return}
 msg('Секунду…');send('/done',{idToken:c.idToken||'',accessToken:c.accessToken||''}).then(function(){t.textContent='Готово!';msg('Вход выполнен. Возвращайся в SONORA.','ok');b.style.display='none';R.style.display='flex';setTimeout(function(){location.href=R.href},400)})
 .catch(function(){msg('SONORA закрыта или вход уже отменён. Запусти вход в приложении заново.','e')})}
function fail(e){var c=e&&e.code||'';b.disabled=false;
 if(c==='auth/popup-closed-by-user'||c==='auth/cancelled-popup-request'){msg('Окно Google закрыто. Нажми кнопку ещё раз.');return}
 if(c==='auth/network-request-failed'){msg('Нет связи с Google. Проверь интернет и попробуй ещё раз.','e');return}
 msg('Ошибка: '+(e&&e.message||e),'e');send('/log',{code:c,message:String(e&&e.message||e)})}
if(!window.firebase){msg('Не удалось загрузить модуль входа Google. Проверь интернет или VPN и обнови страницу.','e');b.disabled=true}
else{
 firebase.initializeApp({apiKey:'$API_KEY',authDomain:'sonora-ab012.firebaseapp.com',projectId:'sonora-ab012'});
 var A=firebase.auth(),P=new firebase.auth.GoogleAuthProvider();P.addScope('email');P.addScope('profile');P.setCustomParameters({prompt:'select_account'});
 A.getRedirectResult().then(function(r){if(r&&r.credential)done(r)}).catch(fail);
 b.onclick=function(){b.disabled=true;msg('Выбери аккаунт в окне Google…');
  A.signInWithPopup(P).then(done).catch(function(e){if(e&&(e.code==='auth/popup-blocked'||e.code==='auth/operation-not-supported-in-this-environment')){msg('Переходим на страницу Google…');A.signInWithRedirect(P).catch(fail)}else fail(e)})};
}
</script></body></html>"""
}
