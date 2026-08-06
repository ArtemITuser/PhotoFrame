// Services/SystemIntegration.Updates.cs — build 56 (создан в build 55)
//
// Полноценная кроссканальная система обновлений — продолжение partial
// class SystemIntegration (см. SystemIntegration.cs). Раньше CheckUpdateAsync
// только сравнивал номер версии по tag_name релиза (ручной строковый поиск
// по JSON, без учёта assets вообще) — этого хватало для показа "доступна
// новая версия", но НЕ хватало, чтобы её реально скачать и поставить.
//
// Поведение по каналам (по ТЗ):
//   • ClickOnce  — автообновление целиком на уровне самого ClickOnce
//     (Update settings в pubxml/CI). Программа НЕ скачивает и не
//     устанавливает ничего сама — максимум может попытаться "подтолкнуть"
//     проверку через устаревший System.Deployment.Application API (доступен
//     только под .NET Framework; на .NET 8 почти наверняка недоступен —
//     см. TryNudgeClickOnceUpdateCheck, безопасный no-op при отсутствии).
//   • InnoSetup  — скачивает подходящий инсталлятор (см. подбор по
//     архитектуре ниже), при необходимости распаковывает zip, затем ПРОСТО
//     ЗАПУСКАЕТ его (Process.Start) — дальше пользователь ведёт обычный
//     диалог InnoSetup сам, вручную. Программа не самообновляется "молча".
//   • MSIX       — аналогично InnoSetup: скачивает .msix/.msixbundle и
//     запускает его (это открывает системный App Installer — тот же принцип
//     "просто открыть скачанный файл", что и для InnoSetup).
//   • Portable/неизвестно (нет следов ни одного из трёх каналов — например,
//     самостоятельно распакованный self-contained .zip без установки) — нет
//     чистого "обновить" сценария; предлагается страница релизов вручную.
//
// Подбор ассета по архитектуре (по ТЗ):
//   • x86-система: годятся ТОЛЬКО имена с x86/x86-32/x32/anycpu/universal —
//     x64-сборка на x86 Windows физически не запустится.
//   • x64-система: сначала ищем x64-сборку, при её отсутствии допустимо
//     взять x86-сборку (x86-бинарники работают на x64 через WOW64).
//   • Если найденный ассет — .zip (см. пример из переписки:
//     GitHub-вложение user-attachments/files/.../PhotoFrame_..._x86_Setup.zip
//     — иногда установщик кладут как zip, а не голый exe), он сначала
//     распаковывается во временную папку, и уже там ищется .exe.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PhotoFrame.Services
{
    public enum InstallChannel { Unknown, Msix, ClickOnce, InnoSetup }

    /// <summary>Один ассет релиза GitHub — то, что реально нужно для
    /// скачивания и запуска (имя файла + прямая ссылка).</summary>
    public sealed record ReleaseAsset(string Name, string DownloadUrl);

    /// <summary>Результат проверки обновления — версия и уже отобранный,
    /// готовый к скачиванию ассет под текущую архитектуру/канал (если
    /// подходящий вообще нашёлся среди assets релиза).</summary>
    public sealed record UpdateInfo(string Tag, bool IsNewer, ReleaseAsset? Asset);

    public static partial class SystemIntegration
    {
        /// <summary>
        /// Определяет, через какой из трёх каналов запущено текущее
        /// приложение — от этого зависит, что вообще можно/нужно предложить
        /// пользователю при обновлении (см. заголовок файла).
        /// </summary>
        public static InstallChannel DetectInstallChannel()
        {
            if (IsRunningAsMsixPackage()) return InstallChannel.Msix;
            if (IsClickOnceDeployed) return InstallChannel.ClickOnce;
            if (IsInnoSetupInstalled()) return InstallChannel.InnoSetup;
            return InstallChannel.Unknown;
        }

        /// <summary>AppId из Installer.iss — используется InnoSetup как ключ
        /// записи в реестре деинсталляции (при совпадении AppId между
        /// версиями InnoSetup сам корректно обновляет поверх старой
        /// установки, а не ставит вторую параллельную копию).</summary>
        private const string InnoSetupAppId = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";

        public static bool IsInnoSetupInstalled()
        {
            try
            {
                // PrivilegesRequired=lowest в Installer.iss → установка
                // по-пользовательская, ключ деинсталляции в HKCU, не HKLM.
                using var k = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InnoSetupAppId}_is1");
                return k != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Полная проверка обновления: версия релиза + (если найдена более
        /// новая) уже отфильтрованный по архитектуре текущей ОС и
        /// расширению файла подходящий ассет. Использует System.Text.Json
        /// (см. build 55 — раньше CheckUpdateAsync искал только "tag_name"
        /// вручную по строке, совсем не читая assets).
        /// </summary>
        public static async Task<UpdateInfo> CheckUpdateWithAssetAsync(
            Version currentVersion, InstallChannel channel, string? customUrl = null)
        {
            string url = string.IsNullOrWhiteSpace(customUrl) ? DefaultUpdateUrl : customUrl;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "PhotoFrame-UpdateChecker");
                using var resp = await _sharedHttp.SendAsync(req, cts.Token);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                var root = doc.RootElement;

                string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(tag)) return new UpdateInfo("", false, null);

                bool isNewer = Version.TryParse(tag.TrimStart('v', 'V').Trim(), out var v)
                               && v > currentVersion;

                var assets = new List<ReleaseAsset>();
                if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in arr.EnumerateArray())
                    {
                        string? name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        string? dl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dl))
                            assets.Add(new ReleaseAsset(name, dl));
                    }
                }

                var chosen = isNewer ? SelectAssetForCurrentSystem(assets, channel) : null;
                return new UpdateInfo(tag, isNewer, chosen);
            }
            catch { return new UpdateInfo("", false, null); }
        }

        /// <summary>
        /// Выбирает лучший ассет под ТЕКУЩУЮ архитектуру ОС и канал
        /// установки. Правила ровно как указано: на x86 годится только
        /// x86/x86-32/x32/anycpu/universal; на x64 сперва ищем x64, при
        /// отсутствии — те же 32-битные варианты (работают через WOW64).
        /// Внутри each архитектурной группы предпочитается расширение,
        /// соответствующее каналу (.exe для InnoSetup, .msix/.msixbundle
        /// для MSIX), но .zip тоже принимается — распаковкой занимается
        /// DownloadAndLaunchUpdateAsync.
        /// </summary>
        public static ReleaseAsset? SelectAssetForCurrentSystem(
            IReadOnlyList<ReleaseAsset> assets, InstallChannel channel)
        {
            if (assets.Count == 0) return null;

            bool is64BitOs = Environment.Is64BitOperatingSystem;
            string[] x86Tokens = { "x86-32", "x86_32", "x86", "x32", "anycpu", "any-cpu", "universal" };
            string[] x64Tokens = { "x64", "amd64", "x86_64", "x86-64" };

            bool WantedExtension(string name) => channel switch
            {
                InstallChannel.Msix => name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)
                                     || name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
                                     || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                _                   => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                     || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
            };

            bool NameHasAnyToken(string name, string[] tokens) =>
                tokens.Any(tok => name.Contains(tok, StringComparison.OrdinalIgnoreCase));

            var candidates = assets.Where(a => WantedExtension(a.Name)).ToList();
            if (candidates.Count == 0) candidates = assets.ToList(); // лучше что-то, чем ничего

            if (is64BitOs)
            {
                var x64Match = candidates.FirstOrDefault(a => NameHasAnyToken(a.Name, x64Tokens));
                if (x64Match != null) return x64Match;
            }
            var x86Match = candidates.FirstOrDefault(a => NameHasAnyToken(a.Name, x86Tokens));
            if (x86Match != null) return x86Match;

            // Ни один известный токен архитектуры не встретился в имени —
            // на x86-системе рискованно брать что попало (может оказаться
            // x64), на x64 — почти наверняка можно (WOW64 разберётся даже
            // если это на самом деле x86 без явной пометки).
            return is64BitOs ? candidates.FirstOrDefault() : null;
        }

        /// <summary>
        /// Скачивает выбранный ассет, при необходимости распаковывает zip и
        /// находит внутри исполняемый/пакетный файл, затем ПРОСТО ЗАПУСКАЕТ
        /// его через ShellExecute — открывает окно инсталлятора InnoSetup
        /// либо системный App Installer для MSIX. Дальше пользователь ведёт
        /// диалог сам вручную (см. заголовок файла) — эта функция не
        /// автоматизирует сам процесс установки, только "долистывает" до
        /// момента, когда установщик уже открыт и готов к работе.
        /// </summary>
        public static async Task<(bool ok, string message)> DownloadAndLaunchUpdateAsync(
            ReleaseAsset asset, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            try
            {
                string workDir = Path.Combine(Path.GetTempPath(), "PhotoFrameUpdate",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);
                string downloadPath = Path.Combine(workDir, asset.Name);

                progress?.Report($"Скачивание {asset.Name}…");
                using (var resp = await _sharedHttp.GetAsync(asset.DownloadUrl,
                           HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long? total = resp.Content.Headers.ContentLength;
                    await using var source = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dest = File.Create(downloadPath);
                    // build 56: процент/МБ во время скачивания (самоконтейнерные
                    // установщики — десятки/сотни МБ, просто "Скачивание…" без
                    // индикации прогресса надолго выглядело бы как зависание.
                    byte[] buffer = new byte[81920];
                    long downloaded = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                        downloaded += read;
                        progress?.Report(total is > 0
                            ? $"Скачано {100 * downloaded / total.Value}% ({downloaded / 1048576} МБ из {total.Value / 1048576} МБ)"
                            : $"Скачано {downloaded / 1048576} МБ");
                    }
                }

                string? runnable = downloadPath;
                if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report("Распаковка…");
                    string extractDir = Path.Combine(workDir, "extracted");
                    ZipFile.ExtractToDirectory(downloadPath, extractDir);

                    // Ищем внутри установщик/пакет: сперва по расширению
                    // (.exe для InnoSetup, .msix/.msixbundle для MSIX), с
                    // предпочтением файлов, чьё имя содержит "Setup" — так
                    // называет их сам Installer.iss (OutputBaseFilename).
                    var candidates = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    runnable = candidates.FirstOrDefault(f =>
                                   Path.GetFileName(f).Contains("Setup", StringComparison.OrdinalIgnoreCase))
                               ?? candidates.FirstOrDefault();

                    if (runnable == null)
                        return (false, $"В архиве {asset.Name} не найден установочный файл (.exe/.msix).");
                }

                progress?.Report("Запуск установщика…");
                var psi = new System.Diagnostics.ProcessStartInfo(runnable)
                { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
                return (true, "Установщик запущен. Дальнейшие шаги — в его окне.");
            }
            catch (OperationCanceledException) { return (false, "Отменено."); }
            catch (Exception ex) { return (false, $"Не удалось скачать/запустить обновление: {ex.Message}"); }
        }

        /// <summary>
        /// Best-effort "подталкивание" проверки обновлений у самого
        /// ClickOnce — не делает ничего для InnoSetup/MSIX (у них другие
        /// методы выше). Классический System.Deployment.Application.
        /// ApplicationDeployment доступен только под .NET Framework; на
        /// .NET 8 (в т.ч. этот проект) сборка/API почти наверняка
        /// недоступны — вызов через reflection с безопасным no-op, если
        /// тип не найден, чтобы не завязывать основной код на
        /// System.Deployment (нет такой зависимости в .csproj и не должно
        /// быть — ClickOnce для .NET 5+ работает через отдельный,
        /// не-программный механизм, см. CI/pubxml UpdateMode/UpdateEnabled).
        /// </summary>
        public static bool TryNudgeClickOnceUpdateCheck()
        {
            if (!IsClickOnceDeployed) return false;
            try
            {
                var t = Type.GetType("System.Deployment.Application.ApplicationDeployment, " +
                                      "System.Deployment", throwOnError: false);
                var current = t?.GetProperty("CurrentDeployment", System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (current == null) return false;
                t!.GetMethod("CheckForUpdate", Type.EmptyTypes)?.Invoke(current, null);
                return true;
            }
            catch { return false; /* ClickOnce на .NET 8: обновляется сам по себе, это не ошибка */ }
        }
    }
}
