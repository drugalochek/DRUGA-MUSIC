// SONORA Plus — приём HTTP-уведомлений ЮMoney (Cloudflare Worker).
//
// Переменные (Workers → sonora-plus → Settings → Variables and Secrets), тип «Secret»:
//   YM_SECRET — секретное слово из настроек HTTP-уведомлений ЮMoney
//   FB_SA     — весь текст JSON-ключа сервисного аккаунта Firebase
//               (Project settings → Service accounts → Generate new private key)
//   (вместо FB_SA можно FB_SECRET — старый «Database secret», если он есть в консоли)
//
// Адрес для ЮMoney: https://<имя-воркера>.<поддомен>.workers.dev/yoomoney
// Проверка, что воркер жив: https://<имя-воркера>.<поддомен>.workers.dev/health

const DB = "https://sonora-ab012-default-rtdb.firebaseio.com";
const PRICE = 49;          // ₽ за один период Plus
const DAYS = 30;           // длина периода, дней
const DAY = 86400000;

const enc = new TextEncoder();
const hex = (buf) => [...new Uint8Array(buf)].map((b) => b.toString(16).padStart(2, "0")).join("");
const rfc3986 = (s) => encodeURIComponent(String(s ?? "")).replace(/[!'()*]/g, (c) => "%" + c.charCodeAt(0).toString(16).toUpperCase());

function safeEq(a, b) {
  a = String(a || "").toLowerCase(); b = String(b || "").toLowerCase();
  if (!a || a.length !== b.length) return false;
  let r = 0;
  for (let i = 0; i < a.length; i++) r |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return r === 0;
}

// Новая подпись ЮMoney: HMAC-SHA256 от отсортированных по имени параметров (кроме sign), значения URL-кодированы
async function checkSign(p, secret) {
  if (!p.sign) return false;
  const str = Object.keys(p).filter((k) => k !== "sign").sort().map((k) => k + "=" + rfc3986(p[k])).join("&");
  const key = await crypto.subtle.importKey("raw", enc.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return safeEq(hex(await crypto.subtle.sign("HMAC", key, enc.encode(str))), p.sign);
}

// Классическая подпись sha1_hash
async function checkSha1(p, secret) {
  if (!p.sha1_hash) return false;
  const g = (k) => p[k] ?? "";
  const str = [g("notification_type"), g("operation_id"), g("amount"), g("currency"), g("datetime"), g("sender"), g("codepro"), secret, g("label")].join("&");
  return safeEq(hex(await crypto.subtle.digest("SHA-1", enc.encode(str))), p.sha1_hash);
}

// --- доступ к базе: OAuth-токен сервисного аккаунта (FB_SA) или старый секрет (FB_SECRET) ---
const b64url = (data) => {
  const bytes = typeof data === "string" ? enc.encode(data) : new Uint8Array(data);
  let bin = ""; for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
};
let tokenCache = { token: null, exp: 0 };

function serviceAccount(env) {
  if (!env.FB_SA) return null;
  const sa = typeof env.FB_SA === "string" ? JSON.parse(env.FB_SA) : env.FB_SA;
  if (!sa.client_email || !sa.private_key) throw new Error("FB_SA: нет client_email/private_key");
  return sa;
}

async function accessToken(sa) {
  const now = Math.floor(Date.now() / 1000);
  if (tokenCache.token && tokenCache.exp - 60 > now) return tokenCache.token;
  const pem = sa.private_key.replace(/\\n/g, "\n").replace(/-----[^-]+-----/g, "").replace(/\s+/g, "");
  const der = Uint8Array.from(atob(pem), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("pkcs8", der, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"]);
  const head = b64url(JSON.stringify({ alg: "RS256", typ: "JWT" }));
  const claim = b64url(JSON.stringify({
    iss: sa.client_email,
    scope: "https://www.googleapis.com/auth/firebase.database https://www.googleapis.com/auth/userinfo.email",
    aud: "https://oauth2.googleapis.com/token", iat: now, exp: now + 3600,
  }));
  const sig = b64url(await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, enc.encode(head + "." + claim)));
  const r = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: "grant_type=" + encodeURIComponent("urn:ietf:params:oauth:grant-type:jwt-bearer") + "&assertion=" + head + "." + claim + "." + sig,
  });
  if (!r.ok) throw new Error("google token: " + r.status + " " + (await r.text()));
  const j = await r.json();
  tokenCache = { token: j.access_token, exp: now + (Number(j.expires_in) || 3600) };
  return j.access_token;
}

async function authQuery(env) {
  const sa = serviceAccount(env);
  if (sa) return "access_token=" + encodeURIComponent(await accessToken(sa));
  return "auth=" + encodeURIComponent(env.FB_SECRET);
}

async function db(env, method, path, body) {
  const r = await fetch(DB + "/" + path + ".json?" + (await authQuery(env)), {
    method,
    headers: body ? { "Content-Type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!r.ok) throw new Error("firebase " + method + " " + path + ": " + r.status + " " + (await r.text()));
  return r.json();
}

const ok = (msg = "ok") => new Response(msg, { status: 200 });
const bad = (msg, status = 400) => new Response(msg, { status });

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/health") {
      return Response.json({ ok: true, ymSecret: !!env.YM_SECRET, firebase: env.FB_SA ? "service-account" : env.FB_SECRET ? "secret" : false, price: PRICE, days: DAYS });
    }
    if (url.pathname !== "/yoomoney") return bad("SONORA Plus", 404);
    if (request.method !== "POST") return bad("POST only", 405);
    if (!env.YM_SECRET || (!env.FB_SA && !env.FB_SECRET)) return bad("secrets are not configured", 500);

    const form = await request.formData();
    const p = {};
    for (const [k, v] of form.entries()) p[k] = String(v);

    // подпись: подходит любая из двух — новая (sign) или классическая (sha1_hash)
    const valid = (await checkSign(p, env.YM_SECRET)) || (await checkSha1(p, env.YM_SECRET));
    if (!valid) return bad("bad signature", 403);

    // кнопка «Протестировать» в настройках ЮMoney — отвечаем 200 и ничего не пишем
    if (p.test_notification === "true") return ok("test ok");
    if (p.currency && p.currency !== "643") return ok("ignored: currency");
    if (p.unaccepted === "true" || p.codepro === "true") return ok("ignored: on hold");

    // метка платежа = uid аккаунта SONORA (его подставляет приложение)
    const uid = (p.label || "").trim();
    const op = (p.operation_id || "").replace(/[^A-Za-z0-9_-]/g, "");
    if (!op) return ok("ignored: no operation_id");
    if (!/^[A-Za-z0-9_-]{6,64}$/.test(uid)) {
      await db(env, "PUT", "payments/" + op, { uid: null, label: p.label || "", amount: p.amount || "", note: "no SONORA label", at: Date.now() });
      return ok("ignored: no label");
    }

    // повторная доставка того же уведомления — не засчитываем дважды
    if (await db(env, "GET", "payments/" + op)) return ok("dup");

    const withdraw = parseFloat(p.withdraw_amount || "0") || 0;   // сколько списали с плательщика
    const amount = parseFloat(p.amount || "0") || 0;              // сколько пришло на кошелёк
    const paid = withdraw > 0 ? withdraw : amount / 0.97;
    const periods = Math.floor((paid + 0.5) / PRICE);

    let until = null;
    if (periods >= 1) {
      const cur = (await db(env, "GET", "plus/" + uid)) || {};
      const now = Date.now();
      const base = Math.max(now, Number(cur.until) || 0);           // продление прибавляется к текущему сроку
      until = base + periods * DAYS * DAY;
      await db(env, "PUT", "plus/" + uid, { until, updatedAt: now, lastPayment: op, total: Math.round(((Number(cur.total) || 0) + amount) * 100) / 100 });
    }

    await db(env, "PUT", "payments/" + op, {
      uid, amount, withdraw, periods, until, type: p.notification_type || "", datetime: p.datetime || "", at: Date.now(),
    });
    return ok();
  },
};
