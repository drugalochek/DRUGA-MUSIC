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

        // Ссылка на оплату ЮMoney. type: "AC" — банковская карта, "PC" — кошелёк ЮMoney.
        // quickpay/confirm принимает и GET: сразу перенаправляет на страницу оплаты (проверено).
        public static string PayUrl(string type, string label = null)
        {
            var uid = label ?? Auth.Current?.Uid ?? "";
            return "https://yoomoney.ru/quickpay/confirm?receiver=" + Wallet + "&quickpay-form=button&paymentType=" + type +
                   "&sum=" + Price + "&label=" + Uri.EscapeDataString(uid);
        }

        public static void Pay(string type)
        {
            if (Auth.Current == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(PayUrl(type)) { UseShellExecute = true });
                Poll();
            }
            catch (Exception ex) { Log.W("plus pay: " + ex.Message); Shell.Toast("Не удалось открыть браузер: " + ex.Message); }
        }

        // После перехода к оплате 15 минут раз в 10 секунд проверяем, не включился ли Plus
        public static async void Poll()
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
                    Shell.I?.PlusPaid();
                    return;
                }
            }
        }
    }

    public sealed partial class Shell
    {
        bool plusSheetOpen;
        public void PlusPaid() { if (plusSheetOpen) { CloseSheet(); PlusSheet(); } }

        // Золотая плашка «PLUS» — фирменный цвет подписки
        public static readonly Brush PlusGold = Th.G("#FDE68A", "#F59E0B");
        public static readonly Brush PlusInk = Th.B("#1C1305");

        public void PlusSheet() { PlusSheet(false); }
        public void PlusSheet(bool preview)
        {
            if (!preview && !Guard.Need("SONORA Plus")) return;
            bool active = Plus.Active && !preview;
            var body = new StackPanel();

            var crown = U.Ico("crown", 30, PlusInk, null, 2.2);
            crown.VerticalAlignment = VerticalAlignment.Center;
            var bt = new TextBlock { Text = "SONORA PLUS", FontFamily = Th.Font, FontSize = 24, Foreground = PlusInk };
            var bs = U.Tx(active ? "Активен до " + Plus.UntilText : Plus.Price + " ₽ за " + Plus.Days + " дней · без автосписаний", 13, Th.B("#1C1305", .8));
            bs.Margin = new Thickness(0, 3, 0, 0);
            var bcol = U.Col(bt, bs);
            bcol.Margin = new Thickness(14, 0, 0, 0);
            bcol.VerticalAlignment = VerticalAlignment.Center;
            body.Children.Add(new Border
            {
                Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 0, 12),
                Background = PlusGold, Effect = U.Shadow(30, 8, .45, Th.C("#F59E0B")), Child = U.Cols("a *", crown, bcol),
            });

            Action<string, string> perk = (icon, text) =>
            {
                var t = U.Wrap(text, 13.5, Th.Txt);
                t.Margin = new Thickness(12, 0, 0, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                body.Children.Add(new Border { Padding = new Thickness(4, 5, 4, 5), Child = U.Cols("a *", U.Ico(icon, 19, Th.B("#FBBF24")), t) });
            };
            perk("globe", "До 100 публичных треков вместо 2");
            perk("upload", "Файлы до 40 МБ и до 30 минут");
            perk("crown", "Значок PLUS в профиле и ранний доступ к новому");
            perk("heart", "Поддержка SONORA — без рекламы, как и раньше");

            // QR-код: оплата с телефона
            var uid = preview ? "preview-uid-000000000000000000" : Auth.Current.Uid;
            FrameworkElement qrBox;
            try
            {
                var src = Qr.Image(Plus.PayUrl("AC", uid));
                double units = src.Width, px = Math.Max(2, Math.Floor(150 / units)) * units;
                var img = new Image { Source = src, Width = px, Height = px, SnapsToDevicePixels = true };
                RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
                qrBox = new Border { CornerRadius = new CornerRadius(14), Background = Brushes.White, Padding = new Thickness(4), Child = img, VerticalAlignment = VerticalAlignment.Center };
            }
            catch (Exception ex) { Log.W("qr: " + ex.Message); qrBox = null; }
            if (qrBox != null)
            {
                var qt = U.Tx(active ? "Продлить с телефона" : "Оплата с телефона", 15, Th.Txt);
                qt.FontWeight = FontWeights.SemiBold;
                var qd = U.Wrap("Наведи камеру на QR-код — откроется оплата " + Plus.Price + " ₽ картой в ЮMoney. Plus включится здесь сам, примерно через минуту.", 12.5, Th.Dim);
                qd.Margin = new Thickness(0, 6, 0, 0);
                var qcol = U.Col(qt, qd);
                qcol.Margin = new Thickness(16, 0, 0, 0);
                qcol.VerticalAlignment = VerticalAlignment.Center;
                body.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(18), Background = Th.Glass(.05), BorderBrush = Th.GlassEdge, BorderThickness = new Thickness(1),
                    Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 12), Child = U.Cols("a *", qrBox, qcol),
                });
            }

            body.Children.Add(Acts(SheetBtn("Картой на компьютере · " + Plus.Price + " ₽", () => Plus.Pay("AC"))));
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
            OpenSheet(active ? "SONORA Plus активен" : "SONORA Plus", null, body, () => plusSheetOpen = false);
            plusSheetOpen = true;
            // пока открыт экран оплаты, ждём платёж (например, по QR с телефона)
            if (!preview && !active) Plus.Poll();
        }
    }
}
