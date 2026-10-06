using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CIMS.Helpers
{
    // 🖼 ที่เก็บรูปสินค้า
    //   ใหม่: \\192.168.10.56\ProgramCHR\2. Store Only\1. CIMS - Inventory Management\1. Image Stock\<STOCK CODE>\<STOCK CODE>-01.png
    //         ImageFileName เก็บเป็น "<STOCK CODE>\<STOCK CODE>-01.png"
    //   เดิม: ...\2. Store Only\StoreSteels\Image\<ชื่อไฟล์>  (ImageFileName ไม่มี "\" = รูปเก่า ยังเปิดได้เหมือนเดิม)
    public static class ImagePaths
    {
        public const string StockRoot = @"\\192.168.10.56\ProgramCHR\2. Store Only\1. CIMS - Inventory Management\1. Image Stock";
        public const string LegacyRoot = @"\\192.168.10.56\ProgramCHR\2. Store Only\StoreSteels\Image";
        // ⏳ ชั่วคราว: รูปของโปรแกรมเดิม StorePC (คลังที่แสดงข้อมูลสดจาก StorePC - CIMS.vw_StorePcLive ส่งมาเป็น "STOREPC:<ไฟล์>")
        public const string StorePcRoot = @"\\192.168.10.56\ProgramCHR\2. Store Only\StorePC\ImageStore";
        private const string StorePcPrefix = "STOREPC:";

        // ImageFileName -> path เต็ม (ไม่เช็กว่ามีไฟล์จริง)
        public static string Resolve(string imageFileName)
        {
            if (string.IsNullOrWhiteSpace(imageFileName)) return null;
            string f = imageFileName.Trim();
            if (f.StartsWith(StorePcPrefix, StringComparison.OrdinalIgnoreCase)) return Path.Combine(StorePcRoot, f.Substring(StorePcPrefix.Length).Trim());
            if (Path.IsPathRooted(f)) return f;
            return f.Contains('\\') || f.Contains('/') ? Path.Combine(StockRoot, f.Replace('/', '\\')) : Path.Combine(LegacyRoot, f);
        }

        // ชื่อโฟลเดอร์ของคลัง (ตัดตัวอักษรที่ใช้ตั้งชื่อโฟลเดอร์ไม่ได้)
        public static string SafeCode(string stockCode)
        {
            string s = (stockCode ?? "STOCK").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            return s.Length == 0 ? "STOCK" : s;
        }

        // โฟลเดอร์ของคลัง - หายหรือยังไม่มีก็สร้างใหม่ / มีอยู่แล้วใช้ของเดิม
        public static string EnsureStockFolder(string stockCode)
        {
            string dir = Path.Combine(StockRoot, SafeCode(stockCode));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        // ชื่อรูปถัดไปของคลัง: STORE-PC-01, STORE-PC-02 ... (เลขต่อจากเลขที่มากที่สุดในโฟลเดอร์)
        // สินค้าที่มีรูปในคลังนี้อยู่แล้ว -> ใช้เลขเดิม (รูปใหม่แทนที่รูปเก่า)
        public static string NextImageName(string stockCode, string currentImageFileName, string extension)
        {
            string code = SafeCode(stockCode);
            string dir = EnsureStockFolder(stockCode);
            extension = string.IsNullOrWhiteSpace(extension) ? ".png" : extension.ToLowerInvariant();

            var rx = new Regex("^" + Regex.Escape(code) + @"-(\d+)$", RegexOptions.IgnoreCase);
            if (!string.IsNullOrWhiteSpace(currentImageFileName))
            {
                string cur = currentImageFileName.Replace('/', '\\');
                string curDir = Path.GetDirectoryName(cur) ?? "";
                if (string.Equals(curDir, code, StringComparison.OrdinalIgnoreCase) && rx.IsMatch(Path.GetFileNameWithoutExtension(cur)))
                    return Path.Combine(code, Path.GetFileNameWithoutExtension(cur) + extension);
            }

            int max = Directory.EnumerateFiles(dir)
                .Select(p => rx.Match(Path.GetFileNameWithoutExtension(p)))
                .Where(m => m.Success)
                .Select(m => int.TryParse(m.Groups[1].Value, out int n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            return Path.Combine(code, $"{code}-{(max + 1):00}{extension}");
        }
    }
}
