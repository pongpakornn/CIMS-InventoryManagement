using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CIMS.Helpers
{
    // 🏷️ ค่าที่แสดงในช่อง PRODUCT CODE ของหน้า Multi-Scanner / HISTORY ตั้งต่อคลัง (CIMS.Stocks.ScanDisplayField)
    //   PartCode (ค่าเดิม) / PartA / PartNumber / Model / Description
    //   FIELD:<FormatID>:<n> = ช่องที่ n ของบาร์โค้ดดิบตาม Barcode Format นั้น (เช่น Panta #11)
    //   ใช้แค่ "แสดงผล" ไม่มีผลกับการบันทึก / ยอด - ดึงค่าไม่ได้ (ป้ายไม่ตรงรูปแบบ / ค่าว่าง) = แสดง PartCode
    public static class ScanDisplay
    {
        public const string Default = "PartCode";

        public static readonly (string Key, string Label)[] Columns =
        {
            ("PartCode", "PRODUCT CODE"),
            ("PartA", "PART A"),
            ("PartNumber", "PART NO"),
            ("Model", "MODEL"),
            ("Description", "PRODUCT NAME")
        };

        public static string FieldKey(int fmtId, int n) => $"FIELD:{fmtId}:{n}";

        public static bool TryParseField(string key, out int fmtId, out int n)
        {
            fmtId = 0; n = 0;
            var p = (key ?? "").Split(':');
            return p.Length == 3 && p[0] == "FIELD" && int.TryParse(p[1], out fmtId) && int.TryParse(p[2], out n) && n > 0;
        }

        public static string Resolve(string key, string partCode, string partA, string partNo, string model, string name,
                                     string rawBarcode, IEnumerable<BarcodeFormatModel> formats)
        {
            string fallback = partCode ?? "";
            string v = null;
            switch (string.IsNullOrWhiteSpace(key) ? Default : key)
            {
                case "PartCode": v = partCode; break;
                case "PartA": v = partA; break;
                case "PartNumber": v = partNo; break;
                case "Model": v = model; break;
                case "Description": v = name; break;
                default:
                    if (TryParseField(key, out int fmtId, out int n) && !string.IsNullOrWhiteSpace(rawBarcode))
                    {
                        var f = formats?.FirstOrDefault(x => x.FmtId == fmtId);
                        if (f != null && f.Matches(rawBarcode))
                        {
                            var fields = f.SplitFields(rawBarcode);
                            if (n <= fields.Length) v = fields[n - 1]?.Trim();
                        }
                    }
                    break;
            }
            return string.IsNullOrWhiteSpace(v) || v == "-" ? fallback : v;
        }
    }
}
