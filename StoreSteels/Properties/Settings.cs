using System;
using System.IO;
using System.Text.Json;

namespace CIMS.Properties
{
    // Lightweight replacement for the VS "Settings.settings" designer file.
    // Persists per-user app settings (remembered login) as JSON under %LocalAppData%.
    public sealed class Settings
    {
        private static readonly Settings _default = Load();

        public static Settings Default => _default;

        public string SavedUsername { get; set; } = string.Empty;
        public string SavedPassword { get; set; } = string.Empty;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CIMS", "settings.json");

        private static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<Settings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch
            {
                // Ignore corrupt/missing settings file and fall back to defaults.
            }

            return new Settings();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(FilePath, json);
        }
    }
}
