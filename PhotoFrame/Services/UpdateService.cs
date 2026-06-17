// Services/UpdateService.cs — v1.0 (build 45 / v1.2.0.2)
//
// GitHub Releases update checker, inspired by NetFix (rupleide/NetFix).
// Checks https://api.github.com/repos/ArtemITuser/PhotoFrame/releases/latest
// Parses tag_name + assets, compares with Assembly version,
// downloads .exe installer to %TEMP%, launches it, exits app.
//
// ClickOnce note:
//   ClickOnce has its own update mechanism (ApplicationDeployment.CurrentDeployment.Update()).
//   This service is for STANDALONE / Inno Setup installs only.
//   IsClickOnceDeployed() detects ClickOnce and skips the GitHub check.

using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PhotoFrame.Services
{
    public record UpdateInfo(Version Version, string TagName, string DownloadUrl);

    public static class UpdateService
    {
        private const string Owner  = "ArtemITuser";
        private const string Repo   = "PhotoFrame";
        private static readonly string ApiUrl =
            $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

        // ── Check ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Check GitHub releases for a newer version.
        /// Returns null when already up to date, on network error, or ClickOnce.
        /// </summary>
        public static async Task<UpdateInfo?> CheckAsync(
            CancellationToken ct = default)
        {
            // Skip for ClickOnce — handled by ClickOnce update mechanism
            if (IsClickOnceDeployed()) return null;

            var current = Assembly.GetExecutingAssembly().GetName().Version
                       ?? new Version(1, 0, 0, 0);
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(12);
                http.DefaultRequestHeaders.Add("User-Agent", $"PhotoFrame/{current}");

                string json = await http.GetStringAsync(ApiUrl, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Parse tag: "v1.2.0.2" → "1.2.0.2"
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string numStr = tag.TrimStart('v', 'V').Trim();
                if (!Version.TryParse(numStr, out var latest)) return null;
                if (latest <= current) return null;

                // Find .exe asset
                string? url = null;
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string? name    = asset.GetProperty("name").GetString();
                        string? dlUrl   = asset.GetProperty("browser_download_url").GetString();
                        if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            && dlUrl != null)
                        { url = dlUrl; break; }
                    }
                }

                if (url == null) return null; // release without installer
                return new UpdateInfo(latest, tag, url);
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        // ── Download + Install ────────────────────────────────────────────────

        /// <summary>
        /// Download installer to %TEMP%, launch it, shutdown the app.
        /// Reports progress 0.0–1.0 via <paramref name="progress"/>.
        /// </summary>
        public static async Task DownloadAndInstallAsync(
            UpdateInfo info,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            string dest = Path.Combine(Path.GetTempPath(),
                $"PhotoFrame_Setup_{info.TagName}.exe");

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            http.DefaultRequestHeaders.Add("User-Agent", "PhotoFrame-Updater");

            using var resp = await http.GetAsync(info.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long? total = resp.Content.Headers.ContentLength;
            using var src  = await resp.Content.ReadAsStreamAsync(ct);
            using var dst  = File.Create(dest);

            var buf = new byte[81920];
            long read = 0;
            int bytes;
            while ((bytes = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, bytes), ct);
                read += bytes;
                if (total > 0)
                    progress?.Report((double)read / total.Value);
            }
            progress?.Report(1.0);

            // Launch installer silently (Inno Setup: /SILENT)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest)
            {
                UseShellExecute = true,
                Arguments       = "/SILENT"
            });
            Application.Current.Dispatcher.Invoke(() =>
                Application.Current.Shutdown());
        }

        // ── ClickOnce update ──────────────────────────────────────────────────

        public static bool IsClickOnceDeployed() =>
            Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>
        /// For ClickOnce installs: trigger the ClickOnce update check.
        /// Returns true if an update was applied (app should restart).
        /// </summary>
        public static bool TryClickOnceUpdate()
        {
            try
            {
                // Reflection to avoid hard dependency on System.Deployment
                var appType = Type.GetType(
                    "System.Deployment.Application.ApplicationDeployment, " +
                    "System.Deployment, Version=4.0.0.0, Culture=neutral");
                if (appType == null) return false;

                var current = appType.GetProperty("CurrentDeployment")?.GetValue(null);
                if (current == null) return false;

                var checkResult = appType.GetMethod("CheckForDetailedUpdate",
                    Array.Empty<Type>())?.Invoke(current, null);

                if (checkResult == null) return false;
                bool updateAvailable = (bool)(checkResult.GetType()
                    .GetProperty("UpdateAvailable")?.GetValue(checkResult) ?? false);

                if (!updateAvailable) return false;

                appType.GetMethod("Update", Array.Empty<Type>())?.Invoke(current, null);
                return true;
            }
            catch { return false; }
        }
    }
}
