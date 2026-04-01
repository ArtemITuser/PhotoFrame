// Services/SettingsService.cs — хранение настроек в %AppData%\PhotoFrame\settings.json
// System.Text.Json (.NET 8) — без внешних зависимостей.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public static class SettingsService
    {
        private static readonly string Dir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoFrame");
        private static readonly string File = Path.Combine(Dir, "settings.json");

        private static readonly JsonSerializerOptions Opts = new()
        {
            WriteIndented = true,
            Converters    = { new JsonStringEnumConverter() }
        };

        public static AppSettings Load()
        {
            try
            {
                if (System.IO.File.Exists(File))
                {
                    string json = System.IO.File.ReadAllText(File);
                    return JsonSerializer.Deserialize<AppSettings>(json, Opts) ?? new AppSettings();
                }
            }
            catch { /* повреждённый файл */ }
            return new AppSettings();
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                System.IO.File.WriteAllText(File, JsonSerializer.Serialize(s, Opts));
            }
            catch { }
        }

        public static string StoragePath => Dir;
    }
}
