using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sonora
{
    public static class Program
    {
        public static string ShotsDir;
        public const string Version = "1.0.0";

        [STAThread]
        public static int Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--shots" && i + 1 < args.Length) ShotsDir = Path.GetFullPath(args[i + 1]);

            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.W("unhandled: " + e.Exception);
                e.Handled = true;
                if (ShotsDir == null) Shell.Toast("Ошибка: " + e.Exception.Message);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.W("fatal: " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) => { Log.W("task: " + e.Exception); e.SetObserved(); };

            Th.InitFont();
            Library.Load();
            if (ShotsDir == null) Auth.LoadSaved();
            var win = new MainWindow(ShotsDir != null);
            app.MainWindow = win;
            win.Show();
            return app.Run();
        }
    }

    public sealed class MainWindow : Window
    {
        readonly Shell shell;
        readonly bool shots;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public MainWindow(bool shotMode)
        {
            shots = shotMode;
            Title = "SONORA";
            Width = shots ? 1280 : 1180;
            Height = shots ? 820 : 800;
            MinWidth = 380;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Th.Bg;
            try { Icon = Img.Res("sonora.ico"); } catch { }
            if (shots) { WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; }

            shell = new Shell(shots);
            Content = shell;
            PreviewKeyDown += (s, e) => shell.HandleKey(e);
            SourceInitialized += (s, e) => DarkTitle();
            Loaded += async (s, e) => await Start();
            Closing += (s, e) => { try { Library.Save(false); } catch { } };
        }

        void DarkTitle()
        {
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                int on = 1;
                if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
                int caption = 0x00120A0A; // COLORREF (BGR) для #0A0A12
                DwmSetWindowAttribute(h, 35, ref caption, 4);
            }
            catch { }
        }

        async Task Start()
        {
            if (shots) { await Shots(); return; }
            if (Auth.Current == null && !Library.D.GuestChosen) shell.OpenWelcome();
            try { await Catalog.LoadAsync(); } catch (Exception ex) { Log.W("catalog start: " + ex.Message); }
            if (Auth.Current != null)
            {
                try { await Auth.LoadProfile(); } catch (Exception ex) { Log.W("profile: " + ex.Message); }
                try { await Cloud.SyncNow(); } catch (Exception ex) { Log.W("sync: " + ex.Message); }
            }
        }

        // ---------- Режим скриншотов: --shots <папка> ----------
        async Task Wait(int ms)
        {
            await Task.Delay(ms);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        async Task Snap(string name)
        {
            await Wait(900);
            shell.UpdateLayout();
            int w = (int)Math.Round(shell.ActualWidth), h = (int)Math.Round(shell.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(shell);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Program.ShotsDir);
            using (var f = File.Create(Path.Combine(Program.ShotsDir, name + ".png"))) enc.Save(f);
        }
        async Task Shots()
        {
            try
            {
                await Snap("00-welcome-loading");
                await Catalog.LoadAsync();
                var all = Catalog.All;
                foreach (var t in all.Take(8)) if (!Library.IsFav(t.Id)) Library.D.Fav.Add(t.Id);
                foreach (var a in Mixes.Artists(all).Take(4)) if (!Library.IsFavArtist(a.Name)) Library.D.FavArtists.Add(a.Name);
                foreach (var t in all.Skip(3).Take(6)) Library.D.Recent.Add(t.Id);
                if (Library.D.Playlists.Count == 0)
                {
                    var p = Library.NewPlaylist("Вечерний вайб", "#8b5cf6");
                    foreach (var t in all.Skip(2).Take(5)) Library.AddToPlaylist(p.Id, t.Id);
                }
                Library.D.GuestChosen = true;

                shell.OpenWelcome(); await Snap("01-welcome");
                shell.OpenAuth(true, "login"); await Snap("02-login");
                shell.OpenAuth(true, "register"); await Snap("03-register");
                shell.CloseSheet();
                shell.Go("home"); await Snap("04-home");
                shell.ScrollTo(560); await Snap("05-home-mixes");
                shell.Go("explore"); await Snap("06-explore");
                shell.Go("fav"); await Snap("07-favorites");
                shell.Go("search"); shell.SetSearch(all.Count > 0 ? all[0].Artist : "a"); await Snap("08-search");
                if (all.Count > 0) { shell.Go("artist", all[0].Artist); await Snap("09-artist"); }
                shell.Go("playlist", "__hour_genre__"); await Snap("10-playlist");
                shell.Go("profile"); await Snap("11-profile");
                if (all.Count > 0)
                {
                    shell.Go("home");
                    var withLyrics = all.FindIndex(t => t.HasLyrics);
                    var idx = Math.Max(0, withLyrics);
                    Player.I.Preview(all, idx);
                    await Snap("12-mini-player");
                    shell.OpenFull(); await Snap("13-player");
                    shell.QueueSheet(); await Snap("14-queue");
                    shell.CloseSheet();
                    if (withLyrics >= 0) { shell.Full.OpenLyrics(); await Snap("15-lyrics"); shell.CloseSheet(); }
                    shell.TimerSheet(); await Snap("16-timer");
                    shell.CloseSheet();
                }
            }
            catch (Exception ex)
            {
                Log.W("shots: " + ex);
                try { File.WriteAllText(Path.Combine(Program.ShotsDir, "error.txt"), ex.ToString()); } catch { }
            }
            Application.Current.Shutdown();
        }
    }
}
