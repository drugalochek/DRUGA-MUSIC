using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sonora
{
    // ---------- SONORA Plus: оплата через ЮMoney, срок подписки хранится в базе SONORA (plus/<uid>) ----------
    // Срок записывает только сервер (Cloudflare Worker, папка server/) после уведомления ЮMoney о платеже.
    // Приложение его только читает — правила базы запрещают менять plus/<uid> с клиента.
    public static class Plus
    {
        public const string Wallet = "4100117325240454";
        public const int Price = 99;
        public const int Days = 30;

        public static long Until { get; private set; }
        public static bool Active { get { return Auth.Current != null && Until > Library.Now; } }
        public static event Action Changed;
        static string uidLoaded;
        static int pollGen;

        public static string UntilText
        {
            get { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(Until).ToLocalTime().ToString("d MMMM yyyy"); }
        }

        public static async Task Refresh()
        {
            var s = Auth.Current;
            if (s == null) { if (Until != 0 || uidLoaded != null) { Until = 0; uidLoaded = null; Changed?.Invoke(); } return; }
            try
            {
                var tok = await Auth.Token();
                var r = await Net.Send("GET", Auth.Db + "/plus/" + Uri.EscapeDataString(s.Uid) + ".json?auth=" + Uri.EscapeDataString(tok));
                if (!r.Ok) { Log.W("plus: " + r.Code + " " + r.Body); return; }
                var d = J.D(J.Parse(r.Body));
                var until = (long)J.Num(d, "until");
                bool changed = until != Until || uidLoaded != s.Uid;
                Until = until; uidLoaded = s.Uid;
                if (changed) Changed?.Invoke();
            }
            catch (Exception ex) { Log.W("plus: " + ex.Message); }
        }

        // Открывает оплату ЮMoney. type: "AC" — банковская карта, "PC" — кошелёк ЮMoney.
        // Форма quickpay принимает только POST, поэтому открываем маленькую страницу, которая сама отправляет форму.
        public static void Pay(string type)
        {
            var s = Auth.Current;
            if (s == null) return;
            string E(string v) { return WebUtility.HtmlEncode(v ?? ""); }
            var html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>SONORA Plus — оплата</title>" +
                "<style>body{background:#0b0d12;color:#fff;font:16px Segoe UI,Arial;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}" +
                "button{background:#2563EB;color:#fff;border:0;border-radius:12px;padding:14px 26px;font-size:16px;cursor:pointer}</style></head>" +
                "<body onload=\"document.forms[0].submit()\"><form method=\"POST\" action=\"https://yoomoney.ru/quickpay/confirm\">" +
                "<input type=\"hidden\" name=\"receiver\" value=\"" + E(Wallet) + "\">" +
                "<input type=\"hidden\" name=\"quickpay-form\" value=\"button\">" +
                "<input type=\"hidden\" name=\"paymentType\" value=\"" + E(type) + "\">" +
                "<input type=\"hidden\" name=\"sum\" value=\"" + Price + "\" data-type=\"number\">" +
                "<input type=\"hidden\" name=\"label\" value=\"" + E(s.Uid) + "\">" +
                "<p>Переходим к оплате SONORA Plus…</p><noscript><button type=\"submit\">Оплатить " + Price + " ₽</button></noscript>" +
                "</form></body></html>";
            try
            {
                var file = Path.Combine(Paths.Sub("cache"), "plus-pay.html");
                File.WriteAllText(file, html, new UTF8Encoding(false));
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                Poll();
            }
            catch (Exception ex) { Log.W("plus pay: " + ex.Message); Shell.Toast("Не удалось открыть браузер: " + ex.Message); }
        }

        // После перехода к оплате 15 минут раз в 10 секунд проверяем, не включился ли Plus
        static async void Poll()
        {
            int gen = ++pollGen;
            var before = Until;
            for (int i = 0; i < 90 && gen == pollGen; i++)
            {
                await Task.Delay(10000);
                if (gen != pollGen || Auth.Current == null) return;
                await Refresh();
                if (Until > before && Active)
                {
                    Shell.Toast("SONORA Plus активен до " + UntilText + " — спасибо!");
                    return;
                }
            }
        }
    }

    public sealed partial class Shell
    {
        public void PlusSheet()
        {
            if (!Guard.Need("SONORA Plus")) return;
            var body = new StackPanel();

            var badge = new Border
            {
                Padding = new Thickness(18, 16, 18, 16), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 0, 14),
                Background = Th.Hot, Effect = U.Shadow(30, 10, .55, Th.C("#2563EB")),
            };
            var bt = new TextBlock { Text = "SONORA PLUS", FontFamily = Th.Font, FontSize = 26, Foreground = Th.White };
            var bs = U.Tx(Plus.Active ? "Активен до " + Plus.UntilText : Plus.Price + " ₽ за " + Plus.Days + " дней", 13.5, Th.B("#FFFFFF", .85));
            bs.Margin = new Thickness(0, 4, 0, 0);
            badge.Child = U.Col(bt, bs);
            body.Children.Add(badge);

            Action<string, string> perk = (icon, text) =>
            {
                var t = U.Wrap(text, 13.5, Th.Txt);
                t.Margin = new Thickness(12, 0, 0, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                body.Children.Add(new Border { Padding = new Thickness(4, 7, 4, 7), Child = U.Cols("a *", U.Ico(icon, 20, Th.Accent3), t) });
            };
            perk("globe", "До 100 публичных треков вместо 2");
            perk("upload", "Файлы до 40 МБ и до 30 минут");
            perk("star", "Значок PLUS в профиле и ранний доступ к новым функциям");
            perk("heart", "Поддержка SONORA — без рекламы, как и раньше");

            body.Children.Add(U.M(Note(Plus.Active
                ? "Оплата продлевает Plus ещё на " + Plus.Days + " дней от текущей даты окончания. Автосписаний нет: продлеваешь, только когда сам захочешь."
                : "Оплата разовая, автосписаний нет. Plus включится сам через минуту после оплаты: платёж привязывается к твоему аккаунту SONORA."), 0, 10, 0, 6));

            body.Children.Add(Acts(SheetBtn("Картой · " + Plus.Price + " ₽", () => Plus.Pay("AC"))));
            body.Children.Add(Acts(
                SheetBtn("Кошельком ЮMoney", () => Plus.Pay("PC"), "ghost"),
                SheetBtn("Я оплатил — проверить", async () =>
                {
                    var was = Plus.Until;
                    await Plus.Refresh();
                    Toast(Plus.Active ? (Plus.Until > was ? "SONORA Plus активен до " : "Plus активен до ") + Plus.UntilText
                                      : "Оплата пока не пришла. Обычно это до минуты — попробуй чуть позже");
                    if (Plus.Active) CloseSheet();
                }, "ghost")));
            OpenSheet(Plus.Active ? "SONORA Plus активен" : "SONORA Plus", null, body);
        }
    }
}
