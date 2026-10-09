// 🔄 แจ้งอัพเดทเวอร์ชันใหม่ (ClickOnce)
//   ClickOnce ตรวจเวอร์ชันก่อนเปิดโปรแกรมอยู่แล้ว (Publish ตั้ง Update before startup) - ตัวนี้ช่วยเพิ่ม:
//   - เปิดโปรแกรมค้างไว้ทั้งวัน -> ตรวจทุก 10 นาทีว่ามีเวอร์ชันใหม่บน Server ไหม ถ้ามีถามผู้ใช้ "อัพเดทตอนนี้"
//   - กด YES -> เปิดไฟล์ CIMS.application บน Server (ClickOnce อัพเดท + เปิดเวอร์ชันใหม่) แล้วปิดตัวเดิม
//   ข้อมูลเวอร์ชัน / ที่อยู่ไฟล์อัพเดท มาจาก ClickOnce (Environment Variables ของ .NET 8 ClickOnce) - เปิดจาก Visual Studio = ไม่ตรวจ
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Xml.Linq;

namespace CIMS.Helpers
{
    public static class AppUpdate
    {
        private static string Env(string name) => Environment.GetEnvironmentVariable(name);

        // เปิดผ่าน ClickOnce (ไอคอนใน Start Menu) = มี Environment Variables
        // เปิดจากไอคอนที่ปักหมุดไว้ที่ Taskbar / Shortcut ไปที่ CIMS.exe ตรงๆ = ไม่มี -> ดูจากโฟลเดอร์ที่ ClickOnce ติดตั้ง (...\Apps\2.0\...)
        private static string BaseDir => AppContext.BaseDirectory ?? "";
        private static bool InClickOnceCache => BaseDir.IndexOf(@"\Apps\2.0\", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsNetworkDeployed =>
            string.Equals(Env("ClickOnce_IsNetworkDeployed"), "true", StringComparison.OrdinalIgnoreCase) || InClickOnceCache;

        // เวอร์ชันที่ติดตั้ง: จาก ClickOnce / ชื่อโฟลเดอร์ติดตั้ง (เช่น ..._0001.0000.0000.000c_... = 1.0.0.12)
        public static Version CurrentVersion
        {
            get
            {
                if (Version.TryParse(Env("ClickOnce_CurrentVersion"), out var v)) return v;
                var m = System.Text.RegularExpressions.Regex.Match(BaseDir, @"_([0-9a-fA-F]{4})\.([0-9a-fA-F]{4})\.([0-9a-fA-F]{4})\.([0-9a-fA-F]{4})_");
                if (m.Success)
                    return new Version(Convert.ToInt32(m.Groups[1].Value, 16), Convert.ToInt32(m.Groups[2].Value, 16),
                                       Convert.ToInt32(m.Groups[3].Value, 16), Convert.ToInt32(m.Groups[4].Value, 16));
                return typeof(AppUpdate).Assembly.GetName().Version;
            }
        }

        // ไฟล์ CIMS.application บน Server (ที่ Publish ไว้) - ไม่มีข้อมูลจาก ClickOnce = โฟลเดอร์ Publish เดียวกับโฟลเดอร์รูป (1. Image Stock)
        public static string UpdateLocation =>
            Env("ClickOnce_UpdateLocation") ?? Env("ClickOnce_ActivationUri")
            ?? (InClickOnceCache ? Path.Combine(Path.GetDirectoryName(ImagePaths.StockRoot.TrimEnd('\\')), "CIMS.application") : null);

        public static string VersionText => IsNetworkDeployed ? $"v{CurrentVersion}" : "v" + CurrentVersion + " (DEV)";

        // อ่านเวอร์ชันล่าสุดจาก CIMS.application (null = อ่านไม่ได้ / ไม่ได้ติดตั้งแบบ ClickOnce)
        public static Version GetPublishedVersion()
        {
            if (!IsNetworkDeployed || string.IsNullOrWhiteSpace(UpdateLocation)) return null;
            string path = UpdateLocation;
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
            if (!File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var doc = XDocument.Load(fs);
                var id = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "assemblyIdentity");
                return Version.TryParse((string)id?.Attribute("version"), out var v) ? v : null;
            }
        }

        // ตรวจเบื้องหลัง -> มีเวอร์ชันใหม่ = ถาม (ไม่ถามซ้ำภายใน 30 นาทีถ้าตอบ NO) / ระหว่างมีหน้าต่างอื่นเปิดอยู่ = รอรอบหน้า
        private static DateTime _snoozeUntil = DateTime.MinValue;
        private static bool _asking;

        public static async Task CheckAndPromptAsync(bool fromLogin = false)
        {
            if (!IsNetworkDeployed || _asking || DateTime.Now < _snoozeUntil) return;
            Version latest;
            try { latest = await Task.Run(GetPublishedVersion); }
            catch { return; }   // Server ช้า / หลุด = เงียบไว้ ลองใหม่รอบหน้า
            if (latest == null || latest <= CurrentVersion) return;
            if (ComponentDispatcher.IsThreadModal) return;   // มีกล่องข้อความ / หน้าต่างเปิดอยู่ -> รอบหน้า

            _asking = true;
            try
            {
                bool yes = DialogHelper.ShowConfirm(
                    $"มีโปรแกรม CIMS เวอร์ชันใหม่ {latest} (เครื่องนี้ {CurrentVersion})\n\n" +
                    (fromLogin ? "" : "• งานที่ยังไม่บันทึกกรุณาบันทึกก่อน\n") +
                    "• กด YES เพื่ออัพเดทตอนนี้ (โปรแกรมจะปิดแล้วเปิดเวอร์ชันใหม่ให้เอง)\n" +
                    "• กด NO เพื่ออัพเดทภายหลัง", "UPDATE VERSION");
                if (!yes) { _snoozeUntil = DateTime.Now.AddMinutes(30); return; }
                Process.Start(new ProcessStartInfo(UpdateLocation) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _snoozeUntil = DateTime.Now.AddMinutes(30);
                DialogHelper.ShowError("เปิดตัวอัพเดทไม่สำเร็จ\n" + ex.Message + "\n\nปิดโปรแกรมแล้วเปิดใหม่ ระบบจะอัพเดทให้ตอนเปิด");
            }
            finally { _asking = false; }
        }

        // ตรวจทุก 10 นาทีระหว่างเปิดโปรแกรม
        private static DispatcherTimer _timer;
        public static void StartPeriodicCheck()
        {
            if (!IsNetworkDeployed || _timer != null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(10) };
            _timer.Tick += async (s, e) => await CheckAndPromptAsync();
            _timer.Start();
        }
    }
}
