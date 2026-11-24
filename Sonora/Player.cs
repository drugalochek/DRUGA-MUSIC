using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sonora
{
    public sealed class Player
    {
        static Player instance;
        public static Player I { get { return instance ?? (instance = new Player()); } }

        readonly MediaPlayer mp = new MediaPlayer();
        readonly DispatcherTimer timer;
        readonly Random rnd = new Random();
        public List<Track> Queue = new List<Track>();
        public int Index = -1;
        public bool IsPlaying { get; private set; }
        public bool Loading { get; private set; }
        public bool Shuffle;
        public int Repeat; // 0 — выкл, 1 — весь список, 2 — один трек
        public DateTime? SleepAt;
        public event Action TrackChanged, StateChanged, Tick;
        int gen;
        bool opened, guestHint;
        string lastSrc;
        int retried;

        public Track Current { get { return Index >= 0 && Index < Queue.Count ? Queue[Index] : null; } }

        Player()
        {
            mp.Volume = Library.D.Volume;
            mp.MediaEnded += (s, e) => Next(true);
            mp.MediaFailed += async (s, e) =>
            {
                Log.W("media failed: " + e.ErrorException?.Message + " · " + (lastSrc ?? "").Split('?')[0]);
                var ct = Current; int my = gen; var src = lastSrc;
                // поток из сети не открылся — скачиваем файл сами и пробуем ещё раз
                if (ct != null && ct.Source != null && src != null && src.StartsWith("http") && retried != my)
                {
                    retried = my;
                    try
                    {
                        var f = await global::Sonora.Ext.Download(ct, src, "ya_");
                        if (my != gen) return;
                        lastSrc = f; mp.Open(new Uri(f)); opened = true; mp.Play();
                        return;
                    }
                    catch (Exception ex) { Log.W("media retry: " + ex.Message); if (my != gen) return; }
                }
                Loading = false; IsPlaying = false; opened = false;
                StateChanged?.Invoke();
                Diag.LastError = "media failed: " + (e.ErrorException?.Message ?? "?");
                Shell.Toast("Не удалось воспроизвести трек" + (e.ErrorException != null ? ": " + Diag.Short(e.ErrorException) : ""));
            };
            mp.MediaOpened += (s, e) =>
            {
                Loading = false;
                var t = Current;
                if (t != null && mp.NaturalDuration.HasTimeSpan)
                {
                    var sec = mp.NaturalDuration.TimeSpan.TotalSeconds;
                    if (t.Duration <= 0 || t.IsLocal) { t.Duration = sec; if (t.IsLocal) Library.SetLocalDuration(t.Id, sec); }
                }
                StateChanged?.Invoke();
            };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                if (SleepAt.HasValue && DateTime.Now >= SleepAt.Value)
                {
                    SleepAt = null;
                    Pause();
                    Shell.Toast("Таймер сна: воспроизведение остановлено");
                }
                if (Guard.Guest && IsPlaying && opened && Position >= Guard.GuestSeconds)
                {
                    mp.Pause(); IsPlaying = false;
                    StateChanged?.Invoke();
                    Shell.I?.GuestLimit();
                }
                Tick?.Invoke();
            };
            timer.Start();
        }

        public double Position { get { return opened ? mp.Position.TotalSeconds : 0; } }
        public double Length
        {
            get
            {
                if (opened && mp.NaturalDuration.HasTimeSpan) return mp.NaturalDuration.TimeSpan.TotalSeconds;
                return Current?.Duration ?? 0;
            }
        }
        public double Volume
        {
            get { return mp.Volume; }
            set { mp.Volume = Math.Max(0, Math.Min(1, value)); Library.D.Volume = mp.Volume; }
        }

        public void PlayList(List<Track> list, int idx)
        {
            if (list == null || list.Count == 0) return;
            Queue = list.ToList();
            Index = Math.Max(0, Math.Min(idx, Queue.Count - 1));
            Start();
        }
        public void Preview(List<Track> list, int idx)
        {
            Queue = list.ToList();
            Index = idx;
            TrackChanged?.Invoke();
            StateChanged?.Invoke();
        }
        public void JumpTo(int i)
        {
            if (i < 0 || i >= Queue.Count) return;
            Index = i;
            Start();
        }
        public void PlayNext(Track t)
        {
            if (Current == null) { PlayList(new List<Track> { t }, 0); return; }
            Queue.Insert(Index + 1, t);
            Shell.Toast("Трек будет следующим");
        }

        async void Start()
        {
            var t = Current;
            if (t == null) return;
            int my = ++gen;
            Loading = true; IsPlaying = true; opened = false;
            try { mp.Stop(); mp.Close(); } catch { }
            TrackChanged?.Invoke();
            StateChanged?.Invoke();
            try
            {
                var file = await Catalog.AudioFile(t);
                if (my != gen) return;
                if (string.IsNullOrEmpty(file)) throw new global::Sonora.ExtException("Не удалось получить ссылку на трек");
                lastSrc = file;
                mp.Open(new Uri(file));
                opened = true;
                mp.Play();
                if (!Guard.Guest) Library.AddRecent(t.Id);
                Beat.Analyze(t, file);
                if (Guard.Guest && !guestHint) { guestHint = true; Shell.Toast("Гостевой режим: по 30 секунд от каждого трека. Зарегистрируйся, чтобы слушать целиком"); }
            }
            catch (Exception ex)
            {
                if (my != gen) return;
                Log.W("load audio: " + ex);
                Loading = false; IsPlaying = false;
                StateChanged?.Invoke();
                Diag.LastError = (t.Artist + " — " + t.Title) + ": " + Diag.Short(ex);
                Shell.Toast(Diag.Explain(ex, t));
            }
        }

        public void Toggle()
        {
            if (Current == null) return;
            if (IsPlaying) { mp.Pause(); IsPlaying = false; }
            else
            {
                if (!opened) { Start(); return; }
                if (Guard.Guest && Position >= Guard.GuestSeconds - .3) { Shell.I?.GuestLimit(); return; }
                mp.Play(); IsPlaying = true;
            }
            StateChanged?.Invoke();
        }
        public void Pause()
        {
            if (!IsPlaying) return;
            mp.Pause(); IsPlaying = false;
            StateChanged?.Invoke();
        }
        public void Next(bool auto = false)
        {
            if (Queue.Count == 0) return;
            if (auto && Repeat == 2) { mp.Position = TimeSpan.Zero; mp.Play(); return; }
            if (Shuffle && Queue.Count > 1)
            {
                int n;
                do n = rnd.Next(Queue.Count); while (n == Index);
                Index = n;
            }
            else if (Index + 1 < Queue.Count) Index++;
            else if (Repeat == 1 || !auto) Index = 0;
            else
            {
                IsPlaying = false;
                mp.Pause();
                mp.Position = TimeSpan.Zero;
                StateChanged?.Invoke();
                return;
            }
            Start();
        }
        public void Prev()
        {
            if (Queue.Count == 0) return;
            if (Position > 3) { Seek(0); return; }
            Index = Index > 0 ? Index - 1 : Queue.Count - 1;
            Start();
        }
        public void Seek(double sec)
        {
            if (!opened) return;
            mp.Position = TimeSpan.FromSeconds(Math.Max(0, sec));
            Tick?.Invoke();
        }
        public void Stop()
        {
            gen++;
            try { mp.Stop(); mp.Close(); } catch { }
            opened = false;
            Queue = new List<Track>();
            Index = -1;
            IsPlaying = false; Loading = false;
            TrackChanged?.Invoke();
            StateChanged?.Invoke();
        }
        public void CycleRepeat() { Repeat = (Repeat + 1) % 3; StateChanged?.Invoke(); }
        public void ToggleShuffle() { Shuffle = !Shuffle; StateChanged?.Invoke(); }
    }
}
