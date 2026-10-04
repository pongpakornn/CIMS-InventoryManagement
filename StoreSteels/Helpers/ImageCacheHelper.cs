using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CIMS.Helpers
{
    // 🖼 รูปสินค้า (อยู่บน \\192.168.10.56) - โหลดเบื้องหลัง ย่อขนาดตอนถอดรหัส แล้วเก็บไว้ในหน่วยความจำ
    //   - หน้าจอไม่รอ Network: ครั้งแรกคืน null ทันที แล้วแจ้งให้วาดใหม่เมื่อรูปพร้อม (เลื่อนตารางลื่น ไม่กระตุก)
    //   - รูปเดิมเปิดซ้ำ = ใช้จากหน่วยความจำ (ไม่อ่านไฟล์ซ้ำ) / ไฟล์ไม่มีจริง = จำไว้ 2 นาที ไม่เช็กซ้ำทุกแถว
    //   - โหลดพร้อมกันไม่เกิน 6 ไฟล์ ไม่ให้ Server / Network ถูกถล่ม
    public static class ImageCacheHelper
    {
        private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new ConcurrentDictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _missing = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _found = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Task<BitmapImage>> _loading = new ConcurrentDictionary<string, Task<BitmapImage>>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(6);
        private static readonly TimeSpan MissingTtl = TimeSpan.FromMinutes(2);
        private const int MaxCached = 3000;

        private static string Key(string path, int width) => width + "|" + path;

        private static bool KnownMissing(string path) =>
            _missing.TryGetValue(path, out var t) && DateTime.Now - t < MissingTtl;

        // มีไฟล์จริงไหม (จำผลไว้ ไม่ต้องถาม Network ทุกครั้ง)
        public static bool Exists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || KnownMissing(path)) return false;
            if (_found.TryGetValue(path, out var t) && DateTime.Now - t < MissingTtl) return true;
            if (File.Exists(path)) { _missing.TryRemove(path, out _); _found[path] = DateTime.Now; return true; }
            _missing[path] = DateTime.Now;
            return false;
        }

        // แบบเดิม (รอโหลดเสร็จ) - ใช้ที่ต้องการรูปทันที เช่นหน้าสแกน 1 รูป
        public static BitmapImage LoadImage(string path, int width = 120)
        {
            if (string.IsNullOrWhiteSpace(path) || KnownMissing(path)) return null;
            if (_cache.TryGetValue(Key(path, width), out var hit)) return hit;
            return Decode(path, width);
        }

        // แบบไม่รอ: มีในหน่วยความจำ -> คืนเลย / ยังไม่มี -> คืน null แล้วโหลดเบื้องหลัง เสร็จแล้วเรียก onReady บน UI thread
        public static BitmapImage GetOrLoad(string path, int width, Action onReady)
        {
            if (string.IsNullOrWhiteSpace(path) || KnownMissing(path)) return null;
            if (_cache.TryGetValue(Key(path, width), out var hit)) return hit;

            var task = _loading.GetOrAdd(Key(path, width), k => Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try { return Decode(path, width); }
                finally { _gate.Release(); _loading.TryRemove(Key(path, width), out _); }
            }));
            if (onReady != null)
                task.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                        Application.Current?.Dispatcher.BeginInvoke(onReady, System.Windows.Threading.DispatcherPriority.Background);
                }, TaskScheduler.Default);
            return null;
        }

        private static BitmapImage Decode(string path, int width)
        {
            try
            {
                if (!File.Exists(path)) { _missing[path] = DateTime.Now; return null; }
                // อ่านไฟล์ครั้งเดียวเข้าหน่วยความจำ (ไม่ล็อกไฟล์บน Server) แล้วถอดรหัสแบบย่อ
                byte[] bytes = File.ReadAllBytes(path);
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (width > 0) bmp.DecodePixelWidth = width;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                if (_cache.Count > MaxCached) _cache.Clear();
                _cache[Key(path, width)] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        // เปลี่ยน / อัปโหลดรูปใหม่ชื่อเดิม -> ล้างของเก่าออก
        public static void Forget(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            _missing.TryRemove(path, out _);
            _found.TryRemove(path, out _);
            foreach (var k in _cache.Keys)
                if (k.EndsWith("|" + path, StringComparison.OrdinalIgnoreCase)) _cache.TryRemove(k, out _);
        }
    }
}
