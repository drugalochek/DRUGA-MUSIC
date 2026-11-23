using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using Windows.Media;
using Windows.Storage.Streams;

namespace Sonora
{
    // Системные медиа-элементы Windows 10/11: всплывающая панель громкости, медиа-клавиши, экран блокировки
    public static class Smtc
    {
        [ComImport, Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface ISystemMediaTransportControlsInterop
        {
            [return: MarshalAs(UnmanagedType.IInspectable)]
            object GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        }

        static SystemMediaTransportControls s;
        static string lastId;
        static DateTime lastTimeline = DateTime.MinValue;
        public static bool On { get { return s != null; } }

        public static void Init(IntPtr hwnd)
        {
            var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(SystemMediaTransportControls));
            var interop = (ISystemMediaTransportControlsInterop)factory;
            var iid = new Guid("99FA3FF4-1742-42A6-902E-087D41F965EC");
            s = (SystemMediaTransportControls)interop.GetForWindow(hwnd, ref iid);
            s.IsEnabled = true;
            s.IsPlayEnabled = true;
            s.IsPauseEnabled = true;
            s.IsNextEnabled = true;
            s.IsPreviousEnabled = true;
            s.IsStopEnabled = true;
            s.PlaybackStatus = MediaPlaybackStatus.Closed;
            var disp = Application.Current.Dispatcher;
            s.ButtonPressed += (o, e) =>
            {
                var b = e.Button;
                disp.BeginInvoke(new Action(() =>
                {
                    var p = Player.I;
                    switch (b)
                    {
                        case SystemMediaTransportControlsButton.Play: if (!p.IsPlaying) p.Toggle(); break;
                        case SystemMediaTransportControlsButton.Pause: p.Pause(); break;
                        case SystemMediaTransportControlsButton.Stop: p.Pause(); break;
                        case SystemMediaTransportControlsButton.Next: p.Next(); break;
                        case SystemMediaTransportControlsButton.Previous: p.Prev(); break;
                    }
                }));
            };
            s.PlaybackPositionChangeRequested += (o, e) =>
            {
                var pos = e.RequestedPlaybackPosition.TotalSeconds;
                disp.BeginInvoke(new Action(() => Player.I.Seek(pos)));
            };
            Player.I.TrackChanged += () => Safe(UpdateTrack);
            Player.I.StateChanged += () => Safe(UpdateState);
            Player.I.Tick += () => { if ((DateTime.Now - lastTimeline).TotalSeconds > 4) Safe(UpdateTimeline); };
            Safe(UpdateTrack);
        }
        static void Safe(Action a) { try { a(); } catch (Exception ex) { Log.W("smtc: " + ex.Message); } }

        static void UpdateState()
        {
            if (s == null) return;
            var p = Player.I;
            s.PlaybackStatus = p.Current == null ? MediaPlaybackStatus.Closed : p.Loading ? MediaPlaybackStatus.Changing : p.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            if (p.Current != null && p.Current.Id != lastId) UpdateTrack();
            UpdateTimeline();
        }
        static void UpdateTimeline()
        {
            if (s == null) return;
            lastTimeline = DateTime.Now;
            var p = Player.I;
            if (p.Current == null) return;
            var len = Math.Max(0, p.Length);
            var tl = new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromSeconds(len),
                MaxSeekTime = TimeSpan.FromSeconds(len),
                Position = TimeSpan.FromSeconds(Math.Min(len, Math.Max(0, p.Position)))
            };
            s.UpdateTimelineProperties(tl);
        }
        static void UpdateTrack()
        {
            if (s == null) return;
            var t = Player.I.Current;
            var u = s.DisplayUpdater;
            if (t == null) { lastId = null; u.ClearAll(); u.Update(); s.PlaybackStatus = MediaPlaybackStatus.Closed; return; }
            lastId = t.Id;
            u.Type = MediaPlaybackType.Music;
            u.AppMediaId = "SONORA";
            u.MusicProperties.Title = t.Title ?? "";
            u.MusicProperties.Artist = t.Artist ?? "";
            u.MusicProperties.AlbumTitle = t.Album ?? "";
            u.Thumbnail = null;
            u.Update();
            UpdateState();
            SetThumb(t);
        }
        static async void SetThumb(Track t)
        {
            try
            {
                if (string.IsNullOrEmpty(t.Cover)) return;
                RandomAccessStreamReference r;
                if (t.Cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)) r = RandomAccessStreamReference.CreateFromUri(new Uri(t.Cover));
                else
                {
                    var data = t.Cover;
                    int comma = data.IndexOf(',');
                    if (data.StartsWith("data:") && comma > 0) data = data.Substring(comma + 1);
                    var bytes = Convert.FromBase64String(data);
                    var ms = new InMemoryRandomAccessStream();
                    var w = new DataWriter(ms);
                    w.WriteBytes(bytes);
                    await w.StoreAsync();
                    w.DetachStream();
                    ms.Seek(0);
                    r = RandomAccessStreamReference.CreateFromStream(ms);
                }
                if (Player.I.Current?.Id != t.Id) return;
                s.DisplayUpdater.Thumbnail = r;
                s.DisplayUpdater.Update();
            }
            catch (Exception ex) { Log.W("smtc thumb: " + ex.Message); }
        }
    }

    // Кнопки «назад / пауза / вперёд» в превью окна на панели задач
    public static class Taskbar
    {
        static ThumbButtonInfo prev, play, next;
        static Window win;
        public static void Init(Window w)
        {
            win = w;
            var info = new TaskbarItemInfo();
            prev = Btn("prev", "Предыдущий", () => Player.I.Prev());
            play = Btn("play", "Играть", () => Player.I.Toggle());
            next = Btn("next", "Следующий", () => Player.I.Next());
            info.ThumbButtonInfos.Add(prev);
            info.ThumbButtonInfos.Add(play);
            info.ThumbButtonInfos.Add(next);
            w.TaskbarItemInfo = info;
            Player.I.StateChanged += Update;
            Player.I.TrackChanged += Update;
            Update();
        }
        static ThumbButtonInfo Btn(string icon, string tip, Action a)
        {
            var b = new ThumbButtonInfo { ImageSource = Icon(icon), Description = L.T(tip), DismissWhenClicked = false };
            b.Click += (s, e) => a();
            return b;
        }
        static ImageSource Icon(string name)
        {
            Ico ic;
            if (!Icons.All.TryGetValue(name, out ic)) ic = Icons.All["music"];
            var geo = U.Geo(ic.D);
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            dg.Children.Add(ic.Fill ? new GeometryDrawing(Brushes.White, null, geo) : new GeometryDrawing(null, new Pen(Brushes.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geo));
            var img = new DrawingImage(dg);
            img.Freeze();
            return img;
        }
        static void Update()
        {
            var p = Player.I;
            bool has = p.Current != null;
            prev.IsEnabled = next.IsEnabled = play.IsEnabled = has;
            play.ImageSource = Icon(p.IsPlaying ? "pause" : "play");
            play.Description = L.T(p.IsPlaying ? "Пауза" : "Играть");
            var t = p.Current;
            win.Title = t != null ? (t.Artist + " — " + t.Title + " · SONORA") : "SONORA";
            if (win.TaskbarItemInfo != null) win.TaskbarItemInfo.Description = t != null ? t.Artist + " — " + t.Title : "SONORA";
        }
    }
}
