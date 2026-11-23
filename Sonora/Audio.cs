using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;

namespace Sonora
{
    // Декодирование через Windows Media Foundation (NAudio) — только для анализа, играет по-прежнему MediaPlayer
    public static class Audio
    {
        public sealed class ProbeResult { public double Duration; public double Loudness; }

        public static ProbeResult Probe(string file)
        {
            using (var reader = new MediaFoundationReader(file))
            {
                var res = new ProbeResult { Duration = reader.TotalTime.TotalSeconds };
                var sp = reader.ToSampleProvider();
                int sr = sp.WaveFormat.SampleRate, ch = sp.WaveFormat.Channels;
                var buf = new float[sr * ch / 5];
                double best = 0;
                foreach (var k in new[] { .08, .3, .52, .75, .9 })
                {
                    if (res.Duration > 4)
                    {
                        try { reader.CurrentTime = TimeSpan.FromSeconds(res.Duration * k); } catch { }
                    }
                    for (int part = 0; part < 10; part++)
                    {
                        int n = sp.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        double sum = 0;
                        for (int i = 0; i < n; i++) sum += buf[i] * buf[i];
                        best = Math.Max(best, Math.Sqrt(sum / n));
                    }
                }
                res.Loudness = best;
                return res;
            }
        }

        public static float[] Envelope(string file, double step)
        {
            using (var reader = new MediaFoundationReader(file))
            {
                var sp = reader.ToSampleProvider();
                int sr = sp.WaveFormat.SampleRate, ch = Math.Max(1, sp.WaveFormat.Channels);
                int win = Math.Max(64, (int)(sr * step)) * ch;
                var buf = new float[win];
                var full = new List<float>(8000);
                var low = new List<float>(8000);
                double lp = 0, a = 2 * Math.PI * 140 / sr;
                while (true)
                {
                    int n = sp.Read(buf, 0, win);
                    if (n <= 0) break;
                    double s1 = 0, s2 = 0; int frames = 0;
                    for (int i = 0; i + ch - 1 < n; i += ch)
                    {
                        double m = 0;
                        for (int c = 0; c < ch; c++) m += buf[i + c];
                        m /= ch;
                        lp += a * (m - lp);
                        s1 += m * m; s2 += lp * lp; frames++;
                    }
                    if (frames == 0) break;
                    full.Add((float)Math.Sqrt(s1 / frames));
                    low.Add((float)Math.Sqrt(s2 / frames));
                    if (full.Count > 20 * 60 * 30) break; // не больше 30 минут
                }
                int len = full.Count;
                if (len < 4) return null;
                var flux = new float[len];
                for (int i = 0; i < len; i++)
                {
                    double avg = 0; int c = 0;
                    for (int j = Math.Max(0, i - 8); j < i; j++) { avg += low[j]; c++; }
                    avg = c > 0 ? avg / c : low[i];
                    flux[i] = (float)Math.Max(0, low[i] - avg * 1.05);
                }
                double fRef = Pct(flux, .97), lRef = Pct(full.ToArray(), .97);
                var res = new float[len];
                for (int i = 0; i < len; i++)
                {
                    double f = fRef > 1e-6 ? Math.Min(1, flux[i] / fRef) : 0;
                    double l = lRef > 1e-6 ? Math.Min(1, full[i] / lRef) : 0;
                    res[i] = (float)Math.Max(0, Math.Min(1, .62 * f + .38 * Math.Pow(l, 2.2)));
                }
                return res;
            }
        }
        static double Pct(float[] v, double p)
        {
            var s = v.Where(x => x > 0).OrderBy(x => x).ToArray();
            if (s.Length == 0) return 0;
            return s[Math.Min(s.Length - 1, (int)(s.Length * p))];
        }
    }
}
