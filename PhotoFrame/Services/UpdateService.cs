// Services/UpdateService.cs — v3.3
// Полноценный контур централизованного обновления Free-версии:
//   1) Проверка latest-релиза через публичный GitHub API (без токена).
//   2) ПРЯМОЕ скачивание установщика с GitHub Release (confirmed:
//      https://github.com/ArtemITuser/PhotoFrame/releases/download/<tag>/<asset>
//      отдаёт 302 → Azure Blob → 200, работает без авторизации).
//   3) SHA-256-чексумма из ассета <asset>.sha256 (если выложен CI) — verify before run.
//   4) Запуск установщика; при необходимости — UAC (Verb = "runas").
//      InnoSetup сам покажет собственный UAC-диалог для per-machine установки,
//      поэтому сначала пробуем обычный запуск ("open"), и только если Windows
//      отказала (ERROR_CANCELLED / elevation-required) — поднимаем через runas.
//
// ВАЖНО: этот контур НЕ связан с лицензированием Pro. Права на функцию
// «автообновление из приложения» в будущем будут проверяться отдельным
// гейтом (LicenseFeature.AutoUpdate), здесь их нет намеренно.

using System;
using System.Reflection;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoFrame.Services
{
    public sealed class UpdateInfo
    {
        public string Tag            { get; init; } = "";
        public string? ReleaseNotes  { get; init; }
        public string  AssetUrl      { get; init; } = "";
        public string  AssetName     { get; init; } = "";
        public long    AssetSizeBytes{ get; init; }
        public string? Sha256AssetUrl{ get; init; }
        public Version ParsedVersion { get; init; } = new(0, 0);
    }

    public sealed class UpdateCheckResult
    {
        public bool   Ok           { get; init; }
        public bool   IsNewer      { get; init; }
        public string? Error       { get; init; }
        public UpdateInfo? Update  { get; init; }
        public static UpdateCheckResult Fail(string err) => new() { Ok = false, Error = err };
    }

    public static class UpdateService
    {
        public const string RepoApi   = "https://api.github.com/repos/ArtemITuser/PhotoFrame";
        public const string RepoHtml  = "https://github.com/ArtemITuser/PhotoFrame";
        public const string ReleasesUrl = RepoHtml + "/releases";

        /// <summary>Зеркало/альтернативный API (например прокси) — пусто = напрямую.</summary>
        public static string ApiOverride { get; set; } = "";

        // ─── 1. Проверка ──────────────────────────────────────────────────────────

        public static async Task<UpdateCheckResult> CheckAsync(
            Version current, CancellationToken ct = default)
        {
            try
            {
                using var http = MakeClient();
                var api = string.IsNullOrEmpty(ApiOverride) ? RepoApi : ApiOverride;
                using var resp = await http.GetAsync(api + "/releases/latest", ct);
                if (!resp.IsSuccessStatusCode)
                    return UpdateCheckResult.Fail($"GitHub API: {(int)resp.StatusCode}");

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;

                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                var parsed = ParseVersion(tag);
                if (parsed <= current)
                    return new UpdateCheckResult { Ok = true, IsNewer = false };

                // Выбор ассета под текущую ОС/архитектуру
                JsonElement? best = null; long bestSize = 0;
                string wantArch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                if (root.TryGetProperty("assets", out var assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.Contains("_x64", StringComparison.OrdinalIgnoreCase) !=
                            (wantArch == "x64")) continue;
                        best = a;
                        bestSize = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        break;
                    }
                }
                if (best == null)
                    return new UpdateCheckResult
                    {
                        Ok = true, IsNewer = true,
                        Error = "Для вашей архитектуры нет готового установщика.",
                        Update = new UpdateInfo { Tag = tag, ParsedVersion = parsed }
                    };

                string browserUrl = best.Value.GetProperty("browser_download_url").GetString() ?? "";
                string assetName  = best.Value.GetProperty("name").GetString() ?? "";

                // Ищем sidecar .sha256 (публикуется нашим workflow)
                string? shaUrl = null;
                if (root.TryGetProperty("assets", out var assets2))
                    foreach (var a in assets2.EnumerateArray())
                    {
                        string name = a.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : "";
                        if (name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase))
                        { shaUrl = a.GetProperty("browser_download_url").GetString(); break; }
                    }

                string? notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

                return new UpdateCheckResult
                {
                    Ok = true, IsNewer = true,
                    Update = new UpdateInfo
                    {
                        Tag             = tag,
                        ReleaseNotes    = notes,
                        AssetUrl        = browserUrl,
                        AssetName       = assetName,
                        AssetSizeBytes  = bestSize,
                        Sha256AssetUrl  = shaUrl,
                        ParsedVersion   = parsed,
                    }
                };
            }
            catch (TaskCanceledException) { return UpdateCheckResult.Fail("Таймаут сети (10 c)."); }
            catch (Exception ex)          { return UpdateCheckResult.Fail(ex.Message); }
        }

        // ─── 2. Прямое скачивание ─────────────────────────────────────────────────

        /// <summary>
        /// Скачивает установщик во временную папку. Возвращает путь к файлу.
        /// progress — доля 0..1 (или -1, если размер неизвестен).
        /// </summary>
        public static async Task<string> DownloadAsync(
            UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "PhotoFrameUpdate");
            Directory.CreateDirectory(tmp);
            string target = Path.Combine(tmp, info.AssetName);
            if (File.Exists(target)) File.Delete(target);

            using var http = MakeClient(TimeSpan.FromMinutes(10));
            using var resp = await http.GetAsync(info.AssetUrl,
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long? len = resp.Content.Headers.ContentLength;
            await using var src  = await resp.Content.ReadAsStreamAsync(ct);
            await using var dstF = new FileStream(target, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buf = new byte[81920];
            long got = 0; int read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await dstF.WriteAsync(buf.AsMemory(0, read), ct);
                got += read;
                progress?.Report(len is > 0 ? (double)got / len.Value : -1);
            }

            // 3. Чексумма (если CI выложил sidecar-файл)
            if (!string.IsNullOrEmpty(info.Sha256AssetUrl))
            {
                string expected = await GetExpectedSha256Async(info.Sha256AssetUrl, ct);
                if (!string.IsNullOrEmpty(expected))
                {
                    string actual = await Sha256OfFileAsync(target, ct);
                    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(target);
                        throw new InvalidDataException(
                            "SHA-256 не совпал — файл обновлений повреждён или подменён.");
                    }
                }
            }
            return target;
        }

        private static async Task<string> GetExpectedSha256Async(string url, CancellationToken ct)
        {
            try
            {
                using var http = MakeClient();
                string txt = (await http.GetStringAsync(url, ct)).Trim();
                // формат: "<hex>[  filename]"
                int sp = txt.IndexOfAny(new[] { ' ', '\t' });
                return sp > 0 ? txt[..sp] : txt;
            }
            catch { return ""; }
        }

        private static async Task<string> Sha256OfFileAsync(string path, CancellationToken ct)
        {
            await using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        // ─── 4. Установка (UAC по необходимости) ─────────────────────────────────

        /// <summary>
        /// Запускает скачанный установщик. Возвращает true, если процесс стартовал.
        /// Strategy: обычный запуск (InnoSetup сам запросит UAC для per-machine);
        /// если ShellExec отказал из-за требования повышения — повтор с runas.
        /// </summary>
        public static bool Install(string installerPath)
        {
            try
            {
                var psi = new ProcessStartInfo(installerPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(installerPath)!
                };
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223 /*ERROR_CANCELLED*/
                                         || ex.NativeErrorCode == 740 /*ERROR_ELEVATION_REQUIRED*/)
            {
                try
                {
                    var psi = new ProcessStartInfo(installerPath)
                    {
                        UseShellExecute = true,
                        Verb            = "runas",   // явный запрос UAC
                    };
                    Process.Start(psi);
                    return true;
                }
                catch { return false; }
            }
            catch { return false; }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static HttpClient MakeClient(TimeSpan? timeout = null)
        {
            var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "PhotoFrame-Updater");
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return http;
        }

        /// <summary>"v1.2.3.0" / "1.2.3" / "1.2.3-rc1" → Version (4 части).</summary>
        public static Version ParseVersion(string tag)
        {
            try
            {
                string s = tag.TrimStart('v', 'V').Trim();
                int dash = s.IndexOfAny(new[] { '-', '+' });
                if (dash > 0) s = s[..dash];
                var parts = s.Split('.');
                int[] nums = new int[4];
                for (int i = 0; i < Math.Min(4, parts.Length); i++)
                    int.TryParse(parts[i], out nums[i]);
                return new Version(nums[0], nums[1], nums[2], nums[3]);
            }
            catch { return new Version(0, 0); }
        }

        /// <summary>
        /// v1.2.3.1: версия текущего запуска с учётом build-номера CI.
        /// При сборке через workflow InformationalVersion содержит "+build.&lt;N&gt;",
        /// и если тег релиза == версии сборки (например v1.2.3.0), Revision заменяется
        /// на build-номер — иначе приложение «не видит» свежий CI-билд как обновление.
        /// </summary>
        public static Version GetCurrentVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version ?? new Version(0, 0);
            try
            {
                var info = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(info))
                {
                    int plus = info!.IndexOf('+');
                    if (plus >= 0)
                    {
                        string meta = info.Substring(plus + 1); // "build.63" или git-sha
                        if (meta.StartsWith("build.", StringComparison.Ordinal) &&
                            int.TryParse(meta.Substring(6), out int b) && b > 0)
                            v = new Version(v.Major, v.Minor, v.Build, b);
                    }
                }
            }
            catch { }
            return v;
        }

        public static void OpenReleasesPage()
        {
            try { Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true }); }
            catch { }
        }
    }
}
