# SONORA Plus — сервер оплаты (Cloudflare Worker)

Пользователь платит 99 ₽ через ЮMoney (картой или кошельком) → ЮMoney присылает уведомление на этот воркер →
воркер проверяет подпись и записывает в базу SONORA `plus/<uid>/until` (+30 дней) → приложение видит срок и включает Plus.
Метка платежа (`label`) — uid аккаунта SONORA, её подставляет само приложение.

## 1. Создать воркер
1. dash.cloudflare.com → **Workers & Pages** → **Create** → **Create Worker** (шаблон Hello World), имя `sonora-plus` → **Deploy**.
2. **Edit code** → удали всё → вставь содержимое `worker.js` → **Deploy**.
3. Запомни адрес вида `https://sonora-plus.<твой-поддомен>.workers.dev`.

## 2. Секреты (Settings → Variables and Secrets → Add, тип **Secret**)
- `FB_SA` — Firebase Console → проект **sonora-ab012** → ⚙ Project settings → **Service accounts** → **Generate new private key** → открыть скачанный `.json` и вставить **весь** его текст как значение секрета.
  (Если в консоли ещё есть раздел **Database secrets**, вместо `FB_SA` можно задать `FB_SECRET` с этим секретом.)
- `YM_SECRET` — yoomoney.ru/transfer/myservices/http-notification → «Секретное слово» (кнопка «Показать секрет»).

Проверка: открой `https://sonora-plus.<поддомен>.workers.dev/health` — должно быть `"ymSecret":true,"fbSecret":true`.

## 3. ЮMoney
yoomoney.ru/transfer/myservices/http-notification:
- **Куда отправлять**: `https://sonora-plus.<поддомен>.workers.dev/yoomoney`
- галочка «Отправлять HTTP-уведомления» → **Сохранить** → **Протестировать** (воркер ответит 200).

## 4. Правила базы
Firebase Console → Realtime Database → **Rules** → вставить `../database.rules.json` → **Publish**.
Разделы `plus` и `payments` с клиента записывать нельзя: пишет только воркер (через секрет базы).

## Как устроено
- Подпись проверяется по `sign` (HMAC-SHA256) или `sha1_hash`; поддельные уведомления получают 403.
- Повторная доставка того же платежа не засчитывается (`payments/<operation_id>`).
- 198 ₽ = 60 дней и т. д.; оплата продлевает от текущей даты окончания.
- Платёж без метки SONORA сохраняется в `payments` с пометкой `no SONORA label` — такой можно привязать вручную.
- Цена и срок — константы `PRICE` и `DAYS` в начале `worker.js` (и `Plus.Price` в `Sonora/Plus.cs`).
