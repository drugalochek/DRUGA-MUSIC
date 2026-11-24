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
const esc = (v) => String(v ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
const secretOf = (env) => String(env.YM_SECRET || "").trim();   // лишние пробелы/перенос строки при вставке не ломают подпись

// журнал уведомлений: в памяти + в базе (ymlog), чтобы было видно, что пришло от ЮMoney и чем закончилось
const memLog = [];
async function logEvent(env, e) {
  e.at = Date.now();
  console.log("yoomoney", JSON.stringify(e));
  memLog.unshift(e); if (memLog.length > 30) memLog.pop();
  try { await db(env, "PUT", "ymlog/" + e.at + "_" + Math.floor(Math.random() * 1e6), e); } catch (x) { e.logError = String(x.message || x); }
}

// выдать Plus: продление прибавляется к текущему сроку
async function grant(env, uid, periods, op, amount) {
  const cur = (await db(env, "GET", "plus/" + uid)) || {};
  const now = Date.now();
  const base = Math.max(now, Number(cur.until) || 0);
  const until = base + periods * DAYS * DAY;
  await db(env, "PUT", "plus/" + uid, { until, updatedAt: now, lastPayment: op, total: Math.round(((Number(cur.total) || 0) + amount) * 100) / 100 });
  return until;
}

// разбор одного уведомления (общий для живого уведомления и ручного подтверждения из /diag)
async function processPayment(env, p) {
  if (p.currency && p.currency !== "643") return { status: "ignored: currency" };
  if (p.unaccepted === "true" || p.codepro === "true") return { status: "ignored: on hold" };
  const uid = (p.label || "").trim();
  const op = (p.operation_id || "").replace(/[^A-Za-z0-9_-]/g, "");
  if (!op) return { status: "ignored: no operation_id" };
  const withdraw = parseFloat(p.withdraw_amount || "0") || 0;   // сколько списали с плательщика
  const amount = parseFloat(p.amount || "0") || 0;              // сколько пришло на кошелёк
  if (!/^[A-Za-z0-9_-]{6,64}$/.test(uid)) {
    await db(env, "PUT", "payments/" + op, { uid: null, label: p.label || "", amount, withdraw, note: "no SONORA label", at: Date.now() });
    return { status: "ignored: no label (оплата не из SONORA)" };
  }
  const prev = await db(env, "GET", "payments/" + op);
  if (prev && prev.until) return { status: "dup", uid, until: prev.until };
  const paid = withdraw > 0 ? withdraw : amount / 0.97;
  const periods = Math.floor((paid + 0.5) / PRICE);
  let until = null;
  if (periods >= 1) until = await grant(env, uid, periods, op, amount);
  await db(env, "PUT", "payments/" + op, { uid, amount, withdraw, periods, until, type: p.notification_type || "", datetime: p.datetime || "", at: Date.now() });
  return { status: periods >= 1 ? "plus granted" : "too small", uid, periods, until };
}

async function diag(env, url) {
  const key = url.searchParams.get("key") || "";
  if (!secretOf(env) || key.trim() !== secretOf(env)) return bad("Нужен ключ: /diag?key=<YM_SECRET>", 403);
  const out = [];
  // 1) доступ к базе
  let fb = "ok";
  try { await db(env, "PUT", "diag/ping", { at: Date.now() }); const back = await db(env, "GET", "diag/ping"); if (!back) fb = "запись не читается"; }
  catch (x) { fb = "ОШИБКА: " + (x.message || x); }
  // 2) ручное подтверждение платежа из журнала (если подпись не сошлась)
  let action = "";
  const approve = url.searchParams.get("approve");
  if (approve) {
    try {
      const e = await db(env, "GET", "ymlog/" + approve.replace(/[^0-9_]/g, ""));
      if (!e || !e.params) action = "Запись не найдена";
      else { const r = await processPayment(env, e.params); action = "Результат: " + r.status + (r.until ? " · Plus до " + new Date(r.until).toISOString().slice(0, 16).replace("T", " ") + " UTC" : ""); }
    } catch (x) { action = "ОШИБКА: " + (x.message || x); }
  }
  const grantUid = (url.searchParams.get("grant") || "").trim();
  if (grantUid) {
    if (!/^[A-Za-z0-9_-]{6,64}$/.test(grantUid)) action = "Неверный UID";
    else {
      try { const until = await grant(env, grantUid, 1, "manual-" + Date.now(), 0); action = "Выдано вручную: Plus для " + grantUid + " до " + new Date(until).toISOString().slice(0, 16).replace("T", " ") + " UTC"; }
      catch (x) { action = "ОШИБКА: " + (x.message || x); }
    }
  }
  let pays = [];
  try { const all = (await db(env, "GET", "payments")) || {}; pays = Object.entries(all).sort((a, b) => (b[1].at || 0) - (a[1].at || 0)).slice(0, 10); } catch (x) { }
  let log = [];
  try { const all = (await db(env, "GET", "ymlog")) || {}; log = Object.entries(all).sort((a, b) => b[0].localeCompare(a[0])).slice(0, 20); }
  catch (x) { log = memLog.map((e) => [String(e.at), e]); }
  out.push("<h2>SONORA Plus — диагностика</h2>");
  out.push("<p>Секрет YM_SECRET: <b>" + (secretOf(env) ? "есть" : "НЕТ") + "</b> · База Firebase: <b>" + esc(fb) + "</b> · Цена: " + PRICE + " ₽ / " + DAYS + " дн</p>");
  if (action) out.push("<p style='background:#1e3a8a;padding:8px'>" + esc(action) + "</p>");
  out.push("<h3>Последние уведомления от ЮMoney</h3>");
  if (!log.length) out.push("<p><b>Ни одного уведомления не пришло.</b> Значит, ЮMoney не отправляет их на этот адрес: проверь галочку «Отправлять HTTP-уведомления» и адрес …/yoomoney.</p>");
  out.push("<table border=1 cellpadding=4 style='border-collapse:collapse'><tr><th>Когда (UTC)</th><th>Тип</th><th>Сумма</th><th>Метка (uid)</th><th>Итог</th><th></th></tr>");
  for (const [id, e] of log) {
    const p = e.params || {};
    const canApprove = p.label && /bad signature|error/i.test(e.status || "");
    out.push("<tr><td>" + esc(new Date(e.at).toISOString().slice(0, 19).replace("T", " ")) + "</td><td>" + esc(p.notification_type) + "</td><td>" + esc(p.withdraw_amount || p.amount) + "</td><td>" + esc(p.label) + "</td><td>" + esc(e.status) + "</td><td>" +
      (canApprove ? "<a href='/diag?key=" + encodeURIComponent(key) + "&approve=" + encodeURIComponent(id) + "'>Выдать Plus</a>" : "") + "</td></tr>");
  }
  out.push("</table>");
  out.push("<h3>Платежи, записанные сервером</h3><table border=1 cellpadding=4 style='border-collapse:collapse'><tr><th>Операция</th><th>Когда (UTC)</th><th>Сумма</th><th>UID</th><th>Итог</th></tr>");
  if (!pays.length) out.push("<tr><td colspan=5>пока ни одного</td></tr>");
  for (const [op, v] of pays) out.push("<tr><td>" + esc(op) + "</td><td>" + esc(v.at ? new Date(v.at).toISOString().slice(0, 19).replace("T", " ") : "") + "</td><td>" + esc(v.withdraw || v.amount) + "</td><td>" + esc(v.uid || v.label) + "</td><td>" + esc(v.until ? "Plus до " + new Date(v.until).toISOString().slice(0, 10) : v.note || ("периодов: " + v.periods)) + "</td></tr>");
  out.push("</table>");
  out.push("<h3>Выдать Plus вручную (30 дней)</h3><form method=get action=/diag><input type=hidden name=key value='" + esc(key) + "'><input name=grant placeholder='UID пользователя из Firebase → Authentication' size=40> <button>Выдать</button></form>");
  return new Response("<!doctype html><meta charset=utf-8><body style='font-family:sans-serif;background:#0b0b0f;color:#eee'>" + out.join("\n"), { headers: { "content-type": "text/html; charset=utf-8" } });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/health") {
      return Response.json({ ok: true, ymSecret: !!secretOf(env), firebase: env.FB_SA ? "service-account" : env.FB_SECRET ? "secret" : false, price: PRICE, days: DAYS, version: 4 });
    }
    if (url.pathname === "/diag") return diag(env, url);
    // уведомления принимаем и на /yoomoney, и на корневой адрес (если в ЮMoney адрес указан без /yoomoney)
    if (url.pathname !== "/yoomoney" && url.pathname !== "/") return bad("SONORA Plus", 404);
    // ЮMoney при сохранении адреса может проверять его обычным запросом — отвечаем 200
    if (request.method !== "POST") return ok("SONORA Plus: endpoint ok");

    let p = {};
    try {
      const form = await request.formData();
      for (const [k, v] of form.entries()) p[k] = String(v);
    } catch (e) { p = {}; }

    // кнопка «Тест» в настройках ЮMoney — всегда 200
    if (p.test_notification === "true") { await logEvent(env, { status: "test ok", params: { notification_type: p.notification_type, amount: p.amount } }); return ok("test ok"); }
    const params = { ...p }; delete params.sha1_hash; delete params.sign;
    if (!secretOf(env) || (!env.FB_SA && !env.FB_SECRET)) { await logEvent(env, { status: "error: secrets are not configured", params }); return bad("secrets are not configured", 500); }

    // подпись: подходит любая из двух — новая (sign) или классическая (sha1_hash)
    const valid = (await checkSign(p, secretOf(env))) || (await checkSha1(p, secretOf(env)));
    if (!valid) {
      await logEvent(env, { status: "bad signature (не совпал YM_SECRET)", params, hasSign: !!p.sign, hasSha1: !!p.sha1_hash });
      return bad("bad signature", 403);
    }
    try {
      const r = await processPayment(env, p);
      await logEvent(env, { status: r.status, params, until: r.until || null });
      return ok(r.status);
    } catch (x) {
      await logEvent(env, { status: "error: " + (x.message || x), params });
      return bad("error", 500);   // ЮMoney повторит уведомление позже
    }
  },
};
