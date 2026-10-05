using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CIMS.Models
{
    // รูปแบบป้ายบาร์โค้ดของ Supplier ที่ผู้ใช้ตั้งเอง (CIMS.BarcodeFormats) เช่น Panta:
    // DCAE9031B-006;CHR; 26071606; -; DCAE9031B-006;SGACE 45/45; 1.000 X 175.00 X COIL; 1; 613.00; 21/08/2026; DCAE9031B;
    //   ตัวคั่น ";"  ช่องรหัสสินค้า 11 (DCAE9031B)  ช่องรหัสสำรอง 1 (DCAE9031B-006)  ช่องจำนวน 9 (613.00)
    // ตำแหน่งช่องนับจาก 1 (ช่องแรก = 1) ให้ตรงกับที่ผู้ใช้เห็นบนหน้าจอตั้งค่า
    public class BarcodeFormatModel
    {
        public int FmtId { get; set; }
        public string Name { get; set; }
        public string Delimiter { get; set; } = ";";
        public int CodePos { get; set; } = 1;
        public int? AltCodePos { get; set; }
        public int? QtyPos { get; set; }
        public int MinFields { get; set; } = 1;
        public string SampleText { get; set; }
        public bool IsActive { get; set; } = true;

        // แสกนป้ายรูปแบบนี้ "รับเข้าคลังหลัก" -> ตัดยอดจากคลังนี้อัตโนมัติ (เช่น Panta -> STOCK-PANTA) / null = ไม่ตัด
        public int? SourceStkId { get; set; }

        // คืน true เมื่อบาร์โค้ดเข้ารูปแบบนี้ - codes = รหัสที่ใช้ค้นหาตามลำดับ (รหัสหลักก่อน แล้วค่อยรหัสสำรอง)
        // qty = จำนวนจากป้าย (null ถ้ารูปแบบนี้ไม่ได้กำหนดช่องจำนวน -> ใช้ Pack Size ของสินค้าแทน)
        // ตัวกรองป้าย (สำหรับป้ายแบบ StorePC: |2A250-00017   0p37   ...   24.00   24.00   piece)
        public string DelimMode { get; set; } = "CHAR";     // CHAR = แยกด้วย Delimiter / SPACES = ช่องว่างกี่ตัวก็ได้นับเป็นตัวคั่นเดียว
        public string TrimChars { get; set; }               // ตัวอักษรที่ตัดทิ้งหัว-ท้ายป้ายก่อนแยกช่อง เช่น "|"
        public string CodePrefix { get; set; } = "NONE";    // NONE / DIGIT (ตัดเลขตัวแรกที่ตามด้วยตัวอักษร) / CUT (ตัดตามจำนวน CodeCut)
        public int? CodeCut { get; set; }
        public string MatchStart { get; set; }              // ป้ายต้องขึ้นต้นด้วยคำนี้ (ไม่สนตัวพิมพ์เล็ก/ใหญ่)
        public string MatchEnd { get; set; }                // ป้ายต้องลงท้ายด้วยคำนี้

        public bool SplitBySpaces => string.Equals(DelimMode, "SPACES", StringComparison.OrdinalIgnoreCase);

        // ป้ายนี้ใช่รูปแบบนี้ไหม (ขึ้นต้น / ลงท้าย / มีตัวคั่น)
        public bool Matches(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            if (!string.IsNullOrEmpty(MatchStart) && !t.StartsWith(MatchStart.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(MatchEnd) && !t.EndsWith(MatchEnd.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (!SplitBySpaces && (string.IsNullOrEmpty(Delimiter) || raw.IndexOf(Delimiter, StringComparison.Ordinal) < 0)) return false;
            return true;
        }

        // แยกป้ายเป็นช่อง (ใช้ทั้งตอนแสกนจริงและตัวอย่างในหน้าตั้งค่า)
        public string[] SplitFields(string raw)
        {
            string t = (raw ?? "").Trim();
            if (!string.IsNullOrEmpty(TrimChars)) t = t.Trim(TrimChars.ToCharArray()).Trim();
            if (SplitBySpaces) return Regex.Split(t, @"\s+").Where(x => x.Length > 0).ToArray();
            return string.IsNullOrEmpty(Delimiter) ? new[] { t } : t.Split(new[] { Delimiter }, StringSplitOptions.None);
        }

        // ตัดหัวรหัสตามที่ตั้งไว้ เช่น 2A250-00017 -> A250-00017
        public string CleanCode(string code)
        {
            string c = (code ?? "").Trim();
            if (string.Equals(CodePrefix, "DIGIT", StringComparison.OrdinalIgnoreCase))
            {
                if (c.Length > 2 && char.IsDigit(c[0]) && char.IsLetter(c[1])) c = c.Substring(1);
            }
            else if (string.Equals(CodePrefix, "CUT", StringComparison.OrdinalIgnoreCase) && CodeCut.HasValue && CodeCut.Value > 0)
            {
                c = c.Length > CodeCut.Value ? c.Substring(CodeCut.Value) : "";
            }
            return c;
        }

        // คืน true เมื่อบาร์โค้ดเข้ารูปแบบนี้ - codes = รหัสที่ใช้ค้นหาตามลำดับ (รหัสหลักก่อน แล้วค่อยรหัสสำรอง)
        // qty = จำนวนจากป้าย (null ถ้ารูปแบบนี้ไม่ได้กำหนดช่องจำนวน -> ใช้ Pack Size ของสินค้าแทน)
        public bool TryParse(string raw, out List<string> codes, out decimal? qty)
        {
            codes = new List<string>();
            qty = null;

            if (!IsActive || !Matches(raw)) return false;

            var fields = SplitFields(raw);
            int required = Math.Max(MinFields, Math.Max(CodePos, Math.Max(AltCodePos ?? 0, QtyPos ?? 0)));
            if (fields.Length < required) return false;

            AddCode(codes, fields, CodePos);
            if (AltCodePos.HasValue) AddCode(codes, fields, AltCodePos.Value);
            if (codes.Count == 0) return false;

            if (QtyPos.HasValue)
            {
                string q = fields[QtyPos.Value - 1].Trim();
                if (!decimal.TryParse(q, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) || parsed <= 0)
                    return false;
                qty = parsed;
            }
            return true;
        }

        private void AddCode(List<string> codes, string[] fields, int pos)
        {
            if (pos < 1 || pos > fields.Length) return;
            string c = CleanCode(fields[pos - 1]);
            if (!string.IsNullOrEmpty(c) && c != "-" && !codes.Contains(c)) codes.Add(c);
        }
        public override string ToString() => Name;
    }
}
