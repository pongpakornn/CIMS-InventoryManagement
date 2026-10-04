using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CIMS.Helpers
{
    // ค่าหน้าจอที่จำไว้เฉพาะเครื่องนี้ (เช่น ความเร็วการเลื่อน / เปิด-ปิดการ์ดสินค้า)
    // เก็บที่ %LocalAppData%\CIMS\ui-settings.txt แบบ key=value - อ่าน/เขียนไม่ได้ก็ใช้ค่าเริ่มต้น ไม่แจ้ง Error
    public static class UiPrefs
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIMS", "ui-settings.txt");

        private static Dictionary<string, string> _cache;

        private static Dictionary<string, string> Load()
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) _cache[line.Substring(0, i)] = line.Substring(i + 1);
                    }
                }
            }
            catch { }
            return _cache;
        }

        public static string Get(string key, string fallback = null) =>
            Load().TryGetValue(key, out var v) ? v : fallback;

        public static int GetInt(string key, int fallback) =>
            int.TryParse(Get(key), out int v) ? v : fallback;

        public static bool GetBool(string key, bool fallback) =>
            bool.TryParse(Get(key), out bool v) ? v : fallback;

        public static void Set(string key, object value)
        {
            var d = Load();
            d[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value));
            }
            catch { }
        }
    }
}
