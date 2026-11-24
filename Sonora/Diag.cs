using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sonora
{
    // ---------- Диагностика: проверка соединения и отчёт об ошибках ----------
    public static class Diag
    {
        public sealed class Check { public string Name, Host, Result; public bool Ok; public long Ms; }
        public static List<Check> Last = new List<Check>();
        public static string LastError;

        static readonly HttpClient Http = Create();
        static HttpClient Create()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SONORA/" + Program.Version + " (Windows)");
            return c;
        }

        static readonly Tuple<string, string>[] Targets =
        {
            Tuple.Create("Каталог и треки SONORA", Catalog.Db + "/audio/__ping__.json"),
            Tuple.Create("Аккаунты SONORA", Auth.Db + "/__ping__.json"),
            Tuple.Create("Вход (Google)", "https://identitytoolkit.googleapis.com/"),
            Tuple.Create("Deezer", "https://api.deezer.com/search?q=queen&limit=1"),
            Tuple.Create("iTunes", "https://itunes.apple.com/search?term=queen&limit=1&media=music"),
            Tuple.Create("Яндекс Музыка", "https://api.music.yandex.net/genres"),
        };

        public static async Task<List<Check>> Run()
        {
            var tasks = Targets.Select(async t =>
            {
                var c = new Check { Name = t.Item1, Host = new Uri(t.Item2).Host };
                var sw = Stopwatch.StartNew();
                try
                {
                    using (var r = await Http.GetAsync(t.Item2, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        int code = (int)r.StatusCode;
                        // любой HTTP-ответ значит, что сервер доступен; 5xx — проблемы у сервиса
                        c.Ok = code < 500;
                        c.Result = "HTTP " + code;
                    }
                }
                catch (TaskCanceledException) { c.Result = "нет ответа (таймаут)"; }
                catch (Exception ex) { c.Result = Short(ex); }
                c.Ms = sw.ElapsedMilliseconds;
                return c;
            }).ToList();
            var res = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
            Last = res;
            Log.W("diag: " + string.Join(" | ", res.Select(c => c.Host + " " + (c.Ok ? "OK" : "FAIL") + " " + c.Result + " " + c.Ms + "ms")));
            return res;
        }

        // самая глубокая причина без стека
        public static string Short(Exception ex)
        {
            while (ex.InnerException != null && !(ex is ExtException)) ex = ex.InnerException;
            var m = ex.Message ?? ex.GetType().Name;
            return m.Length > 140 ? m.Substring(0, 140) + "…" : m;
        }

        // понятный текст для тоста, когда трек не загрузился
        public static string Explain(Exception ex, Track t)
        {
            if (ex is ExtException) return ex.Message;
            var all = ex.ToString();
            string where = t != null && t.Source != null ? "музыкального сервиса" : "сервера SONORA";
            if (ex is TaskCanceledException || all.Contains("TaskCanceled") || all.Contains("timed out"))
                return "Сервер " + (t != null && t.Source != null ? "сервиса" : "SONORA") + " не ответил вовремя — возможно, его блокирует провайдер. Попробуй VPN или другую сеть";
            if (all.Contains("HttpRequestException") || all.Contains("WebException") || all.Contains("SocketException") || all.Contains("имя") || all.Contains("remote name"))
                return "Нет связи с " + where + " — проверь интернет, VPN или антивирус. Настройки → Диагностика покажет, что недоступно";
            var s = Short(ex);
            if (s.StartsWith("HTTP ")) return "Сервер ответил ошибкой (" + s + ") — попробуй позже";
            return "Не удалось загрузить трек: " + s;
        }

        public static string LogPath { get { return Path.Combine(Paths.Data, "sonora.log"); } }

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("SONORA " + Program.Version + " · " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " x64" : " x86") + " · .NET " + Environment.Version);
            sb.AppendLine("Время: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · Вход: " + (Auth.Current != null ? "да" : "гость") + " · Каталог: " + Catalog.Remote.Count + " треков");
            sb.AppendLine("Аудио: " + Engine.Name + (Engine.Why != null ? " · " + Engine.Why : ""));
            if (!string.IsNullOrEmpty(LastError)) sb.AppendLine("Последняя ошибка: " + LastError);
            if (Last.Count > 0)
            {
                sb.AppendLine("Соединение:");
                foreach (var c in Last) sb.AppendLine("  " + (c.Ok ? "✓ " : "✗ ") + c.Name + " (" + c.Host + "): " + c.Result + ", " + c.Ms + " мс");
            }
            sb.AppendLine("Журнал (последние строки):");
            try
            {
                if (File.Exists(LogPath))
                {
                    string[] lines;
                    using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var rd = new StreamReader(fs)) lines = rd.ReadToEnd().Split('\n');
                    foreach (var l in lines.Where(x => x.Trim().Length > 0).Skip(Math.Max(0, lines.Length - 80)))
                    {
                        var line = l.TrimEnd('\r');
                        // не отдаём токены/ключи
                        line = System.Text.RegularExpressions.Regex.Replace(line, @"(token|key|auth|code)=([^&\s]+)", "$1=***", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        sb.AppendLine("  " + (line.Length > 400 ? line.Substring(0, 400) + "…" : line));
                    }
                }
                else sb.AppendLine("  (пусто)");
            }
            catch (Exception ex) { sb.AppendLine("  не удалось прочитать: " + ex.Message); }
            return sb.ToString();
        }

        public static void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(Paths.Data);
                if (File.Exists(LogPath)) Process.Start("explorer.exe", "/select,\"" + LogPath + "\"");
                else Process.Start("explorer.exe", "\"" + Paths.Data + "\"");
            }
            catch (Exception ex) { Log.W("open log: " + ex.Message); }
        }
    }
}
