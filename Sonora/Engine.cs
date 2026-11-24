using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Sonora
{
    public sealed class FailArgs : EventArgs
    {
        public Exception ErrorException { get; private set; }
        public FailArgs(Exception e) { ErrorException = e; }
    }

    // Воспроизведение: сначала встроенный плеер Windows (WPF MediaPlayer).
    // Если в Windows нет компонентов Windows Media (редакции N, «урезанные» сборки,
    // отключённый Windows Media Player) — MediaPlayer.Open падает с NullReferenceException.
    // Тогда переключаемся на резервный движок NAudio: Media Foundation / ACM + WinMM.
    public sealed class Engine
    {
        static readonly string FlagFile = Path.Combine(Paths.Data, "audio-engine.txt");
        public static bool Fallback;          // true — играем через NAudio
        public static string Why;              // почему переключились
        public static bool Forced;             // выбрано вручную в настройках

        static Engine()
        {
            try
            {
                if (File.Exists(FlagFile))
                {
                    var s = File.ReadAllText(FlagFile).Trim();
                    Fallback = s.StartsWith("naudio");
                    Forced = s.EndsWith("manual");
                    if (Fallback) Why = Forced ? "выбран в настройках" : "встроенный плеер Windows недоступен";
                }
            }
            catch { }
        }
        public static void SetFallback(bool on, bool manual, string why)
        {
            Fallback = on; Forced = manual; Why = why;
            try { Directory.CreateDirectory(Paths.Data); File.WriteAllText(FlagFile, (on ? "naudio" : "wpf") + (manual ? " manual" : "")); } catch { }
            Log.W("audio engine → " + (on ? "NAudio" : "Windows Media") + (why != null ? " (" + why + ")" : ""));
        }
        public static string Name { get { return Fallback ? "резервный (NAudio)" : "Windows Media"; } }

        // самопроверка для CI: декодеры и вывод звука
        public static async System.Threading.Tasks.Task SelfTest(string dir)
        {
            var sb = new System.Text.StringBuilder();
            var wav = Path.Combine(Path.GetTempPath(), "sonora-selftest.wav");
            using (var w = new WaveFileWriter(wav, new WaveFormat(44100, 16, 2)))
            {
                var buf = new byte[44100 * 4];
                for (int i = 0; i < 44100; i++) { short v = (short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 8000); buf[i * 4] = (byte)v; buf[i * 4 + 1] = (byte)(v >> 8); buf[i * 4 + 2] = (byte)v; buf[i * 4 + 3] = (byte)(v >> 8); }
                w.Write(buf, 0, buf.Length);
            }
            Action<string, Action> t = (name, a) => { try { a(); sb.AppendLine("OK   " + name); } catch (Exception ex) { sb.AppendLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message); } };
            t("WaveFileReader", () => { using (var r = new WaveFileReader(wav)) if (r.TotalTime.TotalSeconds < .9) throw new Exception("len " + r.TotalTime); });
            t("MediaFoundationReader", () => { using (var r = new MediaFoundationReader(wav)) { var b = new byte[8192]; if (r.Read(b, 0, b.Length) <= 0) throw new Exception("no data"); } });
            t("Mp3 ACM decompressor type", () => { var x = typeof(AcmMp3FrameDecompressor).FullName; });
            t("WaveOutEvent.Init", () => { using (var o = new WaveOutEvent()) using (var r = new WaveFileReader(wav)) o.Init(r); });
            var e = new Engine();
            string got = null;
            e.MediaOpened += (s, a) => got = got ?? "opened";
            e.MediaFailed += (s, a) => got = got ?? "failed: " + a.ErrorException?.Message;
            var was = Fallback;
            Fallback = true;
            try { e.Open(new Uri(wav)); } catch (Exception ex) { got = "throw: " + ex.Message; }
            for (int i = 0; i < 20 && got == null; i++) await System.Threading.Tasks.Task.Delay(100);
            sb.AppendLine("Engine(NAudio) open: " + (got ?? "timeout") + " · duration " + (e.NaturalDuration.HasTimeSpan ? e.NaturalDuration.TimeSpan.TotalSeconds.ToString("0.00") : "?"));
            e.Close();
            Fallback = false; got = null;
            var e2 = new Engine();
            e2.MediaOpened += (s, a) => got = got ?? "opened";
            e2.MediaFailed += (s, a) => got = got ?? "failed: " + a.ErrorException?.Message;
            try { e2.Open(new Uri(wav)); } catch (Exception ex) { got = "throw: " + ex.Message; }
            for (int i = 0; i < 30 && got == null; i++) await System.Threading.Tasks.Task.Delay(100);
            sb.AppendLine("Engine(WPF) open: " + (got ?? "timeout") + " · fallback now " + Fallback);
            e2.Close();
            Fallback = was;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "audio-selftest.txt"), sb.ToString());
        }

        public event EventHandler MediaOpened, MediaEnded;
        public event EventHandler<FailArgs> MediaFailed;

        readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
        MediaPlayer wpf;
        bool wpfHooked;
        Uri src;
        bool triedNa;

        WaveStream reader;
        VolumeSampleProvider vol;
        WaveOutEvent wout;
        bool manualStop;
        int session;
        double volume = .8;

        MediaPlayer Wpf()
        {
            if (wpf == null) wpf = new MediaPlayer();
            if (!wpfHooked)
            {
                wpfHooked = true;
                wpf.Volume = volume;
                wpf.MediaOpened += (s, e) => { if (!Fallback && !UsingNa) MediaOpened?.Invoke(this, EventArgs.Empty); };
                wpf.MediaEnded += (s, e) => { if (!UsingNa) MediaEnded?.Invoke(this, EventArgs.Empty); };
                wpf.MediaFailed += (s, e) =>
                {
                    if (UsingNa) return;
                    // локальный файл не открылся во встроенном плеере — пробуем резервный движок
                    if (src != null && src.IsFile && !triedNa)
                    {
                        triedNa = true;
                        Log.W("wpf media failed (" + e.ErrorException?.Message + ") → NAudio");
                        OpenNa(src, true); // при неудаче сам сообщит об ошибке
                        return;
                    }
                    MediaFailed?.Invoke(this, new FailArgs(e.ErrorException));
                };
            }
            return wpf;
        }
        bool UsingNa { get { return reader != null; } }

        public double Volume
        {
            get { return volume; }
            set
            {
                volume = Math.Max(0, Math.Min(1, value));
                if (wpf != null) try { wpf.Volume = volume; } catch { }
                if (vol != null) vol.Volume = (float)volume;
            }
        }

        public void Open(Uri u)
        {
            CloseNa();
            src = u; triedNa = false;
            if (!Fallback)
            {
                try { Wpf().Open(u); return; }
                catch (NullReferenceException ex)
                {
                    SetFallback(true, false, "встроенный плеер Windows недоступен");
                    Log.W("wpf MediaPlayer.Open: " + ex.Message + " → NAudio");
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is InvalidOperationException || ex is DllNotFoundException)
                {
                    SetFallback(true, false, "встроенный плеер Windows недоступен: " + ex.Message);
                }
            }
            OpenNa(u, false);
        }

        bool OpenNa(Uri u, bool fromWpfFail)
        {
            CloseNa();
            int my = ++session;
            try
            {
                string path = u.IsFile ? u.LocalPath : u.AbsoluteUri;
                WaveStream r = null;
                Exception first = null;
                try { r = new MediaFoundationReader(path); }
                catch (Exception ex) { first = ex; }
                if (r == null && u.IsFile)
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    try
                    {
                        if (ext == ".wav") r = new WaveFileReader(path);
                        else r = new Mp3FileReaderBase(path, wf => new AcmMp3FrameDecompressor(wf));
                    }
                    catch (Exception ex) { Log.W("naudio reader: " + ex.Message); }
                }
                if (r == null) throw new Exception("не удалось открыть звук" + (first != null ? " (" + first.Message + ")" : ""));
                reader = r;
                vol = new VolumeSampleProvider(r.ToSampleProvider()) { Volume = (float)volume };
                wout = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 3 };
                wout.Init(vol);
                var w = wout;
                wout.PlaybackStopped += (s, e) =>
                {
                    ui.BeginInvoke(new Action(() =>
                    {
                        if (my != session || w != wout) return;
                        if (e.Exception != null) { Log.W("naudio playback: " + e.Exception.Message); MediaFailed?.Invoke(this, new FailArgs(e.Exception)); return; }
                        if (manualStop) { manualStop = false; return; }
                        MediaEnded?.Invoke(this, EventArgs.Empty);
                    }));
                };
                if (fromWpfFail && !Fallback) Log.W("naudio used for one file");
                ui.BeginInvoke(new Action(() => { if (my == session) MediaOpened?.Invoke(this, EventArgs.Empty); }));
                if (playWanted) { manualStop = false; wout.Play(); }
                return true;
            }
            catch (Exception ex)
            {
                Log.W("naudio open: " + ex);
                CloseNa();
                var err = ex; int tok = session;
                ui.BeginInvoke(new Action(() => { if (tok == session) MediaFailed?.Invoke(this, new FailArgs(err)); }));
                return false;
            }
        }

        bool playWanted;
        public void Play()
        {
            playWanted = true;
            if (UsingNa) { try { manualStop = false; wout.Play(); } catch (Exception ex) { Log.W("naudio play: " + ex.Message); } return; }
            if (!Fallback && wpf != null) wpf.Play();
        }
        public void Pause()
        {
            playWanted = false;
            if (UsingNa) { try { wout.Pause(); } catch { } return; }
            if (wpf != null) wpf.Pause();
        }
        public void Stop()
        {
            playWanted = false;
            if (UsingNa) { try { manualStop = true; wout.Stop(); } catch { } return; }
            if (wpf != null) wpf.Stop();
        }
        public void Close()
        {
            playWanted = false;
            CloseNa();
            if (wpf != null) try { wpf.Close(); } catch { }
            src = null;
        }
        void CloseNa()
        {
            session++;
            var w = wout; var r = reader;
            wout = null; reader = null; vol = null;
            if (w != null) { try { manualStop = true; w.Stop(); } catch { } try { w.Dispose(); } catch { } }
            if (r != null) try { r.Dispose(); } catch { }
        }

        public TimeSpan Position
        {
            get
            {
                if (UsingNa) { try { return reader.CurrentTime; } catch { return TimeSpan.Zero; } }
                return wpf != null ? wpf.Position : TimeSpan.Zero;
            }
            set
            {
                if (UsingNa)
                {
                    try
                    {
                        var t = value < TimeSpan.Zero ? TimeSpan.Zero : value;
                        if (t > reader.TotalTime) t = reader.TotalTime;
                        reader.CurrentTime = t;
                        // после конца трека WaveOut остановлен — запускаем снова, если нужно
                        if (playWanted && wout.PlaybackState == PlaybackState.Stopped) { manualStop = false; wout.Play(); }
                    }
                    catch (Exception ex) { Log.W("naudio seek: " + ex.Message); }
                    return;
                }
                if (wpf != null) wpf.Position = value;
            }
        }
        public Duration NaturalDuration
        {
            get
            {
                if (UsingNa) { try { return new Duration(reader.TotalTime); } catch { return Duration.Automatic; } }
                return wpf != null ? wpf.NaturalDuration : Duration.Automatic;
            }
        }
    }
}
