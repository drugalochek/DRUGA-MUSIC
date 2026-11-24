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
        public static readonly string Version = typeof(Program).Assembly.GetName().Version.ToString(3);
        public static string[] StartArgs = new string[0];
        static readonly System.Collections.Generic.Dictionary<string, System.Reflection.Assembly> embedded = new System.Collections.Generic.Dictionary<string, System.Reflection.Assembly>();
        // NAudio вшит в SONORA.exe — подгружаем из ресурсов, чтобы хватало одного файла
        static System.Reflection.Assembly Resolve(object sender, ResolveEventArgs e)
        {
            try
            {
                var name = new System.Reflection.AssemblyName(e.Name).Name + ".dll";
                lock (embedded)
                {
                    if (embedded.TryGetValue(name, out var a)) return a;
                    using (var st = typeof(Program).Assembly.GetManifestResourceStream(name))
                    {
                        if (st == null) return null;
                        var ms = new MemoryStream();
                        st.CopyTo(ms);
                        a = System.Reflection.Assembly.Load(ms.ToArray());
                        embedded[name] = a;
                        return a;
                    }
                }
            }
            catch { return null; }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--shots" && i + 1 < args.Length) ShotsDir = Path.GetFullPath(args[i + 1]);
            StartArgs = ShotsDir != null ? new string[0] : args.Where(a => !a.StartsWith("--")).ToArray();
            if (ShotsDir == null)
            {
                try { if (!Single.TryFirst(StartArgs)) return 0; }
                catch (Exception ex) { Log.W("single start: " + ex.Message); }
            }

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

            try { app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Styles.ScrollBars)); } catch (Exception ex) { Log.W("styles: " + ex.Message); }
            Offline.CleanupTemp();
            app.Exit += (s, e) => Offline.CleanupTemp();
            Th.InitFont();
            Library.Load();
            L.Init(ShotsDir != null && Environment.GetEnvironmentVariable("SONORA_LANG") == "en" ? "en" : Library.D.Lang);
            Ext.Load();
            if (ShotsDir == null) Auth.LoadSaved();
            var win = new MainWindow(ShotsDir != null);
            app.MainWindow = win;
            Single.Received += items => app.Dispatcher.BeginInvoke(new Action(() => Shell.I?.HandleArgs(items)));
            app.Exit += (s, e) => Single.Release();
            win.Show();
            return app.Run();
        }
    }

    public sealed class MainWindow : Window
    {
        readonly Shell shell;
        readonly Splash splash;
        readonly bool shots;
        bool fs; WindowState prevState; WindowStyle prevStyle; ResizeMode prevResize;
        public bool IsFullscreen { get { return fs; } }
        // F11 — во весь экран (без рамки и поверх панели задач)
        public void ToggleFullscreen()
        {
            if (shots) return;
            if (!fs)
            {
                prevState = WindowState; prevStyle = WindowStyle; prevResize = ResizeMode;
                fs = true;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
                Shell.Toast("Полноэкранный режим · F11 — выйти");
            }
            else
            {
                fs = false;
                WindowStyle = prevStyle;
                ResizeMode = prevResize;
                WindowState = WindowState.Normal;
                if (prevState == WindowState.Maximized) WindowState = WindowState.Maximized;
            }
        }

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
            splash = new Splash(shots);
            var root = new System.Windows.Controls.Grid();
            root.Children.Add(shell);
            root.Children.Add(splash);
            Content = root;
            PreviewKeyDown += (s, e) => shell.HandleKey(e);
            SourceInitialized += (s, e) => { DarkTitle(); if (!shots) InitSystem(); };
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

        void InitSystem()
        {
            try { Smtc.Init(new WindowInteropHelper(this).Handle); } catch (Exception ex) { Log.W("smtc init: " + ex.Message); }
            try { Taskbar.Init(this); } catch (Exception ex) { Log.W("taskbar: " + ex.Message); }
            Task.Run(() => WinReg.Register());
        }

        async Task Start()
        {
            if (shots) { await Shots(); return; }
            var started = DateTime.Now;
            splash.SetStatus("Загружаем каталог…");
            try { await Catalog.LoadAsync(); } catch (Exception ex) { Log.W("catalog start: " + ex.Message); }
            splash.SetStatus("Почти готово…");
            var left = 1500 - (DateTime.Now - started).TotalMilliseconds;
            if (left > 0) await Task.Delay((int)left);
            splash.Finish();
            if (Auth.Current == null && !Library.D.GuestChosen) shell.OpenWelcome();
            if (Program.StartArgs.Length > 0) shell.HandleArgs(Program.StartArgs);
            if (Library.D.LastVersion != Program.Version)
            {
                if (!string.IsNullOrEmpty(Library.D.LastVersion)) Shell.Toast(L.T("SONORA обновлена до версии ") + Program.Version);
                Library.D.LastVersion = Program.Version;
                Library.Save(false);
            }
            Offline.Migrate();
            if (Auth.Current != null)
            {
                try { await Auth.LoadProfile(); } catch (Exception ex) { Log.W("profile: " + ex.Message); }
                try { await Plus.Refresh(); } catch (Exception ex) { Log.W("plus start: " + ex.Message); }
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
                await Snap("00-splash");
                try { await Engine.SelfTest(Program.ShotsDir); } catch (Exception ex) { Log.W("selftest: " + ex); }
                await Catalog.LoadAsync();
                splash.Finish();
                var all = Catalog.All;
                foreach (var t in all.Take(8)) if (!Library.IsFav(t.Id)) Library.D.Fav.Add(t.Id);
                foreach (var a in Mixes.Artists(all).Take(4)) if (!Library.IsFavArtist(a.Name)) Library.D.FavArtists.Add(a.Name);
                foreach (var t in all.Skip(3).Take(6)) Library.D.Recent.Add(t.Id);
                if (Library.D.Playlists.Count == 0)
                {
                    var p = Library.NewPlaylist("Вечерний вайб", "#3B82F6");
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
                shell.Go("services"); await Snap("17-services");
                shell.Go("settings"); await Snap("18-settings");
                try { Qr.SelfTest(Path.Combine(Program.ShotsDir, "qr-selftest.txt")); } catch (Exception ex) { Log.W("qr selftest: " + ex); }
                shell.Go("home"); shell.PlusSheet(true); await Snap("20-plus");
                shell.CloseSheet();
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
                    shell.Full.Close();
                    shell.NeedAccount("Скачивание треков"); await Snap("19-guest");
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
